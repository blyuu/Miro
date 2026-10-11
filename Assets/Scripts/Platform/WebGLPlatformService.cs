using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace ProjectMiro.Platform
{
    /// Actual WebGL implementation of IPlatformService, backed by
    /// Assets/Plugins/WebGL/ProjectMiroBridge.jslib - which is itself a Mock
    /// SDK (see that file's header comment), not a real ad/login backend.
    /// Every call is a fire-and-forget request into JS tagged with a
    /// requestId; PlatformBridgeReceiver owns the pending-request table and
    /// the per-call timeout, and resolves the original callback once the
    /// jslib calls back via SendMessage (or the timeout fires first).
    ///
    /// The DllImport externs below only exist in an actual WebGL build
    /// (UNITY_WEBGL && !UNITY_EDITOR) - this class is still compiled in the
    /// Editor so other code can reference its type, but
    /// PlatformServiceProvider never constructs it there.
    public class WebGLPlatformService : IPlatformService
    {
        private const float DefaultTimeoutSeconds = 10f;

        public bool IsInitialized { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void ProjectMiroBridge_Initialize(int requestId, string receiverGoName);
        [DllImport("__Internal")] private static extern void ProjectMiroBridge_Login(int requestId, string receiverGoName);
        [DllImport("__Internal")] private static extern void ProjectMiroBridge_ShowRewardedAd(int requestId, string receiverGoName);
        [DllImport("__Internal")] private static extern void ProjectMiroBridge_ShowMidgameAd(int requestId, string receiverGoName);
        [DllImport("__Internal")] private static extern void ProjectMiroBridge_SubmitScore(int requestId, int score, string receiverGoName);
        [DllImport("__Internal")] private static extern void ProjectMiroBridge_ForceNextOutcome(string apiName, string outcome);
#endif

        public void Initialize(Action<PlatformResult> onComplete)
        {
            var receiver = PlatformBridgeReceiver.EnsureInstance();
            int requestId = receiver.RegisterPendingRequest("initialize", DefaultTimeoutSeconds, result =>
            {
                IsInitialized = result.Status == PlatformResultStatus.Success;
                onComplete?.Invoke(result);
            });
#if UNITY_WEBGL && !UNITY_EDITOR
            ProjectMiroBridge_Initialize(requestId, PlatformBridgeReceiver.ReceiverGameObjectName);
#endif
        }

        public void Login(Action<PlatformResult> onComplete)
        {
            var receiver = PlatformBridgeReceiver.EnsureInstance();
            int requestId = receiver.RegisterPendingRequest("login", DefaultTimeoutSeconds, onComplete);
#if UNITY_WEBGL && !UNITY_EDITOR
            ProjectMiroBridge_Login(requestId, PlatformBridgeReceiver.ReceiverGameObjectName);
#endif
        }

        public void ShowRewardedAd(Action<PlatformResult> onComplete)
        {
            var receiver = PlatformBridgeReceiver.EnsureInstance();
            int requestId = receiver.RegisterPendingRequest("showRewardedAd", DefaultTimeoutSeconds, onComplete);
#if UNITY_WEBGL && !UNITY_EDITOR
            ProjectMiroBridge_ShowRewardedAd(requestId, PlatformBridgeReceiver.ReceiverGameObjectName);
#endif
        }

        public void ShowMidgameAd(Action<PlatformResult> onComplete)
        {
            var receiver = PlatformBridgeReceiver.EnsureInstance();
            int requestId = receiver.RegisterPendingRequest("showMidgameAd", DefaultTimeoutSeconds, onComplete);
#if UNITY_WEBGL && !UNITY_EDITOR
            ProjectMiroBridge_ShowMidgameAd(requestId, PlatformBridgeReceiver.ReceiverGameObjectName);
#endif
        }

        public void SubmitScore(int score, Action<PlatformResult> onComplete)
        {
            var receiver = PlatformBridgeReceiver.EnsureInstance();
            int requestId = receiver.RegisterPendingRequest("submitScore", DefaultTimeoutSeconds, onComplete);
#if UNITY_WEBGL && !UNITY_EDITOR
            ProjectMiroBridge_SubmitScore(requestId, score, PlatformBridgeReceiver.ReceiverGameObjectName);
#endif
        }

        public void NotifyGameplayStart() => Debug.Log("[WebGLPlatformService] NotifyGameplayStart (Mock bridge has no gameplay-lifecycle concept - no-op).");
        public void NotifyGameplayStop() => Debug.Log("[WebGLPlatformService] NotifyGameplayStop (Mock bridge has no gameplay-lifecycle concept - no-op).");

        /// Test hook mirroring MockPlatformService.ForceNextOutcome: forces
        /// the next call to apiName ("initialize"|"login"|"showRewardedAd"|
        /// "showMidgameAd"|"submitScore") to resolve as
        /// Failed/Cancelled/TimedOut instead of Success. No-op outside an
        /// actual WebGL build.
        public void ForceNextOutcome(string apiName, PlatformResultStatus outcome)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            ProjectMiroBridge_ForceNextOutcome(apiName, outcome.ToString());
#endif
        }
    }
}
