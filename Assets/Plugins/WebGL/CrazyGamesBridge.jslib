// CrazyGamesBridge.jslib
//
// Real external SDK adapter: CrazyGames SDK v3
// (https://docs.crazygames.com/sdk/intro/, loaded from
// https://sdk.crazygames.com/crazygames-sdk-v3.js). Unlike
// ProjectMiroBridge.jslib (a Mock SDK that fabricates every response
// locally), this file talks to a real third-party script.
//
// No index.html / WebGL template change was needed to add the SDK's
// required <script> tag - CrazyGamesBridge_Initialize injects it into
// document.head itself at runtime, the first (and only expected) time it's
// called. If the script fails to load, times out, or
// CrazyGames.SDK.environment comes back "disabled" (any domain that isn't
// crazygames.com or localhost), this reports a Failed result back to C# -
// see CrazyGamesPlatformService.cs, which then transparently falls back to
// the existing Mock bridge (ProjectMiroBridge.jslib) for everything.
//
// SendMessage targets: this file reuses PlatformBridgeReceiver's generic
// {requestId, status, message, data} JSON parser via 'OnInitializeResult' /
// 'OnLoginResult' / 'OnRewardedAdResult' / 'OnMidgameAdResult' (see
// PlatformBridgeReceiver.cs) - no per-API parsing needed on the C# side as
// long as the payload shape matches. 'OnAdStarted' is the one exception: a
// fire-and-forget mute signal with no requestId to resolve, sent from
// adStarted on both ad calls below (see PlatformBridgeReceiver.OnAdStarted
// and the matching unmute in its Complete()).
//
// Debug/verification surface: window.__crazyGamesBridgeDebug, mirroring
// window.__projectMiroBridgeDebug's shape (see ProjectMiroBridge.jslib) so
// the WebGL porting pipeline's Phase 7 (directory/name scan, not a hardcoded
// file reference) picks this bridge up automatically too.

mergeInto(LibraryManager.library, {

  $crazyGamesBridgeState: {
    scriptRequested: false,
  },

  $crazyGamesBridgeInstallDebugManifest: function () {
    if (typeof window === 'undefined') return;
    if (window.__crazyGamesBridgeDebug) return;
    window.__crazyGamesBridgeDebug = {
      mock: false,
      exports: [
        'CrazyGamesBridge_Initialize',
        'CrazyGamesBridge_Login',
        'CrazyGamesBridge_RequestRewardedAd',
        'CrazyGamesBridge_RequestMidgameAd',
        'CrazyGamesBridge_GameplayStart',
        'CrazyGamesBridge_GameplayStop',
      ],
      environment: null, // set once Initialize resolves: "local" | "crazygames" | "disabled" | "load-failed" | "timeout"
      available: false,
      lastResponses: {},
      confirmedCompletions: {},
    };
  },
  $crazyGamesBridgeInstallDebugManifest__deps: ['$crazyGamesBridgeState'],

  // Loads https://sdk.crazygames.com/crazygames-sdk-v3.js exactly once (a
  // second Initialize call, if one ever happened, would just re-run this
  // against the already-loaded window.CrazyGames - not expected in practice,
  // PlatformServiceBootstrap only calls Initialize once per page load).
  $crazyGamesBridgeLoadScript: function (onLoaded, onFailed) {
    if (typeof window === 'undefined') { onFailed('no window object'); return; }
    if (window.CrazyGames && window.CrazyGames.SDK) { onLoaded(); return; }
    if (crazyGamesBridgeState.scriptRequested) {
      // Already injected by an earlier call - poll briefly for it to finish.
      var waited = 0;
      var poll = setInterval(function () {
        waited += 100;
        if (window.CrazyGames && window.CrazyGames.SDK) { clearInterval(poll); onLoaded(); }
        else if (waited >= 8000) { clearInterval(poll); onFailed('timed out waiting for an in-flight script load'); }
      }, 100);
      return;
    }
    crazyGamesBridgeState.scriptRequested = true;

    var script = document.createElement('script');
    script.src = 'https://sdk.crazygames.com/crazygames-sdk-v3.js';
    script.async = true;
    script.onload = function () { onLoaded(); };
    script.onerror = function () { onFailed('script failed to load'); };
    document.head.appendChild(script);
  },
  $crazyGamesBridgeLoadScript__deps: ['$crazyGamesBridgeState'],

  CrazyGamesBridge_Initialize: function (requestId, receiverGoNamePtr) {
    var receiverGoName = UTF8ToString(receiverGoNamePtr);
    crazyGamesBridgeInstallDebugManifest();

    var settled = false;
    var respond = function (status, message, data) {
      if (settled) return;
      settled = true;
      var payload = { requestId: requestId, status: status, message: message || null, data: data ? JSON.stringify(data) : null };
      window.__crazyGamesBridgeDebug.lastResponses.initialize = payload;
      SendMessage(receiverGoName, 'OnInitializeResult', JSON.stringify(payload));
    };

    var timeoutId = setTimeout(function () {
      window.__crazyGamesBridgeDebug.environment = 'timeout';
      respond('Failed', 'CrazyGames SDK did not finish initializing within 8s.');
    }, 8000);

    crazyGamesBridgeLoadScript(
      function onLoaded() {
        window.CrazyGames.SDK.init().then(function () {
          clearTimeout(timeoutId);
          var env = window.CrazyGames.SDK.environment;
          window.__crazyGamesBridgeDebug.environment = env;
          if (env === 'disabled') {
            window.__crazyGamesBridgeDebug.available = false;
            respond('Failed', 'CrazyGames SDK environment is "disabled" (not crazygames.com or localhost).');
          } else {
            window.__crazyGamesBridgeDebug.available = true;
            respond('Success', null, { environment: env });
          }
        }).catch(function (err) {
          clearTimeout(timeoutId);
          window.__crazyGamesBridgeDebug.environment = 'init-error';
          respond('Failed', 'CrazyGames.SDK.init() rejected: ' + err);
        });
      },
      function onFailed(reason) {
        clearTimeout(timeoutId);
        window.__crazyGamesBridgeDebug.environment = 'load-failed';
        respond('Failed', 'Could not load the CrazyGames SDK script: ' + reason);
      }
    );
  },
  CrazyGamesBridge_Initialize__deps: ['$crazyGamesBridgeInstallDebugManifest', '$crazyGamesBridgeLoadScript'],

  // Only called once CrazyGamesPlatformService already knows init succeeded
  // (environment "local"/"crazygames"), so window.CrazyGames.SDK.user is
  // assumed present here. showAuthPrompt() is a Promise - see
  // https://docs.crazygames.com/sdk/user/ - resolving with a user object
  // ({__dangerousUserId, username, profilePictureUrl}) or rejecting with
  // {code: "userCancelled"|"userAlreadySignedIn"|"showAuthPromptInProgress"}.
  // Maps straight onto PlatformResult/LoginResultData's existing
  // userId/displayName shape - no new C# data model needed.
  CrazyGamesBridge_Login: function (requestId, receiverGoNamePtr) {
    var receiverGoName = UTF8ToString(receiverGoNamePtr);
    crazyGamesBridgeInstallDebugManifest();

    var settled = false;
    var respond = function (status, message, data) {
      if (settled) return;
      settled = true;
      var payload = { requestId: requestId, status: status, message: message || null, data: data ? JSON.stringify(data) : null };
      window.__crazyGamesBridgeDebug.lastResponses.login = payload;
      SendMessage(receiverGoName, 'OnLoginResult', JSON.stringify(payload));
    };

    try {
      if (!window.CrazyGames.SDK.user.isUserAccountAvailable) {
        respond('Failed', 'CrazyGames user accounts are not available on this domain.');
        return;
      }
      window.CrazyGames.SDK.user.showAuthPrompt().then(function (user) {
        respond('Success', null, {
          userId: (user && user.__dangerousUserId) || '',
          displayName: (user && user.username) || '',
        });
      }).catch(function (err) {
        var code = (err && err.code) || String(err);
        if (code === 'userCancelled') respond('Cancelled');
        else respond('Failed', 'CrazyGames showAuthPrompt failed: ' + code);
      });
    } catch (e) {
      respond('Failed', 'CrazyGames.SDK.user.showAuthPrompt threw: ' + e);
    }
  },
  CrazyGamesBridge_Login__deps: ['$crazyGamesBridgeInstallDebugManifest'],

  // Only called once CrazyGamesPlatformService already knows init succeeded
  // (environment "local"/"crazygames"), so window.CrazyGames.SDK.ad is
  // assumed present here. adStarted fires on BOTH this and
  // CrazyGamesBridge_RequestMidgameAd below - each sends 'OnAdStarted' so
  // PlatformBridgeReceiver can mute game audio (AudioListener.pause) for
  // whichever ad is actually showing; the matching unmute happens the
  // instant either one's result (success/error/timeout) resolves, in
  // PlatformBridgeReceiver.Complete().
  CrazyGamesBridge_RequestRewardedAd: function (requestId, receiverGoNamePtr) {
    var receiverGoName = UTF8ToString(receiverGoNamePtr);
    crazyGamesBridgeInstallDebugManifest();

    var settled = false;
    var respond = function (status, message, data) {
      if (settled) return;
      settled = true;
      var payload = { requestId: requestId, status: status, message: message || null, data: data ? JSON.stringify(data) : null };
      window.__crazyGamesBridgeDebug.lastResponses.showRewardedAd = payload;
      SendMessage(receiverGoName, 'OnRewardedAdResult', JSON.stringify(payload));
    };

    try {
      window.CrazyGames.SDK.ad.requestAd('rewarded', {
        adStarted: function () {
          console.log('[CrazyGamesBridge] rewarded ad started - muting audio via AudioListener.pause.');
          SendMessage(receiverGoName, 'OnAdStarted', '');
        },
        adFinished: function () {
          respond('Success', null, { rewardGranted: true });
        },
        adError: function (error) {
          respond('Failed', 'CrazyGames rewarded ad error: ' + error);
        },
      });
    } catch (e) {
      respond('Failed', 'CrazyGames.SDK.ad.requestAd threw: ' + e);
    }
  },
  CrazyGamesBridge_RequestRewardedAd__deps: ['$crazyGamesBridgeInstallDebugManifest'],

  CrazyGamesBridge_RequestMidgameAd: function (requestId, receiverGoNamePtr) {
    var receiverGoName = UTF8ToString(receiverGoNamePtr);
    crazyGamesBridgeInstallDebugManifest();

    var settled = false;
    var respond = function (status, message, data) {
      if (settled) return;
      settled = true;
      var payload = { requestId: requestId, status: status, message: message || null, data: data ? JSON.stringify(data) : null };
      window.__crazyGamesBridgeDebug.lastResponses.showMidgameAd = payload;
      SendMessage(receiverGoName, 'OnMidgameAdResult', JSON.stringify(payload));
    };

    try {
      window.CrazyGames.SDK.ad.requestAd('midgame', {
        adStarted: function () {
          console.log('[CrazyGamesBridge] midgame ad started - muting audio via AudioListener.pause.');
          SendMessage(receiverGoName, 'OnAdStarted', '');
        },
        adFinished: function () {
          respond('Success', null, { completed: true });
        },
        adError: function (error) {
          respond('Failed', 'CrazyGames midgame ad error: ' + error);
        },
      });
    } catch (e) {
      respond('Failed', 'CrazyGames.SDK.ad.requestAd threw: ' + e);
    }
  },
  CrazyGamesBridge_RequestMidgameAd__deps: ['$crazyGamesBridgeInstallDebugManifest'],

  CrazyGamesBridge_GameplayStart: function () {
    try {
      if (window.CrazyGames && window.CrazyGames.SDK && window.CrazyGames.SDK.game) {
        window.CrazyGames.SDK.game.gameplayStart();
        console.log('[CrazyGamesBridge] gameplayStart()');
      }
    } catch (e) {
      console.warn('[CrazyGamesBridge] gameplayStart() threw', e);
    }
  },

  CrazyGamesBridge_GameplayStop: function () {
    try {
      if (window.CrazyGames && window.CrazyGames.SDK && window.CrazyGames.SDK.game) {
        window.CrazyGames.SDK.game.gameplayStop();
        console.log('[CrazyGamesBridge] gameplayStop()');
      }
    } catch (e) {
      console.warn('[CrazyGamesBridge] gameplayStop() threw', e);
    }
  },

});
