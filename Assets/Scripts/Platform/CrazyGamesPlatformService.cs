using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace ProjectMiro.Platform
{
    /// Real external-SDK adapter for CrazyGames SDK v3
    /// (https://sdk.crazygames.com/crazygames-sdk-v3.js), backed by
    /// Assets/Plugins/WebGL/CrazyGamesBridge.jslib. Unlike WebGLPlatformService
    /// (itself a Mock SDK over a Mock jslib), this one talks to a real
    /// third-party script loaded dynamically at runtime - see
    /// CrazyGamesBridge.jslib's header for exactly how and why no
    /// index.html/WebGL-template change was needed for that.
    ///
    /// Decorator, not a replacement for the Mock bridge: wraps a
    /// WebGLPlatformService instance as `_fallback`. This is what
    /// PlatformServiceProvider now constructs for an actual WebGL build.
    ///
    ///  - Initialize(): loads + inits the real CrazyGames SDK. Resolves
    ///    Success (Data = CrazyGamesInitResultData{environment}) once the SDK
    ///    confirms it's usable ("local" or "crazygames"), or Failed if the
    ///    script failed to load, timed out, or environment came back
    ///    "disabled" (any domain that isn't CrazyGames.com or localhost).
    ///    Either way `IsInitialized` becomes true and onComplete always
    ///    fires - a Failed real-SDK init never blocks startup, it just means
    ///    every other call below silently uses the Mock fallback instead.
    ///  - ShowRewardedAd()/ShowMidgameAd(): real CrazyGames ad flow once
    ///    available (requestAd("rewarded", ...) / requestAd("midgame", ...));
    ///    otherwise `_fallback`'s Mock equivalent. Both mute game audio for
    ///    the ad's duration via PlatformBridgeReceiver.OnAdStarted
    ///    (AudioListener.pause) - see CrazyGamesBridge.jslib's adStarted
    ///    callback.
    ///  - Login(): real CrazyGames.SDK.user.showAuthPrompt() once available;
    ///    otherwise `_fallback.Login(...)`.
    ///  - NotifyGameplayStart()/NotifyGameplayStop(): real
    ///    CrazyGames.SDK.game.gameplayStart()/gameplayStop() once available
    ///    (fire-and-forget, no response expected); otherwise
    ///    `_fallback`'s no-op/log.
    ///  - SubmitScore(): still out of scope - CrazyGames SDK v3's
    ///    leaderboards need a per-game encryption key from the CrazyGames
    ///    developer dashboard that this adapter has no way to obtain on its
    ///    own, so this always delegates straight to `_fallback`,
    ///    unconditionally, regardless of whether the real SDK is available.
    ///
    /// See Assets/Scripts/Platform/CRAZYGAMES_INTEGRATION.md for the full
    /// writeup (SDK version, source, limitations, what was NOT implemented).
    public class CrazyGamesPlatformService : IPlatformService
    {
        private const float DefaultTimeoutSeconds = 10f;

        private readonly WebGLPlatformService _fallback = new WebGLPlatformService();
        private bool _available;

        public bool IsInitialized { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void CrazyGamesBridge_Initialize(int requestId, string receiverGoName);
        [DllImport("__Internal")] private static extern void CrazyGamesBridge_Login(int requestId, string receiverGoName);
        [DllImport("__Internal")] private static extern void CrazyGamesBridge_RequestRewardedAd(int requestId, string receiverGoName);
        [DllImport("__Internal")] private static extern void CrazyGamesBridge_RequestMidgameAd(int requestId, string receiverGoName);
        [DllImport("__Internal")] private static extern void CrazyGamesBridge_GameplayStart();
        [DllImport("__Internal")] private static extern void CrazyGamesBridge_GameplayStop();
#endif

        public void Initialize(Action<PlatformResult> onComplete)
        {
            var receiver = PlatformBridgeReceiver.EnsureInstance();
            int requestId = receiver.RegisterPendingRequest("crazyGamesInitialize", DefaultTimeoutSeconds, result =>
            {
                _available = result.Status == PlatformResultStatus.Success;
                IsInitialized = true; // the platform layer as a whole is ready either way - real SDK or fallback
                if (!_available)
                {
                    string reason = result.Message != null ? $"{result.Status}: {result.Message}" : result.Status.ToString();
                    Debug.Log($"[CrazyGamesPlatformService] Real CrazyGames SDK unavailable ({reason}) - all subsequent calls will use the Mock bridge instead.");
                }
                onComplete?.Invoke(result);
            });
#if UNITY_WEBGL && !UNITY_EDITOR
            CrazyGamesBridge_Initialize(requestId, PlatformBridgeReceiver.ReceiverGameObjectName);
#endif
        }

        public void Login(Action<PlatformResult> onComplete)
        {
            if (!_available)
            {
                _fallback.Login(onComplete);
                return;
            }

            var receiver = PlatformBridgeReceiver.EnsureInstance();
            int requestId = receiver.RegisterPendingRequest("crazyGamesLogin", DefaultTimeoutSeconds, onComplete);
#if UNITY_WEBGL && !UNITY_EDITOR
            CrazyGamesBridge_Login(requestId, PlatformBridgeReceiver.ReceiverGameObjectName);
#endif
        }

        // Still always Mock - CrazyGames SDK v3 leaderboards require a
        // per-game encryption key issued from the CrazyGames developer
        // dashboard (not something this adapter can self-configure), and the
        // game has no score concept yet. See CRAZYGAMES_INTEGRATION.md.
        public void SubmitScore(int score, Action<PlatformResult> onComplete) => _fallback.SubmitScore(score, onComplete);

        public void ShowRewardedAd(Action<PlatformResult> onComplete)
        {
            if (!_available)
            {
                _fallback.ShowRewardedAd(onComplete);
                return;
            }

            var receiver = PlatformBridgeReceiver.EnsureInstance();
            int requestId = receiver.RegisterPendingRequest("crazyGamesShowRewardedAd", DefaultTimeoutSeconds, onComplete);
#if UNITY_WEBGL && !UNITY_EDITOR
            CrazyGamesBridge_RequestRewardedAd(requestId, PlatformBridgeReceiver.ReceiverGameObjectName);
#endif
        }

        // "midgame" ad slot (CrazyGames.SDK.ad.requestAd("midgame", ...)) -
        // meant for a natural break in play rather than a player-initiated
        // reward. WaveManager calls this once, between waves, with no gating
        // on the result (continues either way).
        public void ShowMidgameAd(Action<PlatformResult> onComplete)
        {
            if (!_available)
            {
                _fallback.ShowMidgameAd(onComplete);
                return;
            }

            var receiver = PlatformBridgeReceiver.EnsureInstance();
            int requestId = receiver.RegisterPendingRequest("crazyGamesShowMidgameAd", DefaultTimeoutSeconds, onComplete);
#if UNITY_WEBGL && !UNITY_EDITOR
            CrazyGamesBridge_RequestMidgameAd(requestId, PlatformBridgeReceiver.ReceiverGameObjectName);
#endif
        }

        public void NotifyGameplayStart()
        {
            if (!_available) { _fallback.NotifyGameplayStart(); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
            CrazyGamesBridge_GameplayStart();
#endif
        }

        public void NotifyGameplayStop()
        {
            if (!_available) { _fallback.NotifyGameplayStop(); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
            CrazyGamesBridge_GameplayStop();
#endif
        }
    }
}
