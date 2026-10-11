#!/usr/bin/env node
'use strict';

// CLI entry point. Deliberately zero external dependencies for the commands
// implemented so far (Phases 1/2/8) - argv parsing is done by hand below
// since it's only a handful of flags. This keeps `npm install` unnecessary
// for anyone who just wants to run static analysis.

const path = require('path');
const fs = require('fs');

function parseArgs(argv) {
  const args = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const key = a.slice(2);
      const next = argv[i + 1];
      if (next === undefined || next.startsWith('--')) {
        args[key] = true;
      } else {
        args[key] = next;
        i++;
      }
    } else {
      args._.push(a);
    }
  }
  return args;
}

function printUsage() {
  console.log(`
Unity -> WebGL Porting Verification Pipeline

Usage:
  node src/cli.js analyze --project-path <path> [--ai-hints]
      Runs Phase 1 (project analysis) + Phase 2 (API/plugin scan) + Phase 8
      (report generation). Pure static analysis - safe to run at any time,
      does not touch the target project, does not require a running Unity
      Editor.

  node src/cli.js build --project-path <path> --confirm
      Phase 3+4: Unity Batchmode WebGL build via a Unity 6+ Build Profile
      (never --execute-method, so no custom script is added to the target
      project and no script recompile is forced), then build-log/compile-
      error analysis. Requires --confirm - it actually invokes the Unity
      Editor and writes build output + logs to disk.
        --output-path <path>    Build output dir (default: reports/<project>/build/WebGL)
        --log-file <path>       Build log path (default: reports/<project>/logs/...)
        --profile <name>        Build profile name (default: WebGL)
        --create-profile-if-missing false   Skip auto-creating a WebGL profile asset
        --editor-path / --editor-version / --timeout   Forwarded to 'unity build'
        --allow-dirty-build false   Do NOT pass --allow-dirty-build to 'unity build'
                                    (default: pass it - other agents likely have a
                                    dirty working tree; this is a verification build)

  node src/cli.js serve --project-path <path> --confirm
      Phase 5: local HTTP server (zero deps) for the WebGL build Phase 3
      produced - correct MIME/Content-Encoding for .gz/.br build files, and
      COOP/COEP headers when webGLThreadsSupport is enabled. Requires
      --confirm - starts a long-running local server (Ctrl+C to stop).
        --build-path <path>   Serve this directory instead of the last recorded build
        --port <n>            Fixed port (default: OS-assigned free port)
        --host <ip>           Bind address (default: 127.0.0.1, loopback-only).
                               Use 0.0.0.0 to also accept connections from other
                               devices on the LAN (e.g. a phone for mobile testing).

  node src/cli.js smoke-test --project-path <path> --confirm
      Phase 6+7: Playwright browser smoke test (loads the build, checks for
      a rendered <canvas>, console/page errors) + SDK Mock / JS Bridge
      verification (static discovery, plus a dynamic window-probe when the
      smoke test produced a live page). Requires --confirm. Requires the
      optional 'playwright' package - if missing, reports NOT_RUN (not a
      pass) and tells you to report + get approval before 'npm install
      playwright'.
        --url <url>      Test an already-running server instead of auto-starting one
        --timeout <sec>  Navigation/selector timeout (default: 60)

Flags:
  --project-path <path>   Required. Absolute or relative path to the target
                           Unity project root (the folder containing both
                           Assets/ and ProjectSettings/). Never hardcoded -
                           this tool is reusable across Unity projects.
  --confirm                Required by build/serve/smoke-test - these are the
                           phases that actually invoke a real Unity Editor
                           process, a real local server, or a real browser.
  --ai-hints               (analyze only) Also emit ai-hints-prompt.md - a
                           ready-to-paste prompt summarizing Phase 2 findings
                           for a Claude Code agent to read the flagged files
                           and suggest remediations. Generates a prompt only;
                           does not invoke an agent itself.
`);
}

function resolveProjectPath(args) {
  if (!args['project-path']) {
    console.error('Error: --project-path is required.\n');
    printUsage();
    process.exit(2);
  }
  return path.resolve(String(args['project-path']));
}

function reportsDirFor(projectPath) {
  const projectName = path.basename(projectPath);
  return path.join(__dirname, '..', 'reports', projectName);
}

function writePhaseJson(reportsDir, fileName, data) {
  fs.mkdirSync(reportsDir, { recursive: true });
  const outPath = path.join(reportsDir, fileName);
  fs.writeFileSync(outPath, JSON.stringify(data, null, 2));
  return outPath;
}

function writeAiHintsPrompt(reportsDir, phase2) {
  const flagged = phase2.apiMatches.filter((m) => m.severity !== 'info');
  const lines = [];
  lines.push('# WebGL Compatibility Review Prompt (for a Claude Code agent)');
  lines.push('');
  lines.push('A static scan (deterministic regex matching, not an LLM) flagged the ');
  lines.push('following locations as possibly WebGL-incompatible. For each one: open ');
  lines.push('the file, read enough surrounding context to judge whether this code ');
  lines.push('path actually runs on WebGL builds, and if so suggest a concrete, ');
  lines.push('minimal fix. Do not assume the static match is correct without reading ');
  lines.push('the code - regex matches do not understand build-target compilation ');
  lines.push('guards (e.g. `#if !UNITY_WEBGL`) and will false-positive on those.');
  lines.push('');
  for (const m of flagged) {
    lines.push(`- \`${m.file}:${m.line}\` (${m.severity}, ${m.category}): ${m.reason}`);
    lines.push(`  \`\`\`csharp\n  ${m.snippet}\n  \`\`\``);
  }
  if (flagged.length === 0) {
    lines.push('_No error/warning-level matches - nothing to review._');
  }
  const outPath = path.join(reportsDir, 'ai-hints-prompt.md');
  fs.mkdirSync(reportsDir, { recursive: true });
  fs.writeFileSync(outPath, lines.join('\n'));
  return outPath;
}

function cmdAnalyze(args) {
  const projectPath = resolveProjectPath(args);
  const { runProjectAnalysis } = require('./phases/01-project-analysis');
  const { runApiPluginScan } = require('./phases/02-api-plugin-scan');

  const reportsDir = reportsDirFor(projectPath);

  console.log(`Analyzing: ${projectPath}`);
  console.log(`Reports will be written to: ${reportsDir}\n`);

  let phase1, phase2;
  try {
    phase1 = runProjectAnalysis(projectPath);
    writePhaseJson(reportsDir, '01-project-analysis.json', phase1);
    console.log(`[Phase 1] Project Analysis - ${phase1.findings.length} findings`);
  } catch (err) {
    console.error(`[Phase 1] FAILED: ${err.message}`);
    process.exit(2);
  }

  try {
    phase2 = runApiPluginScan(projectPath);
    writePhaseJson(reportsDir, '02-api-plugin-scan.json', phase2);
    console.log(
      `[Phase 2] API/Plugin Scan - scanned ${phase2.summary.csFilesScanned} .cs files, ` +
      `${phase2.summary.apiMatches} API matches, ${phase2.summary.pluginFindings} plugin findings, ` +
      `${phase2.summary.packageFindings} package findings`
    );
  } catch (err) {
    console.error(`[Phase 2] FAILED: ${err.message}`);
    process.exit(2);
  }

  // Merge with whatever Phase 3-7 results already exist on disk (from a
  // prior build/serve/smoke-test run) rather than overwriting the combined
  // report with only Phase 1-2 - otherwise re-running `analyze` after a
  // successful build would silently erase those results from the report.
  updateCombinedReport(projectPath, reportsDir, {
    '01-project-analysis': phase1,
    '02-api-plugin-scan': phase2,
  });

  if (args['ai-hints']) {
    const hintsPath = writeAiHintsPrompt(reportsDir, phase2);
    console.log(`\n[ai-hints] Prompt written (not auto-invoked):\n  ${hintsPath}`);
  }

  const errorCount = phase2.summary.bySeverity.error || 0;
  const warningCount = phase2.summary.bySeverity.warning || 0;
  console.log(`\nSummary: ${errorCount} error(s), ${warningCount} warning(s).`);

  // CI-friendly exit codes: 0 = clean, 1 = warnings only, 2 = at least one
  // error-level finding. Phase 9 (GitHub Actions) can gate on this directly.
  if (errorCount > 0) process.exit(2);
  if (warningCount > 0) process.exit(1);
  process.exit(0);
}

function loadExistingPhaseJson(reportsDir, fileName) {
  const p = path.join(reportsDir, fileName);
  if (!fs.existsSync(p)) return null;
  try {
    return JSON.parse(fs.readFileSync(p, 'utf8'));
  } catch (err) {
    return null;
  }
}

function updateCombinedReport(projectPath, reportsDir, newPhases) {
  const { runReport } = require('./phases/08-report');
  const phaseResults = {
    '01-project-analysis': loadExistingPhaseJson(reportsDir, '01-project-analysis.json'),
    '02-api-plugin-scan': loadExistingPhaseJson(reportsDir, '02-api-plugin-scan.json'),
    '03-webgl-build': loadExistingPhaseJson(reportsDir, '03-webgl-build.json'),
    '04-build-log-analysis': loadExistingPhaseJson(reportsDir, '04-build-log-analysis.json'),
    '05-local-server': loadExistingPhaseJson(reportsDir, '05-local-server.json'),
    '06-browser-smoke-test': loadExistingPhaseJson(reportsDir, '06-browser-smoke-test.json'),
    '07-sdk-bridge-verification': loadExistingPhaseJson(reportsDir, '07-sdk-bridge-verification.json'),
    ...newPhases,
  };
  for (const key of Object.keys(phaseResults)) {
    if (phaseResults[key] === null || phaseResults[key] === undefined) delete phaseResults[key];
  }
  const { jsonPath, mdPath } = runReport(projectPath, reportsDir, phaseResults);
  console.log(`\n[Phase 8] Report updated:\n  ${mdPath}\n  ${jsonPath}`);
}

function requireConfirm(args, phaseLabel, exampleCommand) {
  if (args.confirm) return;
  console.log(
    `${phaseLabel} requires --confirm because it performs a real, ` +
    `side-effecting action (invoking Unity, starting a server, or driving ` +
    `a real browser) rather than pure static analysis.\n\n` +
    `Re-run with --confirm when ready:\n  ${exampleCommand}`
  );
  process.exit(0);
}

async function cmdBuild(args) {
  const projectPath = resolveProjectPath(args);
  const reportsDir = reportsDirFor(projectPath);
  requireConfirm(
    args,
    'Phase 3 (Unity Batchmode WebGL Build)',
    `node src/cli.js build --project-path "${projectPath}" --confirm`
  );

  const outputPath = args['output-path']
    ? path.resolve(String(args['output-path']))
    : path.join(reportsDir, 'build', 'WebGL');
  const timestamp = Date.now();
  const logFile = args['log-file']
    ? path.resolve(String(args['log-file']))
    : path.join(reportsDir, 'logs', `build-webgl-${timestamp}.log`);
  const provenancePath = path.join(reportsDir, 'logs', `build-webgl-${timestamp}.provenance.json`);

  console.log(`Building WebGL: ${projectPath}`);
  console.log(`Output: ${outputPath}`);
  console.log(`Log: ${logFile}\n`);

  const { runWebGLBuild } = require('./phases/03-webgl-build');
  const phase3 = await runWebGLBuild(projectPath, {
    outputPath,
    logFile,
    provenancePath,
    profile: args.profile || 'WebGL',
    createProfileIfMissing: args['create-profile-if-missing'] !== 'false',
    editorPath: args['editor-path'],
    editorVersion: args['editor-version'],
    timeoutSec: args.timeout ? Number(args.timeout) : undefined,
    allowDirtyBuild: args['allow-dirty-build'] !== 'false',
  });
  writePhaseJson(reportsDir, '03-webgl-build.json', phase3);
  console.log(`\n[Phase 3] status=${phase3.status}${phase3.reason ? ` - ${phase3.reason}` : ''}`);

  const { analyzeBuildLog } = require('./phases/04-build-log-analysis');
  const phase4 = analyzeBuildLog(projectPath, { logFile: phase3.logFile, provenancePath: phase3.provenancePath });
  writePhaseJson(reportsDir, '04-build-log-analysis.json', phase4);
  console.log(`[Phase 4] status=${phase4.status}, compile errors=${phase4.compileErrors.length}`);

  updateCombinedReport(projectPath, reportsDir, {
    '03-webgl-build': phase3,
    '04-build-log-analysis': phase4,
  });

  if (phase3.status !== 'SUCCESS' || phase4.status === 'FAILED') {
    console.error(
      '\nBuild did not succeed - `serve`/`smoke-test` will refuse to treat ' +
      'this output as a passing build until a successful build exists.'
    );
    process.exit(2);
  }
  process.exit(0);
}

async function cmdServe(args) {
  const projectPath = resolveProjectPath(args);
  const reportsDir = reportsDirFor(projectPath);
  requireConfirm(
    args,
    'Phase 5 (Local WebGL Server)',
    `node src/cli.js serve --project-path "${projectPath}" --confirm`
  );

  let buildDir = args['build-path'] ? path.resolve(String(args['build-path'])) : null;
  const phase3 = loadExistingPhaseJson(reportsDir, '03-webgl-build.json');

  if (!buildDir) {
    if (!phase3 || phase3.status !== 'SUCCESS') {
      console.error('No successful Phase 3 build found in reports, and no --build-path was given.');
      console.error(`Run: node src/cli.js build --project-path "${projectPath}" --confirm`);
      console.error('...or pass --build-path pointing at an existing WebGL build output directory.');
      process.exit(2);
    }
    buildDir = phase3.outputPath;
  } else if (phase3 && phase3.status === 'FAILED') {
    console.warn(
      'Warning: the last recorded Phase 3 build FAILED, but --build-path was ' +
      'given explicitly, so serving it anyway. This output may be stale or broken.'
    );
  }

  const phase1 = loadExistingPhaseJson(reportsDir, '01-project-analysis.json');
  const threadsSupport = !!(phase1 && phase1.playerSettings && phase1.playerSettings.webGLThreadsSupport === '1');

  const { startLocalServer } = require('./phases/05-local-server');
  let handle;
  try {
    handle = await startLocalServer({
      buildDir,
      port: args.port ? Number(args.port) : 0,
      threadsSupport,
      host: args.host ? String(args.host) : '127.0.0.1',
    });
  } catch (err) {
    console.error(`[Phase 5] FAILED: ${err.message}`);
    process.exit(2);
  }

  const phase5 = {
    phase: 5,
    name: 'local-server',
    projectPath,
    generatedAt: new Date().toISOString(),
    source: 'browser-test',
    status: 'RUNNING',
    buildDir,
    url: handle.url,
    threadsSupportHeaders: threadsSupport,
  };
  writePhaseJson(reportsDir, '05-local-server.json', phase5);
  updateCombinedReport(projectPath, reportsDir, { '05-local-server': phase5 });

  console.log(`\n[Phase 5] Serving ${buildDir}\n  ${handle.url}\nPress Ctrl+C to stop.`);

  process.on('SIGINT', async () => {
    await handle.stop();
    process.exit(0);
  });
}

async function cmdSmokeTest(args) {
  const projectPath = resolveProjectPath(args);
  const reportsDir = reportsDirFor(projectPath);
  requireConfirm(
    args,
    'Phase 6-7 (Playwright Smoke Test / SDK Bridge Verification)',
    `node src/cli.js smoke-test --project-path "${projectPath}" --confirm`
  );

  const { isPlaywrightAvailable, createPage, runBrowserSmokeTest } = require('./phases/06-browser-smoke-test');
  const { runSdkBridgeVerification } = require('./phases/07-sdk-bridge-verification');

  let url = args.url ? String(args.url) : null;
  let serverHandle = null;

  if (!isPlaywrightAvailable()) {
    console.log(
      '[Phase 6] playwright is not installed (optionalDependency). Per the ' +
      'approval requirement, this must be reported before installing: run ' +
      '`npm install playwright` inside Tools/webgl-porting-pipeline after ' +
      'approval, then re-run this command. Recording NOT_RUN (not a pass).'
    );
    const phase6 = {
      phase: 6,
      name: 'browser-smoke-test',
      generatedAt: new Date().toISOString(),
      source: 'browser-test',
      status: 'NOT_RUN',
      url,
      reason: 'The optional `playwright` package is not installed.',
    };
    writePhaseJson(reportsDir, '06-browser-smoke-test.json', phase6);
    const phase7 = await runSdkBridgeVerification(projectPath, { page: null });
    writePhaseJson(reportsDir, '07-sdk-bridge-verification.json', phase7);
    updateCombinedReport(projectPath, reportsDir, {
      '06-browser-smoke-test': phase6,
      '07-sdk-bridge-verification': phase7,
    });
    process.exit(1);
  }

  if (!url) {
    const phase3 = loadExistingPhaseJson(reportsDir, '03-webgl-build.json');
    if (!phase3 || phase3.status !== 'SUCCESS') {
      console.error(
        'No successful build found and no --url given. Run `build --confirm` ' +
        'first, or pass --url pointing at an already-running `serve` instance.'
      );
      process.exit(2);
    }
    const phase1 = loadExistingPhaseJson(reportsDir, '01-project-analysis.json');
    const threadsSupport = !!(phase1 && phase1.playerSettings && phase1.playerSettings.webGLThreadsSupport === '1');
    const { startLocalServer } = require('./phases/05-local-server');
    serverHandle = await startLocalServer({ buildDir: phase3.outputPath, port: 0, threadsSupport });
    url = serverHandle.url;
    console.log(`[Phase 5] Auto-started local server for smoke test: ${url}`);
  }

  const { browser, page } = await createPage();
  const phase6 = await runBrowserSmokeTest(page, {
    url,
    screenshotDir: path.join(reportsDir, 'screenshots'),
    timeoutMs: args.timeout ? Number(args.timeout) * 1000 : 60000,
  });
  writePhaseJson(reportsDir, '06-browser-smoke-test.json', phase6);
  console.log(`[Phase 6] status=${phase6.status}${phase6.reason ? ` - ${phase6.reason}` : ''}`);

  const phase6Ok = phase6.status === 'PASSED' || phase6.status === 'PASSED_WITH_WARNINGS';
  const phase7 = await runSdkBridgeVerification(projectPath, { page: phase6Ok ? page : null });
  writePhaseJson(reportsDir, '07-sdk-bridge-verification.json', phase7);
  console.log(`[Phase 7] status=${phase7.status}${phase7.reason ? ` - ${phase7.reason}` : ''}`);

  await browser.close();
  if (serverHandle) await serverHandle.stop();

  updateCombinedReport(projectPath, reportsDir, {
    '06-browser-smoke-test': phase6,
    '07-sdk-bridge-verification': phase7,
  });

  process.exit(phase6.status === 'PASSED' ? 0 : 1);
}

function main() {
  const [, , command, ...rest] = process.argv;
  const args = parseArgs(rest);

  switch (command) {
    case 'analyze':
      cmdAnalyze(args);
      break;
    case 'build':
      cmdBuild(args).catch((err) => {
        console.error(`[Phase 3] FAILED: ${err.message}`);
        process.exit(2);
      });
      break;
    case 'serve':
      cmdServe(args).catch((err) => {
        console.error(`[Phase 5] FAILED: ${err.message}`);
        process.exit(2);
      });
      break;
    case 'smoke-test':
      cmdSmokeTest(args).catch((err) => {
        console.error(`[Phase 6-7] FAILED: ${err.message}`);
        process.exit(2);
      });
      break;
    default:
      printUsage();
      process.exit(command ? 2 : 0);
  }
}

main();
