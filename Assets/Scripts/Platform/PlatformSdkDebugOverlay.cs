#if UNITY_WEBGL && DEVELOPMENT_BUILD
using System;
using UnityEngine;

namespace ProjectMiro.Platform
{
    /// Temporary SDK test UI, available ONLY in a WebGL Development Build
    /// (UNITY_WEBGL && DEVELOPMENT_BUILD) - never compiled into the Editor,
    /// a non-WebGL player, or a release (non-Development) WebGL build. Draws
    /// a plain IMGUI overlay (no Canvas/Scene asset involved) so it can be
    /// added/removed without touching any Scene or UI prefab. Exercises
    /// PlatformServiceProvider.Current end to end and logs each result
    /// (including requestId where visible via Debug.Log from
    /// PlatformBridgeReceiver) so a human - or the WebGL porting pipeline's
    /// Phase 7 - can confirm the real JS -> Unity -> JS round trip.
    internal class PlatformSdkDebugOverlay : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("PlatformSdkDebugOverlay (WebGL Dev Build Only - Mock SDK Test)");
            DontDestroyOnLoad(go);
            go.AddComponent<PlatformSdkDebugOverlay>();
        }

        private string _log = string.Empty;
        private Vector2 _scroll;
        private bool _collapsed;

        private void OnGUI()
        {
            const float width = 380f;
            var rect = new Rect(10, 10, width, _collapsed ? 32 : 500);
            GUILayout.BeginArea(rect, GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Platform SDK Bridge Test (WebGL Dev Build)", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(_collapsed ? "+" : "-", GUILayout.Width(24))) _collapsed = !_collapsed;
            GUILayout.EndHorizontal();

            if (!_collapsed)
            {
                GUILayout.Label("Initialize/Login/ShowRewardedAd/ShowMidgameAd/GameplayStart/Stop use the REAL CrazyGames SDK when available, Mock otherwise. SubmitScore always uses the Mock bridge (ProjectMiroBridge.jslib) - no leaderboard/encryption key configured yet, see CRAZYGAMES_INTEGRATION.md.");

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Initialize")) Invoke("Initialize", cb => PlatformServiceProvider.Current.Initialize(cb));
                if (GUILayout.Button("Login")) Invoke("Login", cb => PlatformServiceProvider.Current.Login(cb));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("ShowRewardedAd")) Invoke("ShowRewardedAd", cb => PlatformServiceProvider.Current.ShowRewardedAd(cb));
                if (GUILayout.Button("ShowMidgameAd")) Invoke("ShowMidgameAd", cb => PlatformServiceProvider.Current.ShowMidgameAd(cb));
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SubmitScore(123)")) Invoke("SubmitScore", cb => PlatformServiceProvider.Current.SubmitScore(123, cb));
                GUILayout.EndHorizontal();

                GUILayout.Space(6);
                GUILayout.Label("CrazyGames real SDK (gameplay-lifecycle, fire-and-forget):");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("GameplayStart")) { PlatformServiceProvider.Current.NotifyGameplayStart(); Append("NotifyGameplayStart() called"); }
                if (GUILayout.Button("GameplayStop")) { PlatformServiceProvider.Current.NotifyGameplayStop(); Append("NotifyGameplayStop() called"); }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);
                GUILayout.Label("Force the NEXT Login call to resolve as:");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Failed")) ForceLoginOutcome(PlatformResultStatus.Failed);
                if (GUILayout.Button("Cancelled")) ForceLoginOutcome(PlatformResultStatus.Cancelled);
                if (GUILayout.Button("TimedOut")) ForceLoginOutcome(PlatformResultStatus.TimedOut);
                GUILayout.EndHorizontal();

                GUILayout.Space(6);
                _scroll = GUILayout.BeginScrollView(_scroll, GUI.skin.textArea, GUILayout.Height(220));
                GUILayout.Label(_log);
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        private void Invoke(string label, Action<Action<PlatformResult>> call)
        {
            Append($"{label} -> (pending...)");
            call(result =>
            {
                string extra = result.Status == PlatformResultStatus.Success ? $" data={result.Data}"
                    : result.Message != null ? $" message={result.Message}"
                    : string.Empty;
                Append($"{label} -> {result.Status}{extra}");
            });
        }

        private void ForceLoginOutcome(PlatformResultStatus outcome)
        {
            var service = PlatformServiceProvider.Current;
            if (service is WebGLPlatformService web) web.ForceNextOutcome("login", outcome);
            Append($"Forced next Login outcome -> {outcome}");
        }

        private void Append(string line)
        {
            string stamped = $"[{Time.realtimeSinceStartup:0.0}s] {line}";
            _log = stamped + "\n" + _log;
            Debug.Log($"[PlatformSdkDebugOverlay] {stamped}");
        }
    }
}
#endif
