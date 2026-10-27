// Encoding per vehicle: each vehicle's saved encoding settings in plain words,
// with an editor (the shared /web/vehicleEncoding.js, in Horizon's classes).
import '/web/vehicleEncoding.js';
import { h, toast, ask, liveDraw } from '../core.js';

export default {
  async mount(el, { params, status }) {
    el.innerHTML = `<section class="ptitle hud"><div><h1 class="chrome">Encoding per vehicle</h1><p>What each vehicle converts with: codec, size, output, verification, clip groups, devices and automatic conversion. <a href="#/encoder">Open the Encoder</a></p></div></section><div id="ve-root"></div>`;
    const view = window.SentryVehicleEncoding.mount(el.querySelector('#ve-root'), {
      intro: false, edit: params?.get?.('edit') || null,
      classes: { card: 'panel hud', btn: 'btn', primary: 'btn go', input: 'in', field: 'fld', check: 'chk', muted: 'faint' },
      toast: (text, bad) => toast(text, bad ? 'bad' : 'ok'),
      confirm: (text) => ask({ title: 'Replace originals?', body: `<p>${h(text)}</p>`, yes: 'Replace originals', danger: true })
    });
    await view.ready;
    status?.('Saved settings per vehicle');
    return {
      // (held while text in it is highlighted, then caught up)
      onTopics(t) { if (t.includes('vehicles') && !el.contains(document.activeElement)) liveDraw(el, () => view.refresh().catch(() => {}), 'vehicles'); },
      unmount() { view.destroy(); }
    };
  }
};
