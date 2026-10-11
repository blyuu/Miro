'use strict';

// Phase 8 - Report Generation
// Aggregates whatever phase JSON artifacts exist for a project into one
// combined JSON + one Markdown report. Only ever writes under this tool's
// own reports/ directory - never touches the target project.

const fs = require('fs');
const path = require('path');

function severityBadge(severity) {
  return { error: '🔴 ERROR', warning: '🟡 WARNING', info: '🔵 INFO' }[severity] || severity;
}

function renderProjectAnalysisSection(phase1) {
  if (!phase1) return '_Phase 1 (Project Analysis) was not run._\n';
  const lines = [];
  lines.push(`- **Unity version**: ${phase1.unityVersion.version || 'unknown'}`);
  lines.push(`- **Assets/ size**: ${phase1.assets.sizeFormatted}`);
  lines.push(`- **StreamingAssets present**: ${phase1.assets.hasStreamingAssets}`);
  lines.push(`- **Package dependencies**: ${phase1.packages.manifest ? Object.keys(phase1.packages.manifest.dependencies || {}).length : 'unknown'}`);
  lines.push(`- **scriptingBackend overrides**: ${JSON.stringify(phase1.scriptingBackendPerPlatform)}`);
  lines.push('');
  lines.push('| Severity | Finding |');
  lines.push('|---|---|');
  for (const f of phase1.findings) {
    lines.push(`| ${severityBadge(f.severity)} | ${f.message} |`);
  }
  return lines.join('\n') + '\n';
}

function renderApiPluginSection(phase2) {
  if (!phase2) return '_Phase 2 (API/Plugin Scan) was not run._\n';
  const lines = [];
  const s = phase2.summary;
  lines.push(`- **.cs files scanned**: ${s.csFilesScanned}`);
  lines.push(`- **API pattern matches**: ${s.apiMatches}`);
  lines.push(`- **Plugin findings**: ${s.pluginFindings}`);
  lines.push(`- **Package findings**: ${s.packageFindings}`);
  lines.push(`- **Total across all three, by severity**: 🔴 ${s.bySeverity.error || 0} / 🟡 ${s.bySeverity.warning || 0} / 🔵 ${s.bySeverity.info || 0}`);
  lines.push('');

  if (phase2.apiMatches.length > 0) {
    lines.push('### Incompatible API usage');
    lines.push('');
    lines.push('| Severity | Category | File:Line | Reason |');
    lines.push('|---|---|---|---|');
    for (const m of phase2.apiMatches) {
      lines.push(`| ${severityBadge(m.severity)} | ${m.category} | \`${m.file}:${m.line}\` | ${m.reason} |`);
    }
    lines.push('');
  } else {
    lines.push('No incompatible API patterns matched. ✅');
    lines.push('');
  }

  if (phase2.pluginFindings.length > 0) {
    lines.push('### Native plugins / JS bridge');
    lines.push('');
    lines.push('| Severity | Category | Detail |');
    lines.push('|---|---|---|');
    for (const f of phase2.pluginFindings) {
      const detail = f.file ? `\`${f.file}\` - ${f.reason}` : f.reason;
      lines.push(`| ${severityBadge(f.severity)} | ${f.category} | ${detail} |`);
    }
    lines.push('');
  }

  if (phase2.packageFindings.length > 0) {
    lines.push('### Package compatibility notes');
    lines.push('');
    lines.push('| Severity | Package | Note |');
    lines.push('|---|---|---|');
    for (const f of phase2.packageFindings) {
      lines.push(`| ${severityBadge(f.severity)} | \`${f.package}\`@${f.version} | ${f.reason} |`);
    }
    lines.push('');
  }

  return lines.join('\n');
}

function statusBadge(status) {
  return (
    {
      SUCCESS: '✅ SUCCESS',
      PASSED: '✅ PASSED',
      FAILED: '🔴 FAILED',
      ERROR: '🔴 ERROR',
      NOT_RUN: '⬜ NOT_RUN',
      NOT_APPLICABLE: '⬜ NOT_APPLICABLE',
      UNKNOWN: '🟡 UNKNOWN',
      RUNNING: '🟢 RUNNING',
      PASSED_WITH_WARNINGS: '🟡 PASSED_WITH_WARNINGS',
      BLOCKED_BY_FAILED_BUILD: '🔴 BLOCKED_BY_FAILED_BUILD',
    }[status] || status || 'NOT_RUN'
  );
}

// Hard rule from the task brief: a failed (or not-yet-run) WebGL build must
// never let a downstream browser-test phase be read as a passing result,
// even if that phase's own JSON claims otherwise (defense in depth - the CLI
// already refuses to start Phase 5/6 in this situation, but the report
// enforces it again at render time in case phase JSON files are inspected
// independently of the CLI, e.g. hand-edited or copied between runs).
function effectiveStatus(phase3, phaseResult) {
  if (!phaseResult) return null;
  const buildOk = phase3 && phase3.status === 'SUCCESS';
  if (!buildOk && ['PASSED', 'SUCCESS', 'PASSED_WITH_WARNINGS'].includes(phaseResult.status)) {
    return 'BLOCKED_BY_FAILED_BUILD';
  }
  return phaseResult.status;
}

function renderBuildSection(phase3) {
  if (!phase3) return '_Phase 3 (Unity Batchmode WebGL Build) has not been run. Status: NOT_RUN._\n';
  const lines = [];
  lines.push(`- **Status**: ${statusBadge(phase3.status)}`);
  if (phase3.command) lines.push(`- **Command**: \`${phase3.command}\``);
  if (phase3.outputPath) lines.push(`- **Output path**: \`${phase3.outputPath}\``);
  if (phase3.logFile) lines.push(`- **Log file**: \`${phase3.logFile}\``);
  if (phase3.provenancePath) lines.push(`- **Provenance manifest**: \`${phase3.provenancePath}\``);
  if (phase3.durationMs != null) lines.push(`- **Duration**: ${(phase3.durationMs / 1000).toFixed(1)}s`);
  if (phase3.exitCode != null) lines.push(`- **unity CLI exit code**: ${phase3.exitCode}`);
  if (phase3.reason) lines.push(`- **Detail**: ${phase3.reason}`);
  return lines.join('\n') + '\n';
}

function renderBuildLogSection(phase4) {
  if (!phase4) return '_Phase 4 (Build Log Analysis) has not been run. Status: NOT_RUN._\n';
  const lines = [];
  lines.push(`- **Status**: ${statusBadge(phase4.status)}`);
  if (phase4.buildResult) lines.push(`- **Unity build-result marker**: ${phase4.buildResult}`);
  lines.push(`- **Compile errors found**: ${phase4.compileErrors.length}`);
  if (phase4.reason) lines.push(`- **Detail**: ${phase4.reason}`);
  if (phase4.compileErrors.length > 0) {
    lines.push('');
    lines.push('| Code | File:Line | Message |');
    lines.push('|---|---|---|');
    for (const ce of phase4.compileErrors) {
      lines.push(`| ${ce.code} | \`${ce.file}:${ce.line}\` | ${ce.message} |`);
    }
  }
  return lines.join('\n') + '\n';
}

function renderServerSection(phase5) {
  if (!phase5) return '_Phase 5 (Local WebGL Server) has not been run. Status: NOT_RUN._\n';
  const lines = [];
  lines.push(`- **Status**: ${statusBadge(phase5.status)}`);
  if (phase5.buildDir) lines.push(`- **Serving**: \`${phase5.buildDir}\``);
  if (phase5.url) lines.push(`- **URL**: ${phase5.url}`);
  lines.push(`- **COOP/COEP headers (threads support)**: ${phase5.threadsSupportHeaders ? 'enabled' : 'not needed'}`);
  return lines.join('\n') + '\n';
}

function renderSmokeTestSection(phase3, phase6) {
  if (!phase6) return '_Phase 6 (Playwright Browser Smoke Test) has not been run. Status: NOT_RUN._\n';
  const status = effectiveStatus(phase3, phase6);
  const lines = [];
  lines.push(`- **Status**: ${statusBadge(status)}`);
  if (status === 'BLOCKED_BY_FAILED_BUILD') {
    lines.push(`- **Detail**: Phase 3's build did not succeed, so this phase's own reported status (${phase6.status}) is overridden - a browser test is never treated as passing without a successful build backing it.`);
  } else if (phase6.reason) {
    lines.push(`- **Detail**: ${phase6.reason}`);
  }
  if (phase6.url) lines.push(`- **URL tested**: ${phase6.url}`);
  if (phase6.canvasSize) lines.push(`- **Canvas size**: ${phase6.canvasSize.width}x${phase6.canvasSize.height}`);
  lines.push(`- **Console errors**: ${phase6.consoleErrors ? phase6.consoleErrors.length : 0}`);
  lines.push(`- **Uncaught page errors**: ${phase6.pageErrors ? phase6.pageErrors.length : 0}`);
  if (phase6.screenshotPath) lines.push(`- **Screenshot**: \`${phase6.screenshotPath}\``);
  return lines.join('\n') + '\n';
}

function renderBridgeSection(phase3, phase7) {
  if (!phase7) return '_Phase 7 (SDK Mock / JS Bridge Verification) has not been run. Status: NOT_RUN._\n';
  const status = effectiveStatus(phase3, phase7);
  const lines = [];
  lines.push(`- **Status**: ${statusBadge(status)}`);
  lines.push(`- **.jslib files found**: ${phase7.jslibFiles.length}`);
  lines.push(`- **Bridge-named scripts found (heuristic)**: ${phase7.bridgeNamedScripts.length}`);
  if (phase7.reason) lines.push(`- **Detail**: ${phase7.reason}`);
  if (phase7.dynamicProbe && phase7.dynamicProbe.length > 0) {
    lines.push('');
    lines.push('| Exported function | Found on `window` after load |');
    lines.push('|---|---|');
    for (const p of phase7.dynamicProbe) {
      lines.push(`| \`${p.name}\` | ${p.existsOnWindow ? '✅' : '❌'} |`);
    }
  }
  return lines.join('\n') + '\n';
}

function runReport(projectPath, projectReportsDir, phaseResults) {
  const phase3 = phaseResults['03-webgl-build'];
  const combined = {
    projectPath,
    generatedAt: new Date().toISOString(),
    source:
      'This report mixes static-analysis (deterministic, Phases 1-2), ' +
      'compiler (ground truth, Phases 3-4), and browser-test (ground truth, ' +
      'Phases 5-7) findings. A phase that was not actually executed is ' +
      'recorded as NOT_RUN, never as a passing result. A browser-test phase ' +
      'is reported BLOCKED_BY_FAILED_BUILD, never as passing, whenever Phase ' +
      "3's build did not succeed. No ai-analysis findings are included " +
      'unless --ai-hints was used to generate a separate prompt file.',
    phases: phaseResults,
  };

  const md = [];
  md.push(`# Unity → WebGL Porting Report`);
  md.push('');
  md.push(`**Project**: \`${projectPath}\``);
  md.push(`**Generated**: ${combined.generatedAt}`);
  md.push('');
  md.push(
    '> Phases 1-2 are static file analysis. Phases 3-4 reflect an actual ' +
    'Unity Editor build (compiler ground truth) only when `build --confirm` ' +
    'has been run. Phases 5-7 reflect an actual local server + real browser ' +
    '(Playwright) run only when `serve`/`smoke-test --confirm` have been ' +
    'run. Any phase shown as NOT_RUN below was not executed - its status is ' +
    'not a pass.'
  );
  md.push('');
  md.push('## Phase 1 — Project Analysis');
  md.push('');
  md.push(renderProjectAnalysisSection(phaseResults['01-project-analysis']));
  md.push('## Phase 2 — API / Plugin Compatibility Scan');
  md.push('');
  md.push(renderApiPluginSection(phaseResults['02-api-plugin-scan']));
  md.push('## Phase 3 — Unity Batchmode WebGL Build');
  md.push('');
  md.push(renderBuildSection(phase3));
  md.push('## Phase 4 — Build Log / Compile Error Analysis');
  md.push('');
  md.push(renderBuildLogSection(phaseResults['04-build-log-analysis']));
  md.push('## Phase 5 — Local WebGL Server');
  md.push('');
  md.push(renderServerSection(phaseResults['05-local-server']));
  md.push('## Phase 6 — Playwright Browser Smoke Test');
  md.push('');
  md.push(renderSmokeTestSection(phase3, phaseResults['06-browser-smoke-test']));
  md.push('## Phase 7 — SDK Mock / JS Bridge Verification');
  md.push('');
  md.push(renderBridgeSection(phase3, phaseResults['07-sdk-bridge-verification']));

  fs.mkdirSync(projectReportsDir, { recursive: true });
  const jsonPath = path.join(projectReportsDir, 'webgl-porting-report.json');
  const mdPath = path.join(projectReportsDir, 'webgl-porting-report.md');
  fs.writeFileSync(jsonPath, JSON.stringify(combined, null, 2));
  fs.writeFileSync(mdPath, md.join('\n'));

  return { jsonPath, mdPath };
}

module.exports = { runReport };
