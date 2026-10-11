'use strict';

// Phase 1 - Project Analysis
// Pure filesystem read of ProjectSettings/, Packages/, and top-level Assets/
// structure. No live Unity Editor required. Nothing is written back to the
// target project - this only writes its own JSON artifact under reports/.

const fs = require('fs');
const path = require('path');
const {
  assertUnityProject,
  getUnityVersion,
  readProjectSettingsYamlText,
  extractYamlScalar,
  extractYamlNestedMap,
  getPackageManifest,
  getPackageLock,
  getBuildTargetsFromEditorBuildSettings,
  dirSizeBytes,
  formatBytes,
} = require('../lib/unity-project');

const WEBGL_RELEVANT_FIELDS = [
  'webGLMemorySize',
  'webGLExceptionSupport',
  'webGLTemplate',
  'webGLCompressionFormat',
  'webGLLinkerTarget',
  'webGLThreadsSupport',
  'webGLDecompressionFallback',
  'webGLPowerPreference',
  'webEnableSubmoduleStrippingCompatibility',
  'webProgressiveAssetLoading',
  'colorSpace',
  'defaultScreenWidthWeb',
];

function runProjectAnalysis(projectPath) {
  assertUnityProject(projectPath);

  const findings = [];
  const info = (message, data) => findings.push({ severity: 'info', source: 'static-analysis', message, data });
  const warn = (message, data) => findings.push({ severity: 'warning', source: 'static-analysis', message, data });

  const unityVersion = getUnityVersion(projectPath);
  info(`Unity Editor version: ${unityVersion.version || 'UNKNOWN'}`, unityVersion);

  const yamlText = readProjectSettingsYamlText(projectPath);
  if (!yamlText) {
    warn(
      'ProjectSettings.asset could not be read as text - the project may use ' +
      'binary asset serialization (Edit > Project Settings > Editor > Asset ' +
      'Serialization = Mixed/Binary). Force Text is required for this tool ' +
      '(and strongly recommended for version control generally).',
      {}
    );
  }

  const playerSettings = {};
  for (const field of WEBGL_RELEVANT_FIELDS) {
    playerSettings[field] = extractYamlScalar(yamlText, field);
  }
  const scriptingBackendPerPlatform = extractYamlNestedMap(yamlText, 'scriptingBackend');

  if (!('WebGL' in scriptingBackendPerPlatform)) {
    info(
      'No explicit WebGL scriptingBackend override found - Unity will use ' +
      'the implicit default, which is IL2CPP (the only backend WebGL ' +
      'supports in modern Unity; Mono/WebGL was removed). Not a problem, ' +
      'just not explicitly configured.',
      { scriptingBackendPerPlatform }
    );
  } else if (scriptingBackendPerPlatform.WebGL !== '1') {
    warn(
      `scriptingBackend.WebGL = ${scriptingBackendPerPlatform.WebGL} (expected 1 = IL2CPP). ` +
      'WebGL does not support the Mono scripting backend in modern Unity.',
      { scriptingBackendPerPlatform }
    );
  }

  if (playerSettings.webGLThreadsSupport === '1') {
    warn(
      'webGLThreadsSupport is enabled. This requires the browser to send ' +
      'COOP/COEP headers (cross-origin isolation) and SharedArrayBuffer ' +
      'support - Phase 5 (local server) and the real deployment host must ' +
      'both serve those headers or the build will fail to load threads.',
      { webGLThreadsSupport: playerSettings.webGLThreadsSupport }
    );
  }

  if (playerSettings.colorSpace && playerSettings.colorSpace !== '1') {
    info(
      `Color Space = ${playerSettings.colorSpace} (0 = Gamma, 1 = Linear). ` +
      'Linear color space requires WebGL2; verify target browsers/devices support it.',
      { colorSpace: playerSettings.colorSpace }
    );
  }

  const buildSettings = getBuildTargetsFromEditorBuildSettings(projectPath);

  const manifest = getPackageManifest(projectPath);
  const lock = getPackageLock(projectPath);
  const dependencyCount = manifest && manifest.dependencies ? Object.keys(manifest.dependencies).length : 0;
  info(`${dependencyCount} direct package dependencies in Packages/manifest.json`, {
    dependencies: manifest ? manifest.dependencies : null,
  });

  const assetsDir = path.join(projectPath, 'Assets');
  const assetsSizeBytes = dirSizeBytes(assetsDir);
  info(`Assets/ folder size: ${formatBytes(assetsSizeBytes)}`, { bytes: assetsSizeBytes });

  const streamingAssetsDir = path.join(projectPath, 'Assets', 'StreamingAssets');
  if (fs.existsSync(streamingAssetsDir)) {
    const streamingSizeBytes = dirSizeBytes(streamingAssetsDir);
    info(`StreamingAssets/ present: ${formatBytes(streamingSizeBytes)}`, { bytes: streamingSizeBytes });
  }

  return {
    phase: 1,
    name: 'project-analysis',
    projectPath,
    generatedAt: new Date().toISOString(),
    unityVersion,
    playerSettings,
    scriptingBackendPerPlatform,
    buildSettings,
    packages: {
      manifest,
      lock: lock ? { packageCount: Object.keys(lock.dependencies || {}).length } : null,
    },
    assets: {
      sizeBytes: assetsSizeBytes,
      sizeFormatted: formatBytes(assetsSizeBytes),
      hasStreamingAssets: fs.existsSync(streamingAssetsDir),
    },
    findings,
  };
}

module.exports = { runProjectAnalysis };
