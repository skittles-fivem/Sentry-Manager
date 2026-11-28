//! A port of `server/proc.js`: run a command without a shell, collect its
//! output, kill it with SIGKILL after a timeout. Never fails: errors are in
//! the result.

use std::io;
use std::process::Stdio;
use std::time::Duration;
use tokio::io::AsyncReadExt;

#[derive(Debug, Clone, Default)]
pub struct RunResult {
    /// The exit code; -1 when killed by a signal or when it could not start.
    pub code: i32,
    pub stdout: String,
    pub stderr: String,
    pub timed_out: bool,
    /// Why it could not start (`error.code` in Node; `NotFound` is ENOENT).
    pub error: Option<io::ErrorKind>,
    pub signal: Option<i32>,
}

pub const MAX_OUTPUT: usize = 4 * 1024 * 1024;

type Collected = std::sync::Arc<std::sync::Mutex<Vec<u8>>>;

async fn collect<R: tokio::io::AsyncRead + Unpin>(r: Option<R>, max: usize, out: Collected) {
    let Some(mut r) = r else { return };
    let mut buf = [0u8; 16384];
    loop {
        match r.read(&mut buf).await {
            Ok(0) | Err(_) => break,
            // as in Node: a chunk is added while the total is under the limit
            Ok(n) => {
                let mut out = out.lock().unwrap_or_else(|e| e.into_inner());
                if out.len() < max {
                    out.extend_from_slice(&buf[..n]);
                }
            }
        }
    }
}

/// How long the output is still read once the program has ended (a helper it started may
/// still hold the pipes: `mount` leaves `mount.cifs` stuck in the kernel).
const OUTPUT_GRACE: Duration = Duration::from_secs(2);

/// The output `task` collected, waiting at most OUTPUT_GRACE for the rest (then it stops
/// reading and what came so far is the output).
async fn output(mut task: tokio::task::JoinHandle<()>, got: Collected) -> String {
    if tokio::time::timeout(OUTPUT_GRACE, &mut task).await.is_err() {
        task.abort();
    }
    let got = std::mem::take(&mut *got.lock().unwrap_or_else(|e| e.into_inner()));
    String::from_utf8_lossy(&got).into_owned()
}

/// `run(cmd, args, { timeoutMs, maxOutput })`. The program runs in a process group of its
/// own; past the timeout the whole group is killed (a helper it started too), and run()
/// returns within a few seconds of the timeout whatever is still stuck.
pub async fn run(cmd: &str, args: &[String], timeout: Duration, max_output: usize) -> RunResult {
    let mut c = tokio::process::Command::new(cmd);
    c.args(args).stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped()).kill_on_drop(true).process_group(0);
    let mut child = match c.spawn() {
        Ok(ch) => ch,
        Err(e) => return RunResult { code: -1, error: Some(e.kind()), ..Default::default() },
    };
    let (out_buf, err_buf) = (Collected::default(), Collected::default());
    let out = tokio::spawn(collect(child.stdout.take(), max_output, out_buf.clone()));
    let err = tokio::spawn(collect(child.stderr.take(), max_output, err_buf.clone()));
    let mut timed_out = false;
    let status = match tokio::time::timeout(timeout, child.wait()).await {
        Ok(s) => s,
        Err(_) => {
            timed_out = true;
            if let Some(pid) = child.id().and_then(|p| i32::try_from(p).ok()) {
                // SAFETY: a signal to the group the child leads (its pid, as process_group(0) made it)
                unsafe { libc::kill(-pid, libc::SIGKILL) };
            }
            let _ = child.start_kill();
            // (one stuck in the kernel does not die at once: it is reaped when it does)
            match tokio::time::timeout(OUTPUT_GRACE, child.wait()).await {
                Ok(s) => s,
                Err(_) => Err(io::Error::new(io::ErrorKind::TimedOut, "did not end after SIGKILL")),
            }
        }
    };
    // 'close': the process has ended and its output is closed (or is given up after OUTPUT_GRACE)
    let stdout = output(out, out_buf).await;
    let stderr = output(err, err_buf).await;
    match status {
        Ok(st) => {
            use std::os::unix::process::ExitStatusExt;
            RunResult { code: st.code().unwrap_or(-1), stdout, stderr, timed_out, error: None, signal: st.signal() }
        }
        Err(e) if timed_out && e.kind() == io::ErrorKind::TimedOut => RunResult { code: -1, stdout, stderr, timed_out, error: None, signal: Some(libc::SIGKILL) },
        Err(e) => RunResult { code: -1, stdout, stderr, timed_out, error: Some(e.kind()), signal: None },
    }
}

/// Remove ffmpeg's "[h264_nvenc @ 0x55bc...]" context prefixes (`/\[[^\]]* @ 0x[0-9a-f]+\]\s*/gi`).
fn strip_contexts(line: &str) -> String {
    let b = line.as_bytes();
    let mut out = String::with_capacity(line.len());
    let mut i = 0;
    let mut copied = 0;
    while i < b.len() {
        if b[i] == b'[' {
            if let Some(rel) = line[i + 1..].find(']') {
                let j = i + 1 + rel;
                let inner = &line[i + 1..j];
                let ok = inner.rfind(" @ 0").is_some_and(|k| {
                    let after = &inner[k + 4..];
                    let mut it = after.chars();
                    matches!(it.next(), Some('x' | 'X')) && {
                        let hex = it.as_str();
                        !hex.is_empty() && hex.bytes().all(|c| c.is_ascii_hexdigit())
                    }
                });
                if ok {
                    out.push_str(&line[copied..i]);
                    let mut end = j + 1;
                    while let Some(c) = line[end..].chars().next() {
                        if super::js::is_js_ws(c) {
                            end += c.len_utf8();
                        } else {
                            break;
                        }
                    }
                    copied = end;
                    i = end;
                    continue;
                }
            }
        }
        i += line[i..].chars().next().map_or(1, char::len_utf8);
    }
    out.push_str(&line[copied..]);
    out
}

pub fn clean_lines(text: &str) -> Vec<String> {
    text.split('\n')
        .map(|l| super::js::js_trim(&strip_contexts(l)).to_string())
        .filter(|l| !l.is_empty())
        .collect()
}

/// Last few meaningful lines, for error messages (`tail`).
pub fn tail(text: &str, lines: usize) -> String {
    let all = clean_lines(text);
    all[all.len().saturating_sub(lines)..].join(" | ")
}

/// First few lines (`head`).
pub fn head(text: &str, lines: usize) -> String {
    clean_lines(text).into_iter().take(lines).collect::<Vec<_>>().join(" | ")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn tail_and_head_clean_lines() {
        let t = "[h264_nvenc @ 0x55bcAF] first\n\n  [x] keep [a @ 0xzz] \nlast [hevc @ 0X1f]  one\n";
        assert_eq!(clean_lines(t), ["first", "[x] keep [a @ 0xzz]", "last one"]);
        assert_eq!(tail(t, 2), "[x] keep [a @ 0xzz] | last one");
        assert_eq!(head(t, 1), "first");
        assert_eq!(tail("", 2), "");
    }

    #[tokio::test]
    async fn runs_without_a_shell_and_times_out() {
        let r = run("sh", &["-c".into(), "echo out; echo err >&2; exit 3".into()], Duration::from_secs(10), MAX_OUTPUT).await;
        assert_eq!((r.code, r.stdout.as_str(), r.stderr.as_str(), r.timed_out), (3, "out\n", "err\n", false));
        let r = run("sleep", &["5".into()], Duration::from_millis(100), MAX_OUTPUT).await;
        assert!(r.timed_out);
        assert_eq!(r.code, -1);
        let r = run("/nonexistent/mount", &[], Duration::from_secs(1), MAX_OUTPUT).await;
        assert_eq!((r.code, r.error), (-1, Some(io::ErrorKind::NotFound)));
    }

    #[tokio::test]
    async fn a_helper_holding_the_output_does_not_keep_it_past_the_timeout() {
        // `mount` starting `mount.cifs`, which holds the pipes and hangs
        let start = std::time::Instant::now();
        let r = run("sh", &["-c".into(), "sleep 30 & sleep 30".into()], Duration::from_millis(100), MAX_OUTPUT).await;
        assert!(r.timed_out);
        assert_eq!(r.code, -1);
        assert!(start.elapsed() < Duration::from_secs(3), "{:?}", start.elapsed());
        // and the helper was killed with it
        let r = run("sh", &["-c".into(), "echo $$; sleep 30 & echo $!; wait".into()], Duration::from_millis(300), MAX_OUTPUT).await;
        let helper: i32 = r.stdout.lines().nth(1).expect("helper pid").trim().parse().unwrap();
        tokio::time::sleep(Duration::from_millis(200)).await;
        // SAFETY: signal 0 only asks whether the process is there
        let alive = unsafe { libc::kill(helper, 0) } == 0 && !std::fs::read_to_string(format!("/proc/{helper}/stat")).unwrap_or_default().contains(") Z ");
        assert!(!alive, "the helper was left running");
    }

    #[tokio::test]
    async fn output_a_helper_keeps_open_after_a_normal_end_is_not_waited_for() {
        let start = std::time::Instant::now();
        let r = run("sh", &["-c".into(), "echo done; sleep 30 &".into()], Duration::from_secs(20), MAX_OUTPUT).await;
        assert_eq!((r.code, r.timed_out, r.stdout.as_str()), (0, false, "done\n"));
        assert!(start.elapsed() < Duration::from_secs(5), "{:?}", start.elapsed());
    }
}
