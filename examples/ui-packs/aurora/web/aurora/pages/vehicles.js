// Encoding per vehicle: each vehicle's saved encoding settings in plain words,
// with an editor (the shared /web/vehicleEncoding.js, in Aurora's classes).
import '/web/vehicleEncoding.js';
import { esc, toast, confirmDialog } from '../core.js';

export default {
  title: 'Encoding per vehicle',
  kicker: 'Encoder',
  icon: 'orbit',
  wide: true,
  async mount(ctx) {
    const { body } = ctx;
    ctx.sub('What each vehicle converts with: codec, size, output, verification, clip groups, devices and automatic conversion.');
    body.innerHTML = '<p class="muted" style="margin:0 0 14px"><a href="#/encoder">Open the Encoder</a></p><div id="ve-root"></div>';
    const view = window.SentryVehicleEncoding.mount(body.querySelector('#ve-root'), {
      intro: false, edit: ctx.query?.get('edit') || null,
      classes: { card: 'panel', btn: 'btn', primary: 'btn primary', input: 'input', field: 'field', check: 'check', muted: 'muted' },
      toast: (text, bad) => toast(text, bad ? 'bad' : 'ok'),
      confirm: (text) => confirmDialog({ title: 'Replace originals?', body: `<p>${esc(text)}</p>`, ok: 'Replace originals', danger: true })
    });
    await view.ready;
    return { destroy() { view.destroy(); } };
  }
};
