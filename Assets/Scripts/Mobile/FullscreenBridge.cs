#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// Thin wrapper over Assets/Plugins/WebGL/FullscreenBridge.jslib, mirroring
/// PlatformDetectBridge.cs's exact pattern: a synchronous
/// [DllImport("__Internal")] call, no-op outside an actual WebGL build.
/// Called from LobbyController.OnStartClicked() - see that call site for why
/// it must happen synchronously, inside the click itself.
public static class FullscreenBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int FullscreenBridge_RequestFullscreen();
    [DllImport("__Internal")] private static extern void FullscreenBridge_InjectWebAppMeta();
#endif

    public static void RequestFullscreen()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        FullscreenBridge_RequestFullscreen();
#endif
    }

    public static void InjectWebAppMeta()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        FullscreenBridge_InjectWebAppMeta();
#endif
    }
}
