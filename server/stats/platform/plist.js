'use strict';

// A small reader for XML property lists (ioreg -a, diskutil -plist). It
// knows the plist elements and the five XML entities, and nothing else: a
// DOCTYPE with its own declarations, any other entity, or a document that
// is too big or too deep is refused. Values come back as plain JavaScript:
// dict -> object, array -> array, integer and real -> number, true and
// false -> boolean, string, data (base64) and date -> string.

const MAX_BYTES = 8 * 1024 * 1024;
const MAX_DEPTH = 64;
const MAX_NODES = 500000;

const ENTITIES = { lt: '<', gt: '>', amp: '&', quot: '"', apos: "'" };
const TEXT_TAGS = new Set(['key', 'string', 'integer', 'real', 'data', 'date']);
const TAG = /<([A-Za-z]+)(?:\s[^<>]*?)?(\/?)>/y;

class PlistError extends Error {}

function decode(text) {
  return text.replace(/&([^;&\s]{0,8})(;?)/g, (whole, name, semi) => {
    if (!semi) throw new PlistError('a bare &');
    if (Object.prototype.hasOwnProperty.call(ENTITIES, name)) return ENTITIES[name];
    // character references are not entities: &#38; and &#x26;
    const m = /^#(?:x([0-9A-Fa-f]{1,6})|(\d{1,7}))$/.exec(name);
    const code = m ? (m[1] ? parseInt(m[1], 16) : Number(m[2])) : NaN;
    if (!Number.isInteger(code) || code > 0x10ffff) throw new PlistError(`unknown entity ${whole}`);
    return String.fromCodePoint(code);
  });
}

/** An object with `key` set as its own property ("__proto__" stays a plain key). */
function setKey(obj, key, value) {
  Object.defineProperty(obj, key, { value, enumerable: true, writable: true, configurable: true });
}

function parsePlist(input, { maxBytes = MAX_BYTES, maxDepth = MAX_DEPTH, maxNodes = MAX_NODES } = {}) {
  const s = String(input ?? '');
  if (s.length > maxBytes) throw new PlistError('plist too large');
  let pos = 0;
  let nodes = 0;

  // whitespace, the XML declaration, comments and a DOCTYPE without its own declarations
  const skip = () => {
    for (;;) {
      while (pos < s.length && /\s/.test(s[pos])) pos++;
      if (s.startsWith('<?', pos)) {
        const end = s.indexOf('?>', pos);
        if (end < 0) throw new PlistError('open <?');
        pos = end + 2;
      } else if (s.startsWith('<!--', pos)) {
        const end = s.indexOf('-->', pos);
        if (end < 0) throw new PlistError('open comment');
        pos = end + 3;
      } else if (s.startsWith('<!', pos)) {
        const end = s.indexOf('>', pos);
        if (end < 0 || !s.startsWith('<!DOCTYPE', pos) || s.slice(pos, end).includes('[')) throw new PlistError('declarations are not read');
        pos = end + 1;
      } else return;
    }
  };

  const openTag = () => {
    skip();
    TAG.lastIndex = pos;
    const m = TAG.exec(s);
    if (!m) throw new PlistError(`expected a tag at ${pos}`);
    pos = TAG.lastIndex;
    return { name: m[1], empty: m[2] === '/' };
  };

  const closing = (name) => {
    skip();
    const tag = `</${name}>`;
    if (!s.startsWith(tag, pos)) return false;
    pos += tag.length;
    return true;
  };

  const text = (name) => {
    const end = s.indexOf(`</${name}>`, pos);
    if (end < 0) throw new PlistError(`open <${name}>`);
    const raw = s.slice(pos, end);
    if (raw.includes('<')) throw new PlistError(`markup inside <${name}>`);
    pos = end + name.length + 3;
    return decode(raw);
  };

  const value = (depth, tag = openTag()) => {
    if (depth > maxDepth) throw new PlistError('plist too deep');
    if (++nodes > maxNodes) throw new PlistError('plist too long');
    const { name, empty } = tag;
    if (name === 'true' || name === 'false') {
      if (!empty && !closing(name)) throw new PlistError(`open <${name}>`);
      return name === 'true';
    }
    if (TEXT_TAGS.has(name)) {
      const t = empty ? '' : text(name);
      if (name === 'integer') {
        const v = /^\s*-?0x[0-9a-f]+\s*$/i.test(t) ? (t.trim().startsWith('-') ? -1 : 1) * Number(t.trim().replace(/^-/, '')) : Number(t);
        if (!t.trim() || !Number.isFinite(v)) throw new PlistError('bad integer');
        return v;
      }
      if (name === 'real') {
        const v = Number(t);
        if (!t.trim() || Number.isNaN(v)) throw new PlistError('bad real');
        return v;
      }
      if (name === 'data') return t.replace(/\s+/g, '');
      if (name === 'date') return t.trim();
      if (name === 'key') throw new PlistError('a key outside a dict');
      return t;
    }
    if (name === 'array') {
      const out = [];
      if (empty) return out;
      while (!closing('array')) out.push(value(depth + 1));
      return out;
    }
    if (name === 'dict') {
      const out = {};
      if (empty) return out;
      while (!closing('dict')) {
        const k = openTag();
        if (k.name !== 'key') throw new PlistError('a dict value without its key');
        const key = k.empty ? '' : text('key');
        setKey(out, key, value(depth + 1));
      }
      return out;
    }
    throw new PlistError(`unknown element <${name}>`);
  };

  skip();
  if (pos >= s.length) return null;
  let result;
  const first = openTag();
  if (first.name === 'plist') {
    if (first.empty) return null;
    result = closing('plist') ? null : value(1);
    if (result !== null && !closing('plist')) throw new PlistError('open <plist>');
  } else result = value(1, first);
  skip();
  if (pos < s.length) throw new PlistError('text after the plist');
  return result;
}

/** parsePlist, but null instead of an error (a command's odd answer only means no number). */
function readPlist(text, options) {
  try {
    return parsePlist(text, options);
  } catch {
    return null;
  }
}

module.exports = { parsePlist, readPlist, PlistError, MAX_BYTES, MAX_DEPTH };
