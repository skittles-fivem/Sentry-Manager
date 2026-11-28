//! A short text kept in place: up to 22 bytes live inside the value itself (a scanned file's
//! day, camera and event folder all fit), longer text in one boxed copy. It reads as a &str
//! and compares, hashes and orders as one, so it stands in for a String that is set once.

use std::borrow::Borrow;
use std::fmt;
use std::hash::{Hash, Hasher};
use std::ops::Deref;

const INLINE: usize = 22;

#[derive(Clone)]
pub enum Short {
    Inline(u8, [u8; INLINE]),
    Boxed(Box<str>),
}

impl Short {
    pub fn new(s: &str) -> Short {
        if s.len() <= INLINE {
            let mut b = [0u8; INLINE];
            b[..s.len()].copy_from_slice(s.as_bytes());
            Short::Inline(s.len() as u8, b)
        } else {
            Short::Boxed(s.into())
        }
    }

    pub fn as_str(&self) -> &str {
        match self {
            // SAFETY: the bytes were copied from a &str, whole (new() is the only way in)
            Short::Inline(n, b) => unsafe { std::str::from_utf8_unchecked(&b[..*n as usize]) },
            Short::Boxed(s) => s,
        }
    }
}

impl Default for Short {
    fn default() -> Short {
        Short::Inline(0, [0; INLINE])
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
            Short::Boxed(s.into_boxed_str())
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
        match s {
            Short::Boxed(b) => b.into_string(),
            s => s.as_str().to_string(),
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
        assert!(matches!(Short::new(&"x".repeat(22)), Short::Inline(..)));
        assert!(matches!(Short::new(&"x".repeat(23)), Short::Boxed(..)));
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
}
