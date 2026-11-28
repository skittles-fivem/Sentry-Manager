import { ownedRender } from '../core.js';
import { esc, act } from '../core.js';
import { serverBody, hostLine } from '../serverStats.js';
async function renderPage(el, ctx) {
  const { api, toast } = ctx.scope;
  const admin = (await api('/api/auth/me')).role === 'admin';
  const { createSavingsChart } = await ctx.scope.wait(import('/web/savingsChart.js'));
  if (!ctx.alive()) return;
  let range = '15m',
    snap = null,
    hist = null,
    histAt = 0,
    stopped = false,
    timer = null,
    loading = false,
    // why the last sample could not be read ('' once one comes)
    failed = '';
  const more = {},
    seen = new Map();
  el.innerHTML =
    '<a class="back" href="#/health">‹ Health</a><h2>Server details</h2><p data-host></p><div class="toolbar"><div class="segments"><button data-range="15m" class="active">15 min</button><button data-range="24h">24 h</button></div><a data-csv download>Download CSV</a></div><section class="card savings"><h3>Space freed by conversions</h3><div data-savings></div></section><div data-stats>Reading the server…</div>';
  const savings = createSavingsChart(el.querySelector('[data-savings]'), {
    vehicleId: ctx.current()?.id,
  });
  ctx.scope.signal.addEventListener('abort', () => savings.destroy(), {
    once: true,
  });
  function draw() {
    if (stopped || !ctx.alive()) return;
    el.querySelector('[data-host]').innerHTML = hostLine(snap?.host);
    el.querySelector('[data-stats]').innerHTML = !snap && failed
      ? `<div class="card"><p>${esc(failed)}</p><div class="form-actions"><button class="btn solid" data-again>Try again</button></div></div>`
      : snap
      ? serverBody(snap, {
          admin,
          range,
          hist,
          more,
          seen,
          currency: snap.price?.currency || ctx.state.power?.currency || '$',
        })
      : 'Waiting for a sample…';
    el.querySelector('[data-csv]').href = '/api/stats/export.csv?range=' + range;
    el.querySelectorAll('[data-range]').forEach((b) =>
      b.classList.toggle('active', b.dataset.range === range),
    );
  }
  async function poll() {
    clearTimeout(timer);
    if (stopped || !ctx.alive() || document.hidden || loading) return;
    loading = true;
    try {
      // (the day's history changes slowly: read again at most once a minute)
      const day = range === '24h' && Date.now() - histAt > 60000;
      const values = await Promise.all([
        api('/api/stats', { timeout: 10000 }),
        day ? api('/api/stats/history?range=24h', { timeout: 20000 }) : null,
      ]);
      snap = values[0];
      if (day) histAt = Date.now();
      if (day || range !== '24h') hist = values[1];
      failed = '';
      draw();
    } catch (e) {
      if (e?.name === 'AbortError' || stopped || !ctx.alive()) return;
      // said once, not on every poll; with nothing shown yet, in place of the numbers
      if (e.message !== failed) {
        failed = e.message;
        if (snap) toast(e.message, true);
        else draw();
      }
    } finally {
      loading = false;
      if (!stopped && ctx.alive() && !document.hidden) timer = setTimeout(poll, 2000);
    }
  }
  el.onclick = act(async (e) => {
    if (e.target.closest('[data-again]')) {
      failed = '';
      el.querySelector('[data-stats]').textContent = 'Reading the server…';
      await poll();
      return;
    }
    const b = e.target.closest('[data-range]'),
      m = e.target.closest('[data-nd-more]');
    if (b) {
      range = b.dataset.range;
      histAt = 0;
      seen.clear();
      await poll();
    }
    if (m) {
      more[m.dataset.ndMore] = !more[m.dataset.ndMore];
      draw();
    }
  });
  const visible = () => {
    clearTimeout(timer);
    if (!document.hidden) poll();
  };
  document.addEventListener('visibilitychange', visible);
  await poll();
  return () => {
    stopped = true;
    clearTimeout(timer);
    el.onclick = null;
    document.removeEventListener('visibilitychange', visible);
  };
}

export const render = ownedRender(renderPage);
