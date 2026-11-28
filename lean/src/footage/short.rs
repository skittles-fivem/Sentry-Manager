//! Compact text for the scan's file list. Short: a short text kept in place: up to 22 bytes live inside the value itself (a scanned file's
//! day, camera and event folder all fit), longer text in one boxed copy. It reads as a &str
//! and compares, hashes and orders as one, so it stands in for a String that is set once.

use std::borrow::Borrow;
use std::fmt;
use std::hash::{Hash, Hasher};
use std::ops::Deref;
use std::sync::Arc;

const INLINE: usize = 22;

/// (the inner kinds stay private: new() and the From conversions are the only ways in, which
/// keeps the in-place bytes whole UTF-8)
#[derive(Clone)]
pub struct Short(Repr);

#[derive(Clone)]
enum Repr {
    Inline(u8, [u8; INLINE]),
    Boxed(Box<str>),
}

impl Short {
    pub fn new(s: &str) -> Short {
        if s.len() <= INLINE {
            let mut b = [0u8; INLINE];
            b[..s.len()].copy_from_slice(s.as_bytes());
            Short(Repr::Inline(s.len() as u8, b))
        } else {
            Short(Repr::Boxed(s.into()))
        }
    }

    pub fn as_str(&self) -> &str {
        match &self.0 {
            // SAFETY: the bytes were copied from a &str, whole (new() is the only way in)
            Repr::Inline(n, b) => unsafe { std::str::from_utf8_unchecked(&b[..*n as usize]) },
            Repr::Boxed(s) => s,
        }
    }
}

impl Default for Short {
    fn default() -> Short {
        Short(Repr::Inline(0, [0; INLINE]))
    }
}

impl Deref for Short {
    type Target = str;
    fn deref(&self) -> &str {
        self.as_str()
    }
}

impl AsRef<str> for Short {
    fn as_ref(&self) -> &str {
        self.as_str()
    }
}

impl Borrow<str> for Short {
    fn borrow(&self) -> &str {
        self.as_str()
    }
}

impl From<&str> for Short {
    fn from(s: &str) -> Short {
        Short::new(s)
    }
}

impl From<String> for Short {
    fn from(s: String) -> Short {
        if s.len() <= INLINE {
            Short::new(&s)
        } else {
            Short(Repr::Boxed(s.into_boxed_str()))
        }
    }
}

impl From<&String> for Short {
    fn from(s: &String) -> Short {
        Short::new(s)
    }
}

impl From<Short> for String {
    fn from(s: Short) -> String {
        match s.0 {
            Repr::Boxed(b) => b.into_string(),
            r => Short(r).as_str().to_string(),
        }
    }
}

impl PartialEq for Short {
    fn eq(&self, o: &Short) -> bool {
        self.as_str() == o.as_str()
    }
}
impl Eq for Short {}

impl PartialEq<str> for Short {
    fn eq(&self, o: &str) -> bool {
        self.as_str() == o
    }
}
impl PartialEq<&str> for Short {
    fn eq(&self, o: &&str) -> bool {
        self.as_str() == *o
    }
}
impl PartialEq<String> for Short {
    fn eq(&self, o: &String) -> bool {
        self.as_str() == o.as_str()
    }
}
impl PartialEq<Short> for str {
    fn eq(&self, o: &Short) -> bool {
        self == o.as_str()
    }
}
impl PartialEq<Short> for &str {
    fn eq(&self, o: &Short) -> bool {
        *self == o.as_str()
    }
}
impl PartialEq<Short> for String {
    fn eq(&self, o: &Short) -> bool {
        self.as_str() == o.as_str()
    }
}

impl PartialOrd for Short {
    fn partial_cmp(&self, o: &Short) -> Option<std::cmp::Ordering> {
        Some(self.cmp(o))
    }
}
impl Ord for Short {
    fn cmp(&self, o: &Short) -> std::cmp::Ordering {
        self.as_str().cmp(o.as_str())
    }
}

impl Hash for Short {
    fn hash<H: Hasher>(&self, h: &mut H) {
        self.as_str().hash(h)
    }
}

impl fmt::Debug for Short {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        fmt::Debug::fmt(self.as_str(), f)
    }
}

impl fmt::Display for Short {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        fmt::Display::fmt(self.as_str(), f)
    }
}

impl serde::Serialize for Short {
    fn serialize<S: serde::Serializer>(&self, s: S) -> Result<S::Ok, S::Error> {
        s.serialize_str(self.as_str())
    }
}

/// A scanned file's path: its own boxed copy, or a part of the text its file list keeps all its
/// paths in (one block for the whole list, given back whole, instead of a block a file). It reads,
/// compares and hashes as the text.
#[derive(Clone)]
pub struct Rel(RelRepr);

#[derive(Clone)]
enum RelRepr {
    Own(Box<str>),
    /// (all the paths, where this one starts, its length)
    In(Arc<String>, u32, u32),
}

impl Rel {
    pub fn as_str(&self) -> &str {
        match &self.0 {
            RelRepr::Own(s) => s,
            RelRepr::In(all, at, n) => &all[*at as usize..(*at + *n) as usize],
        }
    }

    /// The paths of `items` put in one text, each then a part of it; left as they are when they
    /// do not fit a u32's length.
    pub fn pack<T>(items: &mut [T], rel_of: impl Fn(&mut T) -> &mut Rel) {
        let total: usize = items.iter_mut().map(|i| rel_of(i).len()).sum();
        if total > u32::MAX as usize {
            return;
        }
        let mut all = String::with_capacity(total);
        for i in items.iter_mut() {
            all.push_str(rel_of(i));
        }
        let all = Arc::new(all);
        let mut at = 0u32;
        for i in items.iter_mut() {
            let r = rel_of(i);
            let n = r.len() as u32;
            *r = Rel(RelRepr::In(all.clone(), at, n));
            at += n;
        }
    }
}

impl Default for Rel {
    fn default() -> Rel {
        Rel(RelRepr::Own("".into()))
    }
}

impl Deref for Rel {
    type Target = str;
    fn deref(&self) -> &str {
        self.as_str()
    }
}

impl AsRef<str> for Rel {
    fn as_ref(&self) -> &str {
        self.as_str()
    }
}

impl AsRef<std::path::Path> for Rel {
    fn as_ref(&self) -> &std::path::Path {
        std::path::Path::new(self.as_str())
    }
}

impl AsRef<std::ffi::OsStr> for Rel {
    fn as_ref(&self) -> &std::ffi::OsStr {
        std::ffi::OsStr::new(self.as_str())
    }
}

impl Borrow<str> for Rel {
    fn borrow(&self) -> &str {
        self.as_str()
    }
}

impl From<String> for Rel {
    fn from(s: String) -> Rel {
        Rel(RelRepr::Own(s.into_boxed_str()))
    }
}

impl From<&str> for Rel {
    fn from(s: &str) -> Rel {
        Rel(RelRepr::Own(s.into()))
    }
}

impl From<&String> for Rel {
    fn from(s: &String) -> Rel {
        Rel(RelRepr::Own(s.as_str().into()))
    }
}

impl From<Rel> for String {
    fn from(r: Rel) -> String {
        match r.0 {
            RelRepr::Own(b) => b.into_string(),
            RelRepr::In(..) => r.as_str().to_string(),
        }
    }
}

impl PartialEq for Rel {
    fn eq(&self, o: &Rel) -> bool {
        self.as_str() == o.as_str()
    }
}
impl Eq for Rel {}
impl PartialEq<str> for Rel {
    fn eq(&self, o: &str) -> bool {
        self.as_str() == o
    }
}
impl PartialEq<&str> for Rel {
    fn eq(&self, o: &&str) -> bool {
        self.as_str() == *o
    }
}
impl PartialEq<String> for Rel {
    fn eq(&self, o: &String) -> bool {
        self.as_str() == o.as_str()
    }
}
impl PartialEq<Rel> for str {
    fn eq(&self, o: &Rel) -> bool {
        self == o.as_str()
    }
}
impl PartialEq<Rel> for &str {
    fn eq(&self, o: &Rel) -> bool {
        *self == o.as_str()
    }
}
impl PartialEq<Rel> for String {
    fn eq(&self, o: &Rel) -> bool {
        self.as_str() == o.as_str()
    }
}
impl PartialOrd for Rel {
    fn partial_cmp(&self, o: &Rel) -> Option<std::cmp::Ordering> {
        Some(self.cmp(o))
    }
}
impl Ord for Rel {
    fn cmp(&self, o: &Rel) -> std::cmp::Ordering {
        self.as_str().cmp(o.as_str())
    }
}
impl Hash for Rel {
    fn hash<H: Hasher>(&self, h: &mut H) {
        self.as_str().hash(h)
    }
}
impl fmt::Debug for Rel {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        fmt::Debug::fmt(self.as_str(), f)
    }
}
impl fmt::Display for Rel {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        fmt::Display::fmt(self.as_str(), f)
    }
}
impl serde::Serialize for Rel {
    fn serialize<S: serde::Serializer>(&self, s: S) -> Result<S::Ok, S::Error> {
        s.serialize_str(self.as_str())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_back_what_went_in() {
        for s in ["", "front", "2026-11-08", "2026-11-08_12-00-00", "x".repeat(22).as_str(), "y".repeat(23).as_str(), "día-ünïcode-ç-ok", "a much longer name than fits in place"] {
            let a = Short::new(s);
            assert_eq!(a.as_str(), s);
            assert_eq!(Short::from(s.to_string()).as_str(), s);
            assert_eq!(String::from(a.clone()), s);
            assert_eq!(a, s);
            assert_eq!(format!("{a}"), s);
            assert_eq!(format!("{a:?}"), format!("{s:?}"));
        }
        assert!(matches!(Short::new(&"x".repeat(22)).0, Repr::Inline(..)));
        assert!(matches!(Short::new(&"x".repeat(23)).0, Repr::Boxed(..)));
        assert_eq!(std::mem::size_of::<Short>(), 24);
        assert_eq!(std::mem::size_of::<Option<Short>>(), 24);
    }

    #[test]
    fn orders_and_hashes_as_its_text() {
        use std::collections::hash_map::DefaultHasher;
        let h = |x: &dyn Fn(&mut DefaultHasher)| {
            let mut d = DefaultHasher::new();
            x(&mut d);
            d.finish()
        };
        let long = "z".repeat(40);
        let mut v = vec![Short::new(&long), Short::new("b"), Short::new("a"), Short::new("")];
        v.sort();
        assert_eq!(v.iter().map(|s| s.as_str()).collect::<Vec<_>>(), ["", "a", "b", long.as_str()]);
        for s in ["front", long.as_str()] {
            assert_eq!(h(&|d| Short::new(s).hash(d)), h(&|d| s.hash(d)));
        }
    }

    #[test]
    fn paths_packed_in_one_text_read_as_before() {
        let names = ["TeslaCam/RecentClips/2026-11-08_12-00-00-front.mp4", "", "a/é/b.mp4", "TeslaCam/SentryClips/2026-11-08_12-00-00/event.json"];
        let mut rels: Vec<Rel> = names.iter().map(|&n| Rel::from(n)).collect();
        let copy = rels.clone();
        Rel::pack(&mut rels, |r| r);
        for ((r, c), n) in rels.iter().zip(&copy).zip(names) {
            assert!(matches!(r.0, RelRepr::In(..)));
            assert_eq!(r.as_str(), n);
            assert_eq!(r, c);
            assert_eq!(String::from(r.clone()), n);
            assert_eq!(format!("{r:?}"), format!("{n:?}"));
        }
        let RelRepr::In(all, ..) = &rels[0].0 else { unreachable!() };
        assert_eq!(all.as_str(), names.concat());
        assert_eq!(Arc::strong_count(all), names.len());
        assert_eq!(std::mem::size_of::<Rel>(), 24);
    }
}
