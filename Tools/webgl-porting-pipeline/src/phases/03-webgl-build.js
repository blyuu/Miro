'use strict';

// Phase 3 - Unity Batchmode WebGL Build
// Shells out to the already-installed `unity` CLI (unity-cli skill) to run an
// actual Editor build. This is the one phase in this pipeline that invokes a
// real Unity Editor process - the CLI entry point only reaches this module
// when --confirm was passed explicitly.
//
// Build strategy: a Unity 6+ Build Profile (`--profile`), never
// `--execute-method`. A profile is just an asset under
// `Assets/Settings/Build Profiles/` - creating/using one never forces a C#
// script recompile, unlike injecting a custom Editor build script into the
// target project's Assets/. That matters here because another agent may have
// a live Editor session open doing Shader/Scene/Material/UI work; a
// profile-based build cannot disturb that compile state the way adding a new
// .cs file would.

const fs = require('fs');
const path = require('path');
const { spawn, spawnSync } = require('child_process');
const { assertUnityProject } = require('../lib/unity-project');

function isUnityCliAvailable() {
  const probe = spawnSync('unity', ['--version'], { encoding: 'utf8', windowsHide: true });
  return !probe.error && probe.status === 0;
}

function parseJsonLines(text) {
  const frames = [];
  for (const line of text.split('\n')) {
    const trimmed = line.trim();
    if (!trimmed.startsWith('{')) continue;
    try {
      frames.push(JSON.parse(trimmed));
    } catch (err) {
      /* not a JSON line (raw Unity log leaking onto stdout) - ignore */
    }
  }
  return frames;
}

function runUnityCliCommand(args, { onStdoutLine } = {}) {
  return new Promise((resolve) => {
    let child;
    try {
      child = spawn('unity', args, { windowsHide: true });
    } catch (err) {
      resolve({ exitCode: null, stdout: '', stderr: String(err.message || err), spawnError: err });
      return;
    }
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (chunk) => {
      const text = chunk.toString();
      stdout += text;
      if (onStdoutLine) {
        for (const line of text.split('\n')) {
          if (line.trim()) onStdoutLine(line);
        }
      }
    });
    child.stderr.on('data', (chunk) => {
      stderr += chunk.toString();
      process.stderr.write(chunk);
    });
    child.on('error', (err) => {
      resolve({ exitCode: null, stdout, stderr: stderr + `\n${err.message}`, spawnError: err });
    });
    child.on('close', (code) => {
      resolve({ exitCode: code, stdout, stderr, spawnError: null });
    });
  });
}

function hasWebGLProfileAsset(projectPath) {
  const profilesDir = path.join(projectPath, 'Assets', 'Settings', 'Build Profiles');
  if (!fs.existsSync(profilesDir)) return false;
  try {
    return fs
      .readdirSync(profilesDir)
      .some((f) => f.toLowerCase().endsWith('.asset') && f.toLowerCase().includes('webgl'));
  } catch (err) {
    return false;
  }
}

async function createWebGLBuildProfile(projectPath, { editorPath, editorVersion } = {}) {
  const args = ['build', projectPath, '--create-profile', 'WebGL', '--format', 'json', '--non-interactive'];
  if (editorPath) args.push('--editor-path', editorPath);
  if (editorVersion) args.push('--editor-version', editorVersion);
  console.log(`[Phase 3] No WebGL build profile asset found - creating one (asset-only, no script is added):\n  unity ${args.join(' ')}`);
  return runUnityCliCommand(args, { onStdoutLine: (l) => console.log(l) });
}

async function runWebGLBuild(projectPath, opts = {}) {
  assertUnityProject(projectPath);

  const {
    outputPath,
    logFile,
    provenancePath,
    profile = 'WebGL',
    createProfileIfMissing = true,
    editorPath,
    editorVersion,
    timeoutSec,
    // Other agents are very likely mid-edit on this project's working tree.
    // This is a verification build, not a release build, so a dirty tree is
    // expected and should not block it by default.
    allowDirtyBuild = true,
  } = opts;

  const result = {
    phase: 3,
    name: 'webgl-build',
    projectPath,
    generatedAt: new Date().toISOString(),
    source: 'compiler', // ground truth - a real Editor build, not static analysis
    status: 'NOT_RUN',
    outputPath: outputPath || null,
    logFile: logFile || null,
    provenancePath: null,
    exitCode: null,
    durationMs: null,
    command: null,
    reason: null,
  };

  if (!outputPath) {
    result.status = 'ERROR';
    result.reason = 'runWebGLBuild requires an outputPath (--output-path).';
    return result;
  }

  if (!isUnityCliAvailable()) {
    result.status = 'NOT_RUN';
    result.reason =
      'The `unity` CLI (unity-cli skill) was not found on PATH. Install it ' +
      'first, then re-run this command. This pipeline has no fallback ' +
      'direct-Editor-invocation path by design - it reuses the already-' +
      'vetted unity-cli build/provenance/log machinery instead of ' +
      're-implementing editor discovery and batch-mode flag handling.';
    return result;
  }

  if (createProfileIfMissing && !hasWebGLProfileAsset(projectPath)) {
    const createResult = await createWebGLBuildProfile(projectPath, { editorPath, editorVersion });
    if (createResult.exitCode !== 0) {
      result.status = 'ERROR';
      result.reason = `Failed to create a WebGL build profile (exit ${createResult.exitCode}). stderr: ${createResult.stderr.slice(0, 2000)}`;
      return result;
    }
  }

  const args = ['build', projectPath, '--profile', profile, '--output-path', outputPath, '--format', 'json', '--non-interactive'];
  if (logFile) args.push('--log-file', logFile);
  if (provenancePath) args.push('--provenance-path', provenancePath);
  if (editorPath) args.push('--editor-path', editorPath);
  if (editorVersion) args.push('--editor-version', editorVersion);
  if (timeoutSec) args.push('--timeout', String(timeoutSec));
  if (allowDirtyBuild) args.push('--allow-dirty-build');

  result.command = `unity ${args.join(' ')}`;
  console.log(`[Phase 3] Running:\n  ${result.command}\n`);

  const startedAt = Date.now();
  const run = await runUnityCliCommand(args, { onStdoutLine: (l) => console.log(l) });
  result.durationMs = Date.now() - startedAt;
  result.exitCode = run.exitCode;

  const frames = parseJsonLines(run.stdout);
  const envelope = frames.find((f) => f.command === 'build' && typeof f.success === 'boolean');
  if (envelope && envelope.data) {
    result.logFile = envelope.data.logFile || result.logFile || logFile || null;
    result.provenancePath = envelope.data.provenance || provenancePath || null;
  } else {
    result.logFile = result.logFile || logFile || null;
    result.provenancePath = provenancePath || null;
  }

  if (run.spawnError) {
    result.status = 'ERROR';
    result.reason = `Failed to invoke the unity CLI: ${run.spawnError.message}`;
  } else if (run.exitCode === 0 && (!envelope || envelope.success)) {
    result.status = 'SUCCESS';
  } else {
    result.status = 'FAILED';
    result.reason =
      envelope && Array.isArray(envelope.errors) && envelope.errors.length
        ? envelope.errors.map((e) => (typeof e === 'string' ? e : e.message || JSON.stringify(e))).join('; ')
        : `unity build exited with code ${run.exitCode}. See the log file for detail: ${result.logFile}`;
  }

  return result;
}

module.exports = { runWebGLBuild, isUnityCliAvailable, hasWebGLProfileAsset };
