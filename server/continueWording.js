'use strict';

// The words of the plain-HTTP "continue" page (web/continue.html) that say
// where else the certificate's fingerprint is printed. The page is served
// before anyone can reach the API, so it cannot ask /api/info; the server
// fills in the words for where it runs. On Docker the page stays exactly as
// it is in web/continue.html; on Windows the fingerprint is in the install
// window and in service.log, not in docker logs.

const DOCKER_LOG = 'the container log (<code>docker logs sentry-six-manager</code>)';
const WINDOWS_LOG = 'the install window or <code>%ProgramData%\\SentrySixManager\\data\\logs\\service.log</code>';

/** The continue page's template (web/continue.html) with the log line for this deployment. */
function continueTemplate(html, deployment) {
  if (deployment !== 'windows') return html;
  return html.replace(DOCKER_LOG, WINDOWS_LOG);
}

/** What the page shows in place of the fingerprint when the server has none to show (plain text). */
function fingerprintFallback(deployment) {
  return deployment === 'windows' ? 'shown in the install window and service.log' : 'shown in the container log';
}

module.exports = { continueTemplate, fingerprintFallback, DOCKER_LOG, WINDOWS_LOG };
