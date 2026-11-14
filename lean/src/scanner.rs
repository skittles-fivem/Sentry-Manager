//! Walk a footage root (a port of server/scanner.js): directory listings
//! and one stat per file, never opening a video. Network shares make every
//! call a round trip, so folders and batches of stats run on a few threads.
//!
//! A light rescan gets the previous scan's files and folder times: a folder
//! whose modified time is unchanged keeps what it knew of its files (except
//! files changed in the last two hours: one still being copied grows
//! without touching its folder), so a quiet share costs one listing per
//! folder instead of a stat per file.

use crate::footage::{self, Class};
use std::collections::{HashMap, VecDeque};
use std::os::unix::fs::MetadataExt;
use std::path::Path;
use std::sync::{Condvar, Mutex};
use std::time::{SystemTime, UNIX_EPOCH};

const RECENT_MS: f64 = 2.0 * 3600.0 * 1000.0;
const STAT_BATCH: usize = 64;

#[derive(Debug, Clone)]
pub struct FileEntry {
    pub rel: String,
    pub size: u64,
    pub mtime_ms: f64,
    pub ctime_ms: f64,
    pub kind: footage::Kind,
    pub typ: &'static str,
    pub day: String,
    pub camera: Option<String>,
    pub event_id: Option<String>,
}

#[derive(Default)]
pub struct Previous {
    pub files: HashMap<String, FileEntry>,
    pub dirs: HashMap<String, f64>,
}

pub struct ScanResult {
    pub files: Vec<FileEntry>,
    pub dirs: HashMap<String, f64>,
    pub errors: Vec<(String, String)>,
    pub reused: usize,
    pub scanned_at: f64,
    pub duration_ms: f64,
}

pub fn now_ms() -> f64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_millis() as f64).unwrap_or(0.0)
}

/// Node's stat time in ms: seconds * 1000 + nanoseconds / 1e6, in doubles.
pub fn ms_of(sec: i64, nsec: i64) -> f64 {
    sec as f64 * 1000.0 + nsec as f64 / 1e6
}

enum Task {
    Dir(String),
    Stat(Vec<(String, Class)>),
}

struct Queue {
    tasks: VecDeque<Task>,
    busy: usize,
}

#[derive(Default)]
struct Out {
    files: Vec<FileEntry>,
    dirs: HashMap<String, f64>,
    errors: Vec<(String, String)>,
    reused: usize,
}

fn entry(rel: String, md: &std::fs::Metadata, c: Class) -> FileEntry {
    let mtime_ms = ms_of(md.mtime(), md.mtime_nsec());
    let day = c.day.unwrap_or_else(|| footage::local_day(mtime_ms));
    FileEntry { rel, size: md.size(), mtime_ms, ctime_ms: ms_of(md.ctime(), md.ctime_nsec()), kind: c.kind, typ: c.typ, day, camera: c.camera, event_id: c.event_id }
}

pub fn scan(root: &Path, previous: Option<&Previous>, concurrency: usize) -> ScanResult {
    let started = now_ms();
    let queue = Mutex::new(Queue { tasks: VecDeque::from([Task::Dir(String::new())]), busy: 0 });
    let wake = Condvar::new();
    let out = Mutex::new(Out::default());

    let visit_dir = |rel: String, more: &mut Vec<Task>| {
        let abs = root.join(&rel);
        let mtime = std::fs::metadata(&abs).ok().map(|m| ms_of(m.mtime(), m.mtime_nsec()));
        let list = match std::fs::read_dir(&abs) {
            Ok(l) => l,
            Err(e) => {
                out.lock().unwrap().errors.push((if rel.is_empty() { ".".into() } else { rel }, e.to_string()));
                return;
            }
        };
        if let Some(m) = mtime {
            out.lock().unwrap().dirs.insert(rel.clone(), m);
        }
        let unchanged = previous.filter(|p| mtime.is_some() && p.dirs.get(&rel) == mtime.as_ref());
        let mut batch = Vec::new();
        let mut kept = Vec::new();
        for ent in list.flatten() {
            let Ok(name) = ent.file_name().into_string() else { continue };
            let child = if rel.is_empty() { name.clone() } else { format!("{rel}/{name}") };
            let Ok(ft) = ent.file_type() else { continue };
            if ft.is_symlink() {
                continue;
            }
            if ft.is_dir() {
                if !footage::should_skip_dir(&name) {
                    more.push(Task::Dir(child));
                }
                continue;
            }
            if !ft.is_file() || footage::is_converter_artifact(&name) {
                continue;
            }
            let Some(class) = footage::classify(&child) else { continue };
            if let Some(known) = unchanged.and_then(|p| p.files.get(&child)) {
                if started - known.mtime_ms > RECENT_MS {
                    kept.push(known.clone());
                    continue;
                }
            }
            batch.push((child, class));
            if batch.len() == STAT_BATCH {
                more.push(Task::Stat(std::mem::take(&mut batch)));
            }
        }
        if !batch.is_empty() {
            more.push(Task::Stat(batch));
        }
        if !kept.is_empty() {
            let mut o = out.lock().unwrap();
            o.reused += kept.len();
            o.files.extend(kept);
        }
    };
    let stat_files = |batch: Vec<(String, Class)>| {
        let mut done = Vec::with_capacity(batch.len());
        let mut errors = Vec::new();
        for (rel, class) in batch {
            match std::fs::metadata(root.join(&rel)) {
                Ok(md) => done.push(entry(rel, &md, class)),
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
                Err(e) => errors.push((rel, e.to_string())),
            }
        }
        let mut o = out.lock().unwrap();
        o.files.extend(done);
        o.errors.extend(errors);
    };

    std::thread::scope(|s| {
        for _ in 0..concurrency.max(1) {
            s.spawn(|| loop {
                let task = {
                    let mut q = queue.lock().unwrap();
                    loop {
                        if let Some(t) = q.tasks.pop_front() {
                            q.busy += 1;
                            break Some(t);
                        }
                        if q.busy == 0 {
                            break None;
                        }
                        q = wake.wait(q).unwrap();
                    }
                };
                let Some(task) = task else {
                    wake.notify_all();
                    return;
                };
                let mut more = Vec::new();
                match task {
                    Task::Dir(rel) => visit_dir(rel, &mut more),
                    Task::Stat(b) => stat_files(b),
                }
                let mut q = queue.lock().unwrap();
                q.tasks.extend(more);
                q.busy -= 1;
                wake.notify_all();
            });
        }
    });

    let o = out.into_inner().unwrap();
    let mut files = o.files;
    files.sort_by(|a, b| a.rel.cmp(&b.rel));
    ScanResult { files, dirs: o.dirs, errors: o.errors, reused: o.reused, scanned_at: now_ms(), duration_ms: now_ms() - started }
}
