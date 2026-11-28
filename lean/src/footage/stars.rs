//! Starred moments (index.js /api/stars): a moment marked with a note, kept
//! in the `stars` setting for every interface. An admin's star also protects
//! its recording, so policies and "Delete through this date" leave it alone;
//! an admin removing the last star of such a recording unprotects it. A clip
//! can be in more than one recording (a RecentClips drive and the Sentry or
//! Saved event that copied it), so a star protects every one holding it.

use super::ctx::{Caller, Fail, Res};
use super::js;
use super::inventory::Recording;
use super::mdb::DbResult;
use crate::app::App;
use serde_json::{json, Value};

pub const MAX_STARS: usize = 2000;

/// The stars (an error when manager.db could not read them: never written over then).
fn star_list(app: &App) -> DbResult<Vec<Value>> {
    Ok(match app.core.db().get("stars")? {
        Some(Value::Array(a)) => a,
        _ => Vec::new(),
    })
}

fn save(app: &App, list: &[Value]) -> DbResult<()> {
    app.core.db().set("stars", &Value::Array(list.to_vec()))
}

/// cleanNote: control characters as spaces, trimmed, at most 200 characters.
pub fn clean_note(n: Option<&Value>) -> String {
    let s: String = js::string_or(n, "").chars().map(|c| if (c as u32) < 0x20 { ' ' } else { c }).collect();
    js::slice16(js::trim(&s), 200)
}

/// "YYYY-MM-DD_HH-MM-SS"
pub fn is_clip_key(s: &str) -> bool {
    let b = s.as_bytes();
    b.len() == 19 && b.iter().enumerate().all(|(i, c)| match i {
        4 | 7 | 13 | 16 => *c == b'-',
        10 => *c == b'_',
        _ => c.is_ascii_digit(),
    })
}

/// whether one of the recording's clip files is the clip (its name starts with the key)
fn holds_clip(r: &Recording, clip: &str) -> bool {
    !clip.is_empty() && r.clip_rels().any(|rel| super::inventory::basename(rel).starts_with(clip))
}

/// every recording holding the clip: a RecentClips drive and an event can both have it
fn recordings_of_clip(app: &App, vid: &str, clip: &str) -> Vec<Recording> {
    app.state_of(vid).map(|s| s.recordings.iter().filter(|r| holds_clip(r, clip)).cloned().collect()).unwrap_or_default()
}

/// the recordings a star protected: `protects`, and `alsoProtects` when the clip was in more than one
fn protected_ids(star: &Value) -> Vec<String> {
    let mut ids: Vec<String> = star.get("protects").and_then(Value::as_str).map(String::from).into_iter().collect();
    ids.extend(star.get("alsoProtects").and_then(Value::as_array).into_iter().flatten().filter_map(|x| x.as_str().map(String::from)));
    ids
}

fn set_protected_ids(star: &mut Value, ids: &[String]) {
    let Some(o) = star.as_object_mut() else { return };
    o.remove("protects");
    o.remove("alsoProtects");
    if let Some((first, more)) = ids.split_first() {
        o.insert("protects".into(), json!(first));
        if !more.is_empty() {
            o.insert("alsoProtects".into(), json!(more));
        }
    }
}

/// Protect pressed on recordings: no star owns their protection any more, so
/// removing a star later leaves what the admin protected by hand.
pub fn protected_by_hand(app: &App, vid: &str, rec_ids: &[String]) -> DbResult<()> {
    let mut list = star_list(app)?;
    let mut changed = false;
    for x in list.iter_mut().filter(|x| x.get("vehicle").map(js::string).as_deref() == Some(vid)) {
        let ids = protected_ids(x);
        let left: Vec<String> = ids.iter().filter(|i| !rec_ids.contains(i)).cloned().collect();
        if left.len() != ids.len() {
            set_protected_ids(x, &left);
            changed = true;
        }
    }
    if changed {
        save(app, &list)?;
    }
    Ok(())
}

fn iso_now() -> String {
    chrono::Utc::now().format("%Y-%m-%dT%H:%M:%S%.3fZ").to_string()
}

/// GET /api/stars?vehicle=
pub fn list(app: &App, vehicle: Option<&str>) -> Res<Value> {
    let known: Vec<String> = app.vehicles.try_list()?.into_iter().map(|v| v.id).collect();
    let mut stars: Vec<Value> = star_list(app)?
        .into_iter()
        .filter(|x| {
            let v = x.get("vehicle").and_then(Value::as_str).unwrap_or("");
            known.iter().any(|k| k == v) && vehicle.map_or(true, |w| w == v)
        })
        .collect();
    let clip = |x: &Value| x.get("clip").map(js::string).unwrap_or_else(|| "undefined".into());
    let second = |x: &Value| js::number(x.get("second"));
    stars.sort_by(|a, b| clip(b).cmp(&clip(a)).then(second(b).partial_cmp(&second(a)).unwrap_or(std::cmp::Ordering::Equal)));
    Ok(json!({ "stars": stars }))
}

/// POST /api/stars
pub fn add(app: &App, who: &Caller, body: &Value) -> Res<Value> {
    let vid = if js::truthy(body.get("vehicle")) { js::string(&body["vehicle"]) } else { String::new() };
    let v = app.vehicles.try_get(&vid)?.ok_or_else(|| Fail::new(404, "No such vehicle."))?;
    let clip = if js::truthy(body.get("clip")) { js::string(&body["clip"]) } else { String::new() };
    let second = js::number(body.get("second")).floor();
    if !is_clip_key(&clip) || !second.is_finite() || !(0.0..=7200.0).contains(&second) {
        return Err(Fail::new(400, "Give the clip and second to star."));
    }
    let mut list = star_list(app)?;
    if list.len() >= MAX_STARS {
        return Err(Fail::new(400, format!("There are {MAX_STARS} starred moments already: remove some first.")));
    }
    let mut star = json!({ "id": super::random_hex(6), "vehicle": v.id, "day": &clip[0..10], "clip": clip, "second": js::num(second), "note": clean_note(body.get("note")), "createdAt": iso_now() });
    // its recording is protected (by an admin's star: a watch-only account changes nothing kept)
    let recs = recordings_of_clip(app, &v.id, &clip);
    let fresh: Vec<&Recording> = recs.iter().filter(|r| !r.protected).collect();
    if who.admin_here && !fresh.is_empty() {
        set_protected_ids(&mut star, &fresh.iter().map(|r| r.id.clone()).collect::<Vec<_>>());
        // (protected before the star is kept: a star never stands for a protection that was not kept)
        let rels: Vec<String> = fresh.iter().flat_map(|r| r.files().map(|(f, _)| f.to_string())).collect();
        super::bin::set_protected(app, &v.id, &rels, true)?;
        app.rebuild(&v.id, None);
        app.core.announce("inventory");
    }
    list.push(star.clone());
    save(app, &list)?;
    let protected = !recs.is_empty() && (fresh.is_empty() || star.get("protects").is_some());
    let mut out = json!({ "star": star, "protected": protected });
    // (why nothing was protected, so the page never promises a protection that was not made)
    if !protected {
        out["notProtected"] = json!(if recs.is_empty() {
            "its recording is not in the footage list yet"
        } else {
            "only an administrator's star protects a recording"
        });
    }
    Ok(out)
}

fn not_found() -> Fail {
    Fail::new(404, "No such starred moment.")
}

/// PATCH /api/stars/:id
pub fn edit(app: &App, id: &str, body: &Value) -> Res<Value> {
    let mut list = star_list(app)?;
    let star = list.iter_mut().find(|x| x.get("id").and_then(Value::as_str) == Some(id)).ok_or_else(not_found)?;
    star["note"] = json!(clean_note(body.get("note")));
    let out = star.clone();
    save(app, &list)?;
    Ok(json!({ "star": out }))
}

/// DELETE /api/stars/:id
pub fn remove(app: &App, who: &Caller, id: &str) -> Res<Value> {
    let list = star_list(app)?;
    let star = list.iter().find(|x| x.get("id").and_then(Value::as_str) == Some(id)).cloned().ok_or_else(not_found)?;
    let rest: Vec<Value> = list.into_iter().filter(|x| x.get("id").and_then(Value::as_str) != Some(id)).collect();
    // (the star goes first: a failure after it leaves its recording protected, never the other way)
    save(app, &rest)?;
    // a recording that only this star protected is no longer kept for it (an admin's
    // doing only, like protecting: a watch-only account changes nothing kept)
    // (only the recordings this star itself protected: never one protected by hand)
    let ids = protected_ids(&star);
    if !ids.is_empty() && who.admin_here {
        let vid = star.get("vehicle").map(js::string).unwrap_or_default();
        let mine: Vec<&str> = rest.iter().filter(|x| x.get("vehicle").map(js::string).as_deref() == Some(vid.as_str())).filter_map(|x| x.get("clip").and_then(Value::as_str)).collect();
        let recs: Vec<Recording> = app.state_of(&vid).map(|s| s.recordings.iter().filter(|r| ids.contains(&r.id) && r.protected && !mine.iter().any(|c| holds_clip(r, c))).cloned().collect()).unwrap_or_default();
        if !recs.is_empty() {
            let rels: Vec<String> = recs.iter().flat_map(|r| r.files().map(|(f, _)| f.to_string())).collect();
            super::bin::set_protected(app, &vid, &rels, false)?;
            app.rebuild(&vid, None);
            app.core.announce("inventory");
        }
    }
    Ok(json!({ "ok": true }))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn notes_and_keys() {
        assert_eq!(clean_note(Some(&json!("  a\tb\n "))), "a b");
        assert_eq!(clean_note(None), "");
        assert_eq!(clean_note(Some(&json!(12))), "12");
        assert!(is_clip_key("2025-03-01_08-00-00") && !is_clip_key("2025-03-01 08-00-00"));
    }

    #[test]
    fn stars_that_cannot_be_read_are_never_written_over_and_answer_500() {
        let f = crate::footage::testing::on_core("stars");
        let who = Caller { user: Some("admin".into()), client: "127.0.0.1".into(), admin_here: true, confirmed: true };
        let first = add(&f.app, &who, &json!({ "vehicle": "car", "clip": "2020-01-01_08-00-00", "second": 3 })).unwrap();
        let id = first["star"]["id"].as_str().unwrap().to_string();
        let before = f.kept("stars");
        f.db.fail_setting_reads("stars");
        assert_eq!(add(&f.app, &who, &json!({ "vehicle": "car", "clip": "2020-01-01_09-00-00", "second": 1 })).unwrap_err().status, 0);
        assert_eq!(edit(&f.app, &id, &json!({ "note": "new" })).unwrap_err().status, 0, "not \"no such starred moment\"");
        assert_eq!(remove(&f.app, &who, &id).unwrap_err().status, 0);
        assert_eq!(list(&f.app, None).unwrap_err().status, 0, "not an empty list");
        assert_eq!(f.kept("stars"), before);
    }

    fn protected_of(f: &crate::footage::testing::OnCore) -> Vec<(String, bool)> {
        let mut v: Vec<(String, bool)> = f.app.state_of("car").unwrap().recordings.iter().map(|r| (r.typ.to_string(), r.protected)).collect();
        v.sort();
        v
    }

    #[test]
    fn a_star_protects_every_recording_of_its_clip_and_only_an_admin_unprotects() {
        let f = crate::footage::testing::on_core("stars-both");
        let car = f.dir.join("footage/car");
        for rel in ["RecentClips/2025-03-01_07-59-00-front.mp4", "RecentClips/2025-03-01_08-00-00-front.mp4", "SentryClips/2025-03-01_08-09-00/2025-03-01_08-00-00-front.mp4", "SentryClips/2025-03-01_08-09-00/2025-03-01_08-01-00-front.mp4"] {
            std::fs::create_dir_all(car.join(rel).parent().unwrap()).unwrap();
            std::fs::write(car.join(rel), b"x").unwrap();
        }
        f.app.scan_now("car", false).unwrap();
        assert_eq!(f.app.state_of("car").unwrap().recordings.len(), 2);
        let admin = Caller { user: Some("admin".into()), client: "127.0.0.1".into(), admin_here: true, confirmed: true };
        let viewer = Caller { user: Some("v".into()), client: "127.0.0.1".into(), admin_here: false, confirmed: false };
        let star = add(&f.app, &admin, &json!({ "vehicle": "car", "clip": "2025-03-01_08-00-00", "second": 3 })).unwrap();
        assert_eq!(star["protected"], json!(true));
        assert!(protected_of(&f).iter().all(|x| x.1), "the drive and the event both protected: {:?}", protected_of(&f));
        let id = star["star"]["id"].as_str().unwrap().to_string();
        // a watch-only account removes the star but not the protection
        remove(&f.app, &viewer, &id).unwrap();
        assert!(protected_of(&f).iter().all(|x| x.1));

        // an admin's removal unprotects what the star protected
        let f = crate::footage::testing::on_core("stars-admin");
        let car = f.dir.join("footage/car");
        for rel in ["RecentClips/2025-03-01_08-00-00-front.mp4", "SentryClips/2025-03-01_08-09-00/2025-03-01_08-00-00-front.mp4"] {
            std::fs::create_dir_all(car.join(rel).parent().unwrap()).unwrap();
            std::fs::write(car.join(rel), b"x").unwrap();
        }
        f.app.scan_now("car", false).unwrap();
        let star = add(&f.app, &admin, &json!({ "vehicle": "car", "clip": "2025-03-01_08-00-00", "second": 3 })).unwrap();
        remove(&f.app, &admin, star["star"]["id"].as_str().unwrap()).unwrap();
        assert!(protected_of(&f).iter().all(|x| !x.1), "{:?}", protected_of(&f));

        // protected by hand after the star: removing the star keeps it protected
        let star = add(&f.app, &admin, &json!({ "vehicle": "car", "clip": "2025-03-01_08-00-00", "second": 3 })).unwrap();
        let recent: Vec<String> = f.app.state_of("car").unwrap().recordings.iter().filter(|r| r.typ == "RecentClips").map(|r| r.id.clone()).collect();
        protected_by_hand(&f.app, "car", &recent).unwrap();
        remove(&f.app, &admin, star["star"]["id"].as_str().unwrap()).unwrap();
        let p = protected_of(&f);
        assert!(p.iter().any(|x| x.0 == "RecentClips" && x.1) && p.iter().any(|x| x.0 != "RecentClips" && !x.1), "{p:?}");
    }

    #[test]
    fn a_star_that_protects_nothing_says_why() {
        let f = crate::footage::testing::on_core("stars");
        let viewer = Caller { user: Some("v".into()), client: "127.0.0.1".into(), admin_here: false, confirmed: true };
        let r = add(&f.app, &viewer, &json!({ "vehicle": "car", "clip": "2020-01-01_08-00-00", "second": 3 })).unwrap();
        assert_eq!(r["protected"], json!(false));
        assert!(r["notProtected"].as_str().is_some_and(|t| !t.is_empty()));
    }
}
