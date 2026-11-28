//! The footage catalog (server/catalog.js) in the manager's database: every
//! file of the last scan (`catalog_files`), the folders' modified times
//! (`catalog_dirs`, for light rescans), the last finished scan
//! (`catalog_scans`: the first scan after a restart starts from it) and each
//! clip's codec (`clip_codecs`). The same tables, columns and FORMAT as Node,
//! so either server resumes from the other's last scan. Only the manager's
//! own data folder is written; the footage is only ever listed and read.
//!
//! It has its own connection to manager.db (WAL: the scan's writes do not
//! hold up core's), and writes in batches as catalog.js does.

use crate::footage::Kind;
use crate::scanner::{FileEntry, Previous, ScanResult};
use rusqlite::{params, Connection, OptionalExtension};
use std::collections::{HashMap, HashSet};
use std::path::Path;
use std::sync::{Arc, Mutex};

pub struct Catalog {
    db: Mutex<Connection>,
}

/// catalog.js FORMAT: what a scan row vouches for (Node's rows and these are the same).
const FORMAT: i64 = 1;
/// rows per transaction (catalog.js BATCH)
pub const BATCH: usize = 1000;

const UPSERT_CODEC: &str = "INSERT INTO clip_codecs (vehicle_id, rel, size, mtime_ms, codec) VALUES (?1, ?2, ?3, ?4, ?5)
             ON CONFLICT(vehicle_id, rel) DO UPDATE SET size = excluded.size, mtime_ms = excluded.mtime_ms, codec = excluded.codec";

const INSERT_MISSING_CODEC: &str = "INSERT INTO clip_codecs (vehicle_id, rel, size, mtime_ms, codec) VALUES (?1, ?2, ?3, ?4, ?5)
             ON CONFLICT(vehicle_id, rel) DO NOTHING";

const UPSERT_FILE: &str = "INSERT INTO catalog_files (vehicle_id, rel, size, mtime_ms, ctime_ms, birthtime_ms, kind, type, day, camera, event_id)
      VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11)
      ON CONFLICT(vehicle_id, rel) DO UPDATE SET size = excluded.size, mtime_ms = excluded.mtime_ms, ctime_ms = excluded.ctime_ms,
        birthtime_ms = excluded.birthtime_ms, kind = excluded.kind, type = excluded.type, day = excluded.day, camera = excluded.camera,
        event_id = excluded.event_id";

pub type CodecMap = HashMap<String, (u64, f64, Option<String>)>;

fn type_of(s: &str) -> &'static str {
    match s {
        "RecentClips" => "RecentClips",
        "SentryClips" => "SentryClips",
        "SavedClips" => "SavedClips",
        _ => "Other",
    }
}

struct LastScan {
    format: i64,
    root: String,
    started_ms: f64,
    scanned_at: f64,
    files: i64,
    bytes: i64,
}

impl Catalog {
    /// `path`: DATA_PATH/manager.db (the tables are made as catalog.js makes them when missing).
    pub fn open(path: &Path) -> rusqlite::Result<Catalog> {
        let db = Connection::open(path)?;
        db.pragma_update(None, "journal_mode", "WAL")?;
        db.pragma_update(None, "synchronous", "NORMAL")?;
        db.busy_timeout(std::time::Duration::from_secs(10))?;
        // a small page cache: the catalog is read in a few big queries
        db.pragma_update(None, "cache_size", -2000)?;
        db.execute_batch(crate::core::db::CATALOG_SCHEMA)?;
        Ok(Catalog { db: Mutex::new(db) })
    }

    fn conn(&self) -> std::sync::MutexGuard<'_, Connection> {
        self.db.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// catalog.js each: the list in transactions of BATCH rows.
    fn each<T>(&self, list: &[T], sql: &str, f: impl Fn(&mut rusqlite::Statement, &T) -> rusqlite::Result<()>) -> rusqlite::Result<()> {
        for part in list.chunks(BATCH) {
            let mut c = self.conn();
            let tx = c.transaction()?;
            {
                let mut st = tx.prepare_cached(sql)?;
                for x in part {
                    f(&mut st, x)?;
                }
            }
            tx.commit()?;
        }
        Ok(())
    }

    fn put_files(&self, vehicle: &str, files: &[&FileEntry]) -> rusqlite::Result<()> {
        self.each(files, UPSERT_FILE, |st, f| {
            st.execute(params![vehicle, f.rel, f.size as i64, f.mtime_ms, f.ctime_ms, f.birthtime_ms, f.kind.as_str(), f.typ, f.day, f.camera, f.event_id]).map(|_| ())
        })
    }

    fn remove_files(&self, vehicle: &str, rels: &[String]) -> rusqlite::Result<()> {
        self.each(rels, "DELETE FROM catalog_files WHERE vehicle_id = ?1 AND rel = ?2", |st, rel| st.execute(params![vehicle, rel]).map(|_| ()))
    }

    fn rels(&self, vehicle: &str) -> rusqlite::Result<HashSet<String>> {
        let c = self.conn();
        let mut q = c.prepare("SELECT rel FROM catalog_files WHERE vehicle_id = ?1")?;
        let rows = q.query_map([vehicle], |r| r.get::<_, String>(0))?;
        rows.collect()
    }

    fn invalidate(&self, vehicle: &str) -> rusqlite::Result<()> {
        self.conn().execute("DELETE FROM catalog_scans WHERE vehicle_id = ?1", [vehicle]).map(|_| ())
    }

    fn totals(&self, vehicle: &str) -> rusqlite::Result<(i64, i64)> {
        self.conn().query_row("SELECT COUNT(*), COALESCE(SUM(size), 0) FROM catalog_files WHERE vehicle_id = ?1", [vehicle], |r| Ok((r.get(0)?, r.get(1)?)))
    }

    fn mark_scan(&self, vehicle: &str, root: &str, started_ms: f64, scanned_at: f64) -> rusqlite::Result<()> {
        let (files, bytes) = self.totals(vehicle)?;
        self.conn().execute(
            "INSERT INTO catalog_scans (vehicle_id, format, root, started_ms, scanned_at, files, bytes) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)
             ON CONFLICT(vehicle_id) DO UPDATE SET format = excluded.format, root = excluded.root, started_ms = excluded.started_ms,
               scanned_at = excluded.scanned_at, files = excluded.files, bytes = excluded.bytes",
            params![vehicle, FORMAT, root, started_ms, scanned_at, files, bytes],
        )?;
        Ok(())
    }

    /// previous(), when the catalog's last scan was of `root` (in this format); else none.
    pub fn previous_for(&self, vehicle: &str, root: &str) -> rusqlite::Result<Option<Previous>> {
        match self.last_scan(vehicle)? {
            Some(l) if l.format == FORMAT && l.root == root => self.previous(vehicle).map(Some),
            _ => Ok(None),
        }
    }

    /// The last scan of `root` found footage there.
    pub fn saw_footage(&self, vehicle: &str, root: &str) -> rusqlite::Result<bool> {
        Ok(self.last_scan(vehicle)?.is_some_and(|l| l.root == root && l.files > 0))
    }

    fn last_scan(&self, vehicle: &str) -> rusqlite::Result<Option<LastScan>> {
        self.conn()
            .query_row("SELECT format, root, started_ms, scanned_at, files, bytes FROM catalog_scans WHERE vehicle_id = ?1", [vehicle], |r| {
                Ok(LastScan { format: r.get(0)?, root: r.get(1)?, started_ms: r.get(2)?, scanned_at: r.get(3)?, files: r.get(4)?, bytes: r.get(5)? })
            })
            .optional()
    }

    /// Every file and folder the catalog has for a vehicle.
    pub fn previous(&self, vehicle: &str) -> rusqlite::Result<Previous> {
        let c = self.conn();
        let mut prev = Previous::default();
        let mut q = c.prepare("SELECT rel, size, mtime_ms, ctime_ms, kind, type, day, camera, event_id, birthtime_ms FROM catalog_files WHERE vehicle_id = ?1")?;
        let rows = q.query_map([vehicle], |r| {
            Ok(FileEntry {
                rel: r.get(0)?,
                size: r.get::<_, i64>(1)? as u64,
                mtime_ms: r.get(2)?,
                ctime_ms: r.get::<_, Option<f64>>(3)?.unwrap_or(0.0),
                birthtime_ms: r.get::<_, Option<f64>>(9)?.unwrap_or(0.0),
                kind: if r.get::<_, String>(4)? == "clip" { Kind::Clip } else { Kind::Asset },
                typ: type_of(&r.get::<_, Option<String>>(5)?.unwrap_or_default()),
                day: r.get::<_, Option<String>>(6)?.unwrap_or_default(),
                camera: r.get(7)?,
                event_id: r.get(8)?,
            })
        })?;
        prev.files = Arc::new(rows.collect::<rusqlite::Result<Vec<_>>>()?.into());
        prev.dirs = dirs_of(&c, vehicle)?;
        Ok(prev)
    }

    /// What the last finished scan of this root knew, for the first scan after a restart
    /// (catalog.js resume): None for another root or format, or rows that do not add up to that scan.
    pub fn resume(&self, vehicle: &str, root: &str) -> rusqlite::Result<Option<Previous>> {
        let Some(last) = self.last_scan(vehicle)? else { return Ok(None) };
        if last.format != FORMAT || last.root != root {
            return Ok(None);
        }
        if self.totals(vehicle)? != (last.files, last.bytes) {
            return Ok(None);
        }
        let mut prev = self.previous(vehicle)?;
        if prev.dirs.is_empty() {
            return Ok(None);
        }
        prev.started_ms = Some(last.started_ms);
        Ok(Some(prev))
    }

    /// A finished scan of `root`: its files (only what changed is written), its folders
    /// and the scan row, written last (inventory.js and catalog.js putScan).
    pub fn save_scan(&self, vehicle: &str, root: &str, scan: &ScanResult) -> rusqlite::Result<()> {
        // (the scan row goes first and comes back last: a write that stops halfway leaves none)
        self.invalidate(vehicle)?;
        let (gone, changed, known_dirs) = self.changes(vehicle, &scan.files)?;
        self.remove_files(vehicle, &gone)?;
        self.put_files(vehicle, &changed)?;
        // (the folders' rows as the scan has them: only the ones that differ are written; a row
        // the same to the bit as the scan's time is left as it is)
        let old_dirs: Vec<&String> = known_dirs.keys().filter(|r| !scan.dirs.contains_key(*r)).collect();
        let new_dirs: Vec<(&String, &f64)> = scan.dirs.iter().filter(|(rel, m)| known_dirs.get(*rel).is_none_or(|k| k.to_bits() != m.to_bits())).collect();
        self.each(&old_dirs, "DELETE FROM catalog_dirs WHERE vehicle_id = ?1 AND rel = ?2", |st, rel| st.execute(params![vehicle, rel]).map(|_| ()))?;
        self.each(&new_dirs, "INSERT INTO catalog_dirs (vehicle_id, rel, mtime_ms) VALUES (?1, ?2, ?3) ON CONFLICT(vehicle_id, rel) DO UPDATE SET mtime_ms = excluded.mtime_ms", |st, (rel, m)| {
            st.execute(params![vehicle, rel, m]).map(|_| ())
        })?;
        self.mark_scan(vehicle, root, scan.started_at, scan.scanned_at)
    }

    /// What save_scan writes of `files`: the rows the catalog has that are not among them (gone),
    /// each file whose row is missing or not the same (changed, in their order), and the folders'
    /// rows. The rows are compared as previous() reads them, where they are, and fail as it fails.
    #[allow(clippy::type_complexity)]
    fn changes<'f>(&self, vehicle: &str, files: &'f [FileEntry]) -> rusqlite::Result<(Vec<String>, Vec<&'f FileEntry>, HashMap<String, f64>)> {
        // (each rel's first place among the files, and from each place the next with that rel)
        const NONE: usize = usize::MAX;
        let mut at: HashMap<&str, usize> = HashMap::with_capacity(files.len());
        let mut next = vec![NONE; files.len()];
        for (i, f) in files.iter().enumerate().rev() {
            if let Some(later) = at.insert(f.rel.as_str(), i) {
                next[i] = later;
            }
        }
        let mut same = vec![false; files.len()];
        let mut gone = Vec::new();
        let c = self.conn();
        let mut q = c.prepare("SELECT rel, size, mtime_ms, ctime_ms, kind, type, day, camera, event_id, birthtime_ms FROM catalog_files WHERE vehicle_id = ?1")?;
        let mut rows = q.query([vehicle])?;
        while let Some(r) = rows.next()? {
            // (in the order previous() reads them, so a row it cannot read fails the same way)
            let rel = text(r, 0)?;
            let size = r.get::<_, i64>(1)? as u64;
            let mtime_ms: f64 = r.get(2)?;
            let ctime_ms = r.get::<_, Option<f64>>(3)?.unwrap_or(0.0);
            let birthtime_ms = r.get::<_, Option<f64>>(9)?.unwrap_or(0.0);
            let kind = if text(r, 4)? == "clip" { Kind::Clip } else { Kind::Asset };
            let typ = type_of(text_or_null(r, 5)?.unwrap_or_default());
            let day = text_or_null(r, 6)?.unwrap_or_default();
            let camera = text_or_null(r, 7)?;
            let event_id = text_or_null(r, 8)?;
            let Some(&first) = at.get(rel) else {
                gone.push(rel.to_string());
                continue;
            };
            let mut i = first;
            while i != NONE {
                let f = &files[i];
                same[i] = f.size == size
                    && f.mtime_ms == mtime_ms
                    && f.ctime_ms == ctime_ms
                    && f.birthtime_ms == birthtime_ms
                    && f.kind == kind
                    && f.typ == typ
                    && f.day == day
                    && f.camera.as_deref() == camera
                    && f.event_id.as_deref() == event_id;
                i = next[i];
            }
        }
        drop(rows);
        let changed = files.iter().zip(&same).filter(|(_, s)| !**s).map(|(f, _)| f).collect();
        Ok((gone, changed, dirs_of(&c, vehicle)?))
    }

    /// The files moved without a scan (Recycle Bin, restore): the rows follow the inventory's
    /// files, and the scan row its totals (catalog.js syncFiles).
    pub fn sync_files(&self, vehicle: &str, files: &[FileEntry]) -> rusqlite::Result<()> {
        let have = self.rels(vehicle)?;
        let now: HashSet<&str> = files.iter().map(|f| f.rel.as_str()).collect();
        let gone: Vec<String> = have.iter().filter(|r| !now.contains(r.as_str())).cloned().collect();
        let added: Vec<&FileEntry> = files.iter().filter(|f| !have.contains(&f.rel)).collect();
        if gone.is_empty() && added.is_empty() {
            return Ok(());
        }
        let last = self.last_scan(vehicle)?;
        self.invalidate(vehicle)?;
        self.remove_files(vehicle, &gone)?;
        self.put_files(vehicle, &added)?;
        if let Some(l) = last.filter(|l| l.format == FORMAT) {
            self.mark_scan(vehicle, &l.root, l.started_ms, l.scanned_at)?;
        }
        Ok(())
    }

    /// The clip codecs an older server kept (inventory.js codecs(), catalog.js putCodecs): each
    /// row the catalog does not have yet is added and a row it has is kept (the catalog is the
    /// newer), in BATCH-row transactions, so run again after a failure partway it adds the rest.
    /// How many rows were added.
    pub fn adopt_codecs(&self, vehicle: &str, rows: &[(String, u64, f64, Option<String>)]) -> rusqlite::Result<usize> {
        let added = std::cell::Cell::new(0);
        self.each(rows, INSERT_MISSING_CODEC, |st, (rel, size, m, codec)| {
            added.set(added.get() + st.execute(params![vehicle, rel, *size as i64, m, codec])?);
            Ok(())
        })?;
        Ok(added.get())
    }

    /// The clip codecs read so far: rel -> (size, mtime, codec).
    pub fn codecs(&self, vehicle: &str) -> rusqlite::Result<CodecMap> {
        let c = self.conn();
        let mut q = c.prepare("SELECT rel, size, mtime_ms, codec FROM clip_codecs WHERE vehicle_id = ?1")?;
        let rows = q.query_map([vehicle], |r| Ok((r.get::<_, String>(0)?, (r.get::<_, i64>(1)? as u64, r.get::<_, f64>(2)?, r.get::<_, Option<String>>(3)?))))?;
        rows.collect()
    }

    /// codecs() a row at a time, read where it is: `f(rel, codec)` for each, as codecs() reads it
    /// (an error where codecs() fails, after the rows before it).
    pub fn each_codec(&self, vehicle: &str, mut f: impl FnMut(&str, u64, f64, Option<&str>)) -> rusqlite::Result<()> {
        let c = self.conn();
        let mut q = c.prepare("SELECT rel, size, mtime_ms, codec FROM clip_codecs WHERE vehicle_id = ?1")?;
        let mut rows = q.query([vehicle])?;
        while let Some(r) = rows.next()? {
            let rel = text(r, 0)?;
            let size = r.get::<_, i64>(1)? as u64;
            let mtime_ms = r.get::<_, f64>(2)?;
            f(rel, size, mtime_ms, text_or_null(r, 3)?);
        }
        Ok(())
    }

    pub fn codec(&self, vehicle: &str, rel: &str) -> rusqlite::Result<Option<(u64, f64, Option<String>)>> {
        self.conn()
            .query_row("SELECT size, mtime_ms, codec FROM clip_codecs WHERE vehicle_id = ?1 AND rel = ?2", [vehicle, rel], |r| Ok((r.get::<_, i64>(0)? as u64, r.get::<_, f64>(1)?, r.get::<_, Option<String>>(2)?)))
            .optional()
    }

    pub fn update_codecs(&self, vehicle: &str, set: &[(String, u64, f64, Option<String>)], gone: &[String]) -> rusqlite::Result<()> {
        self.each(gone, "DELETE FROM clip_codecs WHERE vehicle_id = ?1 AND rel = ?2", |st, rel| st.execute(params![vehicle, rel]).map(|_| ()))?;
        self.each(set, UPSERT_CODEC, |st, (rel, size, m, codec)| st.execute(params![vehicle, rel, *size as i64, m, codec]).map(|_| ()))
    }
}

/// The folders' rows: rel -> mtime.
fn dirs_of(c: &Connection, vehicle: &str) -> rusqlite::Result<HashMap<String, f64>> {
    let mut q = c.prepare("SELECT rel, mtime_ms FROM catalog_dirs WHERE vehicle_id = ?1")?;
    let rows = q.query_map([vehicle], |r| Ok((r.get::<_, String>(0)?, r.get::<_, f64>(1)?)))?;
    rows.collect()
}

/// A text column as `r.get::<_, String>` reads it, without the copy (its error when it is not text).
fn text<'r>(r: &'r rusqlite::Row, i: usize) -> rusqlite::Result<&'r str> {
    match r.get_ref(i)?.as_str() {
        Ok(s) => Ok(s),
        Err(_) => Err(r.get::<_, String>(i).err().unwrap_or(rusqlite::Error::InvalidColumnIndex(i))),
    }
}

/// A text column as `r.get::<_, Option<String>>` reads it, without the copy.
fn text_or_null<'r>(r: &'r rusqlite::Row, i: usize) -> rusqlite::Result<Option<&'r str>> {
    match r.get_ref(i)?.as_str_or_null() {
        Ok(s) => Ok(s),
        Err(_) => Err(r.get::<_, Option<String>>(i).err().unwrap_or(rusqlite::Error::InvalidColumnIndex(i))),
    }
}

#[cfg(test)]
fn same_entry(a: &FileEntry, b: &FileEntry) -> bool {
    a.size == b.size && a.mtime_ms == b.mtime_ms && a.ctime_ms == b.ctime_ms && a.birthtime_ms == b.birthtime_ms && a.kind == b.kind && a.typ == b.typ && a.day == b.day && a.camera == b.camera && a.event_id == b.event_id
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::footage::inventory;

    /// save_scan as it was: every row read with previous(), every folder row written again
    fn save_scan_as_before(cat: &Catalog, vehicle: &str, root: &str, scan: &ScanResult) {
        cat.invalidate(vehicle).unwrap();
        let known = cat.previous(vehicle).unwrap();
        let present: HashSet<&str> = scan.files.iter().map(|f| f.rel.as_str()).collect();
        let gone: Vec<String> = known.files.values().map(|f| &f.rel).filter(|r| !present.contains(r.as_str())).cloned().collect();
        let changed: Vec<&FileEntry> = scan.files.iter().filter(|f| known.files.get(&f.rel).is_none_or(|k| !same_entry(k, f))).collect();
        cat.remove_files(vehicle, &gone).unwrap();
        cat.put_files(vehicle, &changed).unwrap();
        cat.conn().execute("DELETE FROM catalog_dirs WHERE vehicle_id = ?1", [vehicle]).unwrap();
        let dirs: Vec<(&String, &f64)> = scan.dirs.iter().collect();
        cat.each(&dirs, "INSERT INTO catalog_dirs (vehicle_id, rel, mtime_ms) VALUES (?1, ?2, ?3)", |st, (rel, m)| st.execute(params![vehicle, rel, m]).map(|_| ())).unwrap();
        cat.mark_scan(vehicle, root, scan.started_at, scan.scanned_at).unwrap();
    }

    fn tables(cat: &Catalog) -> Vec<String> {
        let c = cat.conn();
        let mut out = Vec::new();
        for sql in [
            "SELECT vehicle_id, rel, size, mtime_ms, ctime_ms, birthtime_ms, kind, type, day, camera, event_id FROM catalog_files ORDER BY vehicle_id, rel",
            "SELECT vehicle_id, rel, mtime_ms, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL FROM catalog_dirs ORDER BY vehicle_id, rel",
            "SELECT vehicle_id, format, root, started_ms, scanned_at, files, bytes, NULL, NULL, NULL, NULL FROM catalog_scans ORDER BY vehicle_id",
        ] {
            let mut q = c.prepare(sql).unwrap();
            let rows = q.query_map([], |r| Ok((0..11).map(|i| format!("{:?}", r.get_ref(i).unwrap())).collect::<Vec<_>>().join("|"))).unwrap();
            out.extend(rows.map(Result::unwrap));
            out.push("--".into());
        }
        out
    }

    fn entry(rel: &str, size: u64, mtime: f64) -> FileEntry {
        let c = crate::footage::classify(rel).unwrap();
        FileEntry { rel: rel.into(), size, mtime_ms: mtime, ctime_ms: mtime + 1.0, birthtime_ms: 0.0, kind: c.kind, typ: c.typ, day: "2026-10-01".into(), camera: c.camera, event_id: c.event_id }
    }

    #[test]
    fn a_scan_writes_the_catalog_as_writing_every_row_again_would() {
        let dir = std::env::temp_dir().join(format!("catalog-save-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let now = Catalog::open(&dir.join("now.db")).unwrap();
        let before = Catalog::open(&dir.join("before.db")).unwrap();
        let rel = |i: usize| format!("TeslaCam/RecentClips/2026-10-01_10-{:02}-00-front.mp4", i % 60);
        let ev = |i: usize| format!("TeslaCam/SentryClips/2026-10-01_11-00-00/2026-10-01_10-{:02}-00-back.mp4", i);
        let dirs = |pairs: &[(&str, f64)]| pairs.iter().map(|(r, m)| (r.to_string(), *m)).collect::<HashMap<_, _>>();
        let first: Vec<FileEntry> = (0..40).map(|i| entry(&rel(i), 100 + i as u64, 1000.0 + i as f64)).chain((0..5).map(|i| entry(&ev(i), 7, 5.0))).collect();
        let mut second = first.clone();
        second.remove(3); // gone
        second[0].size = 1; // changed
        second[1].camera = None;
        second[2].birthtime_ms = 9.0;
        second[4].day = String::new();
        second.push(entry(&rel(55), 1, 1.0)); // new
        // a rel listed twice: the first copy as the catalog has it, the second not, and the other way
        let mut dup = second[5].clone();
        dup.size += 1;
        second.push(dup);
        let dup = second[6].clone();
        second[6].mtime_ms += 0.5;
        second.push(dup);
        second.push(second[7].clone());
        second.retain(|f| f.rel != ev(4));
        let scans = [
            ScanResult::of(first.clone(), dirs(&[("", 1.0), ("TeslaCam", 2.0), ("TeslaCam/RecentClips", 3.0), ("TeslaCam/SentryClips", -0.0)]), 10.0, 11.0),
            ScanResult::of(first, dirs(&[("", 1.0), ("TeslaCam", 2.0), ("TeslaCam/RecentClips", 3.0), ("TeslaCam/SentryClips", -0.0)]), 12.0, 13.0),
            ScanResult::of(second, dirs(&[("", 1.0), ("TeslaCam", 2.5), ("TeslaCam/SavedClips", 4.0), ("TeslaCam/SentryClips", 0.0)]), 14.0, 15.0),
            ScanResult::of(Vec::new(), HashMap::new(), 16.0, 17.0),
        ];
        for scan in &scans {
            for v in ["v1", "v2"] {
                now.save_scan(v, "/footage", scan).unwrap();
                save_scan_as_before(&before, v, "/footage", scan);
                assert_eq!(tables(&now), tables(&before));
            }
        }
        std::fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn one_codec_is_the_row_the_whole_table_has() {
        let dir = std::env::temp_dir().join(format!("catalog-codec-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let cat = Catalog::open(&dir.join("manager.db")).unwrap();
        let set = vec![("SentryClips/a/x-front.mp4".to_string(), 10, 1.5, Some("hevc".to_string())), ("RecentClips/y-front.mp4".to_string(), 20, 2.0, None)];
        cat.update_codecs("v1", &set, &[]).unwrap();
        let all = cat.codecs("v1").unwrap();
        for (rel, ..) in &set {
            assert_eq!(cat.codec("v1", rel).unwrap().as_ref(), all.get(rel));
        }
        assert_eq!(cat.codec("v1", "missing.mp4").unwrap(), None);
        assert_eq!(cat.codec("v2", "RecentClips/y-front.mp4").unwrap(), None);
        std::fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn the_codecs_read_a_row_at_a_time_are_the_ones_read_whole() {
        let dir = std::env::temp_dir().join(format!("catalog-each-codec-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cat = Catalog::open(&dir.join("manager.db")).unwrap();
        let files: Vec<FileEntry> = (0..30).map(|i| entry(&format!("TeslaCam/RecentClips/2026-10-01_10-{i:02}-00-front.mp4"), 1, 1.0)).collect();
        let list = Arc::new(crate::scanner::FileList::from(files.clone()));
        let codec = |i: usize| match i % 4 {
            0 => None,
            1 => Some("h264".to_string()),
            2 => Some("hevc".to_string()),
            _ => Some("h265".to_string()),
        };
        let mut rows: Vec<_> = files.iter().enumerate().filter(|(i, _)| i % 5 != 0).map(|(i, f)| (f.rel.clone(), 1, 1.0, codec(i))).collect();
        rows.push(("TeslaCam/RecentClips/gone-front.mp4".into(), 1, 1.0, Some("av1".into())));
        cat.update_codecs("v1", &rows, &[]).unwrap();
        let read = |v: &str| {
            let mut c = inventory::Codecs::none(list.clone());
            cat.each_codec(v, |rel, _, _, x| c.set(rel, x)).map(|()| c.iter().map(|(r, x)| (r.to_string(), x.map(String::from))).collect::<Vec<_>>())
        };
        let whole = |v: &str| cat.codecs(v).map(|m| inventory::Codecs::new(list.clone(), m.into_iter().map(|(rel, (_, _, c))| (rel, c))).iter().map(|(r, x)| (r.to_string(), x.map(String::from))).collect::<Vec<_>>());
        assert_eq!(read("v1").unwrap(), whole("v1").unwrap());
        assert_eq!(read("v1").unwrap().len(), 24);
        assert_eq!(read("v2").unwrap(), Vec::new());
        cat.conn().execute("INSERT INTO clip_codecs (vehicle_id, rel, size, mtime_ms, codec) VALUES ('v1', 'TeslaCam/x-front.mp4', 'big', 1.0, 'h264')", []).unwrap();
        assert_eq!(read("v1").unwrap_err().to_string(), whole("v1").unwrap_err().to_string(), "a row that cannot be read fails both");
        std::fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn kept_codecs_fill_in_what_the_catalog_lacks_and_never_replace_its_rows() {
        let dir = std::env::temp_dir().join(format!("catalog-adopt-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let cat = Catalog::open(&dir.join("manager.db")).unwrap();
        let x = "SentryClips/a/x-front.mp4".to_string();
        let y = "RecentClips/y-front.mp4".to_string();
        cat.update_codecs("v1", &[(x.clone(), 10, 1.5, Some("h265".to_string()))], &[]).unwrap();
        let kept = vec![(x.clone(), 99, 9.0, Some("h264".to_string())), (y.clone(), 20, 2.0, None)];
        assert_eq!(cat.adopt_codecs("v1", &kept).unwrap(), 1, "only the row the catalog lacked");
        assert_eq!(cat.codec("v1", &x).unwrap(), Some((10, 1.5, Some("h265".to_string()))), "the catalog's own row is kept");
        assert_eq!(cat.codec("v1", &y).unwrap(), Some((20, 2.0, None)));
        assert_eq!(cat.adopt_codecs("v1", &kept).unwrap(), 0, "run again, nothing more to add");
        let many: Vec<_> = (0..BATCH + 5).map(|i| (format!("RecentClips/{i}-front.mp4"), i as u64, 1.0, Some("h264".to_string()))).collect();
        assert_eq!(cat.adopt_codecs("v2", &many).unwrap(), BATCH + 5, "more than one batch");
        assert_eq!(cat.codecs("v2").unwrap().len(), BATCH + 5);
        std::fs::remove_dir_all(dir).unwrap();
    }
}
