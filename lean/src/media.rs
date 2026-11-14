//! Files over HTTP: footage with Range requests (server/studioApi.js
//! sendMedia: at most 1 MiB an answer, so six cameras never hog a
//! connection) and the stock UI's static files as they are (server/http.js
//! serveFile, without compression or page rewriting). Footage is opened
//! read-only, never through a symlink, and checked to be inside a vehicle's
//! folder through the open descriptor.

use axum::body::Body;
use axum::http::{header, HeaderMap, HeaderValue, Method, Response, StatusCode};
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::os::unix::fs::{MetadataExt, OpenOptionsExt};
use std::path::{Path, PathBuf};

pub const MEDIA_CHUNK: u64 = 1024 * 1024;

fn media_type(p: &Path) -> &'static str {
    match p.extension().map(|e| e.to_string_lossy().to_lowercase()).as_deref() {
        Some("mp4") => "video/mp4",
        Some("mov") => "video/quicktime",
        Some("mkv") => "video/x-matroska",
        Some("avi") => "video/x-msvideo",
        Some("png") => "image/png",
        Some("jpg" | "jpeg") => "image/jpeg",
        Some("json") => "application/json",
        Some("txt" | "ass") => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    }
}

fn static_type(p: &Path) -> &'static str {
    match p.extension().map(|e| e.to_string_lossy().to_lowercase()).as_deref() {
        Some("html") => "text/html; charset=utf-8",
        Some("js" | "mjs" | "cjs") => "text/javascript; charset=utf-8",
        Some("css") => "text/css; charset=utf-8",
        Some("json") => "application/json; charset=utf-8",
        Some("png") => "image/png",
        Some("jpg" | "jpeg") => "image/jpeg",
        Some("svg") => "image/svg+xml",
        Some("ico") => "image/x-icon",
        Some("woff2") => "font/woff2",
        Some("woff") => "font/woff",
        Some("ttf") => "font/ttf",
        Some("proto" | "txt" | "md") => "text/plain; charset=utf-8",
        Some("map") => "application/json",
        Some("gif") => "image/gif",
        Some("webp") => "image/webp",
        _ => "application/octet-stream",
    }
}

pub fn base36(mut n: u64) -> String {
    if n == 0 {
        return "0".into();
    }
    let mut s = Vec::new();
    while n > 0 {
        s.push(b"0123456789abcdefghijklmnopqrstuvwxyz"[(n % 36) as usize]);
        n /= 36;
    }
    s.reverse();
    String::from_utf8(s).unwrap()
}

/// Paths the viewer must never read: the Recycle Bin and binned files.
pub fn hidden_path(p: &str) -> bool {
    p.contains("/.sentry-recycle-bin/") || p.ends_with("/.sentry-recycle-bin") || p.ends_with(".binned")
}

/// The real path of p when it lies inside one of the roots (after symlinks), else None.
pub fn resolve_inside(p: &str, roots: &[PathBuf]) -> Option<PathBuf> {
    if p.is_empty() || p.contains('\0') || !p.starts_with('/') {
        return None;
    }
    let real = std::fs::canonicalize(p).ok()?;
    roots.iter().filter_map(|r| std::fs::canonicalize(r).ok()).any(|r| real.starts_with(&r)).then_some(real)
}

fn empty(status: StatusCode) -> Response<Body> {
    Response::builder().status(status).body(Body::empty()).unwrap()
}

/// Open a regular file read-only, not through a final symlink, and check where the descriptor really is.
fn open_confined(abs: &Path, roots: &[PathBuf]) -> Option<(File, std::fs::Metadata)> {
    let f = std::fs::OpenOptions::new().read(true).custom_flags(libc::O_NOFOLLOW).open(abs).ok()?;
    let md = f.metadata().ok()?;
    if !md.is_file() {
        return None;
    }
    use std::os::fd::AsRawFd;
    let real = std::fs::read_link(format!("/proc/self/fd/{}", f.as_raw_fd())).ok()?;
    let inside = roots.iter().filter_map(|r| std::fs::canonicalize(r).ok()).any(|r| real.starts_with(&r));
    inside.then_some((f, md))
}

/// A footage file with Range support (studioApi.js sendMedia).
pub fn send_media(method: &Method, headers: &HeaderMap, abs: &Path, roots: &[PathBuf]) -> Response<Body> {
    let Some((mut file, md)) = open_confined(abs, roots) else { return empty(StatusCode::NOT_FOUND) };
    let size = md.size();
    let mtime_ms = crate::scanner::ms_of(md.mtime(), md.mtime_nsec());
    let etag = format!("\"{}-{}-{}\"", base36(size), base36(mtime_ms.floor() as u64), base36(md.ino()));
    let mut start = 0u64;
    let mut end = size.saturating_sub(1);
    let mut status = StatusCode::OK;
    let mut b = Response::builder()
        .header(header::CONTENT_TYPE, media_type(abs))
        .header(header::ACCEPT_RANGES, "bytes")
        .header(header::ETAG, &etag)
        .header(header::CACHE_CONTROL, "no-cache")
        .header("X-Content-Type-Options", "nosniff");
    let range = headers.get(header::RANGE).and_then(|v| v.to_str().ok());
    let if_range = headers.get(header::IF_RANGE).and_then(|v| v.to_str().ok());
    if let Some(range) = range.filter(|_| if_range.map_or(true, |i| i == etag)) {
        let bad = || Response::builder().status(StatusCode::RANGE_NOT_SATISFIABLE).header(header::CONTENT_RANGE, format!("bytes */{size}")).body(Body::empty()).unwrap();
        let Some(spec) = range.strip_prefix("bytes=") else { return bad() };
        let Some((a, z)) = spec.split_once('-') else { return bad() };
        let digits = |s: &str| s.bytes().all(|c| c.is_ascii_digit());
        if !digits(a) || !digits(z) || (a.is_empty() && z.is_empty()) {
            return bad();
        }
        // (Number() of a long digit string: a double)
        let n = |s: &str| s.parse::<f64>().unwrap_or(f64::INFINITY);
        let mut e = end as f64;
        let s = if a.is_empty() {
            (size as f64 - n(z)).max(0.0)
        } else {
            if !z.is_empty() {
                e = e.min(n(z));
            }
            n(a)
        };
        if s > e || s >= size as f64 {
            return bad();
        }
        start = s as u64;
        end = (e as u64).min(start + MEDIA_CHUNK - 1);
        status = StatusCode::PARTIAL_CONTENT;
        b = b.header(header::CONTENT_RANGE, format!("bytes {start}-{end}/{size}"));
    }
    let len = if size == 0 { 0 } else { end - start + 1 };
    b = b.status(status).header(header::CONTENT_LENGTH, len);
    if method == Method::HEAD || size == 0 {
        return b.body(Body::empty()).unwrap();
    }
    if file.seek(SeekFrom::Start(start)).is_err() {
        return empty(StatusCode::INTERNAL_SERVER_ERROR);
    }
    if len <= MEDIA_CHUNK {
        let mut buf = vec![0u8; len as usize];
        if file.read_exact(&mut buf).is_err() {
            return empty(StatusCode::INTERNAL_SERVER_ERROR);
        }
        return b.body(Body::from(buf)).unwrap();
    }
    // the whole of a big file: streamed from the descriptor that was checked
    let reader = tokio::io::AsyncReadExt::take(tokio::fs::File::from_std(file), len);
    b.body(Body::from_stream(tokio_util::io::ReaderStream::new(reader))).unwrap()
}

/// A static file of the app (http.js serveFile); None when there is none.
pub fn serve_static(method: &Method, headers: &HeaderMap, root: &Path, rel: &str, cache: &str) -> Option<Response<Body>> {
    if rel.contains('\0') || rel.split('/').any(|s| s == "..") {
        return None;
    }
    let abs = root.join(rel.trim_start_matches('/'));
    if !abs.starts_with(root) {
        return None;
    }
    let md = std::fs::symlink_metadata(&abs).ok()?;
    if !md.is_file() {
        return None;
    }
    let mtime = crate::sei::js_round(crate::scanner::ms_of(md.mtime(), md.mtime_nsec())) as u64;
    let etag = format!("W/\"{}-{}\"", base36(md.size()), base36(mtime));
    let b = Response::builder()
        .header(header::CONTENT_TYPE, static_type(&abs))
        .header(header::CACHE_CONTROL, cache)
        .header(header::ETAG, &etag)
        .header("X-Content-Type-Options", "nosniff");
    let none_match = headers.get(header::IF_NONE_MATCH).and_then(|v| v.to_str().ok()).unwrap_or("");
    if none_match.split(',').any(|t| t.trim() == etag) {
        return Some(b.status(StatusCode::NOT_MODIFIED).body(Body::empty()).unwrap());
    }
    let data = std::fs::read(&abs).ok()?;
    let b = b.header(header::CONTENT_LENGTH, HeaderValue::from(data.len()));
    Some(b.status(StatusCode::OK).body(if method == Method::HEAD { Body::empty() } else { Body::from(data) }).unwrap())
}
