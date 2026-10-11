namespace ProjectMiro.Platform
{
    /// Single access point for gameplay/UI code that wants to reach a
    /// platform SDK: `PlatformServiceProvider.Current`. Resolves to
    /// CrazyGamesPlatformService (the real external SDK adapter, which
    /// itself falls back to the Mock WebGLPlatformService when the real SDK
    /// isn't available) in an actual WebGL build, MockPlatformService
    /// everywhere else (Editor, Standalone, ...), so calling code never
    /// needs its own #if UNITY_WEBGL. Deliberately plain C#, not a
    /// MonoBehaviour/Scene singleton - adding this never touches a .unity
    /// Scene file. Referenced by exactly two gameplay call sites
    /// (WaveManager.RunLoop and GameFlowController.EndCommon, both just
    /// NotifyGameplayStart/Stop) - everything else about this bridge stays
    /// an independent structure gameplay doesn't otherwise touch.
    public static class PlatformServiceProvider
    {
        private static IPlatformService _current;

        public static IPlatformService Current => _current ??= CreateDefault();

        private static IPlatformService CreateDefault()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new CrazyGamesPlatformService();
#else
            return new MockPlatformService();
#endif
        }

        /// Test seam - lets an EditMode/PlayMode test swap in a fake
        /// IPlatformService without touching the #if above.
        public static void OverrideForTesting(IPlatformService service) => _current = service;
    }
}
