# CrazyGames SDK v3 Integration

## What this is

A real external platform SDK adapter alongside the existing Mock bridge
(`MockPlatformService` / `WebGLPlatformService` / `ProjectMiroBridge.jslib`,
unchanged, still the Editor path and the fallback). Nothing Mock was deleted
or replaced.

## Source / version

- SDK: **CrazyGames SDK v3**
- Loaded from: `https://sdk.crazygames.com/crazygames-sdk-v3.js`
- Official docs: https://docs.crazygames.com/sdk/intro/ , https://docs.crazygames.com/sdk/game/ , https://docs.crazygames.com/sdk/video-ads/
- No npm package, no Unity Package Manager package, no `.unitypackage` — this
  is the raw script-tag + JS-call SDK, wrapped by a hand-written jslib
  (`Assets/Plugins/WebGL/CrazyGamesBridge.jslib`) in exactly the same pattern
  as the existing Mock bridge's `ProjectMiroBridge.jslib`. CrazyGames also
  publishes an official Unity package, but it requires importing a
  third-party asset and (per their docs) adding a loading scene as the
  build's first scene — both would need separate approval and weren't used
  here, in favor of the zero-dependency approach below.

## How it loads (no project settings changed)

`CrazyGamesBridge_Initialize` (called once, from
`CrazyGamesPlatformService.Initialize`, which `PlatformServiceBootstrap`
triggers automatically at startup) injects the `<script src="...">` tag into
`document.head` itself at runtime. **No `index.html` / WebGL template edit,
no Build Settings change, no scene reordering.** Once the script loads,
it calls `await CrazyGames.SDK.init()`, reads `CrazyGames.SDK.environment`,
and reports back:

| environment | meaning | PlatformResult |
|---|---|---|
| `"local"` | localhost - CrazyGames shows demo/simulated ads | Success |
| `"crazygames"` | running on crazygames.com - full functionality | Success |
| `"disabled"` | any other domain - every SDK call would throw | Failed |
| (script failed to load / timed out after 8s) | ad-blocker, network, offline build, etc. | Failed |

A `Failed` result here never blocks the game - `CrazyGamesPlatformService`
just flips an internal `_available = false` flag and every other method
transparently delegates to the existing Mock `WebGLPlatformService` instead.
`IsInitialized` becomes `true` either way once this resolves.

## What's wired up

- **Initialize** - loads the SDK, detects environment, reports Success/Failed.
- **`NotifyGameplayStart()` / `NotifyGameplayStop()`** - new `IPlatformService`
  methods (fire-and-forget, no response) mapped to
  `CrazyGames.SDK.game.gameplayStart()` / `gameplayStop()`. Called from
  exactly two gameplay sites, one line each, no other gameplay logic
  touched:
  - `WaveManager.RunLoop()` - right before wave 1 begins (`NotifyGameplayStart`).
  - `GameFlowController.EndCommon()` - the single place both Clear and Game
    Over funnel through (`NotifyGameplayStop`).
  Gameplay code only ever calls `PlatformServiceProvider.Current` - it never
  references CrazyGames directly.
- **`ShowRewardedAd`** - routes to the real `CrazyGames.SDK.ad.requestAd("rewarded", ...)`
  when the real SDK is available, otherwise the Mock bridge's fabricated ad.
  Exposed in `PlatformSdkDebugOverlay` (WebGL Dev Build only) for manual
  testing without playing a full run.
- **`ShowMidgameAd`** - same pattern, `ad.requestAd("midgame", ...)`. Called
  once per run from `WaveManager.RunLoop()`, right after the middle wave
  clears (e.g. after wave 2 of 3) and only if it isn't the last wave - no
  enemies are alive at that point, so it never costs the player a free hit.
  The result is ignored either way (gameplay just continues once it
  resolves); this is a breather beat, not something that gates progress.
- **`Login`** - routes to the real `CrazyGames.SDK.user.showAuthPrompt()`
  when the real SDK is available (`isUserAccountAvailable` checked first),
  otherwise the Mock bridge. Resolves `LoginResultData{userId, displayName}`
  from the real user object (`__dangerousUserId`/`username`), or
  `PlatformResultStatus.Cancelled` if the player dismisses the prompt
  (`err.code === "userCancelled"`). No gameplay call site exists yet - still
  only reachable from `PlatformSdkDebugOverlay`/`PlatformBridgeDebugTester`.
  If/when a real login UI is wanted, it calls this the same way.
- **Audio mute during an ad** - `CrazyGamesBridge.jslib`'s `adStarted`
  callback (on both `ShowRewardedAd` and `ShowMidgameAd`) sends
  `'OnAdStarted'`, which `PlatformBridgeReceiver` turns into
  `AudioListener.pause = true`; the matching `false` happens in
  `PlatformBridgeReceiver.Complete()` the instant either ad's result
  resolves (success, error, or this layer's own timeout) - so it can't be
  left stuck muted. Deliberately `AudioListener.pause`, not `Time.timeScale`
  or `AudioManager` - a global engine mute that doesn't touch gameplay
  pausing (the two current call sites already own their own pause: ReviveUI
  sets `Time.timeScale = 0` itself; the midgame slot has no enemies alive to
  pause) or require AudioManager API changes.

## What's explicitly NOT implemented (out of scope for this pass)

- **`SubmitScore`** still always uses the Mock bridge, unconditionally.
  CrazyGames SDK v3's leaderboards (`CrazyGames.SDK.user.submitScore({encryptedScore, score})`,
  see https://docs.crazygames.com/sdk/leaderboards-client/) require a
  **per-game, per-leaderboard AES-GCM encryption key issued from the
  CrazyGames developer dashboard** - something this adapter has no way to
  obtain on its own, not just an API call to wire up. The dashboard also
  requires picking a metric type (XP/KDA/POINTS/MINUTES) and sort order
  before any key exists. Separately, the game itself has no "score" concept
  yet (it's a wave-survival run, not a scored one) - `WaveManager.TotalDied`/
  `TotalSpawned`/the current wave index would be the natural candidates if
  one is added. Once a leaderboard + key exist, wiring the real call is the
  same shape as `Login`/`ShowRewardedAd` above: a `CrazyGamesBridge_SubmitScore`
  export that calls `submitScore()` (AES-GCM encryption done in JS via
  `window.crypto.subtle`, keyed by the dashboard-issued secret), reported
  back through `OnSubmitScoreResult` (already exists, reused from the Mock
  bridge's shape).
- **No actual extra reward is granted** when a rewarded ad finishes beyond
  what already consumes it - `ReviveUI` is still the only caller, and it
  already treats `Status == Success` as "revived"; nothing else currently
  reads `RewardedAdResultData.rewardGranted`. Adding a second ad-gated reward
  (bonus currency, an extra life bank, etc.) would need that system to exist
  first - no action taken here since none does yet.
- **Loading start/stop and happytime** - not implemented (loadingStart/Stop
  is explicitly "not supported for Unity" per CrazyGames' own docs anyway;
  `happytime()` wasn't asked for).

## Local dev vs. real platform

- `http://localhost:8923` (or any literal `localhost`/`127.0.0.1` URL) →
  CrazyGames treats this as `"local"` → real SDK loads, demo-mode ads.
- Any other address that isn't `crazygames.com` - including a LAN IP like
  `http://192.168.x.x:8923` used for phone testing - is **not** "local" per
  CrazyGames' own rules, so `environment` comes back `"disabled"` and the
  adapter falls back to the Mock bridge automatically. This is a convenient
  built-in test case for the "SDK unavailable" fallback path.
- A `?useLocalSdk=true` query parameter can force local mode on a non-local
  domain if that's ever needed for testing - not wired into any UI here, just
  documented since CrazyGames' docs mention it.

## Compatibility

Unity 6000.6.3f1 / WebGL IL2CPP: no concern beyond the existing jslib
pattern - this is pure JS interop via `[DllImport("__Internal")]`, identical
mechanism to the existing Mock bridge, so there's no Unity-version-specific
compatibility surface (no official package with its own version matrix was
used).
