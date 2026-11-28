'use strict';

// Early data for the main page: the server lists the API calls this
// interface made the last times it opened (data-urls on this script tag),
// and they are asked for here, all at once, while the page's own scripts
// still load. When the page then asks for one of them (a GET of the same
// address), it gets that answer instead of waiting for a new one. Anything
// not asked for within 10 seconds is dropped; nothing else changes.
(() => {
  let urls = [];
  try { urls = JSON.parse(document.currentScript?.dataset.urls || '[]'); } catch { /* none */ }
  if (!Array.isArray(urls) || !urls.length || typeof window.fetch !== 'function') return;
  const realFetch = window.fetch.bind(window);
  const early = new Map(); // absolute URL -> Promise<Response>
  for (const u of urls.slice(0, 20)) {
    if (typeof u !== 'string' || !u.startsWith('/api/')) continue;
    const abs = new URL(u, location.href).href;
    const p = realFetch(abs, { credentials: 'same-origin', headers: { 'X-Requested-With': 'sentry-six-manager' } });
    p.catch(() => {});
    early.set(abs, p);
  }
  setTimeout(() => early.clear(), 10000);
  window.fetch = function fetch(input, init) {
    if (early.size && typeof input === 'string' && (!init || ((init.method || 'GET').toUpperCase() === 'GET' && !init.body && !init.signal?.aborted))) {
      let abs = null;
      try { abs = new URL(input, location.href).href; } catch { /* not a URL */ }
      const p = abs && early.get(abs);
      if (p) {
        early.delete(abs); // each answer is used once
        const fallback = p.catch(() => realFetch(input, init));
        const sig = init && init.signal;
        if (!sig) return fallback;
        // (the early request carries no signal: the caller's timeout or abort still ends its wait)
        return new Promise((res, rej) => {
          const onAbort = () => rej(sig.reason ?? new DOMException('Aborted', 'AbortError'));
          sig.addEventListener('abort', onAbort, { once: true });
          fallback.then(
            (r) => { sig.removeEventListener('abort', onAbort); res(r); },
            (e) => { sig.removeEventListener('abort', onAbort); rej(e); }
          );
        });
      }
    }
    return realFetch(input, init);
  };
})();
