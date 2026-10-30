'use strict';

// The viewer's speed graph: one average speed a second from a clip's records.

const test = require('node:test');
const assert = require('node:assert/strict');
const { speedOf, speedsOf } = require('../server/telemetry');

// a record with gear (field 2, varint), frame (3, varint) and speed (4, float)
function record(speed) {
  const f = Buffer.alloc(4);
  f.writeFloatLE(speed);
  return Buffer.concat([Buffer.from([0x10, 0x01, 0x18, 0x96, 0x01, 0x25]), f]).toString('base64');
}

test('speed graph: reads the speed field past the others', () => {
  assert.equal(Math.round(speedOf(record(12.5)) * 10), 125);
  assert.equal(speedOf(Buffer.from([0x10, 0x01]).toString('base64')), null);
  assert.equal(speedOf('###'), null);
});

test('speed graph: averages each second and leaves gaps empty', () => {
  const body = JSON.stringify({ frames: [[1000, record(10)], [1500, record(20)], [3200, record(5)]] });
  assert.deepEqual(speedsOf(body), [15, null, 5]);
  assert.equal(speedsOf('{"frames":[]}'), null);
  assert.equal(speedsOf('not json'), null);
});
