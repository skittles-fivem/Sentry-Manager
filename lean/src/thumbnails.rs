//! Recording card thumbnails made from a clip when Tesla saved no thumb.png
//! (a port of server/thumbnails.js): one frame of the front camera, one
//! second in (else the first frame of a very short clip), 320 pixels wide,
//! made once with ffmpeg run as a child process and cached in
//! DATA_PATH/thumbnails under the same name the Node server gives it.
//!
//! ffmpeg only reads the clip (`-protocol_whitelist file`, the clip as its
//! only input); what it writes goes to the cache, never next to the footage.
//! Without ffmpeg there is no thumbnail, as on the Node server.

use std::collections::HashSet;
use std::os::unix::fs::MetadataExt;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::{Arc, Condvar, Mutex, OnceLock};
use std::time::{Duration, Instant};

const TIMEOUT: Duration = Duration::from_secs(60);
/// How long a request waits for its place in line before it gives up (no thumbnail this time)
const WAIT_FOR_PLACE: Duration = Duration::from_secs(90);

/// How many ffmpeg runs make pictures at once: recording card thumbnails and clip strips share it.
/// Places are given in the order they were asked for.
pub struct Slots {
    line: Mutex<Line>,
    freed: Condvar,
    at_once: Box<dyn Fn() -> usize + Send + Sync>,
}

/// (places taken; the next ticket to give; the ticket that goes next; tickets given up)
#[derive(Default)]
struct Line {
    busy: usize,
    next: u64,
    serving: u64,
    left: HashSet<u64>,
}

impl Line {
    /// Past the tickets whose holders stopped waiting
    fn skip_left(&mut self) {
        while self.left.remove(&self.serving) {
            self.serving += 1;
        }
    }
}

/// A run's place; freed when dropped.
pub struct Slot<'a>(&'a Slots);

impl Slots {
    /// `at_once`: how many (at least one), asked whenever one would start.
    pub fn new(at_once: impl Fn() -> usize + Send + Sync + 'static) -> Arc<Slots> {
        Arc::new(Slots { line: Mutex::new(Line::default()), freed: Condvar::new(), at_once: Box::new(at_once) })
    }

    pub fn limit(&self) -> usize {
        (self.at_once)().max(1)
    }

    /// Wait for a free place, after everyone who asked before (a limit raised meanwhile is seen
    /// within half a second).
    pub fn take(&self) -> Slot<'_> {
        self.take_by(None).expect("no deadline")
    }

    /// take, or None once `deadline` passes: the place in line is given up, and those behind go on.
    pub fn take_until(&self, deadline: Instant) -> Option<Slot<'_>> {
        self.take_by(Some(deadline))
    }

    fn take_by(&self, deadline: Option<Instant>) -> Option<Slot<'_>> {
        let mut l = self.line.lock().unwrap();
        let me = l.next;
        l.next += 1;
        loop {
            l.skip_left();
            if l.serving == me && l.busy < self.limit() {
                break;
            }
            let now = Instant::now();
            if deadline.is_some_and(|d| now >= d) {
                l.left.insert(me);
                l.skip_left();
                drop(l);
                self.freed.notify_all();
                return None;
            }
            let wait = deadline.map_or(Duration::from_millis(500), |d| (d - now).min(Duration::from_millis(500)));
            l = self.freed.wait_timeout(l, wait).unwrap().0;
        }
        l.serving += 1;
        l.busy += 1;
        // (the next in line may go too, if there is room)
        self.freed.notify_all();
        Some(Slot(self))
    }
}

impl Drop for Slot<'_> {
    fn drop(&mut self) {
        self.0.line.lock().unwrap().busy -= 1;
        self.0.freed.notify_all();
    }
}

/// The server's: as many as the Viewer allows (`Viewer::thumbnails_at_once`), two before it starts.
pub fn server_slots() -> Arc<Slots> {
    static SLOTS: OnceLock<Arc<Slots>> = OnceLock::new();
    SLOTS.get_or_init(|| Slots::new(|| crate::viewer::get().map_or(2, |v| v.thumbnails_at_once()))).clone()
}

/// Cached pictures kept at most in a folder (the newest)
pub const KEPT: usize = 20_000;
/// A prune every so many pictures written
pub const PRUNE_EVERY: usize = 500;
/// A leftover work file (its run ended with the process) goes once it is this old
const STALE_TMP: Duration = Duration::from_secs(10 * 60);

/// Keep the newest `keep` pictures (*.jpg) in `dir`, by time; work files (*.tmp.jpg) are not
/// counted, and go once they are older than any run can be.
pub fn prune(dir: &Path, keep: usize) {
    let Ok(list) = std::fs::read_dir(dir) else { return };
    let now = std::time::SystemTime::now();
    let mut pictures: Vec<(std::time::SystemTime, PathBuf)> = Vec::new();
    for e in list.flatten() {
        let name = e.file_name().to_string_lossy().into_owned();
        if !name.ends_with(".jpg") {
            continue;
        }
        let Ok(m) = e.metadata() else { continue };
        if !m.is_file() {
            continue;
        }
        let at = m.modified().unwrap_or(std::time::UNIX_EPOCH);
        if name.ends_with(".tmp.jpg") {
            if now.duration_since(at).is_ok_and(|age| age > STALE_TMP) {
                let _ = std::fs::remove_file(e.path());
            }
            continue;
        }
        pictures.push((at, e.path()));
    }
    if pictures.len() > keep {
        pictures.sort_by(|a, b| a.0.cmp(&b.0));
        let n = pictures.len() - keep;
        for (_, p) in pictures.into_iter().take(n) {
            let _ = std::fs::remove_file(p);
        }
    }
}

pub struct Thumbnails {
    pub dir: PathBuf,
    /// pictures written (a prune with the first, then every PRUNE_EVERY)
    written: std::sync::atomic::AtomicUsize,
    keep: usize,
    ffmpeg: String,
    /// clips being made now (by their cache file)
    busy: Mutex<HashSet<PathBuf>>,
    changed: Condvar,
    slots: Arc<Slots>,
}

/// Run a command without a shell; its exit code, -1 when it could not start or ran too long (proc.js run).
fn run(cmd: &str, args: &[&str]) -> i32 {
    let Ok(mut child) = Command::new(cmd).args(args).stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null()).spawn() else { return -1 };
    let started = Instant::now();
    loop {
        match child.try_wait() {
            Ok(Some(status)) => return status.code().unwrap_or(-1),
            Ok(None) if started.elapsed() < TIMEOUT => std::thread::sleep(Duration::from_millis(20)),
            _ => {
                let _ = child.kill();
                let _ = child.wait();
                return -1;
            }
        }
    }
}

impl Thumbnails {
    pub fn new(dir: PathBuf, ffmpeg: String, concurrency: usize) -> Thumbnails {
        Thumbnails::with_slots(dir, ffmpeg, Slots::new(move || concurrency))
    }

    /// Runs take their place in `slots` (shared with the clip strips in the server).
    pub fn with_slots(dir: PathBuf, ffmpeg: String, slots: Arc<Slots>) -> Thumbnails {
        Thumbnails { dir, written: Default::default(), keep: KEPT, ffmpeg, busy: Mutex::new(HashSet::new()), changed: Condvar::new(), slots }
    }

    /// The cache file of a clip as it is now: sha1 of "real path|size|floor(mtimeMs)", as thumbnails.js names it.
    pub fn key(real: &Path, md: &std::fs::Metadata) -> String {
        let mtime = crate::scanner::ms_of(md.mtime(), md.mtime_nsec()).floor();
        let mut h = sha1_smol::Sha1::new();
        h.update(format!("{}|{}|{}", real.to_string_lossy(), md.size(), mtime as i64).as_bytes());
        h.digest().to_string()
    }

    /// A JPEG of this clip (made if needed); Ok(None) when ffmpeg cannot make one (or is not
    /// there); Err when ffmpeg ended well but wrote nothing (thumbnails.js: its rename
    /// throws, an internal error). `real`: the clip's checked real path.
    pub fn for_clip(&self, real: &Path) -> Result<Option<PathBuf>, String> {
        let Ok(md) = std::fs::metadata(real) else { return Ok(None) };
        let out = self.dir.join(format!("{}.jpg", Self::key(real, &md)));
        if out.exists() {
            return Ok(Some(out));
        }
        // one run per clip, in a place of `slots`
        {
            let mut b = self.busy.lock().unwrap();
            if b.contains(&out) {
                // (being made for another request: its answer, or none if that takes too long)
                let until = Instant::now() + WAIT_FOR_PLACE + 2 * TIMEOUT;
                while b.contains(&out) {
                    let now = Instant::now();
                    if now >= until {
                        return Ok(None);
                    }
                    b = self.changed.wait_timeout(b, until - now).unwrap().0;
                }
                return Ok(out.exists().then_some(out));
            }
            b.insert(out.clone());
        }
        // (a request waits so long for its place, then has none this time: a page of cards on a
        // slow share cannot hold the server's threads for ever)
        let made = match self.slots.take_until(Instant::now() + WAIT_FOR_PLACE) {
            Some(_place) => self.make(real, &out),
            None => Ok(false),
        };
        self.busy.lock().unwrap().remove(&out);
        self.changed.notify_all();
        made.map(|ok| ok.then_some(out))
    }

    fn make(&self, real: &Path, out: &Path) -> Result<bool, String> {
        std::fs::create_dir_all(&self.dir).map_err(|e| e.to_string())?;
        let tmp = PathBuf::from(format!("{}.{}.tmp.jpg", out.to_string_lossy(), std::process::id()));
        let (clip, to) = (real.to_string_lossy(), tmp.to_string_lossy());
        let tail = ["-frames:v", "1", "-vf", "scale=320:-2", "-q:v", "5", &to];
        let head = ["-hide_banner", "-loglevel", "error", "-nostdin", "-y"];
        let input = ["-protocol_whitelist", "file", "-i", &clip];
        let seek: Vec<&str> = head.iter().copied().chain(["-ss", "1"]).chain(input).chain(tail).collect();
        if run(&self.ffmpeg, &seek) != 0 {
            // very short clip: the first frame instead
            let first: Vec<&str> = head.iter().copied().chain(input).chain(tail).collect();
            if run(&self.ffmpeg, &first) != 0 {
                let _ = std::fs::remove_file(&tmp);
                return Ok(false);
            }
        }
        // (ffmpeg can end well and write nothing: a seek past the end of a clip under a second)
        std::fs::rename(&tmp, out).map_err(|e| e.to_string())?;
        if self.written.fetch_add(1, std::sync::atomic::Ordering::Relaxed) % PRUNE_EVERY == 0 {
            prune(&self.dir, self.keep);
        }
        Ok(true)
    }
}

/// Stand-in tools for tests: they note when each run starts and ends.
#[cfg(test)]
pub(crate) mod fake {
    use std::os::unix::fs::PermissionsExt;
    use std::path::Path;

    fn script(path: &Path, body: &str) -> String {
        std::fs::write(path, format!("#!/bin/sh\n{body}")).unwrap();
        std::fs::set_permissions(path, std::fs::Permissions::from_mode(0o755)).unwrap();
        path.to_string_lossy().into_owned()
    }

    /// An ffmpeg that writes a byte to its last argument (the output), a third of a second long.
    pub fn ffmpeg(dir: &Path, log: &Path) -> String {
        script(&dir.join("ffmpeg"), &format!("for a; do last=$a; done\necho start >> '{0}'\nsleep 0.3\necho x > \"$last\"\necho end >> '{0}'\n", log.display()))
    }

    /// An ffprobe that reports a ten-second clip.
    pub fn ffprobe(dir: &Path) -> String {
        script(&dir.join("ffprobe"), "echo 10.0\n")
    }

    /// The most runs at once in a log.
    pub fn most_at_once(log: &Path) -> usize {
        let (mut now, mut most) = (0i64, 0i64);
        for l in std::fs::read_to_string(log).unwrap_or_default().lines() {
            now += if l == "start" { 1 } else { -1 };
            most = most.max(now);
        }
        most as usize
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};

    #[test]
    fn one_who_stops_waiting_gives_up_the_place_and_the_line_goes_on() {
        let slots = Slots::new(|| 1);
        let held = slots.take();
        let began = Instant::now();
        assert!(slots.take_until(Instant::now() + Duration::from_millis(200)).is_none());
        assert!(began.elapsed() < Duration::from_secs(3));
        let s = slots.clone();
        let next = std::thread::spawn(move || s.take_until(Instant::now() + Duration::from_secs(10)).is_some());
        std::thread::sleep(Duration::from_millis(50));
        drop(held);
        assert!(next.join().unwrap(), "the ticket given up is skipped");
        let _again = slots.take();
    }

    #[test]
    fn a_folder_of_pictures_keeps_the_newest_and_no_old_work_files() {
        let dir = std::env::temp_dir().join(format!("lean-thumb-prune-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let now = std::time::SystemTime::now();
        let put = |name: &str, age: u64| {
            let p = dir.join(name);
            std::fs::write(&p, b"x").unwrap();
            std::fs::File::options().write(true).open(&p).unwrap().set_modified(now - Duration::from_secs(age)).unwrap();
        };
        for (i, n) in ["a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg"].iter().enumerate() {
            put(n, 1000 - i as u64 * 100);
        }
        put("old.jpg.1.tmp.jpg", 3600);
        put("new.jpg.2.tmp.jpg", 5);
        put("notes.txt", 9999);
        prune(&dir, 3);
        let mut left: Vec<String> = std::fs::read_dir(&dir).unwrap().flatten().map(|e| e.file_name().to_string_lossy().into_owned()).collect();
        left.sort();
        assert_eq!(left, ["c.jpg", "d.jpg", "e.jpg", "new.jpg.2.tmp.jpg", "notes.txt"]);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_place_goes_to_who_asked_first_even_when_another_frees_and_asks_again_at_once() {
        use std::sync::atomic::AtomicBool;
        let slots = Slots::new(|| 1);
        let mut held = Some(slots.take());
        let (round, got) = (Arc::new(AtomicUsize::new(0)), Arc::new(AtomicBool::new(false)));
        let waiting = {
            let (s, round, got) = (slots.clone(), round.clone(), got.clone());
            std::thread::spawn(move || {
                let _place = s.take();
                got.store(true, Ordering::SeqCst);
                round.load(Ordering::SeqCst)
            })
        };
        std::thread::sleep(Duration::from_millis(100));
        // (this thread frees its place and asks again at once, twenty times)
        for i in 1..=20 {
            round.store(i, Ordering::SeqCst);
            drop(held.take());
            held = Some(slots.take());
        }
        drop(held);
        assert!(got.load(Ordering::SeqCst));
        assert_eq!(waiting.join().unwrap(), 1, "the one waiting first got the first place freed");
    }

    #[test]
    fn thumbnails_follow_the_limit_asked_each_time_one_would_start() {
        let dir = std::env::temp_dir().join(format!("lean-thumbs-limit-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let log = dir.join("runs.log");
        let limit = Arc::new(AtomicUsize::new(1));
        let l = limit.clone();
        let thumbs = Arc::new(Thumbnails::with_slots(dir.join("cache"), fake::ffmpeg(&dir, &log), Slots::new(move || l.load(Ordering::SeqCst))));
        let make = |names: &[&str]| {
            let runs: Vec<_> = names
                .iter()
                .map(|n| {
                    let clip = dir.join(n);
                    std::fs::write(&clip, n.as_bytes()).unwrap();
                    let t = thumbs.clone();
                    std::thread::spawn(move || t.for_clip(&clip).unwrap().is_some())
                })
                .collect();
            runs.into_iter().all(|r| r.join().unwrap())
        };
        assert!(make(&["a.mp4", "b.mp4", "c.mp4"]));
        assert_eq!(fake::most_at_once(&log), 1);
        std::fs::remove_file(&log).unwrap();
        limit.store(3, Ordering::SeqCst);
        assert!(make(&["d.mp4", "e.mp4", "f.mp4"]));
        assert!(fake::most_at_once(&log) >= 2, "a raised limit runs more at once");
        let _ = std::fs::remove_dir_all(&dir);
    }
}
