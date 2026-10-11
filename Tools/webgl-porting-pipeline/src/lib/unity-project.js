'use strict';

// Pure filesystem helpers for reading a Unity project's structure without
// needing a running Unity Editor. Every function takes an explicit
// projectPath - nothing here assumes a fixed project location, so this
// module (and everything built on it) is reusable across Unity projects.

const fs = require('fs');
const path = require('path');

function readTextIfExists(filePath) {
  try {
    return fs.readFileSync(filePath, 'utf8');
  } catch (err) {
    if (err.code === 'ENOENT') return null;
    throw err;
  }
}

function readJsonIfExists(filePath) {
  const text = readTextIfExists(filePath);
  if (text == null) return null;
  try {
    return JSON.parse(text);
  } catch (err) {
    return { __parseError: String(err.message || err) };
  }
}

function assertUnityProject(projectPath) {
  const projectSettingsDir = path.join(projectPath, 'ProjectSettings');
  const assetsDir = path.join(projectPath, 'Assets');
  if (!fs.existsSync(projectSettingsDir) || !fs.existsSync(assetsDir)) {
    throw new Error(
      `"${projectPath}" does not look like a Unity project root ` +
      `(expected both ProjectSettings/ and Assets/ to exist).`
    );
  }
}

function getUnityVersion(projectPath) {
  const text = readTextIfExists(path.join(projectPath, 'ProjectSettings', 'ProjectVersion.txt'));
  if (!text) return { version: null, revision: null };
  const versionMatch = text.match(/m_EditorVersion:\s*(\S+)/);
  const revisionMatch = text.match(/m_EditorVersionWithRevision:\s*(\S+)/);
  return {
    version: versionMatch ? versionMatch[1] : null,
    revisionLine: revisionMatch ? revisionMatch[1] : null,
  };
}

// ProjectSettings.asset is YAML when the project uses "Force Text" asset
// serialization (Unity's default, and strongly recommended for version
// control - if a project uses binary serialization instead this will return
// null and the caller should report that as a finding, not crash).
function readProjectSettingsYamlText(projectPath) {
  return readTextIfExists(path.join(projectPath, 'ProjectSettings', 'ProjectSettings.asset'));
}

// Minimal targeted YAML field extraction - deliberately not a full YAML
// parser (Unity's serialized YAML has custom tags like %TAG that trip up
// generic parsers). We only need a handful of scalar fields, so a line-based
// regex scan is more robust here than pulling in a YAML dependency.
function extractYamlScalar(yamlText, fieldName) {
  if (!yamlText) return null;
  const re = new RegExp(`^\\s*${fieldName}:\\s*(.+)\\s*$`, 'm');
  const m = yamlText.match(re);
  return m ? m[1].trim() : null;
}

// Extracts a small nested map like:
//   scriptingBackend:
//     Android: 1
// into { Android: "1" }. Deliberately narrow (not a general YAML parser) -
// only handles one level of "key:\n  subkey: value" nesting, which is all
// Unity's per-platform PlayerSettings overrides ever use.
function extractYamlNestedMap(yamlText, fieldName) {
  if (!yamlText) return {};
  const re = new RegExp(`^(\\s*)${fieldName}:\\s*$([\\s\\S]*?)(?=^\\1\\S)`, 'm');
  const m = yamlText.match(re);
  if (!m) return {};
  const block = m[2];
  const result = {};
  for (const line of block.split('\n')) {
    const kv = line.match(/^\s+(\w+):\s*(.+)\s*$/);
    if (kv) result[kv[1]] = kv[2].trim();
  }
  return result;
}

function getPackageManifest(projectPath) {
  return readJsonIfExists(path.join(projectPath, 'Packages', 'manifest.json'));
}

function getPackageLock(projectPath) {
  return readJsonIfExists(path.join(projectPath, 'Packages', 'packages-lock.json'));
}

function getBuildTargetsFromEditorBuildSettings(projectPath) {
  const text = readTextIfExists(path.join(projectPath, 'ProjectSettings', 'EditorBuildSettings.asset'));
  if (!text) return null;
  const sceneMatches = [...text.matchAll(/path:\s*(Assets\/[^\r\n]+\.unity)/g)].map((m) => m[1]);
  return { scenesInBuild: sceneMatches };
}

// Walk a directory tree, yielding absolute file paths. Skips common huge
// non-source directories (Library/, Temp/, .git/, node_modules/) so this
// stays fast even on a multi-GB Unity project.
function* walkFiles(rootDir, { skipDirNames = new Set(['Library', 'Temp', 'obj', '.git', 'node_modules', 'Logs']) } = {}) {
  const stack = [rootDir];
  while (stack.length) {
    const dir = stack.pop();
    let entries;
    try {
      entries = fs.readdirSync(dir, { withFileTypes: true });
    } catch (err) {
      continue; // permission errors etc. - skip, don't crash the whole scan
    }
    for (const entry of entries) {
      if (entry.isDirectory()) {
        if (skipDirNames.has(entry.name)) continue;
        stack.push(path.join(dir, entry.name));
      } else {
        yield path.join(dir, entry.name);
      }
    }
  }
}

function dirSizeBytes(dir) {
  let total = 0;
  if (!fs.existsSync(dir)) return 0;
  for (const file of walkFiles(dir)) {
    try {
      total += fs.statSync(file).size;
    } catch (err) {
      /* ignore */
    }
  }
  return total;
}

function formatBytes(bytes) {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${units[i]}`;
}

module.exports = {
  readTextIfExists,
  readJsonIfExists,
  assertUnityProject,
  getUnityVersion,
  readProjectSettingsYamlText,
  extractYamlScalar,
  extractYamlNestedMap,
  getPackageManifest,
  getPackageLock,
  getBuildTargetsFromEditorBuildSettings,
  walkFiles,
  dirSizeBytes,
  formatBytes,
};
