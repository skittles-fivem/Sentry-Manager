'use strict';

// PNG in pure Node (zlib only): decode to RGBA, encode RGBA, and compare two
// images. Enough for screenshots from Chromium and WPF's PngBitmapEncoder:
// bit depth 8 or 16, greyscale, RGB, palette (with tRNS), grey+alpha, RGBA;
// not interlaced (neither writes Adam7).

const zlib = require('zlib');

const SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function paeth(a, b, c) {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  if (pa <= pb && pa <= pc) return a;
  return pb <= pc ? b : c;
}

/** Decode a PNG buffer to { width, height, data } with data RGBA, 8 bits each. */
function decode(buf) {
  if (!Buffer.isBuffer(buf) || buf.length < 8 || !buf.subarray(0, 8).equals(SIGNATURE)) throw new Error('not a PNG');
  let width = 0;
  let height = 0;
  let depth = 0;
  let type = 0;
  let interlace = 0;
  let palette = null;
  let trns = null;
  const idat = [];
  for (let p = 8; p + 8 <= buf.length;) {
    const len = buf.readUInt32BE(p);
    const kind = buf.toString('latin1', p + 4, p + 8);
    const body = buf.subarray(p + 8, p + 8 + len);
    if (kind === 'IHDR') {
      width = body.readUInt32BE(0);
      height = body.readUInt32BE(4);
      depth = body[8];
      type = body[9];
      interlace = body[12];
    } else if (kind === 'PLTE') palette = body;
    else if (kind === 'tRNS') trns = body;
    else if (kind === 'IDAT') idat.push(body);
    else if (kind === 'IEND') break;
    p += 12 + len;
  }
  const channels = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 }[type];
  if (!width || !height || !channels) throw new Error(`unsupported PNG (colour type ${type})`);
  if (interlace) throw new Error('interlaced PNGs are not supported');
  if (type === 3 ? depth !== 8 : depth !== 8 && depth !== 16) throw new Error(`unsupported PNG bit depth ${depth}`);
  const bpp = channels * (depth / 8);
  const stride = width * bpp;
  const raw = zlib.inflateSync(Buffer.concat(idat));
  if (raw.length < height * (stride + 1)) throw new Error('PNG data is short');
  const px = Buffer.alloc(height * stride);
  let prev = Buffer.alloc(stride);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    const cur = px.subarray(y * stride, (y + 1) * stride);
    for (let i = 0; i < stride; i++) {
      const a = i >= bpp ? cur[i - bpp] : 0;
      const b = prev[i];
      const c = i >= bpp ? prev[i - bpp] : 0;
      let v = line[i];
      if (filter === 1) v += a;
      else if (filter === 2) v += b;
      else if (filter === 3) v += (a + b) >> 1;
      else if (filter === 4) v += paeth(a, b, c);
      else if (filter !== 0) throw new Error(`bad PNG filter ${filter}`);
      cur[i] = v & 0xff;
    }
    prev = cur;
  }
  const data = Buffer.alloc(width * height * 4);
  const step = depth / 8; // 16-bit: the high byte of each sample
  for (let i = 0, o = 0; i < width * height; i++, o += 4) {
    const s = i * bpp;
    if (type === 3) {
      const k = px[s];
      data[o] = palette[k * 3];
      data[o + 1] = palette[k * 3 + 1];
      data[o + 2] = palette[k * 3 + 2];
      data[o + 3] = trns && k < trns.length ? trns[k] : 255;
    } else if (type === 0 || type === 4) {
      const g = px[s];
      data[o] = data[o + 1] = data[o + 2] = g;
      data[o + 3] = type === 4 ? px[s + step] : 255;
    } else {
      data[o] = px[s];
      data[o + 1] = px[s + step];
      data[o + 2] = px[s + 2 * step];
      data[o + 3] = type === 6 ? px[s + 3 * step] : 255;
    }
  }
  return { width, height, data };
}

const CRC_TABLE = (() => {
  const t = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c;
  }
  return t;
})();

function crc32(buf) {
  let c = -1;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ -1) >>> 0;
}

function chunk(kind, body) {
  const head = Buffer.alloc(8);
  head.writeUInt32BE(body.length, 0);
  head.write(kind, 4, 'latin1');
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), body])), 0);
  return Buffer.concat([head, body, crc]);
}

/** Encode { width, height, data (RGBA) } as a PNG buffer. */
function encode({ width, height, data }) {
  const stride = width * 4;
  const raw = Buffer.alloc(height * (stride + 1));
  for (let y = 0; y < height; y++) data.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  return Buffer.concat([SIGNATURE, chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw, { level: 6 })), chunk('IEND', Buffer.alloc(0))]);
}

/**
 * Compare two decoded images. Pixels are laid over black by their alpha; one
 * differs when any channel is more than `threshold` apart (0-255). Images of
 * different sizes are compared over the area they share, and every pixel
 * only one of them has counts as different. Returns the share of differing
 * pixels (0-100) and a diff image: the first image dimmed, differences red.
 */
function compare(a, b, { threshold = 32 } = {}) {
  const width = Math.max(a.width, b.width);
  const height = Math.max(a.height, b.height);
  const out = Buffer.alloc(width * height * 4);
  let differ = 0;
  const at = (img, x, y, c) => {
    const o = (y * img.width + x) * 4;
    return (img.data[o + c] * img.data[o + 3]) / 255;
  };
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const o = (y * width + x) * 4;
      const inA = x < a.width && y < a.height;
      const inB = x < b.width && y < b.height;
      let diff = !(inA && inB);
      if (!diff) {
        for (let c = 0; c < 3 && !diff; c++) diff = Math.abs(at(a, x, y, c) - at(b, x, y, c)) > threshold;
      }
      if (diff) {
        differ++;
        out[o] = 255; out[o + 1] = 0; out[o + 2] = 0;
      } else {
        const g = Math.round((0.299 * at(a, x, y, 0) + 0.587 * at(a, x, y, 1) + 0.114 * at(a, x, y, 2)) * 0.35);
        out[o] = out[o + 1] = out[o + 2] = g;
      }
      out[o + 3] = 255;
    }
  }
  return { percent: (100 * differ) / (width * height), differ, width, height, diff: { width, height, data: out } };
}

module.exports = { decode, encode, compare, crc32 };
