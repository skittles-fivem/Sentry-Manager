'use strict';

// Preloaded (node --require) by darwinServer.test.js: the server then takes
// its macOS paths on this Linux box. PRETEND_ARCH sets process.arch.
Object.defineProperty(process, 'platform', { value: 'darwin' });
if (process.env.PRETEND_ARCH) Object.defineProperty(process, 'arch', { value: process.env.PRETEND_ARCH });
