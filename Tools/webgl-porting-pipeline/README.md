# Unity → H5/WebGL Porting Verification Pipeline

A reusable, project-agnostic pipeline for verifying that a Unity project is
ready for WebGL/H5 deployment. Built for ProjectMiro but designed to be
copied into any other Unity project unchanged — every command takes
`--project-path` as an argument; nothing is hardcoded to ProjectMiro.

## Design principles

1. **Static analysis first, Unity Editor second.** Phases 1-2 (project
   analysis, incompatible API/plugin detection) read project files directly
   from disk (`ProjectSettings/`, `Packages/`, `Assets/**/*.cs`) — they do
   **not** require a running Unity Editor or Pipeline connection. This makes
   them safe to run at any time (including while someone else has the
   project open and is mid-edit on Scenes/Materials/Shaders/UI/ProjectSettings
   — which is exactly the situation this was built under) and makes them
   usable in a CI container that never launches a GUI Editor at all.
2. **Clear separation: static facts vs. build/runtime facts vs. AI opinion.**
   Every report distinguishes three kinds of finding:
   - `static-analysis` — deterministic, from parsing files (e.g. "found a
     `.dylib` plugin", "found `Process.Start(` in PlayerController.cs:42").
   - `compiler` / `browser-test` — actual Unity Batchmode build output, or
     actual Playwright browser assertions (Phases 3-7). These are **ground
     truth**: the project either compiled/ran or it didn't.
   - `ai-analysis` — an optional LLM-assisted pass (Claude Code, via Task/
     Agent) that reads flagged files for additional context and suggests
     remediation. Always labeled as a suggestion, never merged into the
     deterministic findings, never used to override a compiler result.
3. **Each phase is independently invokable** and produces its own JSON
   artifact under `reports/<project-name>/<phase>.json`, plus a final
   aggregated Markdown report (Phase 8). This is what makes Phase 9 (GitHub
   Actions) straightforward — each phase is just one CI step reading the
   previous phase's JSON.
4. **Destructive/slow phases require explicit opt-in.** Phases 3+ (actual
   Unity Batchmode build, serving the build, launching a browser) only run
   when invoked with `--confirm` (or, for CI, a non-interactive
   `--yes`-equivalent flag) — never as a side effect of running analysis.

## Phase plan

| Phase | Name | What it does | Needs live Unity Editor? | Modifies the project? | Status |
|---|---|---|---|---|---|
| 1 | Project Analysis | Unity version, render pipeline, scripting backend, installed packages, target platforms already in Build Settings, asset folder sizes | No (reads `ProjectSettings/`, `Packages/`) | No (read-only) | **Implemented** |
| 2 | API/Plugin Compatibility Scan | Regex/AST-light scan of all `.cs` under `Assets/` for known WebGL-incompatible API patterns (threading, sockets, `Process.Start`, reflection emit, `Task.Wait()`/`.Result` deadlock risk, etc.); scan for native plugin binaries (`.dll` with `isNativePlugin`, `.so`, `.dylib`, `.bundle`, `.a`) and for existing `.jslib` JS-bridge files | No | No (read-only) | **Implemented** |
| 3 | WebGL Build Automation | Shells out to the `unity` CLI (`unity build`) using a **Unity 6+ Build Profile** (`--profile`, auto-created with `--create-profile WebGL` if missing) — never `--execute-method`, so no custom `.cs` is added to the target project and no script recompile is forced | Yes (launches/uses the Editor in batch mode) | Adds a Build Profile **asset** only if none exists yet; **does produce build output on disk** | **Implemented — gated behind `--confirm`** |
| 4 | Build Log/Error Collection | Parses the Phase 3 build log for `error CS####:` lines and Unity's own `Build completed with a result of '...'` marker; cross-checks the unity-cli provenance manifest. Never reports SUCCESS without positive evidence (falls back to `UNKNOWN`, not a pass) | No (reads the log/manifest Phase 3 produced) | No | **Implemented** |
| 5 | Local WebGL Server | Zero-dependency Node `http` static server for the Phase 3 build output, with correct `Content-Type`/`Content-Encoding` for `.gz`/`.br` build files and COOP/COEP headers when `webGLThreadsSupport` is enabled | No | No | **Implemented — gated behind `--confirm`** |
| 6 | Playwright Smoke Test | Launches a real headless browser against the Phase 5 server, waits for a rendered `<canvas>` with non-zero size, checks console/page errors, takes a screenshot | No (needs Node+Playwright, not Unity) | No | **Implemented — gated behind `--confirm`**; requires the optional `playwright` package (not installed — reports `NOT_RUN` until installed with explicit approval) |
| 7 | SDK Mock / JS Bridge Test | Discovers `.jslib` files / SDK-Mock/Bridge-named scripts (reports `NOT_APPLICABLE` when none exist — true today for ProjectMiro); when Phase 6 produced a live page, additionally probes `window` for each `.jslib`'s exported function names | No | No | **Implemented — generic harness; honestly reports `NOT_APPLICABLE` for a project with no bridge yet** |
| 8 | Report Generation | Aggregates every phase's JSON into one Markdown + one combined JSON report; enforces that a browser-test phase is never shown as passing when Phase 3's build did not succeed (`BLOCKED_BY_FAILED_BUILD`) | No | No (writes only under `reports/`) | **Implemented** (consumes whatever phases have run) |
| 9 | CI Integration | A `.github/workflows/webgl-porting.yml` template wiring Phases 1-8 as sequential steps with artifact upload | N/A | Adds a workflow file (opt-in, not created automatically) | Documented, not yet created |

**Phases 1-8 are now implemented.** Phases 3, 5, and 6 are gated behind
`--confirm` and were **not executed** as part of building them — no real
Unity Batchmode build, local server, or Playwright run has happened yet.
That requires your separate, explicit approval (see "Running Phases 3-7"
below).

## Usage (current)

```bash
cd Tools/webgl-porting-pipeline
npm install          # no dependencies yet for Phases 1/2/8 (zero npm deps by design)
node src/cli.js analyze --project-path "C:/Users/junso/Documents/ProjectMiro"
```

This runs Phases 1 → 2 → 8 and writes:
- `reports/ProjectMiro/01-project-analysis.json`
- `reports/ProjectMiro/02-api-plugin-scan.json`
- `reports/ProjectMiro/webgl-porting-report.md`
- `reports/ProjectMiro/webgl-porting-report.json`

Nothing under the target project (`Assets/`, `ProjectSettings/`, `Packages/`)
is ever written to — this tool only reads from the target project and
writes under its own `reports/` folder.

## Running Phases 3-7 (requires your explicit approval)

Nothing below has been executed yet. Each command is a separate, explicit
`--confirm` gate — approving one does not approve the others.

```bash
cd Tools/webgl-porting-pipeline

# Phase 3+4: real Unity Batchmode WebGL build + log analysis
node src/cli.js build --project-path "C:/Users/junso/Documents/ProjectMiro" --confirm

# Phase 5: local static server for that build (foreground; Ctrl+C to stop)
node src/cli.js serve --project-path "C:/Users/junso/Documents/ProjectMiro" --confirm

# Phase 6+7: Playwright smoke test + SDK bridge probe (auto-starts/stops its own
# server unless --url is given; requires `npm install playwright` first - see below)
node src/cli.js smoke-test --project-path "C:/Users/junso/Documents/ProjectMiro" --confirm
```

**New npm package required for Phase 6-7**: `playwright` (listed today only
as an `optionalDependency`, not installed). `smoke-test --confirm` detects
this and reports `NOT_RUN` rather than installing it or silently skipping -
installing it (`npm install playwright` inside this folder) needs your
separate go-ahead, per your instruction to report before installing anything.

**Expected results if you approve all three now:**
- `build --confirm` will very likely need to create a WebGL Build Profile
  asset first (`Assets/Settings/Build Profiles/WebGL.asset` — the only file
  this pipeline adds to the target project, and only if one doesn't already
  exist), then run an actual IL2CPP WebGL build. This can take several
  minutes and will use `--allow-dirty-build` by default since other agents
  are likely mid-edit on the working tree.
- Phase 2 already found zero `.jslib` files anywhere in ProjectMiro, so
  **Phase 7 will report `NOT_APPLICABLE`**, not a pass or a failure — there
  is no existing Platform SDK Mock / JS Bridge for it to test yet. The
  harness is ready for when one is added.
- If the build fails for any reason, `serve`/`smoke-test` will refuse to
  treat that output as servable/passing — per your explicit rule, a failed
  build must never be followed by a browser test reported as a success.

## Rule sets (Phase 2)

Incompatible-API and incompatible-plugin rules live in
`src/rules/incompatible-apis.json` and `src/rules/incompatible-plugins.json`
as plain data, not hardcoded in the scanner logic — so adding a new rule for
a different project/engine version is a one-line JSON edit, not a code
change. Each rule has a `severity` (`error` | `warning` | `info`) and a
human-readable `reason` that gets surfaced verbatim in the report.

## Claude Code / Agent-assisted analysis (optional, not run automatically)

`node src/cli.js analyze --ai-hints` emits one extra file,
`reports/<project>/ai-hints-prompt.md`, containing the Phase 2 findings
formatted as a ready-to-paste prompt for a Claude Code agent to read the
flagged files and suggest concrete remediations. This is a **prompt
generator**, not an automatic agent invocation — keeping a hard boundary
between "a script found this deterministically" and "an LLM suggested this
fix," per design principle #2. Whether/how to actually invoke an agent on
that prompt is left to the calling context (e.g. a future Phase 9 CI step
could post it as a PR comment for a human or agent to act on).
