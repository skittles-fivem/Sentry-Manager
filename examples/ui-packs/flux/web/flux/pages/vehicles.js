// Encoding per vehicle: each vehicle's saved encoding settings in plain words,
// with an editor (the shared /web/vehicleEncoding.js, in Flux's own classes).
import '/web/vehicleEncoding.js';
import { esc, toast, confirmDialog, liveDraw } from '../core.js';

export default {
  async mount(el, { params }) {
    el.innerHTML = `<div class="phead"><div><h1>Encoding per vehicle</h1><p class="muted small">What each vehicle converts with: codec, size, output, verification, clip groups, devices and automatic conversion. <a href="#/encoder">Open the Encoder</a></p></div></div><div id="ve-root"></div>`;
    const view = window.SentryVehicleEncoding.mount(el.querySelector('#ve-root'), {
      intro: false, edit: params?.get?.('edit') || null,
      classes: { card: 'card', btn: 'btn', primary: 'btn primary', input: 'input', field: 'field', check: 'check', muted: 'muted' },
      toast: (text, bad) => toast(text, bad ? 'bad' : 'ok'),
      confirm: (text) => confirmDialog({ title: 'Replace originals?', body: `<p>${esc(text)}</p>`, ok: 'Replace originals', danger: true })
    });
    await view.ready;
    return {
      // (held while text in it is highlighted, then caught up)
      onTopics(t) { if (t.includes('vehicles') && !el.contains(document.activeElement)) liveDraw(el, () => view.refresh().catch(() => {}), 'vehicles'); },
      unmount() { view.destroy(); }
    };
  }
};
