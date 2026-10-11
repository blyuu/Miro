using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// Single place platform/device detection lives for the PC-vs-mobile touch
/// UI decision (used by MobileControlsRoot.Awake()). In the Editor (and any
/// non-WebGL player) this is just Application.isMobilePlatform - the same
/// thing MobileControlsRoot checked before, so Editor behavior (always PC
/// mode unless the forceMobileControls debug toggle is on) is unchanged.
///
/// In an actual WebGL build, Application.isMobilePlatform is useless (it is
/// never true for a browser, regardless of the device it's running on), so
/// this instead asks the browser itself via
/// Assets/Plugins/WebGL/PlatformDetectBridge.jslib - a synchronous call (no
/// PlatformBridgeReceiver request/response needed, unlike the Platform SDK
/// bridge - user-agent/touch-capability checks are plain synchronous JS).
public static class PlatformInputDetector
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int PlatformDetectBridge_IsMobileBrowser();
    [DllImport("__Internal")] private static extern float PlatformDetectBridge_GetDevicePixelRatio();
#endif

    public static bool IsMobileBrowser
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return PlatformDetectBridge_IsMobileBrowser() != 0;
#else
            return Application.isMobilePlatform;
#endif
        }
    }

    // window.devicePixelRatio - not readable from C# directly. Needed to
    // convert a Canvas reference-resolution size back into actual CSS
    // pixels (Canvas.scaleFactor's own units are CSS px * this ratio).
    // Editor/non-WebGL has no browser to ask, so 1 (no-op) is correct there.
    public static float DevicePixelRatio
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            float dpr = PlatformDetectBridge_GetDevicePixelRatio();
            return dpr > 0f ? dpr : 1f;
#else
            return 1f;
#endif
        }
    }
}
