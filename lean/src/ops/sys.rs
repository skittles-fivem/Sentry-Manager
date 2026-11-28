//! Machine facts several ops parts need: disk space (fsSpace.js), errno
//! names as Node reports them, and running a tool for its first line.

use std::io::Read;
use std::path::Path;
use std::process::{Command, Stdio};
use std::time::{Duration, Instant};

#[derive(Clone, Copy, Debug)]
pub struct Space {
    pub total: f64,
    pub free: f64,
    pub used: f64,
}

/// statfs in fragment units (f_frsize, what `stat -f -c %S` prints).
pub fn disk_space(p: &Path) -> Option<Space> {
    use std::os::unix::ffi::OsStrExt;
    let c = std::ffi::CString::new(p.as_os_str().as_bytes()).ok()?;
    let mut s: libc::statvfs = unsafe { std::mem::zeroed() };
    if unsafe { libc::statvfs(c.as_ptr(), &mut s) } != 0 {
        return None;
    }
    let unit = if s.f_frsize > 0 { s.f_frsize as f64 } else { s.f_bsize as f64 };
    let blocks = s.f_blocks as f64;
    Some(Space { total: blocks * unit, free: s.f_bavail as f64 * unit, used: (blocks - s.f_bfree as f64).max(0.0) * unit })
}

/// err.code as Node names it (EACCES, ENOENT, EROFS, ...), else the message.
pub fn errno_code(e: &std::io::Error) -> String {
    let Some(n) = e.raw_os_error() else { return e.to_string() };
    let name = match n {
        libc::EACCES => "EACCES",
        libc::EPERM => "EPERM",
        libc::ENOENT => "ENOENT",
        libc::EROFS => "EROFS",
        libc::ENOSPC => "ENOSPC",
        libc::EEXIST => "EEXIST",
        libc::ENOTDIR => "ENOTDIR",
        libc::EISDIR => "EISDIR",
        libc::EIO => "EIO",
        libc::EDQUOT => "EDQUOT",
        libc::EBUSY => "EBUSY",
        libc::ELOOP => "ELOOP",
        libc::ENAMETOOLONG => "ENAMETOOLONG",
        libc::EINVAL => "EINVAL",
        libc::ENOTCONN => "ENOTCONN",
        libc::EHOSTDOWN => "EHOSTDOWN",
        libc::ESTALE => "ESTALE",
        _ => return e.to_string(),
    };
    name.to_string()
}

/// Runs a tool and reads the first line of what it prints (health.js firstLine):
/// Ok(line) when it exits 0, else Err(reason) as Node words it. It runs in a process group
/// of its own, killed whole past the timeout, and first_line never waits much past it: not
/// for a tool stuck in the kernel, nor for output a helper it started keeps open.
pub fn first_line(cmd: &str, args: &[&str], timeout: Duration) -> Result<String, String> {
    use std::os::unix::process::CommandExt;
    let mut child = match Command::new(cmd).args(args).stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::null()).process_group(0).spawn() {
        Ok(c) => c,
        Err(e) => {
            // Node: "spawn <cmd> ENOENT"
            return Err(format!("spawn {cmd} {}", errno_code(&e)));
        }
    };
    let grace = Duration::from_secs(2);
    let mut out = child.stdout.take().unwrap();
    let buf: std::sync::Arc<std::sync::Mutex<Vec<u8>>> = Default::default();
    let (done_tx, done) = std::sync::mpsc::channel::<()>();
    {
        let buf = buf.clone();
        std::thread::spawn(move || {
            let mut chunk = [0u8; 4096];
            loop {
                match out.read(&mut chunk) {
                    Ok(0) | Err(_) => break,
                    Ok(n) => {
                        let mut b = buf.lock().unwrap_or_else(|e| e.into_inner());
                        if b.len() < 4096 {
                            b.extend_from_slice(&chunk[..n]);
                        }
                    }
                }
            }
            let _ = done_tx.send(());
        });
    }
    let start = Instant::now();
    let status = loop {
        match child.try_wait() {
            Ok(Some(s)) => break Some(s),
            Ok(None) if start.elapsed() >= timeout => {
                if let Ok(pid) = i32::try_from(child.id()) {
                    // SAFETY: a signal to the group the child leads (process_group(0))
                    unsafe { libc::kill(-pid, libc::SIGKILL) };
                }
                let _ = child.kill();
                super::gpu::reap(child, grace);
                // (killed: as Node's exit code null)
                break None;
            }
            Ok(None) => std::thread::sleep(Duration::from_millis(20)),
            Err(_) => break None,
        }
    };
    // the rest of the output, for a moment at most (a helper may still hold the pipe)
    let _ = done.recv_timeout(grace);
    let buf = std::mem::take(&mut *buf.lock().unwrap_or_else(|e| e.into_inner()));
    match status {
        Some(s) if s.success() => Ok(String::from_utf8_lossy(&buf).split('\n').next().unwrap_or("").trim().to_string()),
        Some(s) => Err(format!("exit {}", s.code().map_or("null".to_string(), |c| c.to_string()))),
        None => Err("exit null".into()),
    }
}

#[cfg(test)]
mod first_line_tests {
    use super::*;

    #[test]
    fn first_line_reads_the_first_line() {
        assert_eq!(first_line("sh", &["-c", "echo one; echo two"], Duration::from_secs(10)), Ok("one".into()));
        assert_eq!(first_line("sh", &["-c", "exit 3"], Duration::from_secs(10)), Err("exit 3".into()));
    }

    #[test]
    fn first_line_never_waits_long_past_its_timeout() {
        // a wrapper whose helper keeps the output open and runs on
        let start = Instant::now();
        assert_eq!(first_line("sh", &["-c", "sleep 30 & sleep 30"], Duration::from_millis(200)), Err("exit null".into()));
        assert!(start.elapsed() < Duration::from_secs(5), "{:?}", start.elapsed());
        // one that ended but left a helper holding the output: what it printed is its line
        let start = Instant::now();
        assert_eq!(first_line("sh", &["-c", "echo ffmpeg version 7; sleep 30 &"], Duration::from_secs(10)), Ok("ffmpeg version 7".into()));
        assert!(start.elapsed() < Duration::from_secs(5), "{:?}", start.elapsed());
    }
}
