'use strict';

// What the engine does differently for the Mac app (docs/plans/macos-app.md,
// section 9): "in your login" mode (S2), the macos-app update channel with
// its .dmg link (S3) and the setup-code words (S4). Linux, Windows, Docker
// and the Terminal Mac install keep their answers.

const test = require('node:test');
const assert = require('node:assert/strict');
const fsp = require('fs/promises');
const path = require('path');
const { realTmp } = require('./fixtures/tmp');
const { darwinAccount, runMode } = require('../server/platform/darwinRuntime');
const { ReleaseUpdates, chooseUpdates, dmgUrl, HOW_MACOS, HOW_MACOS_APP } = require('../server/releaseUpdates');
const { Auth } = require('../server/auth');
const { ManagerDB } = require('../server/db');
const { forPlatform } = require('../server/platform');

const REPO = 'skittles-fivem/Sentry-Manager';
const [OWNER, NAME] = REPO.split('/');

// ---- S2: in your login

test('run mode: daemon unless the launcher says agent; the account is then the user', () => {
  assert.equal(runMode({}), 'daemon');
  assert.equal(runMode({ SERVICE_MODE: 'daemon' }), 'daemon');
  assert.equal(runMode({ SERVICE_MODE: 'Agent' }), 'daemon');
  assert.equal(runMode({ SERVICE_MODE: 'agent' }), 'agent');
  const fda = { openSync: () => { throw Object.assign(new Error('x'), { code: 'ENOENT' }); } };
  assert.deepEqual(darwinAccount({ env: {}, ...fda }), { account: '_sentrysix', elevated: false, fullDiskAccess: null, runMode: 'daemon' });
  assert.deepEqual(darwinAccount({ env: { SERVICE_MODE: 'agent' }, userInfo: () => ({ username: 'anna' }), ...fda }),
    { account: 'anna', elevated: false, fullDiskAccess: null, runMode: 'agent' });
  // a user that cannot be read: the service account's name, still agent
  assert.equal(darwinAccount({ env: { SERVICE_MODE: 'agent' }, userInfo: () => { throw new Error('no'); }, ...fda }).account, '_sentrysix');
});

test('in your login: the user\'s own state folder is guarded like the service\'s; folder roots stay the same', () => {
  const home = '/Users/anna/Library/Application Support/SentrySixManager';
  const env = { SERVICE_MODE: 'agent', DATA_PATH: `${home}/data` };
  const agent = forPlatform('darwin', { env, exists: () => false });
  const daemon = forPlatform('darwin', { env: {}, exists: () => false });
  const deny = agent.denyRoots({ data: `${home}/data`, run: `${home}/run`, appRoot: '/Applications/Sentry Six Manager.app/Contents/Resources/engine/app' });
  assert.ok(deny.includes(home), deny.join('\n'));
  assert.ok(agent.systemDirs().includes(`${home}/manager.env`));
  // the Terminal install keeps today's lists
  assert.ok(!daemon.denyRoots({ data: '/Library/Application Support/SentrySixManager/data' }).includes('/Library/Application Support'));
  assert.deepEqual(daemon.systemDirs(), forPlatform('darwin', { env: { DATA_PATH: `${home}/data` }, exists: () => false }).systemDirs());
  // folder vehicles in the home folder are still refused: no home in the roots
  assert.deepEqual(agent.folderRoots('/f'), daemon.folderRoots('/f'));
  assert.ok(!agent.folderRoots('/f').some((r) => r.startsWith('/Users/anna')));
});

// ---- S3: the macos-app update channel

const fakeDb = () => { const m = new Map(); return { get: (k, d) => (m.has(k) ? m.get(k) : d), set: (k, v) => m.set(k, v) }; };
const dl = (tag, file) => `https://github.com/${REPO}/releases/download/${tag}/${file}`;
const asset = (tag, file) => ({ name: file, browser_download_url: dl(tag, file) });

function fakeList(list) {
  const calls = [];
  const fetchImpl = async (url) => {
    calls.push(url);
    if (/\/releases\?per_page=100&page=1$/.test(url)) return Response.json(list);
    if (/\/releases\?per_page=100&page=\d+$/.test(url)) return Response.json([]);
    const m = /\/releases\/download\/([^/]+)\/release\.json$/.exec(url);
    if (m) return new Response(JSON.stringify({ sha: 'c'.repeat(40), builtAt: '2026-10-20T04:00:00Z', version: '1.3.0', critical: { level: 0, reason: '' } }), { status: 200 });
    return new Response('', { status: 404 });
  };
  return { fetchImpl, calls };
}
const build = (channel) => ({ sha: 'b'.repeat(40), builtAt: '2026-10-13T04:00:00Z', version: '1.1.0', release: REPO, channel });

test('macos-app: the same macos-v* releases, the app\'s own steps, and the .dmg link', async () => {
  assert.equal(chooseUpdates({ channel: 'macos-app' }), 'release');
  const tag = 'macos-v1.3.0-ccccccc';
  const list = [{
    tag_name: tag, draft: false, prerelease: false, published_at: '2026-10-22T00:00:00Z',
    assets: [asset(tag, 'release.json'), asset(tag, 'SentrySixManager-1.3.0-ccccccc-macos-arm64.tar.gz'), asset(tag, 'SentrySixManager-1.3.0-ccccccc-macos-arm64.dmg')]
  }];
  const st = await new ReleaseUpdates({ db: fakeDb(), buildInfo: build('macos-app'), fetchImpl: fakeList(list).fetchImpl }).check();
  assert.equal(st.error, null);
  assert.equal(st.latest.release, tag);
  assert.equal(st.how, HOW_MACOS_APP);
  assert.equal(st.how, 'To update, download the new Sentry Six Manager from the release page, drag it to Applications and open it.');
  assert.equal(st.latest.downloadUrl, dl(tag, 'SentrySixManager-1.3.0-ccccccc-macos-arm64.dmg'));
  // the Terminal Mac install: no such key, its own steps
  const mac = await new ReleaseUpdates({ db: fakeDb(), buildInfo: build('macos'), fetchImpl: fakeList(list).fetchImpl }).check();
  assert.equal(mac.how, HOW_MACOS);
  assert.equal('downloadUrl' in mac.latest, false);
});

test('the .dmg link: signed before unsigned, only this repository\'s github.com downloads, else null', () => {
  const tag = 'macos-v1.3.0-ccccccc';
  const signed = 'SentrySixManager-1.3.0-ccccccc-macos-arm64.dmg';
  const unsigned = 'SentrySixManager-1.3.0-ccccccc-macos-arm64-unsigned.dmg';
  assert.equal(dmgUrl({ assets: [asset(tag, unsigned), asset(tag, signed)] }, OWNER, NAME), dl(tag, signed));
  assert.equal(dmgUrl({ assets: [asset(tag, unsigned)] }, OWNER, NAME), dl(tag, unsigned));
  assert.equal(dmgUrl({ assets: [asset(tag, 'release.json')] }, OWNER, NAME), null);
  assert.equal(dmgUrl({}, OWNER, NAME), null);
  for (const url of [
    `https://github.com/someone/else/releases/download/${tag}/${signed}`,
    `http://github.com/${REPO}/releases/download/${tag}/${signed}`,
    `https://evil.example/${REPO}/releases/download/${tag}/${signed}`,
    `https://user:pw@github.com/${REPO}/releases/download/${tag}/${signed}`,
    'not a url'
  ]) assert.equal(dmgUrl({ assets: [{ name: signed, browser_download_url: url }] }, OWNER, NAME), null, url);
  // a name that is not ours
  assert.equal(dmgUrl({ assets: [asset(tag, 'Other.dmg')] }, OWNER, NAME), null);
});

// ---- S4: setup-code words

test('setup code: the app also points to its menu bar; the Terminal install and the others do not', async () => {
  const dir = await realTmp('sm-macapp-auth-');
  const said = (platformName, channel) => {
    const db = new ManagerDB(path.join(dir, `${platformName}-${channel}.db`));
    const auth = new Auth({ db, log: { info() {}, warn() {} }, platform: forPlatform(platformName, { env: {}, exists: () => false }), channel });
    auth.bootstrap({});
    try {
      auth.setup('admin', 'correct horse battery', 'WRONG-CODE');
      return null;
    } catch (err) {
      return err.message;
    } finally {
      db.close();
    }
  };
  try {
    assert.equal(said('darwin', 'macos-app'), 'That setup code is not right. It was shown when the install finished. To see it again, run sudo sentry-six-manager setup-code in Terminal. Or choose Setup code in the Sentry Six Manager menu bar.');
    assert.equal(said('darwin', 'macos'), 'That setup code is not right. It was shown when the install finished. To see it again, run sudo sentry-six-manager setup-code in Terminal.');
    assert.equal(said('linux', 'macos-app'), 'That setup code is not right. It is printed in the container log (docker logs sentry-six-manager).');
  } finally {
    await fsp.rm(dir, { recursive: true, force: true });
  }
});
