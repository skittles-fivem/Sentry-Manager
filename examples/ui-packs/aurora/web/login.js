'use strict';
// Aurora sign-in: sign in, or create the account on the first start (setup code
// from the container log). Throttled attempts (429) count down on the button.
(async () => {
  const $ = (id) => document.getElementById(id);
  const form = $('form');
  const err = $('err');
  const submit = $('submit');
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
  let me = {};
  try {
    me = await fetch('/api/auth/me', { credentials: 'same-origin' }).then((r) => r.json());
  } catch {
    err.textContent = 'Could not reach Sentry Six Manager. Check the address and reload.';
  }
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
  if (setup) {
    $('title').textContent = 'Create your account';
    $('lead').textContent = `First start: enter the setup code from ${logWhere}, then choose the user name and password that protect this server.`;
    $('confirm-row').hidden = false;
    $('code-row').hidden = false;
    submit.textContent = 'Create account';
    form.password.autocomplete = 'new-password';
  }
  (form.username.value ? form.password : form.username).focus();

  let waitTimer = null;
  function countDown(message) {
    const m = /(\d+)\s*s\b/.exec(message || '');
    let left = m ? Number(m[1]) : 0;
    clearInterval(waitTimer);
    if (!left) {
      err.textContent = message;
      return;
    }
    submit.disabled = true;
    const tick = () => {
      if (left <= 0) {
        clearInterval(waitTimer);
        submit.disabled = false;
        submit.textContent = setup ? 'Create account' : 'Sign in';
        err.textContent = 'You can try again now.';
        return;
      }
      err.textContent = `Too many attempts. Try again in ${left} s.`;
      submit.textContent = `Wait ${left} s`;
      left -= 1;
    };
    tick();
    waitTimer = setInterval(tick, 1000);
  }

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    err.textContent = '';
    const f = new FormData(form);
    const username = String(f.get('username') || '').trim();
    const password = String(f.get('password') || '');
    if (!username || !password) {
      err.textContent = 'Enter your user name and password.';
      return;
    }
    if (setup) {
      if (password.length < 8) { err.textContent = 'Choose a password of at least 8 characters.'; return; }
      if (password !== f.get('confirm')) { err.textContent = 'The passwords do not match.'; return; }
      if (!String(f.get('code') || '').trim()) { err.textContent = `Enter the setup code from ${logWhere}.`; return; }
    }
    submit.disabled = true;
    const label = submit.textContent;
    submit.textContent = setup ? 'Creating…' : 'Signing in…';
    let res = null;
    try {
      res = await fetch(setup ? '/api/auth/setup' : '/api/auth/login', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'sentry-six-manager' },
        body: JSON.stringify({ username, password, ...(setup ? { code: String(f.get('code') || '').trim() } : {}) })
      });
    } catch {
      res = null;
    }
    if (res && res.ok) {
      location.replace(next);
      return;
    }
    submit.disabled = false;
    submit.textContent = label;
    const data = res ? await res.json().catch(() => ({})) : {};
    if (res && res.status === 429) {
      countDown(data.error || 'Too many attempts. Try again later.');
      return;
    }
    err.textContent = data.error || 'Could not reach Sentry Six Manager.';
    form.password.select();
  });
})();
