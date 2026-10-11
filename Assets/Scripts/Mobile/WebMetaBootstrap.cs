using UnityEngine;

/// Auto-installs mobile web-app <meta>/<link> tags once per page load, before
/// any Scene loads - mirrors PlatformServiceBootstrap's
/// [RuntimeInitializeOnLoadMethod] pattern (see Assets/Scripts/Platform/) so
/// this never needs wiring into any Scene. Mobile-only (PC doesn't need
/// "Add to Home Screen" support); FullscreenBridge.InjectWebAppMeta() itself
/// is also a no-op outside an actual WebGL build.
internal static class WebMetaBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (!PlatformInputDetector.IsMobileBrowser) return;
        FullscreenBridge.InjectWebAppMeta();
    }
}
