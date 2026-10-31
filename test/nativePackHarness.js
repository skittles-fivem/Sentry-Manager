'use strict';

// Shared by the native app's UI pack parity tests (test/windowsNative<Pack>*.test.js):
// runs a shipped pack's own scripts (examples/ui-packs/<pack>/web/...) in Node
// with a small pretend DOM, on fixed API answers, a fixed clock and timers
// that only run when the test says so, and records what they ask, show and say.
// The native pages (windows/app/Core/<Pack>/*.cs) work out the same things
// from the same answers; their Core.Tests compare against what this writes.
//
//   const { loadPack } = require('./nativePackHarness');
//   const pack = loadPack('horizon', { now, answers, confirm });
//   const page = await pack.mount('pages/jobs.js', { params: {} });
//   pack.outline(page.el, marks)   // the text in order, with markers
//
// Nothing here is a browser: only what the packs' pages use is pretended
// (innerHTML parsed into elements, querySelector with simple selectors,
// classList, dataset, events, forms' values).

process.env.TZ = 'UTC';

const fs = require('fs');
const path = require('path');

const REPO = path.join(__dirname, '..');

// ---------------------------------------------------------------- the DOM

const VOID = new Set(['input', 'br', 'wbr', 'img', 'hr', 'meta', 'link', 'source', 'use', 'path', 'circle', 'rect', 'line', 'polyline', 'polygon', 'stop']);
const RAW = new Set(['script', 'style']);
const ENT = { '&lt;': '<', '&gt;': '>', '&quot;': '"', '&#39;': "'", '&amp;': '&', '&nbsp;': ' ', '&times;': '×', '&middot;': '·', '&hellip;': '…', '&rarr;': '→', '&larr;': '←', '&mdash;': '—', '&ndash;': '–' };
const decode = (s) => s.replace(/&(?:[a-z]+|#\d+|#x[0-9a-f]+);/gi, (e) => {
  if (ENT[e] !== undefined) return ENT[e];
  if (/^&#x/i.test(e)) return String.fromCodePoint(parseInt(e.slice(3, -1), 16));
  if (/^&#/.test(e)) return String.fromCodePoint(Number(e.slice(2, -1)));
  return e;
});
const encode = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const encodeAttr = (s) => String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;');

class Text {
  constructor(text) { this.text = text; this.parent = null; this.nodeType = 3; }
  get textContent() { return this.text; }
  set textContent(t) { this.text = String(t); }
  get nodeValue() { return this.text; }
}

function parse(html, doc, parent) {
  const out = [];
  const stack = [{ children: out, root: true }];
  const re = /<!--[\s\S]*?-->|<[^>]*>|[^<]+/g;
  let m;
  const src = String(html);
  while ((m = re.exec(src))) {
    const t = m[0];
    const cur = stack[stack.length - 1];
    if (t[0] !== '<') { const x = new Text(decode(t)); x.parent = cur.root ? parent : cur; cur.children.push(x); continue; }
    if (t.startsWith('<!')) continue;
    if (t[1] === '/') {
      const tag = /^<\/([a-zA-Z0-9-]+)/.exec(t)[1].toLowerCase();
      for (let i = stack.length - 1; i > 0; i--) if (stack[i].tag === tag) { stack.length = i; break; }
      continue;
    }
    const tm = /^<([a-zA-Z0-9-]+)/.exec(t);
    if (!tm) { cur.children.push(new Text(t)); continue; }
    const tag = tm[1].toLowerCase();
    const attrs = new Map();
    for (const a of t.slice(tm[0].length).matchAll(/\s([a-zA-Z_:][a-zA-Z0-9_:.-]*)(?:\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>"']+)))?/g)) {
      const v = a[2] ?? a[3] ?? a[4];
      attrs.set(a[1].toLowerCase(), v === undefined ? '' : decode(v));
    }
    const n = new El(tag, attrs, doc);
    n.parent = cur.root ? parent : cur;
    cur.children.push(n);
    if (RAW.has(tag)) {
      const end = src.indexOf(`</${tag}`, re.lastIndex);
      const body = src.slice(re.lastIndex, end < 0 ? src.length : end);
      if (body) { const x = new Text(body); x.parent = n; n.children.push(x); }
      re.lastIndex = end < 0 ? src.length : src.indexOf('>', end) + 1;
      continue;
    }
    if (!VOID.has(tag) && !/\/>$/.test(t)) stack.push(n);
  }
  return out;
}

class Style {
  constructor(el) { Object.defineProperty(this, '_el', { value: el, enumerable: false }); }
  setProperty(k, v) { this[k] = String(v); }
  removeProperty(k) { delete this[k]; }
  getPropertyValue(k) { return this[k] || ''; }
}

class El {
  constructor(tag, attrs, doc) {
    this.tag = tag;
    this.tagName = tag.toUpperCase();
    this.nodeName = this.tagName;
    this.nodeType = 1;
    this.attrs = attrs || new Map();
    this.children = [];
    this.parent = null;
    this.doc = doc;
    this.listeners = {};
    this.style = new Style(this);
    for (const decl of String(this.attrs.get('style') || '').split(';')) {
      const i = decl.indexOf(':');
      if (i > 0) this.style[decl.slice(0, i).trim()] = decl.slice(i + 1).trim();
    }
    if (tag === 'input' || tag === 'textarea') {
      this._checked = this.attrs.has('checked');
      this._value = tag === 'textarea' ? null : (this.attrs.get('value') ?? '');
    }
    this.scrollTop = 0;
    this.scrollLeft = 0;
  }

  // ---- attributes
  get id() { return this.attrs.get('id') || ''; }
  set id(v) { this.attrs.set('id', String(v)); }
  getAttribute(k) { return this.attrs.has(k) ? this.attrs.get(k) : null; }
  setAttribute(k, v) { this.attrs.set(k, String(v)); if (k === 'value' && this.tag === 'input' && !this._dirty) this._value = String(v); }
  removeAttribute(k) { this.attrs.delete(k); }
  hasAttribute(k) { return this.attrs.has(k); }
  toggleAttribute(k, on) { const v = on === undefined ? !this.attrs.has(k) : on; if (v) this.attrs.set(k, ''); else this.attrs.delete(k); return v; }
  flag(k, on) { if (on) this.attrs.set(k, ''); else this.attrs.delete(k); }
  get hidden() { return this.attrs.has('hidden'); }
  set hidden(on) { this.flag('hidden', on); }
  get disabled() { return this.attrs.has('disabled'); }
  set disabled(on) { this.flag('disabled', on); }
  get open() { return this.attrs.has('open'); }
  set open(on) { this.flag('open', on); }
  get required() { return this.attrs.has('required'); }
  get readOnly() { return this.attrs.has('readonly'); }
  get type() { return this.attrs.get('type') || (this.tag === 'button' ? 'submit' : this.tag === 'select' ? 'select-one' : 'text'); }
  get name() { return this.attrs.get('name') || ''; }
  get href() { return this.attrs.get('href') || ''; }
  set href(v) { this.attrs.set('href', String(v)); }
  get src() { return this.attrs.get('src') || ''; }
  set src(v) { this.attrs.set('src', String(v)); }
  get title() { return this.attrs.get('title') || ''; }
  set title(v) { this.attrs.set('title', String(v)); }
  get placeholder() { return this.attrs.get('placeholder') || ''; }
  set placeholder(v) { this.attrs.set('placeholder', String(v)); }
  get min() { return this.attrs.get('min') || ''; }
  get max() { return this.attrs.get('max') || ''; }
  get className() { return this.attrs.get('class') || ''; }
  set className(c) { this.attrs.set('class', String(c)); }
  get classes() { return this.className.split(/\s+/).filter(Boolean); }
  get classList() {
    const self = this;
    const list = () => self.classes;
    const put = (l) => { self.className = l.join(' '); };
    return {
      contains: (c) => list().includes(c),
      add: (...cs) => { const l = list(); for (const c of cs) if (!l.includes(c)) l.push(c); put(l); },
      remove: (...cs) => put(list().filter((x) => !cs.includes(x))),
      toggle(c, on) {
        const has = list().includes(c);
        const want = on === undefined ? !has : Boolean(on);
        const l = list().filter((x) => x !== c);
        if (want) l.push(c);
        put(l);
        return want;
      },
      get length() { return list().length; }
    };
  }
  get dataset() {
    const self = this;
    const key = (k) => `data-${String(k).replace(/[A-Z]/g, (c) => `-${c.toLowerCase()}`)}`;
    return new Proxy({}, {
      get: (_, k) => (typeof k === 'string' && self.attrs.has(key(k)) ? self.attrs.get(key(k)) : undefined),
      set: (_, k, v) => { self.attrs.set(key(k), String(v)); return true; },
      deleteProperty: (_, k) => { self.attrs.delete(key(k)); return true; },
      has: (_, k) => self.attrs.has(key(k)),
      ownKeys: () => [...self.attrs.keys()].filter((k) => k.startsWith('data-')).map((k) => k.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase())),
      getOwnPropertyDescriptor: () => ({ enumerable: true, configurable: true })
    });
  }

  // ---- form values
  get checked() { return this._checked; }
  set checked(on) { this._checked = Boolean(on); }
  get options() { return this.all().filter((x) => x.tag === 'option'); }
  get selectedIndex() { const o = this.options; return o.indexOf(o.find((x) => x.value === this.value)); }
  set selectedIndex(i) { const o = this.options[i]; if (o) this.value = o.value; }
  get selected() { const s = this.closest('select'); return s ? s.value === this.value : this.attrs.has('selected'); }
  get value() {
    if (this.tag === 'select') {
      if (this._picked !== undefined) return this._picked;
      const opts = this.options;
      const sel = opts.filter((o) => o.attrs.has('selected')).pop() || opts[0];
      return sel ? sel.value : '';
    }
    if (this.tag === 'option') return this.attrs.has('value') ? this.attrs.get('value') : this.textContent;
    if (this.tag === 'textarea') return this._value === null ? this.textContent : this._value;
    if (this.tag === 'button') return this.attrs.get('value') || '';
    return this._value ?? '';
  }
  set value(v) {
    if (this.tag === 'select') { if (this.options.some((o) => o.value === String(v))) this._picked = String(v); else this._picked = ''; return; }
    this._value = String(v);
    this._dirty = true;
  }
  get valueAsNumber() { const n = Number(this.value); return this.value === '' ? NaN : n; }
  get files() { return this._files || []; }
  get form() { return this.closest('form'); }
  get elements() {
    const list = this.all().filter((n) => ['input', 'select', 'textarea', 'button'].includes(n.tag));
    const out = Object.assign([], list);
    for (const n of list) { const nm = n.attrs.get('name') || n.id; if (nm && !(nm in out)) out[nm] = n; }
    out.namedItem = (k) => out[k] || null;
    return out;
  }
  checkValidity() { return true; }
  reportValidity() { return true; }
  setCustomValidity() {}
  select() {}
  reset() {
    for (const n of this.all()) {
      if (n.tag === 'input') { n._value = n.attrs.get('value') ?? ''; n._checked = n.attrs.has('checked'); n._dirty = false; }
      if (n.tag === 'select') delete n._picked;
      if (n.tag === 'textarea') n._value = null;
    }
  }
  requestSubmit() { this.dispatch('submit'); }
  submit() { this.dispatch('submit'); }

  // ---- tree
  get childNodes() { return this.children; }
  get firstChild() { return this.children[0] || null; }
  get lastChild() { return this.children[this.children.length - 1] || null; }
  get elementKids() { return this.children.filter((c) => c instanceof El); }
  get firstElementChild() { return this.elementKids[0] || null; }
  get lastElementChild() { const k = this.elementKids; return k[k.length - 1] || null; }
  get childElementCount() { return this.elementKids.length; }
  get parentElement() { return this.parent instanceof El ? this.parent : null; }
  get parentNode() { return this.parent; }
  get nextElementSibling() { const p = this.parent; if (!p) return null; const k = p.elementKids; return k[k.indexOf(this) + 1] || null; }
  get previousElementSibling() { const p = this.parent; if (!p) return null; const k = p.elementKids; return k[k.indexOf(this) - 1] || null; }
  get isConnected() { for (let x = this; x; x = x.parent) if (x === this.doc.body || x === this.doc.documentElement) return true; return false; }
  contains(n) { for (let x = n; x; x = x.parent) if (x === this) return true; return false; }
  adopt(n) {
    if (typeof n === 'string') n = new Text(n);
    if (n.parent && n.parent.children) n.parent.children = n.parent.children.filter((c) => c !== n);
    n.parent = this;
    return n;
  }
  append(...ns) { for (const n of ns) { this.children.push(this.adopt(n)); if (this.doc.onAppend && n instanceof El) this.doc.onAppend(this, n); } }
  appendChild(n) { this.append(n); return n; }
  prepend(...ns) { this.children.unshift(...ns.map((n) => this.adopt(n))); }
  insertBefore(n, ref) { this.adopt(n); const i = ref ? this.children.indexOf(ref) : -1; if (i < 0) this.children.push(n); else this.children.splice(i, 0, n); return n; }
  before(...ns) { const p = this.parent; if (p) for (const n of ns) p.insertBefore(n, this); }
  after(...ns) { const p = this.parent; if (!p) return; let ref = this; for (const n of ns) { const i = p.children.indexOf(ref); p.adopt(n); p.children.splice(i + 1, 0, n); ref = n; } }
  removeChild(n) { this.children = this.children.filter((c) => c !== n); n.parent = null; return n; }
  replaceChildren(...ns) { for (const c of this.children) c.parent = null; this.children = []; this.append(...ns); }
  replaceWith(...ns) { const p = this.parent; if (!p) return; const i = p.children.indexOf(this); const add = ns.map((n) => p.adopt(n)); p.children.splice(p.children.indexOf(this), 1, ...add); this.parent = null; void i; }
  remove() { if (this.parent && this.parent.children) this.parent.children = this.parent.children.filter((c) => c !== this); this.parent = null; }
  cloneNode(deep) { const n = new El(this.tag, new Map(this.attrs), this.doc); if (deep) n.innerHTML = this.innerHTML; return n; }
  get textContent() { return this.children.map((c) => c.textContent).join(''); }
  set textContent(t) { for (const c of this.children) c.parent = null; this.children = []; if (String(t ?? '') !== '') this.append(new Text(String(t ?? ''))); }
  get innerText() { return this.textContent; }
  set innerText(t) { this.textContent = t; }
  set innerHTML(html) { for (const c of this.children) c.parent = null; this.children = parse(html, this.doc, this); }
  get innerHTML() { return this.children.map((c) => (c instanceof El ? c.outerHTML : encode(c.text))).join(''); }
  get outerHTML() {
    const a = [...this.attrs].map(([k, v]) => (v === '' ? ` ${k}` : ` ${k}="${encodeAttr(v)}"`)).join('');
    return VOID.has(this.tag) ? `<${this.tag}${a}>` : `<${this.tag}${a}>${this.innerHTML}</${this.tag}>`;
  }
  insertAdjacentHTML(where, html) {
    // (no parent yet: append() adopts them, else they would be dropped)
    const ns = parse(html, this.doc, null);
    if (where === 'beforeend') this.append(...ns);
    else if (where === 'afterbegin') this.prepend(...ns);
    else if (where === 'beforebegin') this.before(...ns);
    else this.after(...ns);
  }
  insertAdjacentElement(where, n) { this.insertAdjacentHTML(where, ''); if (where === 'beforeend') this.append(n); else if (where === 'afterbegin') this.prepend(n); else if (where === 'beforebegin') this.before(n); else this.after(n); return n; }
  all() {
    const out = [];
    const walk = (n) => { for (const c of n.children) if (c instanceof El) { out.push(c); walk(c); } };
    walk(this);
    return out;
  }
  querySelectorAll(sel) { const list = this.all().filter((n) => matches(n, sel, this)); list.item = (i) => list[i]; return list; }
  querySelector(sel) { return this.all().find((n) => matches(n, sel, this)) || null; }
  getElementsByTagName(t) { return this.all().filter((n) => n.tag === t.toLowerCase()); }
  closest(sel) { for (let x = this; x instanceof El; x = x.parent) if (matches(x, sel, null)) return x; return null; }
  matches(sel) { return matches(this, sel, null); }

  // ---- events
  addEventListener(type, fn, opts) { (this.listeners[type] = this.listeners[type] || []).push({ fn, once: Boolean(opts && opts.once) }); }
  removeEventListener(type, fn) { this.listeners[type] = (this.listeners[type] || []).filter((l) => l.fn !== fn); }
  dispatchEvent(ev) { return this.fire(ev); }
  fire(ev) {
    if (!ev.target) ev.target = this;
    let stop = false;
    ev.stopPropagation = () => { stop = true; };
    ev.stopImmediatePropagation = () => { stop = true; };
    if (!ev.preventDefault) ev.preventDefault = () => { ev.defaultPrevented = true; };
    const results = [];
    for (let x = this; x && !stop; x = x.parent) {
      if (!(x instanceof El) && x !== this.doc) break;
      ev.currentTarget = x;
      const prop = x[`on${ev.type}`];
      if (typeof prop === 'function') results.push(prop.call(x, ev));
      for (const l of [...((x.listeners && x.listeners[ev.type]) || [])]) {
        if (l.once) x.removeEventListener(ev.type, l.fn);
        results.push(l.fn.call(x, ev));
      }
      if (ev.bubbles === false) break;
      if (!x.parent && x !== this.doc && this.isConnected) { x = this.doc; ev.currentTarget = x; for (const l of [...(x.listeners[ev.type] || [])]) results.push(l.fn.call(x, ev)); break; }
    }
    this.doc.pending.push(...results.filter((r) => r && typeof r.then === 'function'));
    return !ev.defaultPrevented;
  }
  dispatch(type, extra = {}) { return this.fire({ type, bubbles: !['focus', 'blur', 'load', 'error'].includes(type), ...extra }); }
  click() {
    if (this.disabled) return;
    if (this.tag === 'input' && (this.type === 'checkbox')) this._checked = !this._checked;
    if (this.tag === 'input' && this.type === 'radio') {
      const f = this.closest('form') || this.doc.body;
      for (const r of f.querySelectorAll(`input[type="radio"][name="${this.name}"]`)) r._checked = r === this;
    }
    const ok = this.fire({ type: 'click', bubbles: true, button: 0 });
    if (this.tag === 'input' && ['checkbox', 'radio'].includes(this.type)) { this.dispatch('input'); this.dispatch('change'); }
    if (ok && this.tag === 'button' && this.type === 'submit') {
      const dlg = this.closest('dialog');
      const f = this.closest('form');
      if (dlg && f && f.attrs.get('method') === 'dialog') { dlg.returnValue = this.value; dlg.close(this.value); } else if (f) f.dispatch('submit', { submitter: this });
    }
    if (ok && this.tag === 'summary' && this.parent && this.parent.tag === 'details') { this.parent.open = !this.parent.open; this.parent.dispatch('toggle', { bubbles: false }); }
  }
  /** The reader types: the value, then input (and change). */
  type_(text, change = true) { this.value = text; this.dispatch('input'); if (change) this.dispatch('change'); }
  pick(value) { this.value = value; this.dispatch('input'); this.dispatch('change'); }
  key(key, extra = {}) { return this.fire({ type: 'keydown', key, bubbles: true, ...extra }); }
  focus() { this.doc.activeElement = this; }
  blur() { if (this.doc.activeElement === this) this.doc.activeElement = this.doc.body; }

  // ---- layout (none: sizes are zero)
  getBoundingClientRect() { return { x: 0, y: 0, left: 0, top: 0, right: 0, bottom: 0, width: 0, height: 0 }; }
  get offsetWidth() { return 0; }
  get offsetHeight() { return 0; }
  get offsetLeft() { return 0; }
  get offsetTop() { return 0; }
  get clientWidth() { return 0; }
  get clientHeight() { return 0; }
  get scrollWidth() { return 0; }
  get scrollHeight() { return 0; }
  scrollTo() {}
  scrollBy() {}
  scrollIntoView() {}
  animate() { return { finished: Promise.resolve(), cancel() {}, onfinish: null }; }
  getContext() { return null; }
  setPointerCapture() {}
  releasePointerCapture() {}
  // <dialog>
  showModal() { this.open = true; this.doc.dialogs.push(this); if (this.doc.onModal) this.doc.onModal(this); }
  show() { this.open = true; }
  close(v) { if (!this.open) return; this.open = false; if (v !== undefined) this.returnValue = v; this.dispatch('close', { bubbles: false }); }
  // media
  play() { return Promise.resolve(); }
  pause() {}
  load() {}
}

// ---- selectors: compound selectors, descendant and child combinators, lists
function compound(n, c) {
  const m = /^([a-z0-9*-]*)((?:#[\w-]+|\.[\w-]+|\[[^\]]+\]|:[a-z-]+(?:\([^)]*\))?)*)$/i.exec(c);
  if (!m) throw new Error(`selector not understood: ${c}`);
  if (m[1] && m[1] !== '*' && n.tag !== m[1].toLowerCase()) return false;
  for (const x of m[2].matchAll(/#([\w-]+)|\.([\w-]+)|\[([\w:-]+)(?:([~^$*|]?=)(?:"([^"]*)"|'([^']*)'|([^\]]*)))?\]|:([a-z-]+)(?:\(([^)]*)\))?/gi)) {
    if (x[1] && n.id !== x[1]) return false;
    if (x[2] && !n.classes.includes(x[2])) return false;
    if (x[3]) {
      const k = x[3].toLowerCase();
      if (!n.attrs.has(k)) return false;
      const want = x[5] ?? x[6] ?? x[7];
      if (want !== undefined) {
        const have = n.attrs.get(k);
        const op = x[4];
        if (op === '=' && have !== want) return false;
        if (op === '~=' && !have.split(/\s+/).includes(want)) return false;
        if (op === '^=' && !have.startsWith(want)) return false;
        if (op === '$=' && !have.endsWith(want)) return false;
        if (op === '*=' && !have.includes(want)) return false;
      }
    }
    if (x[8]) {
      const p = x[8].toLowerCase();
      if (p === 'checked' && !n.checked) return false;
      if (p === 'disabled' && !n.disabled) return false;
      if (p === 'enabled' && n.disabled) return false;
      if (p === 'focus' && n.doc.activeElement !== n) return false;
      if (p === 'focus-visible' && n.doc.activeElement !== n) return false;
      if (p === 'not' && matches(n, x[9], null)) return false;
      if (p === 'first-child' && n.parent && n.parent.elementKids[0] !== n) return false;
      if (p === 'last-child' && n.parent && n.parent.lastElementChild !== n) return false;
      if (p === 'scope') { /* the root of the search */ }
    }
  }
  return true;
}

function matchOne(n, sel, scope) {
  const parts = sel.trim().replace(/\s*>\s*/g, ' > ').split(/\s+/);
  const last = parts.pop();
  if (!compound(n, last.replace(/:scope/g, ''))) return false;
  let x = n;
  while (parts.length) {
    const comb = parts[parts.length - 1] === '>' ? parts.pop() : ' ';
    const want = parts.pop();
    if (want === ':scope') { if (comb === '>') return x.parent === scope; return scope ? scope.contains(x) && x !== scope : true; }
    if (comb === '>') {
      x = x.parent;
      if (!(x instanceof El) || x === scope || !compound(x, want)) return false;
    } else {
      x = x.parent;
      while (x instanceof El && x !== scope && !compound(x, want)) x = x.parent;
      if (!(x instanceof El) || x === scope) return false;
    }
  }
  return true;
}

function splitList(sel) {
  const out = [];
  let depth = 0;
  let cur = '';
  for (const ch of sel) {
    if (ch === '(' || ch === '[') depth++;
    if (ch === ')' || ch === ']') depth--;
    if (ch === ',' && depth === 0) { out.push(cur); cur = ''; } else cur += ch;
  }
  out.push(cur);
  return out.map((s) => s.trim()).filter(Boolean);
}

function matches(n, sel, scope) {
  return splitList(sel).some((s) => matchOne(n, s, scope));
}

class FakeFormData {
  constructor(form) {
    this.list = [];
    if (!form) return;
    for (const n of form.all()) {
      const name = n.attrs.get('name');
      if (!name || n.disabled || n.closest('fieldset[disabled]')) continue;
      if (n.tag === 'input') {
        const type = n.type;
        if (type === 'radio' || type === 'checkbox') { if (n.checked) this.list.push([name, n.attrs.get('value') ?? 'on']); } else if (type !== 'button' && type !== 'submit' && type !== 'file') this.list.push([name, n.value]);
      } else if (n.tag === 'select' || n.tag === 'textarea') this.list.push([name, n.value]);
    }
  }
  get(k) { const e = this.list.find((x) => x[0] === k); return e ? e[1] : null; }
  getAll(k) { return this.list.filter((x) => x[0] === k).map((x) => x[1]); }
  has(k) { return this.list.some((x) => x[0] === k); }
  set(k, v) { this.list = this.list.filter((x) => x[0] !== k); this.list.push([k, String(v)]); }
  append(k, v) { this.list.push([k, v]); }
  delete(k) { this.list = this.list.filter((x) => x[0] !== k); }
  entries() { return this.list[Symbol.iterator](); }
  keys() { return this.list.map((x) => x[0])[Symbol.iterator](); }
  [Symbol.iterator]() { return this.list[Symbol.iterator](); }
}

function makeDocument() {
  const doc = { pending: [], dialogs: [], listeners: {}, visibilityState: 'visible', hidden: false, title: '' };
  doc.documentElement = new El('html', new Map(), doc);
  doc.body = new El('body', new Map(), doc);
  doc.documentElement.append(doc.body);
  doc.activeElement = doc.body;
  doc.createElement = (t) => new El(String(t).toLowerCase(), new Map(), doc);
  doc.createElementNS = (_, t) => new El(String(t).toLowerCase(), new Map(), doc);
  doc.createTextNode = (t) => new Text(String(t));
  doc.createDocumentFragment = () => new El('#fragment', new Map(), doc);
  doc.querySelector = (s) => doc.documentElement.querySelector(s);
  doc.querySelectorAll = (s) => doc.documentElement.querySelectorAll(s);
  doc.getElementById = (id) => doc.documentElement.querySelector(`#${id}`);
  doc.addEventListener = (t, fn) => { (doc.listeners[t] = doc.listeners[t] || []).push({ fn }); };
  doc.removeEventListener = (t, fn) => { doc.listeners[t] = (doc.listeners[t] || []).filter((l) => l.fn !== fn); };
  doc.getSelection = () => ({ isCollapsed: true, rangeCount: 0, toString: () => '' });
  doc.fonts = { ready: Promise.resolve() };
  doc.hasFocus = () => true;
  return doc;
}

// ---------------------------------------------------------------- what a reader sees

/**
 * The text a reader sees (trimmed text nodes, in order; hidden elements,
 * <template>, <script>, <style>, <svg> and aria-hidden ones left out) with
 * markers: mark(el) gives a marker for an element ("[pill grn]") or null.
 * A form control's value is its marker's business.
 */
function outline(root, mark = () => null) {
  const out = [];
  const walk = (n) => {
    for (const c of n.children) {
      if (!(c instanceof El)) { const s = c.text.replace(/\s+/g, ' ').trim(); if (s) out.push(s); continue; }
      if (c.hidden || ['template', 'script', 'style', 'svg', 'option'].includes(c.tag) || c.attrs.get('aria-hidden') === 'true') {
        const m = mark(c);
        if (m && c.tag === 'option') out.push(m);
        continue;
      }
      if (c.classes.includes('sr-only')) continue;
      const m = mark(c);
      if (m) out.push(m);
      if (c.tag === 'select') { for (const o of c.options) { const om = mark(o); if (om) out.push(om); } continue; }
      walk(c);
    }
  };
  walk(root);
  return out;
}

/** The visible text of some HTML (a dialog's body), markers left out. */
function textOf(html) {
  const doc = makeDocument();
  const n = new El('div', new Map(), doc);
  n.innerHTML = html;
  return outline(n);
}

// ---------------------------------------------------------------- the pack's modules

const readSrc = (pack, rel) => fs.readFileSync(path.join(REPO, 'examples', 'ui-packs', pack, 'web', rel), 'utf8');

/** export'ed names of a module's source. */
function exportsOf(src) {
  const names = new Set();
  for (const m of src.matchAll(/^export\s+(?:async\s+)?(?:function\s*\*?|const|let|var|class)\s+([A-Za-z_$][\w$]*)/gm)) names.add(m[1]);
  for (const m of src.matchAll(/^export\s*\{([^}]*)\}/gm)) for (const p of m[1].split(',')) { const x = p.trim().split(/\s+as\s+/); if (x[0]) names.add((x[1] || x[0]).trim()); }
  return [...names];
}

/** An ES module's source as a function body: imports read from __imp(spec), exports into __exp. */
function asFunction(src) {
  const names = exportsOf(src);
  let body = src
    .replace(/^import\s*\{([^}]*)\}\s*from\s*['"]([^'"]+)['"];?/gm, (_, list, spec) => `const {${list.replace(/\s+as\s+/g, ': ')}} = __imp(${JSON.stringify(spec)});`)
    .replace(/^import\s+\*\s+as\s+(\w+)\s+from\s*['"]([^'"]+)['"];?/gm, (_, name, spec) => `const ${name} = __imp(${JSON.stringify(spec)});`)
    .replace(/^import\s+(\w+)\s+from\s*['"]([^'"]+)['"];?/gm, (_, name, spec) => `const ${name} = __imp(${JSON.stringify(spec)}).default;`)
    .replace(/^import\s*['"][^'"]+['"];?/gm, '')
    .replace(/^export\s+default\s+/m, '__exp.default = ')
    .replace(/^export\s*\{[^}]*\};?/gm, '')
    .replace(/^export\s+(async\s+function|function|const|let|var|class)\b/gm, '$1')
    // import.meta.url (a module's own address)
    .replace(/import\.meta\.url/g, '__meta_url')
    // dynamic import() of a page
    .replace(/\bimport\(/g, '__dyn(');
  return { body, names };
}

const GLOBALS = ['window', 'document', 'location', 'history', 'navigator', 'localStorage', 'sessionStorage', 'matchMedia', 'fetch', 'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'requestAnimationFrame', 'cancelAnimationFrame', 'queueMicrotask', 'Date', 'FormData', 'URLSearchParams', 'URL', 'Event', 'CustomEvent', 'KeyboardEvent', 'HTMLElement', 'Element', 'Node', 'getComputedStyle', 'ResizeObserver', 'IntersectionObserver', 'MutationObserver', 'AbortController', 'Image', 'EventSource', 'innerWidth', 'innerHeight', 'devicePixelRatio', 'performance', 'alert', 'confirm', 'prompt', 'open', 'scrollTo', 'addEventListener', 'removeEventListener', 'dispatchEvent', 'globalThis', 'self', 'console', 'crypto', 'Blob', 'File', 'FileReader', 'TextDecoder', 'TextEncoder'];

/**
 * A shipped pack's scripts with everything around them pretended.
 * o.now: the clock (ms); o.answers: { "GET /api/x": answer } ({ $error, $status, $code } fails,
 * { $seq: [...] } answers in turn, { $pending: true } never answers); a POST/PUT/DELETE
 * without an answer gives { ok: true }, a GET without one fails ("no answer for GET ...").
 * o.conv: { name: value } converter answers (conv(name) gives { ok: true, value }).
 * o.confirm: true/false or a list (each ask()/confirm in turn); o.prompts: answers for askText().
 * o.storage: localStorage contents; o.width: innerWidth (1400).
 * o.overrides(core): replace more of core.js (e.g. { loadCars }).
 */
function loadPack(pack, o = {}) {
  const NOW = o.now ?? Date.UTC(2026, 9, 31, 15, 10, 2);
  const doc = makeDocument();
  const used = {};
  const rec = { asked: [], toasts: [], dialogs: [], conv: [], navigations: [], timers: [], opened: [], status: [] };
  let clock = NOW;
  // ---- timers: only run when the test says (flush)
  let tid = 0;
  const timers = new Map();
  const setT = (fn, ms = 0, ...a) => { const id = ++tid; timers.set(id, { fn, ms: Number(ms) || 0, a, at: clock + (Number(ms) || 0), every: false }); return id; };
  const setI = (fn, ms = 0, ...a) => { const id = ++tid; timers.set(id, { fn, ms: Number(ms) || 0, a, at: clock + (Number(ms) || 0), every: true }); return id; };
  const clearT = (id) => { timers.delete(id); };
  // ---- the clock
  const RealDate = Date;
  class FDate extends RealDate {
    constructor(...a) { if (a.length) super(...a); else super(clock); }
    static now() { return clock; }
    toLocaleString(l, opt) { return super.toLocaleString(l || 'en-US', opt); }
    toLocaleDateString(l, opt) { return super.toLocaleDateString(l || 'en-US', opt); }
    toLocaleTimeString(l, opt) { return super.toLocaleTimeString(l || 'en-US', opt); }
  }
  FDate.UTC = RealDate.UTC;
  FDate.parse = RealDate.parse;
  // ---- storage
  const store = new Map(Object.entries(o.storage || {}));
  const localStorage = {
    getItem: (k) => (store.has(k) ? store.get(k) : null),
    setItem: (k, v) => { store.set(k, String(v)); },
    removeItem: (k) => { store.delete(k); },
    clear: () => store.clear(),
    key: (i) => [...store.keys()][i] ?? null,
    get length() { return store.size; }
  };
  // ---- the network
  const answerFor = (key) => {
    const answers = o.answers || {};
    if (!Object.prototype.hasOwnProperty.call(answers, key)) return undefined;
    let a = answers[key];
    if (a && a.$seq) { const i = used[key] || 0; used[key] = i + 1; a = a.$seq[Math.min(i, a.$seq.length - 1)]; }
    return a;
  };
  const clone = (x) => (x === undefined ? undefined : JSON.parse(JSON.stringify(x)));
  const location = { hash: o.hash || '', pathname: '/', search: '', href: 'https://sentry.local:9443/', host: 'sentry.local:9443', hostname: 'sentry.local', protocol: 'https:', origin: 'https://sentry.local:9443', reload() { rec.navigations.push('reload'); }, assign(u) { rec.navigations.push(String(u)); }, replace(u) { rec.navigations.push(String(u)); } };
  const fetchFake = async (url, init = {}) => {
    const key = `${(init.method || 'GET').toUpperCase()} ${url}`;
    let a = answerFor(key);
    // (a write nobody gave an answer for: { ok: true }, as the comment above says)
    if (a === undefined && (init.method || 'GET').toUpperCase() !== 'GET') a = { ok: true };
    rec.asked.push(init.body !== undefined && init.body !== '' && typeof init.body === 'string' ? `${key} ${init.body}` : key);
    if (a === undefined || (a && a.$pending)) return new Promise(() => {});
    const status = a && a.$error ? a.$status || 500 : 200;
    const body = a && a.$error ? { error: a.$error, ...(a.$code ? { code: a.$code } : {}) } : a;
    return { ok: status < 300, status, statusText: '', json: async () => clone(body), text: async () => JSON.stringify(body), headers: { get: () => null } };
  };
  const win = {
    innerWidth: o.width || 1400,
    innerHeight: o.height || 900,
    devicePixelRatio: 1,
    listeners: {},
    addEventListener(t, fn) { (this.listeners[t] = this.listeners[t] || []).push(fn); },
    removeEventListener(t, fn) { this.listeners[t] = (this.listeners[t] || []).filter((f) => f !== fn); },
    dispatchEvent() { return true; },
    matchMedia: (q) => ({ matches: mediaMatches(q, o.width || 1400), media: q, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} }),
    getComputedStyle: () => ({ getPropertyValue: () => '' }),
    scrollTo() {},
    open(u) { rec.opened.push(String(u)); return null; }
  };
  win.window = win;
  win.self = win;
  win.document = doc;
  win.location = location;
  const globals = {
    window: win,
    self: win,
    globalThis: win,
    document: doc,
    location,
    history: { pushState() {}, replaceState() {}, back() {} },
    navigator: { userAgent: 'node', platform: 'Win32', clipboard: { writeText: async (t) => { rec.copied = t; } }, maxTouchPoints: 0, onLine: true },
    localStorage,
    sessionStorage: localStorage,
    matchMedia: win.matchMedia,
    fetch: fetchFake,
    setTimeout: setT,
    clearTimeout: clearT,
    setInterval: setI,
    clearInterval: clearT,
    requestAnimationFrame: (fn) => setT(() => fn(clock), 16),
    cancelAnimationFrame: clearT,
    queueMicrotask,
    Date: FDate,
    FormData: FakeFormData,
    URLSearchParams,
    URL,
    Event: class { constructor(type, init = {}) { this.type = type; Object.assign(this, init); } },
    CustomEvent: class { constructor(type, init = {}) { this.type = type; this.detail = init.detail; Object.assign(this, init); } },
    KeyboardEvent: class { constructor(type, init = {}) { this.type = type; Object.assign(this, init); } },
    HTMLElement: El,
    Element: El,
    Node: El,
    getComputedStyle: win.getComputedStyle,
    ResizeObserver: class { observe() {} unobserve() {} disconnect() {} },
    IntersectionObserver: class { observe() {} unobserve() {} disconnect() {} },
    MutationObserver: class { observe() {} disconnect() {} },
    AbortController,
    Image: class { set src(v) { this._src = v; } get src() { return this._src; } },
    EventSource: class { constructor() { this.readyState = 0; } addEventListener() {} close() {} },
    innerWidth: win.innerWidth,
    innerHeight: win.innerHeight,
    devicePixelRatio: 1,
    performance: { now: () => clock - NOW },
    alert: (t) => { rec.dialogs.push({ alert: String(t) }); },
    confirm: (t) => { const v = nextConfirm(); rec.dialogs.push({ confirm: String(t), answer: v }); return v; },
    prompt: () => null,
    open: win.open,
    scrollTo() {},
    addEventListener: (t, fn) => win.addEventListener(t, fn),
    removeEventListener: (t, fn) => win.removeEventListener(t, fn),
    dispatchEvent: () => true,
    console: { log() {}, warn() {}, error() {}, info() {}, debug() {} },
    crypto: { randomUUID: () => '00000000-0000-4000-8000-000000000000', getRandomValues: (a) => a },
    Blob: class { constructor(parts, opt) { this.parts = parts; this.type = opt && opt.type; } },
    File: class {},
    FileReader: class {},
    TextDecoder,
    TextEncoder
  };
  Object.assign(win, globals, { window: win });
  // ---- the reader's answers
  const confirms = Array.isArray(o.confirm) ? [...o.confirm] : null;
  const nextConfirm = () => (confirms ? Boolean(confirms.length ? confirms.shift() : false) : Boolean(o.confirm));
  const prompts = [...(o.prompts || [])];

  // ---- the pack's own page around its scripts (index.html's body: the dialog, the toasts box...)
  if (o.html !== false) {
    const html = readSrc(pack, o.html || 'index.html');
    const b = /<body[^>]*>([\s\S]*)<\/body>/i.exec(html);
    doc.body.innerHTML = (b ? b[1] : html).replace(/<script[\s\S]*?<\/script>/gi, '');
  }
  // toasts: the elements put into the toast box (their text is read at the end)
  const toastEls = [];
  doc.onAppend = (parent, child) => { if (o.toasts && parent.matches(o.toasts)) toastEls.push(child); };
  Object.defineProperty(rec, 'toasts', { enumerable: true, get: () => toastEls.map((t) => ({ text: t.textContent.trim(), tone: t.classes.filter((c) => c !== 'toast' && c !== 'gone').join(' ') })) });
  // dialogs: what they say, then the reader's answer (o.confirm / o.prompts), as a click on its button
  doc.onModal = (dlg) => {
    setImmediate(() => {
      const text = outline(dlg, (e) => (e.tag === 'button' ? `[btn${e.attrs.get('value') ? ` ${e.attrs.get('value')}` : ''}${e.classes.filter((c) => c !== 'btn').map((c) => ` ${c}`).join('')}]` : e.tag === 'input' ? `[input ${e.name}=${e.value}]` : null));
      const input = dlg.querySelector('input[name="v"], input:not([type="hidden"])');
      const answer = (o.answerDialog || defaultAnswer)(dlg, { input, text });
      rec.dialogs.push({ text, answer: answer.label });
      if (answer.value !== undefined && input) input.value = answer.value;
      if (answer.click) answer.click.click(); else dlg.close(answer.returnValue);
    });
  };
  function defaultAnswer(dlg, { input }) {
    const btns = dlg.querySelectorAll('button');
    const yes = btns.find((b) => ['yes', 'ok', 'confirm', 'go'].includes(b.attrs.get('value'))) || null;
    const no = btns.find((b) => ['no', 'cancel', 'close'].includes(b.attrs.get('value'))) || null;
    if (input && input.type !== 'checkbox') {
      const v = prompts.length ? prompts.shift() : null;
      if (v === null) return { label: 'cancel', click: no, returnValue: 'no' };
      return { label: `type ${v}`, value: v, click: yes, returnValue: 'yes' };
    }
    if (!yes) return { label: 'close', click: no, returnValue: 'close' };
    return nextConfirm() ? { label: 'yes', click: yes, returnValue: 'yes' } : { label: 'no', click: no, returnValue: 'no' };
  }

  const modules = new Map();
  const importer = (from) => (spec) => {
    const rel = path.posix.normalize(path.posix.join(path.posix.dirname(from), spec));
    return load(rel);
  };
  function load(rel) {
    if (modules.has(rel)) return modules.get(rel);
    const src = readSrc(pack, rel);
    const { body, names } = asFunction(src);
    const exp = {};
    modules.set(rel, exp);
    const hook = o.hook && o.hook[rel];
    const tail = hook ? hook.tail || '' : '';
    // eslint-disable-next-line no-new-func
    const fn = new Function('__imp', '__exp', '__dyn', '__meta_url', '__hook', ...GLOBALS, `${body}\n${tail}\n;return {${names.join(',')}};`);
    const values = fn(importer(rel), exp, (spec) => Promise.resolve(load(path.posix.normalize(path.posix.join(path.posix.dirname(rel), spec)))), `https://sentry.local:9443/${rel}`, hook || {}, ...GLOBALS.map((g) => globals[g]));
    Object.assign(exp, values, exp.default ? { default: exp.default } : {});
    return exp;
  }

  /** Runs every timer due within ms (a debounce, a poll), in time order. */
  async function flush(ms = 0, max = 200) {
    const until = clock + ms;
    for (let n = 0; n < max; n++) {
      await settle();
      let next = null;
      for (const [id, t] of timers) if (t.at <= until && (!next || t.at < next[1].at)) next = [id, t];
      if (!next) break;
      const [id, t] = next;
      clock = Math.max(clock, t.at);
      if (t.every) t.at = clock + Math.max(1, t.ms); else timers.delete(id);
      try { t.fn(...t.a); } catch (err) { rec.errors = (rec.errors || []).concat(String(err && err.message)); }
    }
    clock = Math.max(clock, until);
    await settle();
  }
  /** Lets every promise (requests, handlers) finish. */
  async function settle() {
    for (let i = 0; i < 50; i++) {
      await new Promise((r) => setImmediate(r));
      const p = doc.pending.splice(0);
      if (p.length) await Promise.allSettled(p);
    }
  }

  return {
    doc,
    win,
    rec,
    load,
    flush,
    settle,
    globals,
    store,
    timers,
    get now() { return clock; },
    set now(v) { clock = v; },
    answerFor,
    nextConfirm,
    nextPrompt: () => (prompts.length ? prompts.shift() : null),
    clone,
    outline,
    textOf,
    El
  };
}

/** prefers-reduced-motion: no; (max-width: N) and (min-width: N) by the width. */
function mediaMatches(q, width) {
  let ok = true;
  for (const m of String(q).matchAll(/\((max|min)-width:\s*(\d+)px\)/g)) ok = ok && (m[1] === 'max' ? width <= Number(m[2]) : width >= Number(m[2]));
  if (/reduced-motion:\s*reduce/.test(q)) return false;
  if (/pointer:\s*coarse/.test(q)) return false;
  if (/hover:\s*none/.test(q)) return false;
  return ok;
}

// en-US numbers whatever this machine says (pages call toLocaleString without a locale)
let keepNum = null;
function enUS() {
  if (keepNum) return;
  keepNum = Number.prototype.toLocaleString;
  // eslint-disable-next-line no-extend-native
  Number.prototype.toLocaleString = function (loc, opt) { return keepNum.call(this, loc || 'en-US', opt); };
}

/** Writes or checks a Core.Tests fixture: UPDATE_NATIVE_PARITY=1 writes it. */
function expectFixture(assert, file, data, hint) {
  const full = path.join(REPO, 'windows', 'app', 'Core.Tests', 'Fixtures', file);
  const text = `${JSON.stringify(data, null, 1)}\n`;
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(full, text);
  assert.ok(fs.existsSync(full), `${file} exists (UPDATE_NATIVE_PARITY=1 writes it)`);
  const have = JSON.parse(fs.readFileSync(full, 'utf8'));
  assert.deepEqual(have, JSON.parse(text), hint || `the web's page changed: rewrite ${file} with UPDATE_NATIVE_PARITY=1 and port the change`);
}

/** Every `public const string X = "..."` of a C# file, as [name, text]. */
function csConsts(file) {
  const cs = fs.readFileSync(path.join(REPO, file), 'utf8');
  return [...cs.matchAll(/public const string (\w+) = "((?:[^"\\]|\\.)*)";/g)].map((m) => [m[1], JSON.parse(`"${m[2].replace(/\\u([0-9a-fA-F]{4})/g, '\\u$1')}"`)]);
}

module.exports = { loadPack, outline, textOf, makeDocument, El, FakeFormData, readSrc, expectFixture, csConsts, enUS, REPO, decode };
