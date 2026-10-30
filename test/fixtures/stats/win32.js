'use strict';

// The Windows test PC for the stats tests: Windows 11 Pro on a Ryzen 9 9950X
// (16 cores, 32 threads, with its small Radeon iGPU), an RTX 4070 Ti, two
// NTFS disks (C: on an NVMe SSD, Z: on a hard disk) and one Ethernet port.
// SentryHelper.exe's answers to sys.info, gpu.list and perf.sample on it are
// in win11-9950x/ (written by its generate.js: perf.sample.N is N × 2 s
// after the first, and every counter moves on by a known amount). A fake
// helper serves them; the test sets which reading perf.sample answers with.

const fs = require('fs');
const path = require('path');

const DIR = path.join(__dirname, 'win11-9950x');
const load = (name) => JSON.parse(fs.readFileSync(path.join(DIR, name), 'utf8'));
const clone = (v) => JSON.parse(JSON.stringify(v));

const RECORDED = {
  sys: load('sys.info.json'),
  gpus: load('gpu.list.json'),
  perf: [0, 1, 2].map((n) => load(`perf.sample.${n}.json`)),
  perfNoEnergy: [0, 1].map((n) => load(`perf.sample.no-energy.${n}.json`)),
  nvidiaLines: load('nvidia-smi.json').lines
};

// the per-thread loads the readings were made with (LOADS in generate.js)
const LOADS = [62, 55, 48, 51, 44, 47, 53, 49, 58, 41, 46, 50, 45, 52, 43, 56, 60, 54, 42, 48, 47, 50, 44, 53, 49, 45, 57, 46, 51, 42, 55, 48];
const MEM_TOTAL = 68182605824;
const MEM_FREE = 41875931136;
const APP_PID = 4200;

/**
 * A stand-in for the SentryHelper client: request(op, args) answers from
 * `answers` on the next turn of the event loop. `perf` is a list of
 * readings and `phase` picks the one perf.sample answers with; an op set to
 * 'hang' never answers, 'fail' rejects as the real client does.
 */
function fakeHelper({ perf = RECORDED.perf, sys = RECORDED.sys, gpus = RECORDED.gpus, ops = {}, available = true } = {}) {
  const h = {
    phase: 0,
    perf,
    calls: [],
    available: () => available,
    request(op, args) {
      h.calls.push({ op, args });
      const mode = ops[op];
      if (mode === 'hang') return new Promise(() => {});
      if (mode === 'fail') {
        return new Promise((resolve, reject) => setImmediate(() => reject(Object.assign(new Error('The helper stopped.'), { code: 'EHELPER' }))));
      }
      let answer;
      if (op === 'sys.info') answer = sys;
      else if (op === 'gpu.list') answer = gpus;
      else if (op === 'perf.sample') answer = h.perf[Math.min(h.phase, h.perf.length - 1)];
      else answer = {};
      return new Promise((resolve) => setImmediate(() => resolve(clone(answer))));
    }
  };
  return h;
}

/** node:os as it answers on the test PC. */
function fakeOs() {
  return {
    totalmem: () => MEM_TOTAL,
    freemem: () => MEM_FREE,
    uptime: () => 200000,
    hostname: () => 'GARAGE-PC',
    release: () => '10.0.26100',
    arch: () => 'x64',
    machine: () => 'x86_64'
  };
}

/** Timers that fire only when the test says so (fire() runs every one that is due). */
function manualTimers() {
  const pending = new Map();
  let next = 0;
  return {
    pending,
    setTimeout(fn, ms) {
      const h = { id: ++next, fn, ms, unref() {} };
      pending.set(h.id, h);
      return h;
    },
    clearTimeout(h) {
      if (h) pending.delete(h.id);
    },
    fire() {
      const list = [...pending.values()];
      pending.clear();
      for (const h of list) h.fn();
    }
  };
}

/** A perf.sample reading with some counters changed: edit(copy) changes the copy. */
function edited(reading, edit) {
  const copy = clone(reading);
  edit(copy);
  return copy;
}

module.exports = { RECORDED, LOADS, MEM_TOTAL, MEM_FREE, APP_PID, fakeHelper, fakeOs, manualTimers, edited, clone };
