using System;

namespace ProjectMiro.Platform
{
    /// Abstraction over a third-party platform SDK (login, rewarded ads,
    /// leaderboard score submission). Every call is asynchronous and always
    /// resolves exactly once via the callback with a PlatformResult whose
    /// Status is one of Success/Failed/Cancelled/TimedOut - callers must
    /// branch on Status rather than assume Success.
    ///
    /// Three implementations exist:
    ///  - MockPlatformService: used in the Editor (and any non-WebGL player).
    ///  - WebGLPlatformService: a Mock SDK backed by
    ///    Assets/Plugins/WebGL/ProjectMiroBridge.jslib - kept around as the
    ///    fallback CrazyGamesPlatformService falls back to when the real SDK
    ///    isn't available.
    ///  - CrazyGamesPlatformService: the real external SDK adapter
    ///    (CrazyGames SDK v3), used in an actual WebGL build. See
    ///    Assets/Scripts/Platform/CRAZYGAMES_INTEGRATION.md.
    /// Use PlatformServiceProvider.Current to get the right one rather than
    /// constructing any of them directly.
    public interface IPlatformService
    {
        bool IsInitialized { get; }
        void Initialize(Action<PlatformResult> onComplete);
        void Login(Action<PlatformResult> onComplete);
        void ShowRewardedAd(Action<PlatformResult> onComplete);
        void ShowMidgameAd(Action<PlatformResult> onComplete);
        void SubmitScore(int score, Action<PlatformResult> onComplete);

        /// Fire-and-forget gameplay-lifecycle signals (no response - these
        /// either succeed silently or no-op). Call NotifyGameplayStart when
        /// the player starts/resumes playing (wave 1 beginning, revive,
        /// entering a level, ...) and NotifyGameplayStop on every break
        /// (menu, pause, run end). See CRAZYGAMES_INTEGRATION.md for why
        /// these exist - they map to CrazyGames SDK v3's
        /// game.gameplayStart()/gameplayStop().
        void NotifyGameplayStart();
        void NotifyGameplayStop();
    }
}
