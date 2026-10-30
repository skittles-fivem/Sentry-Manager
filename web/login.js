'use strict';

(async () => {
  const $ = (id) => document.getElementById(id);
  // Only ever continue to a page on this server: resolve against our origin
  // and refuse anything that ends up elsewhere (//host, /\host, tabs, ...).
  const safeNext = (() => {
    const raw = new URLSearchParams(location.search).get('next') || '/';
    try {
      const u = new URL(raw, location.origin);
      if (u.origin !== location.origin || !raw.startsWith('/') || /[\\\s]/.test(raw)) return '/';
      return u.pathname + u.search + u.hash;
    } catch {
      return '/';
    }
  })();
  // deployment (public GET /api/info): on the Windows service the setup code
  // and the fingerprint are in the install window and service.log, not in
  // docker logs; on a Mac the code comes from a Terminal command and the
  // fingerprint from the installer. The page's own words are Docker's.
  const [me, info] = await Promise.all([
    fetch('/api/auth/me').then((r) => r.json()).catch(() => ({})),
    fetch('/api/info').then((r) => (r.ok ? r.json() : {})).catch(() => ({}))
  ]);
  if (me.user) return location.replace(safeNext);
  const windows = info?.deployment === 'windows';
  if (windows) {
    const code = (text) => Object.assign(document.createElement('code'), { textContent: text });
    const log = '%ProgramData%\\SentrySixManager\\data\\logs\\service.log';
    $('code-row').querySelector('span').replaceChildren('Printed in the install window and in ', code(log));
    // after the line break: where else the fingerprint is shown
    const br = $('fingerprint').querySelector('br');
    while (br?.nextSibling) br.nextSibling.remove();
    br?.after('The same as in the install window, in ', code(log), ' and in your browser\'s certificate details.');
  }
  const mac = info?.deployment === 'macos';
  if (mac) {
    const code = (text) => Object.assign(document.createElement('code'), { textContent: text });
    $('code-row').querySelector('span').replaceChildren('In Terminal on the Mac, run ', code('sudo sentry-six-manager setup-code'));
    const br = $('fingerprint').querySelector('br');
    while (br?.nextSibling) br.nextSibling.remove();
    br?.after('The same as in the installer\'s output in Terminal and in your browser\'s certificate details.');
  }
  if (location.protocol !== 'https:') $('insecure').hidden = false;
  if (me.fingerprint) {
    // a line may break after each colon, never inside a byte
    $('fingerprint').querySelector('.mono').replaceChildren(...me.fingerprint.split(':').flatMap((part, n) => (n ? [':', document.createElement('wbr'), part] : [part])));
    $('fingerprint').hidden = false;
  }
  const setup = me.needsSetup;
  if (setup) {
    $('title').textContent = 'Create your account';
    $('lead').textContent = `This is the first start. Enter the setup code from ${windows ? 'the install window' : 'the container log'}, then choose the user name and password that protect this Sentry Six Manager.`;
    if (mac) $('lead').textContent = 'This is the first start. Enter the setup code (in Terminal on the Mac, run sudo sentry-six-manager setup-code), then choose the user name and password that protect this Sentry Six Manager.';
    $('confirm-row').hidden = false;
    $('code-row').hidden = false;
    $('submit').textContent = 'Create account';
    document.querySelector('[name=password]').autocomplete = 'new-password';
    document.querySelector('[name=password]').minLength = 8;
    document.querySelector('[name=code]').required = true;
  }
  $('form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const f = new FormData(e.target);
    if (setup && f.get('password') !== f.get('confirm')) {
      $('err').textContent = 'The passwords do not match.';
      return;
    }
    $('submit').disabled = true;
    const res = await fetch(setup ? '/api/auth/setup' : '/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'sentry-six-manager' },
      body: JSON.stringify({ username: f.get('username'), password: f.get('password'), ...(setup ? { code: f.get('code') } : {}) })
    }).catch(() => null);
    $('submit').disabled = false;
    if (res && res.ok) return location.replace(safeNext);
    const data = res ? await res.json().catch(() => ({})) : {};
    $('err').textContent = data.error || 'Could not reach Sentry Six Manager.';
  });
})();
