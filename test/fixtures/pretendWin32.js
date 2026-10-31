'use strict';

// Preloaded (node --require) by windowsLocalOnly.test.js: the server then
// takes its Windows rules (where it listens, the setup code) on this Linux box.
Object.defineProperty(process, 'platform', { value: 'win32' });
