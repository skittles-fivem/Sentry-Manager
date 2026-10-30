'use strict';

// The small XML plist reader the macOS stats use for ioreg -a and
// diskutil -plist: every plist element, the five XML entities, and a
// refusal for anything else (declarations, other entities, too big or too
// deep).

const test = require('node:test');
const assert = require('node:assert/strict');
const { parsePlist, readPlist, PlistError } = require('../server/stats/platform/plist');
const D = require('./fixtures/stats/darwin');

const wrap = (body) => D.plist(body);
const plain = (v) => JSON.parse(JSON.stringify(v));

test('plist: every element, as plain values', () => {
  const v = parsePlist(wrap(`<dict>
	<key>name</key><string>Apple SSD</string>
	<key>empty</key><string/>
	<key>size</key><integer>500277790720</integer>
	<key>neg</key><integer>-12</integer>
	<key>hex</key><integer>0x1F</integer>
	<key>ratio</key><real>0.25</real>
	<key>exp</key><real>1e3</real>
	<key>yes</key><true/>
	<key>no</key><false/>
	<key>blob</key><data>
	AAEC
	AwQ=
	</data>
	<key>when</key><date>2026-10-30T06:04:40Z</date>
	<key>list</key><array><integer>1</integer><array/><dict/></array>
	<!-- a comment -->
	<key></key><string>no name</string>
</dict>`));
  assert.deepEqual(v, {
    name: 'Apple SSD', empty: '', size: 500277790720, neg: -12, hex: 31, ratio: 0.25, exp: 1000, yes: true, no: false,
    blob: 'AAECAwQ=', when: '2026-10-30T06:04:40Z', list: [1, [], {}], '': 'no name'
  });
  assert.equal(Object.getPrototypeOf(v), Object.prototype);
});

test('plist: the five XML entities and character references', () => {
  const v = parsePlist(wrap('<array><string>&lt;a&gt; &amp; &quot;b&quot; &apos;c&apos; &#38; &#x26; &#x1F600;</string></array>'));
  assert.deepEqual(v, ['<a> & "b" \'c\' & & \u{1F600}']);
  // an &amp; that decodes to "&lt;" stays text, it is not read twice
  assert.deepEqual(parsePlist(wrap('<array><string>&amp;lt;</string></array>')), ['&lt;']);
});

test('plist: a "__proto__" key is a plain key and touches no prototype', () => {
  const v = parsePlist(wrap('<dict><key>__proto__</key><dict><key>polluted</key><true/></dict><key>constructor</key><string>x</string></dict>'));
  assert.equal(Object.getPrototypeOf(v), Object.prototype);
  assert.deepEqual(Object.keys(v), ['__proto__', 'constructor']);
  assert.equal(v.__proto__.polluted, true); // eslint-disable-line no-proto
  assert.equal({}.polluted, undefined);
});

test('plist: ioreg and diskutil answers', () => {
  const block = parsePlist(D.ioregBlock(0));
  assert.equal(block.length, 3);
  assert.equal(block[0].IOClass, 'IOBlockStorageDriver');
  assert.equal(block[0].IORegistryEntryChildren[0]['BSD Name'], 'disk0');
  assert.equal(block[0].IORegistryEntryChildren[0].Whole, true);
  assert.equal(block[0].Statistics['Bytes (Read)'], 80000000000);
  assert.deepEqual(block[2].IORegistryEntryChildren, []);
  const accel = parsePlist(D.ioregAccel(0));
  assert.equal(accel[0].PerformanceStatistics['Device Utilization %'], 30);
  assert.deepEqual(plain(parsePlist(D.DISKUTIL_LIST)).WholeDisks, ['disk0', 'disk4']);
  assert.deepEqual(parsePlist(D.DISKUTIL_INFO.disk3s1s1).APFSPhysicalStores, [{ APFSPhysicalStore: 'disk0s2' }]);
  // ioreg with nothing to show prints nothing; a bare value without <plist> is read too
  assert.equal(parsePlist(''), null);
  assert.equal(parsePlist('  \n'), null);
  assert.equal(parsePlist('<plist version="1.0"/>'), null);
  assert.deepEqual(parsePlist('<array><true/></array>'), [true]);
});

test('plist: declarations, unknown entities and bad markup are refused', () => {
  const bad = [
    // an internal DTD subset (entity expansion) is never read
    '<?xml version="1.0"?><!DOCTYPE plist [<!ENTITY a "aaaa">]><plist><string>&a;</string></plist>',
    wrap('<string>&nbsp;</string>'),
    wrap('<string>a & b</string>'),
    wrap('<string>&#x110000;</string>'),
    wrap('<string><![CDATA[x]]></string>'),
    wrap('<dict><string>no key</string></dict>'),
    wrap('<dict><key>a</key></dict>'),
    wrap('<key>loose</key>'),
    wrap('<integer>12abc</integer>'),
    wrap('<integer></integer>'),
    wrap('<real>x</real>'),
    wrap('<blob>x</blob>'),
    wrap('<array><string>open</array>'),
    wrap('<array>'),
    wrap('<true>'),
    `${wrap('<true/>')}<true/>`,
    '<plist><true/>',
    '<!-- open',
    '<?xml open',
    'just text'
  ];
  for (const text of bad) {
    assert.throws(() => parsePlist(text), PlistError, text);
    assert.equal(readPlist(text), null, text);
  }
});

test('plist: size, depth and length caps', () => {
  const deep = (n) => wrap(`${'<array>'.repeat(n)}${'</array>'.repeat(n)}`);
  assert.doesNotThrow(() => parsePlist(deep(60)));
  assert.throws(() => parsePlist(deep(70)), /too deep/);
  assert.throws(() => parsePlist(deep(200000)), PlistError, 'far too deep is refused too, not a stack overflow');
  assert.throws(() => parsePlist(wrap('<string>x</string>'), { maxBytes: 50 }), /too large/);
  assert.throws(() => parsePlist(wrap(`<array>${'<true/>'.repeat(20)}</array>`), { maxNodes: 10 }), /too long/);
  assert.equal(readPlist(deep(70)), null);
});
