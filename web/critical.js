'use strict';

// The critical-update bar. The server puts this script into every page it
// serves (the built-in interface, the Studio pages and every interface pack),
// so a critical update shows whatever interface is chosen. It asks
// /api/updates every 10 minutes; when the newest image is marked critical
// (CRITICAL_UPDATE.txt when it was built) and is not installed yet, a red bar
// sits at the very top of the page. It cannot be closed: it goes away by
// itself once the update is installed.
(() => {
  // a page shown inside another page (the Encoder tab) leaves it to the outer one
  try {
    if (window.top !== window.self) return;
  } catch {
    return;
  }
  const RECHECK_MS = 10 * 60 * 1000;
  // ---- how to install (no DOM; test/deploymentWording.test.js runs it) ----
  // What the bar says under the reason. The server's own words come first
  // (status().how in /api/updates, plain text: the Windows build's release
  // updates give them); then the Windows steps when the server is the
  // Windows service (deployment, GET /api/info); else Docker's command and
  // the TrueNAS line, as they always were.
  const COMMAND = 'docker compose pull && docker compose up -d';
  const WIN_UPDATE = 'Run update.ps1 as administrator, or download the latest release and run Install.cmd.';
  function howToInstall(st, deployment) {
    const told = typeof st?.how === 'string' ? st.how.trim() : '';
    if (told) return { run: told, code: null, nas: null };
    if (deployment === 'windows') return { run: WIN_UPDATE, code: null, nas: null };
    return { run: 'Run where you set it up:', code: COMMAND, nas: 'TrueNAS: docker pull the image in System → Shell, then Stop and Start the app.' };
  }
  // ---- how to install: end ----
  const ID = 'sentry-critical-update';
  let bar = null;
  let spacer = null;
  let reasonEl = null;
  let howEl = null;
  // 'docker', 'windows' or 'linux' once asked (only while a critical update shows)
  let deployment = null;
  let collapsed = false;

  const css = `
#${ID}{position:fixed;top:0;left:0;right:0;z-index:2147483647;box-sizing:border-box;margin:0;
  padding:10px max(16px,env(safe-area-inset-right)) 10px max(16px,env(safe-area-inset-left));
  padding-top:max(10px,env(safe-area-inset-top));background:#b3001b;color:#fff;
  font:500 14px/1.4 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;text-align:left;
  box-shadow:0 2px 0 #ffd400,0 6px 18px rgba(0,0,0,.45);border-bottom:2px solid #ffd400;
  display:flex;gap:12px;align-items:flex-start;overflow-wrap:anywhere;color-scheme:dark;cursor:default}
#${ID} *{box-sizing:border-box;color:inherit;font-family:inherit;letter-spacing:normal;text-transform:none}
#${ID} svg{flex:none;width:26px;height:26px;margin-top:1px}
#${ID} .cu-body{flex:1;min-width:0;display:flex;flex-wrap:wrap;gap:4px 14px;align-items:baseline}
#${ID} .cu-title{font-weight:800;font-size:16px;text-transform:uppercase;letter-spacing:.02em}
#${ID} .cu-reason{flex:1 1 260px;min-width:0}
#${ID} .cu-how{flex:1 1 100%;display:flex;flex-wrap:wrap;gap:4px 10px;align-items:center;font-size:13px}
#${ID} code{font:600 13px/1.4 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;background:rgba(0,0,0,.35);
  border:1px solid rgba(255,255,255,.35);border-radius:4px;padding:2px 6px;user-select:all;-webkit-user-select:all;max-width:100%}
#${ID}.cu-slim .cu-reason,#${ID}.cu-slim .cu-how{display:none}
#${ID}.cu-slim{padding-bottom:6px;cursor:pointer}
@media (prefers-reduced-motion:no-preference){
  #${ID} svg{animation:cu-pulse 1.6s ease-in-out infinite}
  @keyframes cu-pulse{50%{transform:scale(1.15)}}
}
@media (max-width:640px){
  #${ID}{font-size:13px;gap:10px;padding-top:max(8px,env(safe-area-inset-top));padding-bottom:8px}
  #${ID} svg{width:22px;height:22px}
  #${ID} .cu-title{font-size:14px}
  #${ID} .cu-how,#${ID} code{font-size:12px}
}
@media print{#${ID}{position:static}}`;

  function build() {
    const style = document.createElement('style');
    style.textContent = css;
    document.head.appendChild(style);
    bar = document.createElement('div');
    bar.id = ID;
    bar.setAttribute('role', 'alert');
    const svgNs = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(svgNs, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    const tri = document.createElementNS(svgNs, 'path');
    tri.setAttribute('d', 'M12 2 1 21h22L12 2Z');
    tri.setAttribute('fill', '#ffd400');
    const mark = document.createElementNS(svgNs, 'path');
    mark.setAttribute('d', 'M11 9h2v6h-2zM11 17h2v2h-2z');
    mark.setAttribute('fill', '#000');
    svg.append(tri, mark);
    const body = document.createElement('div');
    body.className = 'cu-body';
    const title = document.createElement('strong');
    title.className = 'cu-title';
    title.textContent = 'Critical update: install now';
    reasonEl = document.createElement('span');
    reasonEl.className = 'cu-reason';
    howEl = document.createElement('span');
    howEl.className = 'cu-how';
    body.append(title, reasonEl, howEl);
    bar.append(svg, body);
    // keeps the page's own content below the bar
    spacer = document.createElement('div');
    spacer.setAttribute('aria-hidden', 'true');
    spacer.style.cssText = 'display:block;width:100%;flex:none;margin:0;padding:0;border:0';
    bar.addEventListener('click', () => setCollapsed(false));
  }

  const phone = () => window.matchMedia('(max-width: 640px)').matches;
  function setCollapsed(on) {
    if (!bar || collapsed === on) return;
    collapsed = on;
    bar.classList.toggle('cu-slim', on);
    fit();
  }
  function fit() {
    if (!bar?.isConnected) return;
    const h = Math.ceil(bar.getBoundingClientRect().height);
    spacer.style.height = `${h}px`;
    document.documentElement.style.setProperty('--critical-update-bar', `${h}px`);
  }

  /** The how line: a span of words, then the command and the TrueNAS line where they apply. */
  function fillHow(parts) {
    const run = document.createElement('span');
    run.textContent = parts.run;
    const nodes = [run];
    if (parts.code) nodes.push(Object.assign(document.createElement('code'), { textContent: parts.code }));
    if (parts.nas) nodes.push(Object.assign(document.createElement('span'), { textContent: parts.nas }));
    howEl.replaceChildren(...nodes);
  }

  function show(critical, how) {
    if (!bar) build();
    reasonEl.textContent = critical.reason || '';
    fillHow(how);
    if (!bar.isConnected) {
      document.body.prepend(spacer);
      document.body.prepend(bar);
      document.documentElement.classList.add('has-critical-update');
    }
    fit();
  }
  function hide() {
    bar?.remove();
    spacer?.remove();
    document.documentElement.classList.remove('has-critical-update');
    document.documentElement.style.removeProperty('--critical-update-bar');
  }

  async function whereRunning() {
    try {
      const res = await fetch('/api/info', { credentials: 'same-origin', cache: 'no-store' });
      const info = res.ok ? await res.json() : null;
      return typeof info?.deployment === 'string' ? info.deployment : null;
    } catch {
      return null;
    }
  }

  async function check() {
    try {
      const res = await fetch('/api/updates', { credentials: 'same-origin', headers: { 'X-Requested-With': 'sentry-six-manager' }, cache: 'no-store' });
      if (!res.ok) return;
      const st = await res.json();
      if (st && st.critical && typeof st.critical.reason === 'string') {
        // without the server's own words, ask once whether this is the Windows service
        if (!deployment && !(typeof st.how === 'string' && st.how.trim())) deployment = st.deployment || await whereRunning();
        show(st.critical, howToInstall(st, deployment));
      } else hide();
    } catch {
      // offline or signed out: try again later
    }
  }

  function start() {
    check();
    setInterval(check, RECHECK_MS);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) check(); });
    window.addEventListener('resize', fit);
    // on phones it shrinks to a slim bar while scrolling (still there); a tap opens it again
    let lastY = 0;
    document.addEventListener('scroll', (e) => {
      if (!bar?.isConnected || !phone()) return;
      const t = e.target === document ? document.scrollingElement : e.target;
      const y = t?.scrollTop || 0;
      if (y > 40 && y > lastY) setCollapsed(true);
      else if (y < 10) setCollapsed(false);
      lastY = y;
    }, { capture: true, passive: true });
  }
  if (document.body) start();
  else document.addEventListener('DOMContentLoaded', start);
})();
