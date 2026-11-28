//! server/http.js: static files (ETag, 304, brotli or gzip from a cached
//! copy, transformed pages and versioned scripts), text the server makes
//! (sendText) and JSON answers (sendJson: gzip over 1 KB).

use axum::body::{Body, Bytes};
use axum::http::{header, HeaderMap, HeaderValue, Method, Response, StatusCode};
use serde_json::{json, Value};
use std::collections::HashMap;
use std::io::Write;
use std::os::unix::fs::MetadataExt;
use std::path::{Component, Path, PathBuf};
use std::sync::Mutex;

pub type Res = Response<Body>;

/// One request, as the ui routes see it.
pub struct Req {
    pub method: Method,
    /// url.pathname as sent (still percent-encoded)
    pub raw_path: String,
    /// "?a=1" or ""
    pub search: String,
    /// Object.fromEntries(url.searchParams)
    pub query: HashMap<String, String>,
    pub headers: HeaderMap,
    pub body: Bytes,
    pub peer: Option<std::net::SocketAddr>,
    /// the interface this request is shown (Packs::active), worked out once
    pub shown: std::sync::OnceLock<Option<String>>,
}

impl Req {
    pub fn header(&self, name: &str) -> Option<&str> {
        self.headers.get(name).and_then(|v| v.to_str().ok())
    }
    pub fn info(&self) -> super::ctx::ReqInfo<'_> {
        super::ctx::ReqInfo { headers: &self.headers, peer: self.peer }
    }
    pub fn head(&self) -> bool {
        self.method == Method::HEAD
    }
}

/// http.js STATIC_MIME
pub fn static_type(p: &Path) -> &'static str {
    let ext = p.extension().map(|e| e.to_string_lossy().to_lowercase());
    // (path.extname: a name that starts with a dot and has no other has none)
    let name = p.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    if name.starts_with('.') && name[1..].find('.').is_none() {
        return "application/octet-stream";
    }
    match ext.as_deref() {
        Some("html") => "text/html; charset=utf-8",
        Some("js" | "mjs" | "cjs") => "text/javascript; charset=utf-8",
        Some("css") => "text/css; charset=utf-8",
        Some("json") => "application/json; charset=utf-8",
        Some("png") => "image/png",
        Some("jpg" | "jpeg") => "image/jpeg",
        Some("svg") => "image/svg+xml",
        Some("ico") => "image/x-icon",
        Some("icns") => "application/octet-stream",
        Some("woff2") => "font/woff2",
        Some("woff") => "font/woff",
        Some("ttf") => "font/ttf",
        Some("proto") => "text/plain; charset=utf-8",
        Some("map") => "application/json",
        Some("txt" | "md") => "text/plain; charset=utf-8",
        Some("gif") => "image/gif",
        Some("webp") => "image/webp",
        _ => "application/octet-stream",
    }
}

/// COMPRESSIBLE: text worth compressing.
fn textual(t: &str) -> bool {
    t.starts_with("text/") || t.starts_with("application/json") || t.starts_with("application/javascript") || t.starts_with("image/svg")
}

/// A word on its own (JavaScript's \b...\b for plain letters).
fn has_word(s: &str, w: &str) -> bool {
    let b = s.as_bytes();
    let word = |c: u8| c.is_ascii_alphanumeric() || c == b'_';
    let mut i = 0;
    while let Some(at) = s[i..].find(w) {
        let a = i + at;
        let z = a + w.len();
        if (a == 0 || !word(b[a - 1])) && (z == b.len() || !word(b[z])) {
            return true;
        }
        i = a + 1;
    }
    false
}

/// encodingFor: brotli or gzip, as the browser asked.
pub fn encoding_for(headers: &HeaderMap) -> Option<&'static str> {
    let accept = headers.get(header::ACCEPT_ENCODING).and_then(|v| v.to_str().ok()).unwrap_or("");
    if has_word(accept, "br") {
        Some("br")
    } else if has_word(accept, "gzip") {
        Some("gzip")
    } else {
        None
    }
}

pub fn compress(buf: &[u8], enc: &str) -> Vec<u8> {
    if enc == "br" {
        // quality 5: about gzip's speed, smaller output (the result is cached)
        let mut out = Vec::new();
        let params = brotli::enc::BrotliEncoderParams { quality: 5, lgwin: 22, size_hint: buf.len(), ..Default::default() };
        let _ = brotli::BrotliCompress(&mut &buf[..], &mut out, &params);
        out
    } else {
        gzip(buf, 6)
    }
}

pub fn gzip(buf: &[u8], level: u32) -> Vec<u8> {
    let mut e = flate2::write::GzEncoder::new(Vec::new(), flate2::Compression::new(level));
    let _ = e.write_all(buf);
    e.finish().unwrap_or_default()
}

/// The length of a string as JavaScript counts it (UTF-16 units).
pub fn js_len(s: &str) -> usize {
    s.encode_utf16().count()
}

/// sendJson: no-store, gzip (level 4) for answers over 1 KB when the browser accepts it.
pub fn send_json(headers: &HeaderMap, status: StatusCode, body: &Value) -> Res {
    let data = body.to_string();
    let b = Response::builder().status(status).header(header::CONTENT_TYPE, "application/json; charset=utf-8").header(header::CACHE_CONTROL, "no-store");
    if js_len(&data) > 1024 && encoding_for(headers).is_some() {
        return b.header(header::CONTENT_ENCODING, "gzip").header(header::VARY, "Accept-Encoding").body(Body::from(gzip(data.as_bytes(), 4))).unwrap();
    }
    b.body(Body::from(data)).unwrap()
}

pub fn error(headers: &HeaderMap, status: StatusCode, msg: &str) -> Res {
    send_json(headers, status, &json!({ "error": msg }))
}

/// An error with a code the page acts on ({ error, code }).
pub fn error_code(headers: &HeaderMap, status: StatusCode, msg: &str, code: &str) -> Res {
    send_json(headers, status, &json!({ "error": msg, "code": code }))
}

pub fn not_found(headers: &HeaderMap) -> Res {
    error(headers, StatusCode::NOT_FOUND, "Not found")
}

/// A 302 (or other) to `location`, with these extra headers.
pub fn redirect(status: StatusCode, location: &str, extra: &[(&str, String)]) -> Res {
    let mut b = Response::builder().status(status).header(header::LOCATION, location);
    for (k, v) in extra {
        b = b.header(*k, v.as_str());
    }
    b.body(Body::empty()).unwrap()
}

/// `If-None-Match` names this ETag (split by /\s*,\s*/ as http.js does).
pub fn none_match(headers: &HeaderMap, etag: &str) -> bool {
    let v = headers.get(header::IF_NONE_MATCH).and_then(|v| v.to_str().ok()).unwrap_or("");
    let parts: Vec<&str> = v.split(',').collect();
    let n = parts.len();
    parts.iter().enumerate().any(|(i, p)| {
        let mut p = *p;
        if i > 0 {
            p = p.trim_start();
        }
        if i + 1 < n {
            p = p.trim_end();
        }
        p == etag
    })
}

pub fn base36(n: u64) -> String {
    crate::media::base36(n)
}

/// path.resolve(root, './' + rel): `rel` with `.` and empty parts dropped (no `..`).
fn resolve_under(root: &Path, rel: &str) -> Option<PathBuf> {
    let mut abs = root.to_path_buf();
    for c in Path::new(rel).components() {
        match c {
            Component::Normal(s) => abs.push(s),
            Component::CurDir | Component::RootDir => {}
            _ => return None,
        }
    }
    Some(abs)
}

/// What a page or script is changed with before it is sent.
pub type Transform = Box<dyn Fn(&str) -> String + Send>;

#[derive(Default)]
pub struct ServeOpts {
    pub transform: Option<Transform>,
    /// what makes a transformed file differ (a versioned script's version): kept ready
    pub transform_key: Option<String>,
    /// what makes a transformed page differ: its copy (and its compressed ones) kept ready, the
    /// page still sent as one never kept (no-cache)
    pub page_key: Option<String>,
    pub cache: Option<String>,
}

struct Transformed {
    body: Bytes,
    /// the body's length as JavaScript counts it
    js_len: usize,
    packed: HashMap<&'static str, Bytes>,
}

/// The caches of serveFile: compressed copies (48 MB in all, least recently
/// used out first) and transformed files with a key (400).
#[derive(Default)]
pub struct FileCache {
    packed: Mutex<(HashMap<String, (u64, Bytes)>, u64, usize)>,
    transformed: Mutex<(HashMap<String, (u64, Transformed)>, u64)>,
}

const PACKED_MAX: usize = 48 * 1024 * 1024;

impl FileCache {
    fn packed_copy(&self, abs: &Path, key: String, enc: &'static str) -> Option<Bytes> {
        {
            let mut g = self.packed.lock().unwrap();
            g.1 += 1;
            let tick = g.1;
            if let Some(e) = g.0.get_mut(&key) {
                e.0 = tick;
                return Some(e.1.clone());
            }
        }
        let data = std::fs::read(abs).ok()?;
        let buf = Bytes::from(compress(&data, enc));
        let mut g = self.packed.lock().unwrap();
        g.1 += 1;
        let tick = g.1;
        g.2 += buf.len();
        g.0.insert(key, (tick, buf.clone()));
        while g.2 > PACKED_MAX && !g.0.is_empty() {
            let oldest = g.0.iter().min_by_key(|(_, v)| v.0).map(|(k, _)| k.clone()).unwrap();
            if let Some((_, b)) = g.0.remove(&oldest) {
                g.2 -= b.len();
            }
        }
        Some(buf)
    }

    /// serveFile: a file below `root`; None when there is none.
    pub fn serve_file(&self, req: &Req, root: &Path, rel: &str, opts: ServeOpts) -> Option<Res> {
        if rel.contains('\0') || rel.split('/').any(|s| s == "..") {
            return None;
        }
        let abs = resolve_under(root, rel)?;
        if abs != root && !abs.starts_with(root) {
            return None;
        }
        let md = std::fs::symlink_metadata(&abs).ok()?;
        if !md.is_file() {
            return None;
        }
        let typ = static_type(&abs);
        let text = textual(typ);
        let cache = opts.cache.clone().unwrap_or_else(|| "no-cache".into());
        let mtime = crate::sei::js_round(crate::scanner::ms_of(md.mtime(), md.mtime_nsec()));
        if let Some(transform) = opts.transform {
            let kept = opts.transform_key.as_ref().or(opts.page_key.as_ref());
            let memo = kept.map(|k| format!("{}|{}|{}|{}|{}", abs.display(), md.size(), crate::scanner::ms_of(md.mtime(), md.mtime_nsec()), if opts.transform_key.is_some() { "script" } else { "page" }, k));
            let hit = memo.as_ref().and_then(|m| {
                let mut g = self.transformed.lock().unwrap();
                g.1 += 1;
                let t = g.1;
                g.0.get_mut(m).map(|e| {
                    e.0 = t;
                    (e.1.body.clone(), e.1.js_len)
                })
            });
            let (body, len) = match hit {
                Some(b) => b,
                None => {
                    let raw = std::fs::read(&abs).ok()?;
                    let made = transform(&String::from_utf8_lossy(&raw));
                    let len = js_len(&made);
                    let b = Bytes::from(made);
                    if let Some(m) = &memo {
                        let mut g = self.transformed.lock().unwrap();
                        g.1 += 1;
                        let t = g.1;
                        g.0.insert(m.clone(), (t, Transformed { body: b.clone(), js_len: len, packed: HashMap::new() }));
                        if g.0.len() > 400 {
                            let oldest = g.0.iter().min_by_key(|(_, v)| v.0).map(|(k, _)| k.clone()).unwrap();
                            g.0.remove(&oldest);
                        }
                    }
                    (b, len)
                }
            };
            let enc = if text && len > 1024 { encoding_for(&req.headers) } else { None };
            let out: Bytes = match enc {
                Some(e) => {
                    let mut cached = None;
                    if let Some(m) = &memo {
                        let g = self.transformed.lock().unwrap();
                        cached = g.0.get(m).and_then(|x| x.1.packed.get(e).cloned());
                    }
                    match cached {
                        Some(c) => c,
                        None => {
                            let c = Bytes::from(compress(&body, e));
                            if let Some(m) = &memo {
                                if let Some(x) = self.transformed.lock().unwrap().0.get_mut(m) {
                                    x.1.packed.insert(e, c.clone());
                                }
                            }
                            c
                        }
                    }
                }
                None => body,
            };
            // a page is never kept by the browser (even one kept ready here); a versioned script may be
            let mut b = Response::builder()
                .status(StatusCode::OK)
                .header(header::CONTENT_TYPE, typ)
                .header(header::CACHE_CONTROL, if opts.transform_key.is_some() { cache.as_str() } else { "no-cache" })
                .header("X-Content-Type-Options", "nosniff");
            if let Some(e) = enc {
                b = b.header(header::CONTENT_ENCODING, e);
            }
            if text {
                b = b.header(header::VARY, "Accept-Encoding");
            }
            b = b.header(header::CONTENT_LENGTH, out.len());
            return Some(b.body(if req.head() { Body::empty() } else { Body::from(out) }).unwrap());
        }
        let etag = format!("W/\"{}-{}\"", base36(md.size()), base36(mtime.max(0.0) as u64));
        let common = |status: StatusCode| {
            let mut b = Response::builder()
                .status(status)
                .header(header::CONTENT_TYPE, typ)
                .header(header::CACHE_CONTROL, cache.as_str())
                .header(header::ETAG, etag.as_str())
                .header("X-Content-Type-Options", "nosniff");
            if text {
                b = b.header(header::VARY, "Accept-Encoding");
            }
            b
        };
        if none_match(&req.headers, &etag) {
            return Some(common(StatusCode::NOT_MODIFIED).body(Body::empty()).unwrap());
        }
        let enc = if text && md.size() > 1024 { encoding_for(&req.headers) } else { None };
        if let Some(e) = enc {
            let key = format!("{}|{}|{}|{}", abs.display(), md.size(), crate::scanner::ms_of(md.mtime(), md.mtime_nsec()), e);
            let buf = self.packed_copy(&abs, key, e)?;
            let b = common(StatusCode::OK).header(header::CONTENT_ENCODING, e).header(header::CONTENT_LENGTH, buf.len());
            return Some(b.body(if req.head() { Body::empty() } else { Body::from(buf) }).unwrap());
        }
        let b = common(StatusCode::OK).header(header::CONTENT_LENGTH, HeaderValue::from(md.size()));
        if req.head() {
            return Some(b.body(Body::empty()).unwrap());
        }
        let data = std::fs::read(&abs).ok()?;
        Some(b.body(Body::from(data)).unwrap())
    }
}

/// sendText: text the server made, with an ETag, 304 and compression like a file.
pub fn send_text(req: &Req, body: &str, typ: &str, etag: &str, cache: Option<&str>) -> Res {
    let common = |status: StatusCode| {
        Response::builder()
            .status(status)
            .header(header::CONTENT_TYPE, typ)
            .header(header::CACHE_CONTROL, cache.unwrap_or("no-cache"))
            .header(header::ETAG, etag)
            .header(header::VARY, "Accept-Encoding")
            .header("X-Content-Type-Options", "nosniff")
    };
    if none_match(&req.headers, etag) {
        return common(StatusCode::NOT_MODIFIED).body(Body::empty()).unwrap();
    }
    let enc = if js_len(body) > 1024 { encoding_for(&req.headers) } else { None };
    let mut b = common(StatusCode::OK);
    let out = match enc {
        Some(e) => {
            b = b.header(header::CONTENT_ENCODING, e);
            packed_text(body, etag, e)
        }
        None => Bytes::copy_from_slice(body.as_bytes()),
    };
    b.body(if req.head() { Body::empty() } else { Body::from(out) }).unwrap()
}

/// The compressed copy of a text sendText sends, kept by its ETag (which names its content).
fn packed_text(body: &str, etag: &str, enc: &'static str) -> Bytes {
    static KEPT: Mutex<Option<HashMap<(String, &'static str), (usize, Bytes)>>> = Mutex::new(None);
    if let Some((_, b)) = KEPT.lock().unwrap().get_or_insert_with(HashMap::new).get(&(etag.to_string(), enc)).filter(|(n, _)| *n == body.len()) {
        return b.clone();
    }
    let b = Bytes::from(compress(body.as_bytes(), enc));
    let mut g = KEPT.lock().unwrap();
    let m = g.get_or_insert_with(HashMap::new);
    // (a few texts: the module and the languages; whatever it held goes when it fills)
    if m.len() >= 64 {
        m.clear();
    }
    m.insert((etag.to_string(), enc), (body.len(), b.clone()));
    b
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::Arc;

    fn req(enc: &str) -> Req {
        let mut headers = HeaderMap::new();
        if !enc.is_empty() {
            headers.insert(header::ACCEPT_ENCODING, HeaderValue::from_str(enc).unwrap());
        }
        Req { method: Method::GET, raw_path: "/p.html".into(), search: String::new(), query: HashMap::new(), headers, body: Bytes::new(), peer: None, shown: Default::default() }
    }

    async fn body(r: Res) -> (String, String, Vec<u8>) {
        let h = |k| r.headers().get(k).map(|v: &HeaderValue| v.to_str().unwrap().to_string()).unwrap_or_default();
        let (cc, ce) = (h(header::CACHE_CONTROL), h(header::CONTENT_ENCODING));
        (cc, ce, axum::body::to_bytes(r.into_body(), usize::MAX).await.unwrap().to_vec())
    }

    #[tokio::test]
    async fn a_page_is_transformed_once_per_key_and_still_sent_as_one_never_kept() {
        let dir = std::env::temp_dir().join(format!("lean-ui-page-memo-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(dir.join("p.html"), "<html><head></head><body>".to_string() + &"x".repeat(3000) + "</body></html>").unwrap();
        let files = FileCache::default();
        let runs = Arc::new(AtomicUsize::new(0));
        let serve = |enc: &str, key: &str| {
            let runs = runs.clone();
            let k = key.to_string();
            let o = ServeOpts {
                transform: Some(Box::new(move |s: &str| {
                    runs.fetch_add(1, Ordering::SeqCst);
                    format!("{s}<!-- {k} -->")
                })),
                transform_key: None,
                page_key: Some(key.into()),
                cache: Some("public, max-age=31536000, immutable".into()),
            };
            files.serve_file(&req(enc), &dir, "p.html", o).unwrap()
        };
        let (cc, ce, plain) = body(serve("", "a")).await;
        assert_eq!((cc.as_str(), ce.as_str()), ("no-cache", ""), "a page is never kept by the browser");
        assert!(String::from_utf8(plain.clone()).unwrap().ends_with("<!-- a -->"));
        let (cc, ce, br) = body(serve("br", "a")).await;
        assert_eq!((cc.as_str(), ce.as_str()), ("no-cache", "br"));
        let mut un = Vec::new();
        brotli::BrotliDecompress(&mut &br[..], &mut un).unwrap();
        assert_eq!(un, plain);
        assert_eq!(body(serve("br", "a")).await.2, br);
        assert_eq!(runs.load(Ordering::SeqCst), 1, "made once for its key");
        assert!(String::from_utf8(body(serve("", "b")).await.2).unwrap().ends_with("<!-- b -->"));
        assert_eq!(runs.load(Ordering::SeqCst), 2, "another key: made again");
        // the file changed: made again
        std::thread::sleep(std::time::Duration::from_millis(5));
        std::fs::write(dir.join("p.html"), "<html><head></head><body>changed</body></html>").unwrap();
        assert_eq!(String::from_utf8(body(serve("", "a")).await.2).unwrap(), "<html><head></head><body>changed</body></html><!-- a -->");
        assert_eq!(runs.load(Ordering::SeqCst), 3);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn sent_text_is_compressed_once_per_etag() {
        let text = "y".repeat(5000);
        let r = send_text(&req("gzip"), &text, "text/plain", "W/\"memo-test\"", None);
        assert_eq!(r.headers().get(header::CONTENT_ENCODING).unwrap(), "gzip");
        let a = packed_text(&text, "W/\"memo-test\"", "gzip");
        assert_eq!(a, packed_text(&text, "W/\"memo-test\"", "gzip"));
        assert_eq!(a.as_ptr(), packed_text(&text, "W/\"memo-test\"", "gzip").as_ptr(), "the kept copy");
        // (a different text under that ETag is not answered with the kept one)
        assert_ne!(packed_text("z", "W/\"memo-test\"", "gzip"), a);
    }
}
