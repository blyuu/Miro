'use strict';

// Phase 2 - Incompatible API & Plugin Detection
// Regex-based scan of Assets/**/*.cs for known WebGL-incompatible patterns,
// plus a filesystem scan for native plugin binaries and existing .jslib
// JS-bridge files. Pure static analysis - no Unity Editor required, nothing
// written back to the target project.

const fs = require('fs');
const path = require('path');
const { walkFiles, getPackageManifest } = require('../lib/unity-project');

const API_RULES = require('../rules/incompatible-apis.json');
const PLUGIN_RULES = require('../rules/incompatible-plugins.json');

function compileApiRules() {
  return API_RULES.map((rule) => ({ ...rule, regex: new RegExp(rule.pattern, 'g') }));
}

function scanCsFilesForApiUsage(assetsDir) {
  const compiledRules = compileApiRules();
  const matches = [];
  let filesScanned = 0;

  for (const filePath of walkFiles(assetsDir)) {
    if (!filePath.toLowerCase().endsWith('.cs')) continue;
    filesScanned++;

    let text;
    try {
      text = fs.readFileSync(filePath, 'utf8');
    } catch (err) {
      continue;
    }
    const lines = text.split('\n');

    for (const rule of compiledRules) {
      rule.regex.lastIndex = 0;
      let m;
      while ((m = rule.regex.exec(text)) !== null) {
        const upToMatch = text.slice(0, m.index);
        const lineNumber = upToMatch.split('\n').length;
        matches.push({
          ruleId: rule.id,
          severity: rule.severity,
          category: rule.category,
          reason: rule.reason,
          file: path.relative(path.dirname(assetsDir), filePath).replace(/\\/g, '/'),
          line: lineNumber,
          snippet: (lines[lineNumber - 1] || '').trim().slice(0, 200),
        });
      }
    }
  }

  return { filesScanned, matches };
}

function scanForPluginBinaries(assetsDir) {
  const nativeExtSet = new Map(PLUGIN_RULES.alwaysNativeExtensions.map((e) => [e.ext.toLowerCase(), e]));
  const bridgeExtSet = new Map(PLUGIN_RULES.webglBridgeExtensions.map((e) => [e.ext.toLowerCase(), e]));

  const findings = [];
  const jslibFiles = [];

  for (const filePath of walkFiles(assetsDir)) {
    const ext = path.extname(filePath).toLowerCase();
    const relPath = path.relative(path.dirname(assetsDir), filePath).replace(/\\/g, '/');

    if (nativeExtSet.has(ext)) {
      const rule = nativeExtSet.get(ext);
      findings.push({
        severity: rule.severity,
        category: 'native-plugin',
        file: relPath,
        reason: `Native ${rule.platform} binary found - will not run on WebGL unless the project ships a WebGL-specific plugin variant for this functionality.`,
      });
      continue;
    }

    if (ext === '.dll') {
      const metaPath = filePath + '.meta';
      let metaText = null;
      try {
        metaText = fs.readFileSync(metaPath, 'utf8');
      } catch (err) {
        /* no .meta - skip disambiguation, Unity would regenerate it anyway */
      }
      const isNativePlugin = metaText && /isNativePlugin:\s*1/.test(metaText);
      if (isNativePlugin) {
        findings.push({
          severity: 'error',
          category: 'native-plugin',
          file: relPath,
          reason: 'DLL is marked as a native plugin (isNativePlugin: 1 in its .meta) - native Win32/Linux DLLs cannot run on WebGL.',
        });
      }
      continue;
    }

    if (bridgeExtSet.has(ext)) {
      jslibFiles.push(relPath);
    }
  }

  if (jslibFiles.length > 0) {
    findings.push({
      severity: 'info',
      category: 'webgl-bridge',
      reason: `Found ${jslibFiles.length} existing .jslib file(s) - these are the correct WebGL native-interop mechanism and are relevant to Phase 7 (SDK Mock / JS Bridge testing).`,
      files: jslibFiles,
    });
  } else {
    findings.push({
      severity: 'info',
      category: 'webgl-bridge',
      reason: 'No .jslib files found anywhere under Assets/. If this project needs a JS bridge for Phase 7 (Platform SDK Mock communication), it has not been implemented yet - Phase 7 will have nothing to test until one exists.',
      files: [],
    });
  }

  return findings;
}

function scanPackagesForKnownIssues(projectPath) {
  const manifest = getPackageManifest(projectPath);
  const findings = [];
  if (!manifest || !manifest.dependencies) return findings;

  for (const pkgId of Object.keys(manifest.dependencies)) {
    for (const rule of PLUGIN_RULES.knownProblematicPackages) {
      const isMatch = rule.matchType === 'prefix' ? pkgId.startsWith(rule.id) : pkgId === rule.id;
      if (isMatch) {
        findings.push({
          severity: 'warning',
          category: 'package',
          package: pkgId,
          version: manifest.dependencies[pkgId],
          reason: rule.note,
        });
      }
    }
  }
  return findings;
}

function runApiPluginScan(projectPath) {
  const assetsDir = path.join(projectPath, 'Assets');

  const apiScan = scanCsFilesForApiUsage(assetsDir);
  const pluginFindings = scanForPluginBinaries(assetsDir);
  const packageFindings = scanPackagesForKnownIssues(projectPath);

  const bySeverity = { error: 0, warning: 0, info: 0 };
  for (const m of [...apiScan.matches, ...pluginFindings, ...packageFindings]) {
    bySeverity[m.severity] = (bySeverity[m.severity] || 0) + 1;
  }

  return {
    phase: 2,
    name: 'api-plugin-scan',
    projectPath,
    generatedAt: new Date().toISOString(),
    source: 'static-analysis',
    summary: {
      csFilesScanned: apiScan.filesScanned,
      apiMatches: apiScan.matches.length,
      pluginFindings: pluginFindings.length,
      packageFindings: packageFindings.length,
      bySeverity,
    },
    apiMatches: apiScan.matches,
    pluginFindings,
    packageFindings,
  };
}

module.exports = { runApiPluginScan };
