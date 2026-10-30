'use strict';
// Flux sign-in: login, first-run account setup (setup code), throttling.
(async () => {
  const $ = (id) => document.getElementById(id);
  // only ever continue to a page of this server
  const next = (() => {
    const raw = new URLSearchParams(location.search).get('next') || '/';
    if (!raw.startsWith('/') || raw.startsWith('//') || /[\\\s]/.test(raw)) return '/';
    try {
      const u = new URL(raw, location.origin);
      return u.origin === location.origin ? u.pathname + u.search + u.hash : '/';
    } catch {
      return '/';
    }
  })();
  $('host').textContent = location.host;
  // On the Windows service (public GET /api/info) the setup code and the
  // certificate fingerprint are in the install window and service.log, not
  // in docker logs. The page's own words are Docker's; they change here.
  const askInfo = fetch('/api/info', { credentials: 'same-origin' })
    .then((r) => (r.ok ? r.json() : {}));
  const askWindows = askInfo
    .then((info) => info?.deployment === 'windows', () => false);
  const askMac = askInfo.then((info) => info?.deployment === 'macos', () => false);
  let logWhere = 'the container log';
  function windowsWords() {
    logWhere = 'the install window';
    for (const code of document.querySelectorAll('code')) {
      if (code.textContent !== 'docker logs sentry-six-manager') continue;
      code.textContent = '%ProgramData%\\SentrySixManager\\data\\logs\\service.log';
      const before = code.previousSibling;
      if (before?.nodeType !== Node.TEXT_NODE) continue;
      const t = before.textContent;
      before.textContent = /container log: $/.test(t) ? t.replace(/container log: $/, 'install window and in ') : `${t}the install window, ${/\bin $/.test(t) ? 'in ' : ''}`;
    }
  }
  // On a Mac the setup code comes from a Terminal command, and the
  // fingerprint is in what the installer printed.
  function macWords() {
    logWhere = 'Terminal (sudo sentry-six-manager setup-code)';
    for (const code of [...document.querySelectorAll('code')]) {
      if (code.textContent !== 'docker logs sentry-six-manager') continue;
      const before = code.previousSibling;
      const t = before?.nodeType === Node.TEXT_NODE ? before.textContent : '';
      if (/(It’s p|P)rinted in the container log: $/.test(t)) {
        before.textContent = t.replace(/(It’s p|P)rinted in the container log: $/, 'In Terminal on the Mac, run: ');
        code.textContent = 'sudo sentry-six-manager setup-code';
      } else code.replaceWith('the installer’s output in Terminal');
    }
  }
  const me = await fetch('/api/auth/me', { credentials: 'same-origin' }).then((r) => r.json()).catch(() => ({}));
  if (me.user) {
    location.replace(next);
    return;
  }
  if (await askWindows) windowsWords();
  if (await askMac) macWords();
  if (location.protocol !== 'https:') $('insecure').hidden = false;
  if (me.fingerprint) {
    const box = $('fingerprint').querySelector('.mono');
    box.replaceChildren(...String(me.fingerprint).split(':').flatMap((part, i) => (i ? [':', document.createElement('wbr'), part] : [part])));
    $('fingerprint').hidden = false;
  }
  const setup = Boolean(me.needsSetup);
  const form = $('form');
  if (setup) {
    $('title').textContent = 'Create your account';
    $('lead').textContent = `First start. Enter the setup code from ${logWhere}, then choose the user name and password that protect this server.`;
    $('confirm-row').hidden = false;
    $('code-row').hidden = false;
    $('submit').textContent = 'Create account';
    form.password.autocomplete = 'new-password';
  }
  let lockTimer = null;
  function lockFor(seconds, text) {
    clearInterval(lockTimer);
    let left = seconds;
    const btn = $('submit');
    btn.disabled = true;
    const tick = () => {
      $('err').textContent = `${text.replace(/Try again in \d+ s\.?/, '').trim()} Try again in ${left} s.`;
      if (left-- <= 0) {
        clearInterval(lockTimer);
        btn.disabled = false;
        $('err').textContent = '';
      }
    };
    tick();
    lockTimer = setInterval(tick, 1000);
  }
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const f = new FormData(form);
    $('err').textContent = '';
    if (!f.get('username') || !f.get('password')) {
      $('err').textContent = 'Enter your user name and password.';
      return;
    }
    if (setup) {
      if (String(f.get('password')).length < 8) { $('err').textContent = 'Choose a password of at least 8 characters.'; return; }
      if (f.get('password') !== f.get('confirm')) { $('err').textContent = 'The passwords do not match.'; return; }
      if (!f.get('code')) { $('err').textContent = `Enter the setup code from ${logWhere}.`; return; }
    }
    $('submit').disabled = true;
    const res = await fetch(setup ? '/api/auth/setup' : '/api/auth/login', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'sentry-six-manager' },
      body: JSON.stringify({ username: f.get('username'), password: f.get('password'), ...(setup ? { code: f.get('code') } : {}) })
    }).catch(() => null);
    $('submit').disabled = false;
    if (res && res.ok) {
      location.replace(next);
      return;
    }
    const data = res ? await res.json().catch(() => ({})) : {};
    const msg = data.error || (res ? `Sign-in failed (${res.status}).` : 'Could not reach Sentry Six Manager.');
    if (res && res.status === 429) {
      const m = /(\d+)\s*s/.exec(msg);
      lockFor(m ? Number(m[1]) : 30, msg);
      return;
    }
    $('err').textContent = msg;
    form.password.select();
  });
})();
