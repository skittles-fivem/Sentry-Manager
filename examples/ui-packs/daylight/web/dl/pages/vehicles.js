// Encoding per vehicle: each vehicle's saved encoding settings in plain words,
// with an editor (the shared /web/vehicleEncoding.js, in Daylight's classes).
import '/web/vehicleEncoding.js';
import { esc, toast, confirmDialog } from '../core.js';

export async function render(ctx) {
  const { view } = ctx;
  view.innerHTML = `<div class="page"><div class="page-head"><div><h1>Encoding per vehicle</h1><p class="lede">What each vehicle converts with: codec, size, output, checks, clip groups, devices and automatic conversion. <a href="#/encoder">Open the Encoder</a></p></div></div><div id="ve-root"></div></div>`;
  const v = window.SentryVehicleEncoding.mount(view.querySelector('#ve-root'), {
    intro: false, edit: ctx.query?.get('edit') || null,
    classes: { card: 'card', btn: 'btn', primary: 'btn btn-primary', input: 'input', field: 'field', check: 'check', muted: 'muted' },
    toast: (text, bad) => toast(text, { kind: bad ? 'bad' : 'good' }),
    confirm: (text) => confirmDialog({ title: 'Replace originals?', body: `<p>${esc(text)}</p>`, ok: 'Replace originals', danger: true })
  });
  ctx.cleanup(() => v.destroy());
  await v.ready;
}
