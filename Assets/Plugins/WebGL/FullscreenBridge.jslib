// FullscreenBridge.jslib
//
// Two independent, synchronous jslib calls - neither needs the
// PlatformBridgeReceiver request/response queue (no async round trip, each
// either does something immediately or safely no-ops):
//
//   FullscreenBridge_RequestFullscreen() - called from
//   LobbyController.OnStartClicked() as the very first thing that handler
//   does, so it runs inside the SAME trusted user-gesture call stack as the
//   click itself (browsers reject a Fullscreen API request made outside
//   that). Targets Unity's own default WebGL template's
//   "#unity-fullscreen-container" element (same element
//   unityInstance.SetFullscreen(1) already targets via the template's own
//   fullscreen button - that button is just CSS-hidden on mobile UAs, which
//   is the actual gap this closes). Every failure mode - fullscreen simply
//   unsupported, or (document.fullscreenEnabled === false, e.g. embedded in
//   a cross-origin iframe like a CrazyGames embed without the host page's
//   `allowfullscreen` attribute, which this game cannot control) - is
//   swallowed here; the C# caller never blocks game start on this.
//
//   FullscreenBridge_InjectWebAppMeta() - called once at startup (see
//   Assets/Scripts/Mobile/WebMetaBootstrap.cs) to dynamically add
//   apple-mobile-web-app-* <meta> tags and a <link rel="manifest"> pointing
//   at Assets/StreamingAssets/manifest.json (auto-copied into every WebGL
//   build's own StreamingAssets/ folder, no custom WebGL Template needed).
//   NOTE: unlike the viewport <meta> tag Unity's own default template already
//   injects dynamically at runtime (and which works fine done late), iOS
//   Safari's "Add to Home Screen" standalone-mode detection may only honor
//   apple-mobile-web-app-capable if it's present in the document before/at
//   the time the page is added - injecting it after the Unity loader has
//   already started is a best-effort fallback given no custom WebGL Template
//   exists in this project, not a guaranteed fix. A custom Template with
//   these tags baked into the static <head> would be the fully reliable
//   alternative.

mergeInto(LibraryManager.library, {

  FullscreenBridge_RequestFullscreen: function () {
    try {
      if (typeof document === 'undefined') return 0;
      if (document.fullscreenEnabled === false) return 0;

      var el = document.getElementById('unity-fullscreen-container') || document.documentElement;
      if (!el || !el.requestFullscreen) return 0;

      var result = el.requestFullscreen();
      if (result && typeof result.catch === 'function') {
        result.catch(function (err) {
          console.warn('[FullscreenBridge] requestFullscreen() was rejected - continuing without fullscreen.', err);
        });
      }
      return 1;
    } catch (e) {
      console.warn('[FullscreenBridge] requestFullscreen() threw - continuing without fullscreen.', e);
      return 0;
    }
  },

  FullscreenBridge_InjectWebAppMeta: function () {
    try {
      if (typeof document === 'undefined' || !document.head) return;

      function addMeta(name, content) {
        if (document.querySelector('meta[name="' + name + '"]')) return;
        var m = document.createElement('meta');
        m.name = name;
        m.content = content;
        document.head.appendChild(m);
      }

      addMeta('apple-mobile-web-app-capable', 'yes');
      addMeta('apple-mobile-web-app-status-bar-style', 'black-translucent');
      addMeta('apple-mobile-web-app-title', 'ProjectMiro');
      addMeta('mobile-web-app-capable', 'yes');

      if (!document.querySelector('link[rel="manifest"]')) {
        var link = document.createElement('link');
        link.rel = 'manifest';
        link.href = 'StreamingAssets/manifest.json';
        document.head.appendChild(link);
      }
    } catch (e) {
      console.warn('[FullscreenBridge] InjectWebAppMeta failed (non-fatal).', e);
    }
  },

});
