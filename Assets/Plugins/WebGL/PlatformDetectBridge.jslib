// PlatformDetectBridge.jslib
//
// One synchronous query: "is this browser a mobile/touch device?" - used by
// PlatformInputDetector.cs to decide whether MobileControlsRoot shows the
// touch joystick/spray-button/camera-drag UI. No request/response bookkeeping
// needed (unlike the Platform SDK bridges) since user-agent/touch-capability
// checks are plain synchronous JS - the C# side just calls this and gets an
// immediate answer.
//
// Heuristic (deliberately NOT just "has a touchscreen" - a touchscreen
// Windows laptop should still get PC controls):
//   - A classic mobile-phone user agent (Android/iPhone/iPod/"Mobile"/Windows
//     Phone), explicitly excluding iPad's own UA string, counts as mobile.
//   - iPadOS 13+ in "Request Desktop Website" mode reports a Mac-like user
//     agent, so it's detected separately via the well-known trick: Mac
//     platform + more than one simultaneous touch point (a real Mac laptop's
//     trackpad is not a multi-touch *screen* and never reports this).
// A touchscreen PC/laptop matches neither check (desktop UA, and even if
// touch-capable, isn't reporting as MacIntel with multi-touch), so it
// correctly stays in PC mode.

mergeInto(LibraryManager.library, {

  PlatformDetectBridge_IsMobileBrowser: function () {
    if (typeof navigator === 'undefined') return 0;

    var ua = navigator.userAgent || '';
    var isPhoneOrAndroidUA = /Android|iPhone|iPod|Mobile|Windows Phone/i.test(ua) && !/iPad/i.test(ua);
    var isIPadDesktopMode = navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1;
    var isIPadClassicUA = /iPad/i.test(ua);

    return (isPhoneOrAndroidUA || isIPadClassicUA || isIPadDesktopMode) ? 1 : 0;
  },

  // Used by MobileControlsRoot to convert a target CSS-pixel size into a
  // Canvas reference-resolution sizeDelta: Canvas.scaleFactor operates in
  // "Screen px" (CSS px * devicePixelRatio), so converting back to a CSS-px
  // target requires dividing that out - this is the one value C# can't read
  // on its own.
  PlatformDetectBridge_GetDevicePixelRatio: function () {
    return (typeof window !== 'undefined' && window.devicePixelRatio) ? window.devicePixelRatio : 1;
  },

});
