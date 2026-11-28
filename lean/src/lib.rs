//! Sentry Six Manager's lean backend: the Node server's API from one small
//! binary. See lean/README.md for what is done and what is still to come.

pub mod api;
pub mod app;
pub mod arrival;
pub mod converter;
pub mod core;
pub mod can;
pub mod footage;
pub mod jobs;
pub mod media;
pub mod mp4;
pub mod nodes;
pub mod ops;
pub mod sei;
pub mod telemetry;
pub mod thumbnails;
pub mod supervisor;
pub mod ui;
pub mod viewer;
pub mod wire;

/// Node's answers, frozen (tests/fixtures/node-reference), for the unit tests.
#[cfg(test)]
#[path = "../tests/support/reference.rs"]
pub(crate) mod reference;

/// Held across one read, change and write of a setting kept as one JSON value (the saved
/// places, the Studio's settings, the appearance), so two quick saves never lose one:
/// requests run at the same time, and each would otherwise write over the other's change.
pub fn setting_edit() -> std::sync::MutexGuard<'static, ()> {
    static EDIT: std::sync::Mutex<()> = std::sync::Mutex::new(());
    EDIT.lock().unwrap_or_else(|e| e.into_inner())
}

// (moved into footage/; the old paths still work)
pub use footage::{catalog, inventory, scanner};
