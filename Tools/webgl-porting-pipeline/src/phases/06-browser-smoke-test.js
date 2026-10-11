'use strict';

// Phase 6 - Playwright browser smoke test
// Loads a running WebGL build (a Phase 5 server URL) in a real headless
// browser and checks it actually renders - a Unity <canvas> with non-zero
// size appears, no uncaught page errors or console errors fired during
// load. Requires the optional `playwright` package (not installed by
// default - see package.json's optionalDependencies and README). Never
// silently treated as a pass: if Playwright is missing, callers must report
// NOT_RUN, not PASS.
//
// Browser/page lifecycle is split out (createPage/closeBrowser) so a caller
// can hand the same live `page` to Phase 7's dynamic SDK-bridge probe
// afterward, instead of each phase opening and closing its own browser.

const path = require('path');
const fs = require('fs');

function isPlaywrightAvailable() {
  try {
    require.resolve('playwright');
    return true;
  } catch (err) {
    return false;
  }
}

async function createPage() {
  const { chromium } = require('playwright');
  const browser = await chromium.launch();
  const page = await browser.newPage();
  return { browser, page };
}

async function runBrowserSmokeTest(page, { url, screenshotDir, timeoutMs = 60000 } = {}) {
  const result = {
    phase: 6,
    name: 'browser-smoke-test',
    generatedAt: new Date().toISOString(),
    source: 'browser-test',
    status: 'NOT_RUN',
    url,
    consoleErrors: [],
    pageErrors: [],
    screenshotPath: null,
    canvasFound: false,
    canvasSize: null,
    durationMs: null,
    reason: null,
  };

  const startedAt = Date.now();
  try {
    page.on('console', (msg) => {
      if (msg.type() === 'error') result.consoleErrors.push(msg.text());
    });
    page.on('pageerror', (err) => {
      result.pageErrors.push(String(err));
    });

    await page.goto(url, { waitUntil: 'load', timeout: timeoutMs });

    try {
      await page.waitForSelector('canvas', { timeout: timeoutMs });
      const box = await page.evaluate(() => {
        const c = document.querySelector('canvas');
        return c ? { width: c.clientWidth, height: c.clientHeight } : null;
      });
      result.canvasSize = box;
      result.canvasFound = !!(box && box.width > 0 && box.height > 0);
    } catch (err) {
      result.canvasFound = false;
    }

    if (screenshotDir) {
      fs.mkdirSync(screenshotDir, { recursive: true });
      result.screenshotPath = path.join(screenshotDir, 'smoke-test.png');
      await page.screenshot({ path: result.screenshotPath });
    }

    result.durationMs = Date.now() - startedAt;

    if (!result.canvasFound) {
      result.status = 'FAILED';
      result.reason =
        'No <canvas> element with non-zero size appeared within the timeout ' +
        '- the WebGL build did not render. Note: a canvas existing while the ' +
        'Unity loading bar is still filling is a known limitation of this ' +
        'generic check - it does not confirm gameplay actually started, only ' +
        'that a GPU surface was created.';
    } else if (result.pageErrors.length > 0) {
      result.status = 'FAILED';
      result.reason = `${result.pageErrors.length} uncaught page error(s) during load.`;
    } else if (result.consoleErrors.length > 0) {
      result.status = 'PASSED_WITH_WARNINGS';
      result.reason = `Canvas rendered, but ${result.consoleErrors.length} console error(s) were logged during load.`;
    } else {
      result.status = 'PASSED';
    }
  } catch (err) {
    result.status = 'ERROR';
    result.reason = `Smoke test crashed: ${err.message}`;
  }

  return result;
}

module.exports = { isPlaywrightAvailable, createPage, runBrowserSmokeTest };
