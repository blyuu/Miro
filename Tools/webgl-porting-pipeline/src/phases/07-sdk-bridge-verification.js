'use strict';

// Phase 7 - Platform SDK Mock / JS Bridge communication verification
// Two-part check:
//   1. Static (always runs): does this project have any .jslib (WebGL
//      native-plugin JS bridge) files, or C# scripts that look like an SDK
//      mock/bridge, at all? Phase 2 already scans for .jslib presence as
//      part of its WebGL-incompatibility sweep; this phase adds its own
//      narrow naming-pattern scan for SDK/Mock/Bridge-named scripts, since
//      that is specific to bridge *discovery*, not WebGL compatibility.
//   2. Dynamic (only when a live, already-loaded `page` is handed in from
//      Phase 6): checks for a `window.__projectMiroBridgeDebug` manifest -
//      NOT `window[exportedFunctionName]`. A jslib function merged into
//      LibraryManager.library is linked as a DllImport("__Internal") target
//      callable from C#; it is never attached to `window` under its own
//      name, so probing `window[name]` directly (an earlier version of this
//      phase did) would always report FAILED for a real jslib bridge - a
//      false negative. A bridge that wants to be introspectable instead
//      self-reports onto `window.__projectMiroBridgeDebug.confirmedCompletions`
//      once a call has round-tripped JS -> Unity -> JS (see
//      Assets/Plugins/WebGL/ProjectMiroBridge.jslib's header comment). This
//      phase treats that manifest as the ground truth for "the bridge is
//      alive", and is honest about reporting FAILED/NOT_RUN when a project's
//      bridge doesn't expose one.
//
// If no bridge exists at all, this reports NOT_APPLICABLE, not NOT_RUN -
// "nothing here to test" is a more honest status than "a test that should
// have run but didn't".

const fs = require('fs');
const path = require('path');
const { walkFiles } = require('../lib/unity-project');

const BRIDGE_NAME_RE = /sdk|mock|bridge|jsbridge/i;
const MERGE_INTO_MARKER_RE = /mergeInto\s*\(\s*LibraryManager\.library\s*,/;
const TOP_LEVEL_FN_RE = /^\s*([A-Za-z_$][\w$]*)\s*:\s*function\b/gm;

function findJslibFiles(assetsDir) {
  const files = [];
  for (const filePath of walkFiles(assetsDir)) {
    if (filePath.toLowerCase().endsWith('.jslib')) files.push(filePath);
  }
  return files;
}

function findBridgeNamedScripts(assetsDir) {
  const matches = [];
  for (const filePath of walkFiles(assetsDir)) {
    if (!filePath.toLowerCase().endsWith('.cs')) continue;
    if (BRIDGE_NAME_RE.test(path.basename(filePath))) matches.push(filePath);
  }
  return matches;
}

// Given the index of an opening '{' in `text`, returns the index of its
// matching '}' - skipping over string literals and //, /* */ comments so a
// brace or quote mentioned in a comment/string doesn't desync the count.
// Returns -1 if unterminated.
function findMatchingBrace(text, openIndex) {
  let depth = 1;
  let inString = null;
  let inLineComment = false;
  let inBlockComment = false;
  for (let i = openIndex + 1; i < text.length; i++) {
    const c = text[i];
    const next = text[i + 1];
    if (inLineComment) {
      if (c === '\n') inLineComment = false;
      continue;
    }
    if (inBlockComment) {
      if (c === '*' && next === '/') {
        inBlockComment = false;
        i++;
      }
      continue;
    }
    if (inString) {
      if (c === '\\') {
        i++;
        continue;
      }
      if (c === inString) inString = null;
      continue;
    }
    if (c === '/' && next === '/') {
      inLineComment = true;
      i++;
      continue;
    }
    if (c === '/' && next === '*') {
      inBlockComment = true;
      i++;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') {
      inString = c;
      continue;
    }
    if (c === '{') depth++;
    else if (c === '}') {
      depth--;
      if (depth === 0) return i;
    }
  }
  return -1;
}

// Nesting depth of every character in `str`, where 0 = top level of `str`
// itself. Same string/comment-skipping rules as findMatchingBrace, so a
// function name's `depth` can be checked to tell a top-level mergeInto entry
// apart from a function nested inside one (e.g. a debug-manifest object's
// own methods), which TOP_LEVEL_FN_RE alone can't distinguish.
function computeDepthMap(str) {
  const depths = new Array(str.length);
  let depth = 0;
  let inString = null;
  let inLineComment = false;
  let inBlockComment = false;
  for (let i = 0; i < str.length; i++) {
    depths[i] = depth;
    const c = str[i];
    const next = str[i + 1];
    if (inLineComment) {
      if (c === '\n') inLineComment = false;
      continue;
    }
    if (inBlockComment) {
      if (c === '*' && next === '/') {
        inBlockComment = false;
        i++;
      }
      continue;
    }
    if (inString) {
      if (c === '\\') {
        i++;
        continue;
      }
      if (c === inString) inString = null;
      continue;
    }
    if (c === '/' && next === '/') {
      inLineComment = true;
      i++;
      continue;
    }
    if (c === '/' && next === '*') {
      inBlockComment = true;
      i++;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') {
      inString = c;
      continue;
    }
    if (c === '{') depth++;
    else if (c === '}') depth--;
  }
  return depths;
}

function extractMergeIntoObjectBody(text) {
  const markerMatch = text.match(MERGE_INTO_MARKER_RE);
  if (!markerMatch) return null;
  let i = markerMatch.index + markerMatch[0].length;
  while (i < text.length && /\s/.test(text[i])) i++;
  if (text[i] !== '{') return null;
  const end = findMatchingBrace(text, i);
  if (end === -1) return null;
  return text.slice(i + 1, end);
}

function extractExportedFunctionNames(jslibPath) {
  let text;
  try {
    text = fs.readFileSync(jslibPath, 'utf8');
  } catch (err) {
    return [];
  }
  const body = extractMergeIntoObjectBody(text);
  if (!body) return [];

  const depths = computeDepthMap(body);
  const names = [];
  TOP_LEVEL_FN_RE.lastIndex = 0;
  let m;
  while ((m = TOP_LEVEL_FN_RE.exec(body)) !== null) {
    const keyIndex = m.index + m[0].indexOf(m[1]);
    // depth === 0 means this key is a direct property of the object passed
    // to mergeInto(), i.e. an actual exported library function - not a
    // method nested inside one of those functions' own return values
    // (e.g. a debug-manifest object literal's helper methods).
    if (depths[keyIndex] === 0 && !m[1].startsWith('$')) names.push(m[1]);
  }
  return names;
}

async function runSdkBridgeVerification(projectPath, { page } = {}) {
  const assetsDir = path.join(projectPath, 'Assets');
  const result = {
    phase: 7,
    name: 'sdk-bridge-verification',
    projectPath,
    generatedAt: new Date().toISOString(),
    source: 'static-analysis',
    status: 'NOT_APPLICABLE',
    jslibFiles: [],
    bridgeNamedScripts: [],
    exportedFunctions: [],
    dynamicProbe: null,
    reason: null,
  };

  const jslibFiles = findJslibFiles(assetsDir).map((f) => path.relative(projectPath, f).replace(/\\/g, '/'));
  const bridgeScripts = findBridgeNamedScripts(assetsDir).map((f) => path.relative(projectPath, f).replace(/\\/g, '/'));
  result.jslibFiles = jslibFiles;
  result.bridgeNamedScripts = bridgeScripts;

  if (jslibFiles.length === 0 && bridgeScripts.length === 0) {
    result.status = 'NOT_APPLICABLE';
    result.reason =
      'No .jslib bridge files and no SDK/Mock/Bridge-named C# scripts exist ' +
      'in this project yet. There is nothing for Phase 7 to verify - this ' +
      'harness is ready to run as soon as such a bridge is added.';
    return result;
  }

  for (const jslibRel of jslibFiles) {
    const abs = path.join(projectPath, jslibRel);
    result.exportedFunctions.push({ file: jslibRel, functions: extractExportedFunctionNames(abs) });
  }

  if (bridgeScripts.length > 0) {
    result.reason =
      `${bridgeScripts.length} script(s) matched the SDK/Mock/Bridge naming ` +
      'heuristic - this is a name-pattern match, not a semantic check; verify manually.';
  }

  if (!page) {
    result.status = 'NOT_RUN';
    result.reason =
      (result.reason ? result.reason + ' ' : '') +
      `Found ${jslibFiles.length} .jslib file(s) - no live browser page was ` +
      'available (Phase 6 did not run, or did not produce a usable page), ' +
      'so the dynamic manifest probe below was not attempted.';
    return result;
  }

  result.source = 'browser-test';
  const allFnNames = result.exportedFunctions.flatMap((e) => e.functions);

  if (allFnNames.length === 0) {
    result.status = 'NOT_RUN';
    result.reason =
      (result.reason ? result.reason + ' ' : '') +
      'Found .jslib file(s), but no top-level `name: function` entries could ' +
      'be parsed out of the mergeInto(LibraryManager.library, {...}) block, ' +
      'so there is nothing to check a debug manifest against.';
    return result;
  }

  const probe = await page.evaluate(() => {
    const manifest = window.__projectMiroBridgeDebug;
    if (!manifest) return { manifestFound: false };
    return {
      manifestFound: true,
      mock: !!manifest.mock,
      declaredExports: Array.isArray(manifest.exports) ? manifest.exports : [],
      confirmedCompletions: manifest.confirmedCompletions || {},
    };
  });
  result.dynamicProbe = probe;

  if (!probe.manifestFound) {
    result.status = 'FAILED';
    result.reason =
      (result.reason ? result.reason + ' ' : '') +
      'No `window.__projectMiroBridgeDebug` manifest was found after load. ' +
      'Either this bridge does not self-report one (see ' +
      'ProjectMiroBridge.jslib for the expected shape), the bridge code was ' +
      'stripped from the build because nothing in the game ever calls it, or ' +
      'the jslib never linked into this build at all.';
    return result;
  }

  const confirmedApis = Object.keys(probe.confirmedCompletions);
  if (confirmedApis.length === 0) {
    result.status = 'FAILED';
    result.reason =
      (result.reason ? result.reason + ' ' : '') +
      'A debug manifest was found, but no API has a confirmedCompletions ' +
      'entry yet - no JS -> Unity -> JS round trip has completed. If ' +
      'nothing in the game auto-triggers a bridge call on load, re-run this ' +
      'phase after manually triggering one (e.g. via a debug UI), or wait ' +
      'longer before probing.';
    return result;
  }

  result.status = 'PASSED';
  result.reason =
    (result.reason ? result.reason + ' ' : '') +
    `Confirmed a completed JS -> Unity -> JS round trip for: ${confirmedApis.join(', ')} ` +
    '(see dynamicProbe.confirmedCompletions for the status each resolved with - ' +
    'note that Failed/Cancelled/TimedOut here still count as a working round ' +
    'trip, since the bridge mechanism itself delivered that outcome correctly).';

  return result;
}

module.exports = { runSdkBridgeVerification };
