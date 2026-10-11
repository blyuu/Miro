'use strict';

// Phase 4 - Build Log / Compile Error Analysis
// Reads whatever Phase 3 (or a manually-run Unity build) produced - a build
// log file and, if present, the unity-cli provenance manifest - and derives
// a structured success/failure verdict plus individual compile diagnostics.
// Pure text analysis; does not invoke Unity. Findings here are tagged
// source: 'compiler' because they come from the Editor's own build output,
// not from this tool's regex heuristics (contrast with Phase 2's
// source: 'static-analysis'). Never defaults to SUCCESS - a log with no
// recognizable result marker is reported UNKNOWN, not treated as a pass.

const fs = require('fs');

const COMPILE_ERROR_RE = /^(.*\.cs)\((\d+),(\d+)\):\s*error\s+(CS\d+):\s*(.+)$/;
const BUILD_RESULT_RE = /Build completed with a result of '(\w+)'/i;
const EXCEPTION_RE = /^(UnityException|BuildFailedException|System\.\w*Exception):\s*(.+)$/m;

function readLogSafely(logFile) {
  try {
    return fs.readFileSync(logFile, 'utf8');
  } catch (err) {
    return null;
  }
}

function analyzeBuildLog(projectPath, { logFile, provenancePath } = {}) {
  const findings = [];
  const compileErrors = [];

  const result = {
    phase: 4,
    name: 'build-log-analysis',
    projectPath,
    generatedAt: new Date().toISOString(),
    source: 'compiler',
    status: 'NOT_RUN',
    logFile: logFile || null,
    provenancePath: provenancePath || null,
    buildResult: null,
    compileErrors,
    findings,
    reason: null,
  };

  if (!logFile) {
    result.reason = 'No build log file was provided - Phase 3 did not run, or was not pointed at a log.';
    return result;
  }

  const text = readLogSafely(logFile);
  if (text == null) {
    result.status = 'ERROR';
    result.reason = `Build log file not found or unreadable: ${logFile}`;
    return result;
  }

  for (const line of text.split('\n')) {
    const m = line.match(COMPILE_ERROR_RE);
    if (m) {
      compileErrors.push({
        file: m[1].trim(),
        line: Number(m[2]),
        column: Number(m[3]),
        code: m[4],
        message: m[5].trim(),
      });
    }
  }

  const resultMatch = text.match(BUILD_RESULT_RE);
  if (resultMatch) result.buildResult = resultMatch[1];
  const exceptionMatch = text.match(EXCEPTION_RE);

  let provenance = null;
  if (provenancePath) {
    try {
      provenance = JSON.parse(fs.readFileSync(provenancePath, 'utf8'));
    } catch (err) {
      findings.push({
        severity: 'warning',
        source: 'compiler',
        message: `Provenance manifest at ${provenancePath} could not be read/parsed: ${err.message}`,
      });
    }
  }

  if (compileErrors.length > 0) {
    result.status = 'FAILED';
    for (const ce of compileErrors) {
      findings.push({
        severity: 'error',
        source: 'compiler',
        message: `${ce.code}: ${ce.message}`,
        file: ce.file,
        line: ce.line,
      });
    }
  } else if (resultMatch && resultMatch[1].toLowerCase() !== 'succeeded') {
    result.status = 'FAILED';
    findings.push({
      severity: 'error',
      source: 'compiler',
      message: `Build result: '${resultMatch[1]}' (no individual compile-error lines matched - inspect the full log at ${logFile}).`,
    });
  } else if (exceptionMatch) {
    result.status = 'FAILED';
    findings.push({ severity: 'error', source: 'compiler', message: `${exceptionMatch[1]}: ${exceptionMatch[2]}` });
  } else if (resultMatch && resultMatch[1].toLowerCase() === 'succeeded') {
    result.status = 'SUCCESS';
  } else if (provenance && typeof provenance.success === 'boolean') {
    result.status = provenance.success ? 'SUCCESS' : 'FAILED';
    findings.push({
      severity: provenance.success ? 'info' : 'error',
      source: 'compiler',
      message: `No build-result marker line matched in the log text; falling back to the provenance manifest's success=${provenance.success}.`,
    });
  } else {
    result.status = 'UNKNOWN';
    findings.push({
      severity: 'warning',
      source: 'compiler',
      message: 'Could not find a definitive build-result marker in the log or a usable provenance manifest. Reporting UNKNOWN, not SUCCESS - inspect the log manually.',
    });
  }

  return result;
}

module.exports = { analyzeBuildLog };
