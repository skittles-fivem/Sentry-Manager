'use strict';

// The native Windows app's Viewer in Studio's languages
// (windows/app/Core/StudioI18n.cs and its table, Core/StudioWords.Data.cs).
// This test:
//   - reads src/renderer/scripts/data/translations.js and writes the parts of
//     it the Viewer shows (ui.settings, ui.playback, ui.dashboard,
//     ui.clipBrowser, ui.notifications, ui.map, ui.cameras, ui.eventTypes,
//     ui.changelog, ui.loading, ui.startup, ui.dropOverlay) for all of
//     Studio's languages into Core/StudioWords.Data.cs, with index.html's
//     data-i18n keys of those parts and their English words (the native
//     layout keeps the English words and looks each up by them);
//   - runs lib/i18n.js's own t() (setLanguage, then t with and without
//     parameters, keys that are missing in a language, in English, or that
//     name a group) and writes what it returned to
//     windows/app/Core.Tests/Fixtures/expected-viewer-i18n.json, with every
//     page word of the Viewer in every language; Core.Tests
//     (ViewerI18nTests.cs) runs StudioI18n on the same and compares.
// Rewrite both files with UPDATE_NATIVE_PARITY=1 after a change on the web
// side.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { pathToFileURL } = require('url');

const REPO = path.join(__dirname, '..');
const SCRIPTS = path.join(REPO, 'src/renderer/scripts');
const INDEX = fs.readFileSync(path.join(REPO, 'src/renderer/index.html'), 'utf8');
const DATA = path.join(REPO, 'windows/app/Core/StudioWords.Data.cs');
const EXPECTED = path.join(REPO, 'windows/app/Core.Tests/Fixtures/expected-viewer-i18n.json');

/** The groups of ui.* the Viewer shows (the converter, export, shared clips and support chat are not in it). */
const GROUPS = ['settings', 'playback', 'dashboard', 'clipBrowser', 'notifications', 'map', 'cameras', 'eventTypes', 'changelog', 'loading', 'startup', 'dropOverlay'];

async function load() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'viewer-i18n-'));
  fs.copyFileSync(path.join(SCRIPTS, 'data/translations.js'), path.join(dir, 'translations.mjs'));
  const src = fs.readFileSync(path.join(SCRIPTS, 'lib/i18n.js'), 'utf8')
    .replace("from '../data/translations.js'", `from ${JSON.stringify(pathToFileURL(path.join(dir, 'translations.mjs')).href)}`);
  fs.writeFileSync(path.join(dir, 'i18n.mjs'), src);
  // setLanguage writes the page's lang and translates it: a page with nothing on it
  globalThis.window = globalThis.window || {};
  globalThis.document = { documentElement: {}, querySelectorAll: () => [] };
  const { translations } = await import(pathToFileURL(path.join(dir, 'translations.mjs')).href);
  const i18n = await import(pathToFileURL(path.join(dir, 'i18n.mjs')).href);
  return { translations, i18n };
}

function flat(o, prefix, out) {
  for (const [k, v] of Object.entries(o || {})) {
    const key = `${prefix}.${k}`;
    if (v && typeof v === 'object' && !Array.isArray(v)) flat(v, key, out);
    else if (typeof v === 'string') out[key] = v;
  }
  return out;
}

const inViewer = (key) => GROUPS.some((g) => key.startsWith(`ui.${g}.`));

/** C# verbatim string: only " doubles. */
const verbatim = (s) => '@"' + s.replace(/"/g, '""') + '"';

test('the Viewer\'s words in Studio\'s languages: the table and i18n.js t() as the C# has them', async () => {
  const { translations, i18n } = await load();
  const languages = i18n.getAvailableLanguages().map((l) => l.code);
  assert.deepEqual(languages, Object.keys(translations), 'getAvailableLanguages lists the languages translations.js has');

  // the table
  const words = {};
  for (const lang of languages) {
    const out = {};
    for (const g of GROUPS) {
      const group = translations[lang].ui && translations[lang].ui[g];
      if (group && typeof group === 'object') flat(group, `ui.${g}`, out);
    }
    words[lang] = out;
  }
  // index.html's keys in the Viewer's groups, in page order, with their English words
  const page = [];
  const seen = new Set();
  for (const m of INDEX.matchAll(/data-i18n(?:-placeholder|-title)?="([^"]+)"/g)) {
    const key = m[1];
    if (!inViewer(key) || seen.has(key)) continue;
    seen.add(key);
    const en = words.en[key];
    assert.equal(typeof en, 'string', `index.html's ${key} has English words`);
    page.push([key, en]);
  }
  // the native layout finds each word's key by its English words: one key's words for each
  const byEnglish = new Map();
  for (const [key, en] of page) {
    if (!byEnglish.has(en)) { byEnglish.set(en, key); continue; }
    const first = byEnglish.get(en);
    for (const lang of languages) {
      assert.equal(words[lang][key], words[lang][first], `"${en}" is ${first} and ${key}, which ${lang} words differently: the native layout needs the key itself`);
    }
  }
  const table = JSON.stringify({ languages, words, page });
  const cs = [
    '// Written by test/windowsNativeViewerI18n.test.js from Studio\'s',
    '// src/renderer/scripts/data/translations.js and index.html (UPDATE_NATIVE_PARITY=1',
    '// writes it again): the Viewer\'s ui.* words in every language, and the page\'s',
    '// data-i18n keys with their English words. Do not edit.',
    'namespace SentrySix.Core',
    '{',
    '    internal static class StudioWordsData',
    '    {',
    `        internal const string Json = ${verbatim(table)};`,
    '    }',
    '}',
    ''
  ].join('\r\n');
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(DATA, cs);
  assert.ok(fs.existsSync(DATA), 'StudioWords.Data.cs exists (UPDATE_NATIVE_PARITY=1 writes it)');
  assert.equal(fs.readFileSync(DATA, 'utf8'), cs, 'Studio\'s translations changed: rewrite windows/app/Core/StudioWords.Data.cs with UPDATE_NATIVE_PARITY=1');

  // i18n.js t(): what it returns for keys of each kind
  const keys = [
    ['ui.dashboard.manual'], ['ui.dashboard.noData'], ['ui.dashboard.selfDriving'], ['ui.dashboard.autosteer'], ['ui.dashboard.tacc'],
    ['ui.dashboard.kmh'], ['ui.dashboard.mph'],
    ['ui.settings.skipForward'], ['ui.settings.title'], ['ui.clipBrowser.subtitle'],
    ['ui.playback.skipBack', { seconds: 5 }], ['ui.playback.skipForward', { seconds: 1 }], ['ui.playback.skipForward', { seconds: 15 }],
    ['ui.notifications.markerStartSet', { time: '1:02' }],
    ['ui.dashboard'], ['ui.dashboard.nothingHere'], ['ui.nothing.here'], ['nothing'], ['ui.dashboard.manual.more']
  ];
  const calls = [];
  for (const lang of languages) {
    await i18n.setLanguage(lang);
    for (const [key, params] of keys) calls.push({ lang, key, params: params || null, result: i18n.t(key, params || {}) });
  }
  // a key some languages lack (English in one place, missing in another): what t() falls back to
  const partial = Object.keys(words.en).filter((k) => languages.some((l) => !(k in words[l])));
  for (const key of partial.slice(0, 6)) {
    for (const lang of languages) {
      await i18n.setLanguage(lang);
      calls.push({ lang, key, params: null, result: i18n.t(key, {}) });
    }
  }
  const pageWords = {};
  for (const lang of languages) {
    await i18n.setLanguage(lang);
    pageWords[lang] = page.map(([key, en]) => [en, i18n.t(key)]);
  }
  const got = {
    about: 'Written by test/windowsNativeViewerI18n.test.js from Studio\'s lib/i18n.js t() and data/translations.js; checked by windows/app/Core.Tests (ViewerI18nTests.cs, SentrySix.Core.StudioI18n).',
    languages,
    calls,
    pageWords
  };
  const text = JSON.stringify(got, null, 1) + '\n';
  if (process.env.UPDATE_NATIVE_PARITY === '1') fs.writeFileSync(EXPECTED, text);
  assert.ok(fs.existsSync(EXPECTED), 'expected-viewer-i18n.json exists (UPDATE_NATIVE_PARITY=1 writes it)');
  assert.deepEqual(JSON.parse(fs.readFileSync(EXPECTED, 'utf8')), JSON.parse(text), 'Studio\'s words changed: rewrite the file with UPDATE_NATIVE_PARITY=1');
});
