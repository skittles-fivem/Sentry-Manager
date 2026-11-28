//! Giving freed memory back to the system soon after a large piece of work let go of it (a
//! scan's earlier file list), not only at the minute's trim (main.rs): glibc keeps what is freed
//! for reuse until it is trimmed.

#[cfg(target_env = "gnu")]
static PENDING: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

/// Trim in a few seconds (once, however often this is asked meanwhile): time for what the work
/// held, and requests answered with it, to be dropped.
pub fn soon() {
    #[cfg(target_env = "gnu")]
    {
        use std::sync::atomic::Ordering;
        if !PENDING.swap(true, Ordering::SeqCst) {
            let _ = std::thread::Builder::new().name("trim".into()).spawn(|| {
                std::thread::sleep(std::time::Duration::from_secs(3));
                PENDING.store(false, Ordering::SeqCst);
                unsafe { libc::malloc_trim(0) };
            });
        }
    }
}
