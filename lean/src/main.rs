//! sentry-lean: the lean backend's server.
//!
//! The environment is the Node server's (see src/core/config.rs for every
//! variable): PORT (9000), HOST (0.0.0.0), DATA_PATH (/data), APP_ROOT,
//! TLS_MODE, FOOTAGE_PATH or SMB_SHARE and VEHICLE_NAME (the vehicle made on a
//! first start; the vehicles are kept in DATA_PATH/manager.db), TZ, ...
//!
//! Accounts, sessions and HTTPS come from core (the same manager.db and
//! /data/tls as Node): passwords only go over HTTPS. TLS_MODE=off serves
//! plain HTTP, only for running behind a reverse proxy that does HTTPS.
//! Every area is served through core (src/wire/).

use sentry_lean::core;
use std::sync::Arc;
use std::time::Duration;

fn main() {
    // before any thread: as the top of server/index.js, and the supervisor's channel (fd 3)
    sentry_lean::supervisor::link::drop_malloc_arena_max();
    let fd = sentry_lean::supervisor::ipc::take_from_env();
    // Large blocks (128 KiB and up: a job file being written, a read buffer) get pages of their
    // own, given back when freed. glibc would otherwise raise this threshold to the size of each
    // large block freed (up to 32 MiB) and put the next ones in the heap among small blocks,
    // where their pages stay the app's long after (a 40,000-clip job: about 70 MB more). Only
    // this process: ffmpeg is started with glibc's defaults.
    #[cfg(target_env = "gnu")]
    unsafe {
        libc::mallopt(libc::M_MMAP_THRESHOLD, 128 * 1024);
    }
    // room for every connection limit (Docker's default soft limit is 1024)
    let open_files = core::server::raise_open_files_limit();
    // glibc keeps freed memory; hand it back
    #[cfg(target_env = "gnu")]
    std::thread::spawn(|| loop {
        std::thread::sleep(Duration::from_secs(60));
        unsafe { libc::malloc_trim(0) };
    });
    let workers = http_workers(sentry_lean::viewer::work_limits::detect_resources().cpus);
    tokio::runtime::Builder::new_multi_thread().worker_threads(workers).enable_all().build().expect("runtime").block_on(run(fd, open_files));
}

/// The threads that answer the pages (TLS, the network; the routes' own work runs on blocking
/// threads): two, and one more per four CPUs past eight this container may use, at most four.
/// Each is about 100 KB of memory while it is idle.
fn http_workers(cpus: f64) -> usize {
    let c = if cpus.is_finite() { cpus.floor().max(0.0) as usize } else { 0 };
    (2 + c.saturating_sub(8) / 4).min(4)
}

#[cfg(test)]
mod tests {
    #[test]
    fn http_workers_from_the_cpus() {
        use super::http_workers;
        assert_eq!([1.0, 2.0, 4.0, 8.0, 11.9].map(http_workers), [2, 2, 2, 2, 2]);
        assert_eq!([12.0, 16.0, 64.0, f64::NAN].map(http_workers), [3, 4, 4, 2]);
    }
}

async fn run(fd: Option<std::os::fd::OwnedFd>, open_files: u64) {
    let link = sentry_lean::supervisor::link::Link::from_fd(fd).map(Arc::new);
    let mut config = core::Config::from_env();
    // (Node: supervised only with SENTRY_SUPERVISED=1 and a channel to the supervisor)
    config.supervised &= link.is_some();
    // a snapshot chosen for restoring goes in place before manager.db is opened (snapshots.js applyPendingRestore)
    let restored = sentry_lean::ops::snapshots::apply_pending_restore(&config.db_file());
    let state = match core::AppState::open(config) {
        Ok(s) => s,
        Err(e) => {
            eprintln!("{e}");
            std::process::exit(1);
        }
    };
    if open_files < 4096 {
        state.log.warn(format!("Open-files limit is {open_files}: the web port takes fewer connections (raise the container's nofile limit)"));
    }
    match restored {
        Ok(true) => state.log.warn(sentry_lean::ops::snapshots::restored_line(&state.config.db_file())),
        Ok(false) => {}
        Err(e) => state.log.error(format!("Snapshot restore failed: {e}")),
    }
    // the supervisor's link first: the footage area mounts SMB shares through it
    if let Some(l) = link {
        core::supervised::attach(&state, l);
    }
    let mut router = core::router();
    let server = sentry_lean::wire::build(state.clone(), &mut router);
    core::server::install(&state, router);
    sentry_lean::wire::start(&server);
    core::server::sync_device_port(&state);

    // housekeeping (index.js): expired sessions go every minute, on a blocking thread: the
    // database may wait for another connection's write (up to its busy timeout), and the server
    // has only two async workers
    let st = state.clone();
    tokio::spawn(async move {
        let mut t = tokio::time::interval(Duration::from_secs(60));
        loop {
            t.tick().await;
            let db = st.clone();
            // (expired sessions no longer sign in; this only tidies the table)
            let _ = tokio::task::spawn_blocking(move || {
                if let Err(e) = db.db.prune_sessions() {
                    db.log.warn(format!("Expired sessions could not be removed: {e}"));
                }
            })
            .await;
        }
    });

    let st = state.clone();
    tokio::spawn(async move {
        let mut term = tokio::signal::unix::signal(tokio::signal::unix::SignalKind::terminate()).expect("signal");
        let which = tokio::select! { _ = tokio::signal::ctrl_c() => "SIGINT", _ = term.recv() => "SIGTERM" };
        core::server::shutdown(&st, which);
    });
    if let Err(e) = core::server::serve(state.clone()).await {
        state.log.error(format!("listen: {e}"));
        std::process::exit(1);
    }
}
