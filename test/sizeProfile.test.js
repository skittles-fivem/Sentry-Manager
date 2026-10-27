'use strict';

// A size profile (Conservative, Balanced, Small) only fails a clip that came
// out too big: one that shrank further than the target is kept.

const test = require('node:test');
const assert = require('node:assert/strict');
const { isWithinProfile, targetOutputBytes } = require('../src/main/converter/contracts');

test('a clip smaller than its profile\'s target passes; only too big fails', () => {
  const source = 100_000_000;
  // Balanced: about 20% smaller, give or take 2% of the original
  assert.equal(targetOutputBytes(source, 'balanced'), 80_000_000);
  assert.equal(isWithinProfile(source, 80_000_000, 'balanced'), true, 'on target');
  assert.equal(isWithinProfile(source, 81_900_000, 'balanced'), true, 'a little over, inside the 2%');
  assert.equal(isWithinProfile(source, 82_100_000, 'balanced'), false, 'too big');
  assert.equal(isWithinProfile(source, 77_000_000, 'balanced'), true, 'a little smaller');
  assert.equal(isWithinProfile(source, 40_000_000, 'balanced'), true, 'much smaller (a still scene)');
  assert.equal(isWithinProfile(source, 10_000_000, 'aggressive'), true);
  assert.equal(isWithinProfile(source, 97_500_000, 'conservative'), false);
});
