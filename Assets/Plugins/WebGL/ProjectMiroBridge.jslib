// ProjectMiroBridge.jslib
//
// Mock Platform SDK bridge for ProjectMiro (WebGL). This is NOT a real
// ad/login/leaderboard SDK - every response below is fabricated locally in
// the browser after a short delay, purely to exercise a real JS <-> Unity
// round trip (async success/failure/cancel/timeout) without any backend.
// See Assets/Scripts/Platform/WebGLPlatformService.cs for the C# side and
// Assets/Scripts/Platform/PlatformBridgeReceiver.cs for how responses are
// routed back into a pending C# callback via SendMessage.
//
// Debug/verification surface: once any bridge call has run, this installs
// `window.__projectMiroBridgeDebug` with:
//   - mock: true
//   - exports: the list of bridge function names below
//   - lastResponses[apiName]: the last response object this JS side sent
//   - confirmedCompletions[apiName]: the status the C# side reported back
//     via ProjectMiroBridge_ReportCompleted once it actually processed that
//     response - this is what proves a full JS -> Unity -> JS round trip,
//     not just that this file loaded. Read by the WebGL porting pipeline's
//     Phase 7 (Tools/webgl-porting-pipeline/src/phases/07-sdk-bridge-
//     verification.js).
//   - forceNextOutcome(apiName, outcome): same effect as the C#-side
//     ForceNextOutcome, callable from the browser console for manual
//     testing. outcome is one of 'Success' | 'Failed' | 'Cancelled' | 'TimedOut'.

mergeInto(LibraryManager.library, {

  $projectMiroBridgeState: {
    forcedOutcomes: {},
  },

  $projectMiroBridgeInstallDebugManifest: function () {
    if (typeof window === 'undefined') return;
    if (window.__projectMiroBridgeDebug) return;
    window.__projectMiroBridgeDebug = {
      mock: true,
      exports: [
        'ProjectMiroBridge_Initialize',
        'ProjectMiroBridge_Login',
        'ProjectMiroBridge_ShowRewardedAd',
        'ProjectMiroBridge_ShowMidgameAd',
        'ProjectMiroBridge_SubmitScore',
        'ProjectMiroBridge_ForceNextOutcome',
      ],
      lastResponses: {},
      confirmedCompletions: {},
      forceNextOutcome: function (apiName, outcome) {
        projectMiroBridgeState.forcedOutcomes[apiName] = outcome;
      },
    };
  },
  $projectMiroBridgeInstallDebugManifest__deps: ['$projectMiroBridgeState'],

  $projectMiroBridgeSchedule: function (apiName, requestId, receiverGoName, callbackMethod, buildSuccessData) {
    projectMiroBridgeInstallDebugManifest();

    var outcome = projectMiroBridgeState.forcedOutcomes[apiName] || 'Success';
    delete projectMiroBridgeState.forcedOutcomes[apiName];

    // A forced TimedOut outcome deliberately never responds at all within a
    // realistic window - the C# side's own timeout (see
    // PlatformBridgeReceiver.RegisterPendingRequest) is what actually fires
    // first and resolves the call as TimedOut; this long delay just means
    // this (very late) response gets ignored as "already resolved".
    var delayMs = outcome === 'TimedOut' ? 20000 : 350 + Math.floor(Math.random() * 250);

    setTimeout(function () {
      var payload = {
        requestId: requestId,
        status: outcome,
        message: outcome === 'Failed' ? ('Mock ' + apiName + ' failed (simulated failure).') : null,
        data: outcome === 'Success' && buildSuccessData ? JSON.stringify(buildSuccessData()) : null,
      };
      if (window.__projectMiroBridgeDebug) {
        window.__projectMiroBridgeDebug.lastResponses[apiName] = payload;
      }
      SendMessage(receiverGoName, callbackMethod, JSON.stringify(payload));
    }, delayMs);
  },
  $projectMiroBridgeSchedule__deps: ['$projectMiroBridgeState', '$projectMiroBridgeInstallDebugManifest'],

  ProjectMiroBridge_Initialize: function (requestId, receiverGoNamePtr) {
    var receiverGoName = UTF8ToString(receiverGoNamePtr);
    projectMiroBridgeSchedule('initialize', requestId, receiverGoName, 'OnInitializeResult', function () {
      return { sdkVersion: 'mock-1.0.0' };
    });
  },
  ProjectMiroBridge_Initialize__deps: ['$projectMiroBridgeSchedule'],

  ProjectMiroBridge_Login: function (requestId, receiverGoNamePtr) {
    var receiverGoName = UTF8ToString(receiverGoNamePtr);
    projectMiroBridgeSchedule('login', requestId, receiverGoName, 'OnLoginResult', function () {
      return { userId: 'mock-user-0001', displayName: 'Mock Player' };
    });
  },
  ProjectMiroBridge_Login__deps: ['$projectMiroBridgeSchedule'],

  ProjectMiroBridge_ShowRewardedAd: function (requestId, receiverGoNamePtr) {
    var receiverGoName = UTF8ToString(receiverGoNamePtr);
    projectMiroBridgeSchedule('showRewardedAd', requestId, receiverGoName, 'OnRewardedAdResult', function () {
      return { rewardGranted: true };
    });
  },
  ProjectMiroBridge_ShowRewardedAd__deps: ['$projectMiroBridgeSchedule'],

  ProjectMiroBridge_ShowMidgameAd: function (requestId, receiverGoNamePtr) {
    var receiverGoName = UTF8ToString(receiverGoNamePtr);
    projectMiroBridgeSchedule('showMidgameAd', requestId, receiverGoName, 'OnMidgameAdResult', function () {
      return { completed: true };
    });
  },
  ProjectMiroBridge_ShowMidgameAd__deps: ['$projectMiroBridgeSchedule'],

  ProjectMiroBridge_SubmitScore: function (requestId, score, receiverGoNamePtr) {
    var receiverGoName = UTF8ToString(receiverGoNamePtr);
    projectMiroBridgeSchedule('submitScore', requestId, receiverGoName, 'OnSubmitScoreResult', function () {
      return { accepted: true, score: score };
    });
  },
  ProjectMiroBridge_SubmitScore__deps: ['$projectMiroBridgeSchedule'],

  ProjectMiroBridge_ForceNextOutcome: function (apiNamePtr, outcomePtr) {
    var apiName = UTF8ToString(apiNamePtr);
    var outcome = UTF8ToString(outcomePtr);
    projectMiroBridgeState.forcedOutcomes[apiName] = outcome;
  },
  ProjectMiroBridge_ForceNextOutcome__deps: ['$projectMiroBridgeState'],

  ProjectMiroBridge_ReportCompleted: function (apiNamePtr, statusPtr) {
    var apiName = UTF8ToString(apiNamePtr);
    var status = UTF8ToString(statusPtr);
    projectMiroBridgeInstallDebugManifest();
    window.__projectMiroBridgeDebug.confirmedCompletions[apiName] = status;
  },
  ProjectMiroBridge_ReportCompleted__deps: ['$projectMiroBridgeInstallDebugManifest'],

});
