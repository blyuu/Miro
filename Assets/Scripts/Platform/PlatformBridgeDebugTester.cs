using UnityEngine;

namespace ProjectMiro.Platform
{
    /// Optional manual-test harness - NOT added to any Scene by this change.
    /// Attach to any GameObject (e.g. a throwaway empty GameObject created
    /// just for testing) to exercise PlatformServiceProvider.Current from
    /// the Inspector's right-click context menu, without writing a
    /// dedicated test script. Safe to delete at any time; nothing else
    /// depends on it.
    public class PlatformBridgeDebugTester : MonoBehaviour
    {
        [ContextMenu("Initialize")]
        public void TestInitialize() => PlatformServiceProvider.Current.Initialize(r => Log("Initialize", r));

        [ContextMenu("Login")]
        public void TestLogin() => PlatformServiceProvider.Current.Login(r => Log("Login", r));

        [ContextMenu("Show Rewarded Ad")]
        public void TestShowRewardedAd() => PlatformServiceProvider.Current.ShowRewardedAd(r => Log("ShowRewardedAd", r));

        [ContextMenu("Show Midgame Ad")]
        public void TestShowMidgameAd() => PlatformServiceProvider.Current.ShowMidgameAd(r => Log("ShowMidgameAd", r));

        [ContextMenu("Submit Score (123)")]
        public void TestSubmitScore() => PlatformServiceProvider.Current.SubmitScore(123, r => Log("SubmitScore", r));

        [ContextMenu("Force Next Login -> Failed")]
        public void ForceLoginFailed() => ForceLogin(PlatformResultStatus.Failed);

        [ContextMenu("Force Next Login -> Cancelled")]
        public void ForceLoginCancelled() => ForceLogin(PlatformResultStatus.Cancelled);

        [ContextMenu("Force Next Login -> TimedOut")]
        public void ForceLoginTimedOut() => ForceLogin(PlatformResultStatus.TimedOut);

        private void ForceLogin(PlatformResultStatus outcome)
        {
            var service = PlatformServiceProvider.Current;
            if (service is MockPlatformService mock) mock.ForceNextOutcome("login", outcome);
            else if (service is WebGLPlatformService web) web.ForceNextOutcome("login", outcome);
        }

        private static void Log(string api, PlatformResult r)
        {
            string extra = r.Status == PlatformResultStatus.Success ? $" data={r.Data}"
                : r.Status == PlatformResultStatus.Failed ? $" message={r.Message}"
                : string.Empty;
            Debug.Log($"[PlatformBridgeDebugTester] {api} -> {r.Status}{extra}");
        }
    }
}
