using UnityEngine;

namespace ProjectMiro.Platform
{
    /// Auto-initializes the platform SDK bridge at game startup, mirroring
    /// how a real ad/login SDK is typically initialized once before any
    /// scene logic runs. This is the only call made automatically - every
    /// other IPlatformService method is only invoked by whatever
    /// gameplay/UI code explicitly calls PlatformServiceProvider.Current
    /// (ReviveUI.ShowRewardedAd, WaveManager.NotifyGameplayStart/Stop/
    /// ShowMidgameAd - see CRAZYGAMES_INTEGRATION.md for the full list).
    ///
    /// This also keeps WebGLPlatformService's DllImport externs statically
    /// reachable, so IL2CPP's managed-code stripping can't remove the
    /// bridge from a build just because no gameplay code references it yet.
    internal static class PlatformServiceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            PlatformServiceProvider.Current.Initialize(result =>
            {
                if (result.Status == PlatformResultStatus.Success)
                {
                    Debug.Log("[PlatformServiceBootstrap] Platform SDK Initialize -> Success");
                }
                else
                {
                    Debug.LogWarning(
                        $"[PlatformServiceBootstrap] Platform SDK Initialize -> {result.Status}" +
                        (result.Message != null ? $" ({result.Message})" : string.Empty));
                }
            });
        }
    }
}
