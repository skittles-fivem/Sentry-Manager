//! Which recordings have CAN Bridge data, the green "CAN" tag on the Footage
//! page (server/canFootage.js): an indexed minute of one of the car's CAN keys
//! overlaps the recording's clips, from its first clip's start to its last
//! clip's end. The minutes are kept per car until the index changes.

use super::ctx::{Car, Rec};
use std::collections::HashMap;
use std::sync::{Arc, Mutex};

pub const CLIP_MS: f64 = 62_000.0;
const MINUTE_MS: f64 = 60_000.0;

/// Whether an indexed minute (sorted starts) overlaps [from, to).
pub fn minute_in(minutes: &[f64], from: f64, to: f64) -> bool {
    let i = minutes.partition_point(|&m| m < from - MINUTE_MS + 1.0);
    i < minutes.len() && minutes[i] < to
}

/// A recording's first and last clip name stamps, or None.
pub fn clip_range(rec: &Rec) -> Option<(String, String)> {
    clip_range_of(rec.clip_rels.iter().map(String::as_str), rec.start_key.as_deref()).map(|(f, l)| (f.to_string(), l.to_string()))
}

/// clip_range of a recording's clip rels and start key, read where they are.
pub fn clip_range_of<'c, 'a: 'c, 'b: 'c>(clip_rels: impl IntoIterator<Item = &'a str>, start_key: Option<&'b str>) -> Option<(&'c str, &'c str)> {
    let mut first: Option<&'c str> = None;
    let mut last: Option<&'c str> = None;
    for rel in clip_rels {
        let rel: &'c str = rel;
        let Some(m) = super::search::ts_in(rel.rsplit('/').next().unwrap_or(rel)) else { continue };
        if first.is_none_or(|f| m < f) {
            first = Some(m);
        }
        if last.is_none_or(|l| m > l) {
            last = Some(m);
        }
    }
    if first.is_none() {
        if let Some(k) = start_key.and_then(super::search::ts_in) {
            first = Some(k);
            last = Some(k);
        }
    }
    Some((first?, last?))
}

/// The green tag's test for one car: its indexed minutes, read once.
pub struct CanTest {
    minutes: Arc<Vec<f64>>,
    car: Car,
}

impl CanTest {
    /// CAN data covers the recording of these clip rels and start key (none read without minutes).
    pub fn covers<'a>(&self, clip_rels: impl IntoIterator<Item = &'a str>, start_key: Option<&str>) -> bool {
        if self.minutes.is_empty() {
            return false;
        }
        let Some((first, last)) = clip_range_of(clip_rels, start_key) else { return false };
        let (Some(from), Some(to)) = (super::prune::start_of(&self.car, first), super::prune::start_of(&self.car, last)) else { return false };
        minute_in(&self.minutes, from, from.max(to) + CLIP_MS)
    }
}

struct CarMinutes {
    sig: Option<String>,
    minutes: Arc<Vec<f64>>,
    zone: String,
    #[allow(dead_code)]
    stamps: HashMap<String, Option<f64>>,
}

#[derive(Default)]
pub struct CanTags {
    cars: Mutex<HashMap<String, CarMinutes>>,
}

impl CanTags {
    /// A test rec -> CAN data covers it, for car `v` (its minutes read once).
    pub fn for_vehicle(&self, v: &Car, keys: &[String], generation: u64, minute_times: &dyn Fn(&str) -> Vec<f64>) -> impl Fn(&Rec) -> bool {
        let test = self.test_for(v, keys, generation, minute_times);
        move |rec: &Rec| test.covers(rec.clip_rels.iter().map(String::as_str), rec.start_key.as_deref())
    }

    /// for_vehicle's test, for recordings read where they are.
    pub fn test_for(&self, v: &Car, keys: &[String], generation: u64, minute_times: &dyn Fn(&str) -> Vec<f64>) -> CanTest {
        let mut keys = keys.to_vec();
        keys.sort();
        let zone = if v.timezone.is_empty() { "UTC".to_string() } else { v.timezone.clone() };
        let sig = format!("{}|{}", generation, keys.join(","));
        let mut cars = self.cars.lock().unwrap();
        let c = cars.entry(v.id.clone()).or_insert_with(|| CarMinutes { sig: None, minutes: Arc::default(), zone: zone.clone(), stamps: HashMap::new() });
        if c.zone != zone {
            *c = CarMinutes { sig: None, minutes: Arc::default(), zone: zone.clone(), stamps: HashMap::new() };
        }
        if c.sig.as_deref() != Some(sig.as_str()) {
            let mut all: Vec<f64> = keys.iter().flat_map(|k| minute_times(k)).collect();
            all.sort_by(|a, b| a.partial_cmp(b).unwrap());
            all.dedup();
            c.minutes = Arc::new(all);
            c.sig = Some(sig);
        }
        let minutes = c.minutes.clone();
        drop(cars);
        CanTest { minutes, car: v.clone() }
    }

    pub fn forget(&self, vid: &str) {
        self.cars.lock().unwrap().remove(vid);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// clip_range as it was, every stamp copied: what clip_range_of must answer
    fn copied_range(rels: &[&str], start_key: Option<&str>) -> Option<(String, String)> {
        let mut first: Option<String> = None;
        let mut last: Option<String> = None;
        for rel in rels {
            let Some(m) = super::super::search::ts_from(rel.rsplit('/').next().unwrap_or(rel)) else { continue };
            if first.as_ref().is_none_or(|f| m < *f) {
                first = Some(m.clone());
            }
            if last.as_ref().is_none_or(|l| m > *l) {
                last = Some(m);
            }
        }
        if first.is_none() {
            if let Some(k) = start_key.and_then(super::super::search::ts_from) {
                first = Some(k.clone());
                last = Some(k);
            }
        }
        Some((first?, last?))
    }

    #[test]
    fn a_recordings_range_is_read_where_its_rels_are() {
        let cases: Vec<(Vec<&str>, Option<&str>)> = vec![
            (vec!["TeslaCam/RecentClips/2026-10-01_10-02-00-front.mp4", "TeslaCam/RecentClips/2026-10-01_10-00-00-back.mp4", "TeslaCam/RecentClips/2026-10-01_10-01-00-front.mp4"], None),
            (vec!["x/2026-10-01_10-00-00-front.mp4"], Some("2026-09-30_01-00-00")),
            (vec!["no stamp here.mp4", "ééé2026-10-01_10-00-00ü-front.mp4", "2026-10-01_10-00-0-front.mp4"], Some("start 2026-01-01_00-00-00 x")),
            (vec!["nothing.mp4"], None),
            (vec![], Some("2026-02-03_04-05-06")),
            (vec![], Some("bad")),
            (vec!["a/2026-10-01_10-00-00-front.mp4/2026-10-02_10-00-00-front.mp4", "2026-10-01_10-00-00x2026-10-03_10-00-00"], None),
        ];
        for (rels, start) in cases {
            let want = copied_range(&rels, start);
            assert_eq!(clip_range_of(rels.iter().copied(), start).map(|(f, l)| (f.to_string(), l.to_string())), want, "{rels:?}");
            let rec = Rec { id: String::new(), clip_rels: rels.iter().map(|r| r.to_string()).collect(), start_key: start.map(String::from) };
            assert_eq!(clip_range(&rec), want);
        }
    }

    #[test]
    fn the_test_for_a_car_reads_its_minutes_once_and_tags_as_before() {
        let car = Car { id: "v1".into(), name: "Car".into(), timezone: "UTC".into(), root: None, studio_root: None, car: None };
        let tags = CanTags::default();
        let at = |k: &str| super::super::prune::start_of(&car, k).unwrap();
        let minute = at("2026-10-01_10-05-00");
        let reads = std::cell::Cell::new(0);
        let times = |_: &str| {
            reads.set(reads.get() + 1);
            vec![minute]
        };
        let keys = vec!["k".to_string()];
        let rec = |rels: &[&str]| Rec { id: String::new(), clip_rels: rels.iter().map(|r| r.to_string()).collect(), start_key: None };
        let f = tags.for_vehicle(&car, &keys, 1, &times);
        let t = tags.test_for(&car, &keys, 1, &times);
        assert_eq!(reads.get(), 1, "read once while the index is the same");
        for (rels, want) in [
            (vec!["RecentClips/2026-10-01_10-04-00-front.mp4"], true),
            (vec!["RecentClips/2026-10-01_10-00-00-front.mp4", "RecentClips/2026-10-01_10-06-00-front.mp4"], true),
            (vec!["RecentClips/2026-10-01_10-07-00-front.mp4"], false),
            (vec!["RecentClips/2026-10-01_09-00-00-front.mp4"], false),
            (vec!["nothing.mp4"], false),
        ] {
            assert_eq!(f(&rec(&rels)), want, "{rels:?}");
            assert_eq!(t.covers(rels.iter().copied(), None), want, "{rels:?}");
        }
        let none = tags.test_for(&car, &[], 2, &times);
        assert!(!none.covers(["RecentClips/2026-10-01_10-04-00-front.mp4"], None), "no minutes, no tag");
    }
}
