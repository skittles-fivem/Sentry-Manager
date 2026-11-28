//! The arrival checksums on core: `ArrivalCtx` on core's state (footage's
//! vehicles and scans with birth times, the catalog's codecs, Sentry USB's
//! `holds` from the CAN area's status reader, the converter's clips in hand,
//! the `sentry-usb-record` setting, audit and logs). The area's hook goes to
//! the converter (before a replace in place), its view and kick to the CAN
//! area's Sentry USB routes, its forget to the footage area's vehicle removal.

use super::Server;
use crate::arrival::ctx::{ArrivalCtx, ArrivedFile, Car, InventoryView, KnownCodec};
use serde_json::{json, Value};
use std::path::PathBuf;
use std::sync::{Arc, Weak};

pub struct CoreArrival {
    srv: Weak<Server>,
    state: Arc<crate::core::AppState>,
}

impl CoreArrival {
    pub fn new(srv: &Arc<Server>) -> CoreArrival {
        CoreArrival { srv: Arc::downgrade(srv), state: srv.state.clone() }
    }
    fn srv(&self) -> Option<Arc<Server>> {
        self.srv.upgrade()
    }
    fn record_setting(&self) -> Result<serde_json::Map<String, Value>, String> {
        Ok(self.state.setting("sentry-usb-record").map_err(|e| e.to_string())?.and_then(|r| r.as_object().cloned()).unwrap_or_default())
    }
}

impl ArrivalCtx for CoreArrival {
    fn vehicles(&self) -> Vec<Car> {
        let Some(s) = self.srv() else { return Vec::new() };
        s.app().vehicles.list().into_iter().map(|v| Car { id: v.id, name: v.name, timezone: v.timezone }).collect()
    }
    fn root_of(&self, vid: &str) -> PathBuf {
        self.srv().and_then(|s| s.app().vehicle(vid)).map(|v| v.root).unwrap_or_default()
    }
    fn vehicle_for_path(&self, path: &str) -> Option<String> {
        self.vehicle_root_for_path(path).map(|(id, _)| id)
    }
    fn vehicle_root_for_path(&self, path: &str) -> Option<(String, PathBuf)> {
        let s = self.srv()?;
        vehicle_at(&s, path).ok().flatten().map(|(v, root)| (v.id, root))
    }
    fn real_root_of(&self, vid: &str) -> Option<PathBuf> {
        let s = self.srv()?;
        let v = s.app().vehicle(vid)?;
        let healthy = s.app().vehicles.share(vid).is_none_or(|sh| sh.is_healthy());
        crate::footage::vehicles::real_root_quick(&v.root, healthy)
    }
    fn inventory(&self, vid: &str) -> Option<InventoryView> {
        let st = self.srv()?.app().state_of(vid).filter(|s| s.scanned)?;
        let files = st
            .file_list
            .iter()
            .map(|f| ArrivedFile { rel: f.rel.clone(), clip: f.kind == crate::footage::Kind::Clip, size: f.size, mtime_ms: f.mtime_ms, ctime_ms: f.ctime_ms, birthtime_ms: f.birthtime_ms })
            .collect();
        Some(InventoryView { files, scanning: st.scanning })
    }
    fn codec(&self, vid: &str, rel: &str) -> Option<KnownCodec> {
        let (size, mtime_ms, codec) = self.srv()?.app().catalog.codec(vid, rel).ok()??;
        Some(KnownCodec { size, mtime_ms, codec })
    }
    fn holds(&self, abs: &str) -> bool {
        self.srv().is_some_and(|s| holds(&s, abs))
    }
    fn busy_paths(&self) -> Vec<String> {
        self.srv().and_then(|s| s.converter().map(|c| c.busy_paths())).unwrap_or_default()
    }
    fn idle(&self) -> bool {
        !self.srv().and_then(|s| s.converter().map(|c| c.running())).unwrap_or(false)
    }
    fn older_of(&self, vid: &str) -> Result<bool, String> {
        Ok(self.record_setting()?.get(vid).and_then(|r| r.get("older")).cloned() != Some(Value::Bool(false)))
    }
    fn set_older(&self, vid: &str, older: bool) -> Result<(), String> {
        let mut rec = self.record_setting()?;
        let mut mine = rec.get(vid).and_then(|r| r.as_object().cloned()).unwrap_or_default();
        mine.insert("older".into(), json!(older));
        rec.insert(vid.to_string(), Value::Object(mine));
        self.state.set_setting("sentry-usb-record", &Value::Object(rec)).map_err(|e| e.to_string())
    }
    fn forget_setting(&self, vid: &str) -> Result<(), String> {
        let mut rec = self.record_setting()?;
        if rec.remove(vid).is_some() {
            self.state.set_setting("sentry-usb-record", &Value::Object(rec)).map_err(|e| e.to_string())?;
        }
        Ok(())
    }
    fn audit(&self, line: &str) {
        self.state.audit.info(line);
    }
    fn log_warn(&self, line: &str) {
        self.state.logger.scope("sentry-usb").warn(line);
    }
    fn log_debug(&self, line: &str) {
        self.state.logger.scope("sentry-usb").debug(line);
    }
    fn log_converter(&self, level: &str, line: &str) {
        let s = self.state.logger.scope("converter");
        match level {
            "error" => s.error(line),
            "warn" => s.warn(line),
            _ => s.info(line),
        }
    }
}

/// sentryUsb.holds(abs): the vehicle's status file says this clip is still being copied (the
/// file looked at again when the last look is over 5 s old, 1 s while a transfer runs: a
/// transfer that started since the last look holds its clips at once).
pub fn holds(s: &Server, abs: &str) -> bool {
    let Some(can) = s.can().cloned() else { return false };
    // (a converter's clip is spelled under the real folder: a vehicle whose folder is a symlink
    // is found by that too; while the real folder of a vehicle that may hold it is not known,
    // it is held when that vehicle's status says a clip of this name is still being copied: the
    // status looked at again as for a known vehicle when its share answers, its last one when
    // the share is down. A clip of no vehicle, under an extra root, is not held by a share that
    // is down.)
    let healthy = |vid: &str| s.app().vehicles.share(vid).is_none_or(|sh| sh.is_healthy());
    let v = match vehicle_at(s, abs) {
        Ok(Some((v, _))) => v,
        Ok(None) => return false,
        Err(unknown) => return unknown_holds(unknown.iter().map(|v| v.id.as_str()), healthy, |vid| can.usb.fresh(vid), |vid| can.usb.get(vid).and_then(|st| crate::arrival::holds::TransferStatus::parse(&st.raw_json)), crate::scanner::now_ms(), abs),
    };
    // (a share that stopped answering is not looked at here: the arrival would wait on it)
    if healthy(&v.id) {
        can.usb.fresh(&v.id);
    }
    let status = can.usb.get(&v.id).and_then(|st| crate::arrival::holds::TransferStatus::parse(&st.raw_json));
    crate::arrival::holds::holds(status.as_ref(), crate::scanner::now_ms(), abs)
}

/// holds() for a clip in none of the vehicles whose folder is known: held only when the status
/// (`status`) of a vehicle whose folder is not known yet (`unknown`) holds a clip of its name; a
/// vehicle with no Sentry USB status holds nothing. The status of a vehicle whose share answers
/// (`healthy`) is looked at again first (`fresh`), as for a known vehicle: a transfer that
/// started since the last look holds its clips at once; a share that is down is not asked.
fn unknown_holds<'a>(
    unknown: impl Iterator<Item = &'a str>,
    healthy: impl Fn(&str) -> bool,
    fresh: impl Fn(&str),
    status: impl Fn(&str) -> Option<crate::arrival::holds::TransferStatus>,
    now_ms: f64,
    abs: &str,
) -> bool {
    let mut held = false;
    for vid in unknown {
        if healthy(vid) {
            fresh(vid);
        }
        held |= status(vid).is_some_and(|t| crate::arrival::holds::holds(Some(&t), now_ms, abs));
    }
    held
}

/// The vehicle whose folder holds `path`, spelled under the folder it is named by or under its
/// real folder (a converter job's sourceRoot is the real path), with the root it matched; no
/// share that is not answering is asked (vehicle_root_in_quick). Err: a share that may hold it
/// has not answered yet (the vehicles whose real folder is not known).
fn vehicle_at(s: &Server, path: &str) -> Result<Option<(crate::footage::vehicles::Vehicle, PathBuf)>, Vec<crate::footage::vehicles::Vehicle>> {
    if path.is_empty() {
        return Ok(None);
    }
    let vehicles = &s.app().vehicles;
    crate::footage::vehicles::vehicle_root_or_unknown(vehicles.list(), std::path::Path::new(path), |v| vehicles.share(&v.id).is_none_or(|sh| sh.is_healthy()))
}

pub fn make(srv: &Arc<Server>) -> Arc<crate::arrival::Area> {
    let ctx: Arc<dyn ArrivalCtx> = Arc::new(CoreArrival { srv: Arc::downgrade(srv), state: srv.state.clone() });
    Arc::new(crate::arrival::Area::open(ctx, &srv.state.config.data))
}

#[cfg(test)]
mod unknown_holds_tests {
    use super::*;
    use crate::arrival::holds::TransferStatus;
    use serde_json::json;

    #[test]
    fn a_clip_of_no_known_vehicle_is_held_only_by_a_transfer_of_its_name() {
        let start = 1_794_144_660u64;
        let now = (start + 60) as f64 * 1000.0;
        let copying = TransferStatus::parse(&json!({ "schema": 1, "state": "transferring", "transfer": { "started_unix": start,
            "first_clip": "RecentClips/2026-11-08/2026-11-08_13-00-00-front.mp4", "last_clip": "RecentClips/2026-11-08/2026-11-08_13-30-00-back.mp4",
            "expected_finish_unix": start + 600, "give_up_unix": start + 1500 } }));
        let idle = TransferStatus::parse(&json!({ "schema": 1, "state": "idle" }));
        let archive = "/mnt/archive/2025-01-02/2025-01-02_10-00-00-front.mp4";
        let clip = "/share/real/RecentClips/2026-11-08/2026-11-08_13-10-00-front.mp4";
        // (the share of each is down: its last status, none looked at again)
        let last = |list: Vec<Option<TransferStatus>>, abs: &str| {
            let ids: Vec<String> = (0..list.len()).map(|i| i.to_string()).collect();
            unknown_holds(ids.iter().map(String::as_str), |_| false, |_| panic!("a share that is down is not asked"), |vid| list[vid.parse::<usize>().unwrap()].clone(), now, abs)
        };
        // (a vehicle without Sentry USB, or one not copying: an extra root's clips go on)
        assert!(!last(vec![None], archive));
        assert!(!last(vec![idle.clone(), None], archive));
        assert!(!last(vec![copying.clone()], archive), "not a clip of the transfer");
        assert!(last(vec![idle.clone(), copying.clone()], clip), "a clip the vehicle is still copying");

        // (the share answers, its folder not resolved yet: the last look said idle, the transfer started since)
        let looked = std::cell::RefCell::new(idle.clone());
        let fresh = |vid: &str| {
            assert_eq!(vid, "car");
            *looked.borrow_mut() = copying.clone();
        };
        assert!(unknown_holds(["car"].into_iter(), |_| true, fresh, |_| looked.borrow().clone(), now, clip), "the status looked at again holds the clip");
        let now_idle = std::cell::RefCell::new(None);
        assert!(!unknown_holds(["car"].into_iter(), |_| true, |_| *now_idle.borrow_mut() = idle.clone(), |_| now_idle.borrow().clone(), now, archive), "a clip of no vehicle is not held");
    }
}
