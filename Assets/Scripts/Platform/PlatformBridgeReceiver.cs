using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace ProjectMiro.Platform
{
    /// Runtime-only receiver (never placed in any Scene asset - created via
    /// `new GameObject(...)` the first time it's needed) that
    /// ProjectMiroBridge.jslib's SendMessage callbacks target, and that owns
    /// the pending-request / per-call timeout bookkeeping shared by every
    /// WebGLPlatformService call. One instance for the whole game lifetime.
    public class PlatformBridgeReceiver : MonoBehaviour
    {
        public const string ReceiverGameObjectName = "ProjectMiroBridgeReceiver (Mock SDK Bridge)";

        private static PlatformBridgeReceiver _instance;
        private int _nextRequestId = 1;
        private readonly Dictionary<int, PendingRequest> _pending = new Dictionary<int, PendingRequest>();

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void ProjectMiroBridge_ReportCompleted(string apiName, string status);
#endif

        private class PendingRequest
        {
            public string ApiName;
            public Action<PlatformResult> OnResult;
            public Coroutine TimeoutCoroutine;
        }

        public static PlatformBridgeReceiver EnsureInstance()
        {
            if (_instance != null) return _instance;
            var go = new GameObject(ReceiverGameObjectName);
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<PlatformBridgeReceiver>();
            return _instance;
        }

        public int RegisterPendingRequest(string apiName, float timeoutSeconds, Action<PlatformResult> onResult)
        {
            int id = _nextRequestId++;
            var pending = new PendingRequest { ApiName = apiName, OnResult = onResult };
            pending.TimeoutCoroutine = StartCoroutine(TimeoutRoutine(id, timeoutSeconds));
            _pending[id] = pending;
            return id;
        }

        private IEnumerator TimeoutRoutine(int requestId, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Complete(requestId, PlatformResult.Timeout());
        }

        private void Complete(int requestId, PlatformResult result)
        {
            if (!_pending.TryGetValue(requestId, out var pending)) return; // already resolved (response and timeout both fired)
            _pending.Remove(requestId);
            if (pending.TimeoutCoroutine != null) StopCoroutine(pending.TimeoutCoroutine);

            // Mirror of OnAdStarted below - whichever of these two ad calls
            // muted audio for a real CrazyGames ad, this is the one place
            // every path to completion (success, failure, or this class's
            // own timeout) funnels through, so unmuting here can't be missed.
            if (pending.ApiName == "crazyGamesShowRewardedAd" || pending.ApiName == "crazyGamesShowMidgameAd")
            {
                AudioListener.pause = false;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            // Calls back into the jslib so the browser side can confirm a
            // full JS -> Unity -> JS round trip actually completed - see
            // window.__projectMiroBridgeDebug.confirmedCompletions, read by
            // the WebGL porting pipeline's Phase 7.
            ProjectMiroBridge_ReportCompleted(pending.ApiName, result.Status.ToString());
#endif
            pending.OnResult?.Invoke(result);
        }

        // --- SendMessage targets called from ProjectMiroBridge.jslib ---
        public void OnInitializeResult(string json) => CompleteFromJson(json);
        public void OnLoginResult(string json) => CompleteFromJson(json);
        public void OnRewardedAdResult(string json) => CompleteFromJson(json);
        public void OnMidgameAdResult(string json) => CompleteFromJson(json);
        public void OnSubmitScoreResult(string json) => CompleteFromJson(json);

        // --- SendMessage target called from CrazyGamesBridge.jslib only -
        // fired from the real SDK's adStarted callback, before the matching
        // OnRewardedAdResult/OnMidgameAdResult arrives. Not a pending-request
        // completion (no requestId to resolve), just a mute signal - see the
        // ApiName check in Complete() above for the matching unmute.
        public void OnAdStarted(string _) => AudioListener.pause = true;

        private void CompleteFromJson(string json)
        {
            BridgeResponseDto dto;
            try
            {
                dto = JsonUtility.FromJson<BridgeResponseDto>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlatformBridgeReceiver] Malformed bridge response JSON: {e.Message}\n{json}");
                return;
            }

            // JsonUtility does not always throw on malformed input - it can
            // silently hand back null/default fields instead, so check
            // explicitly rather than trust the try/catch alone.
            if (dto == null || string.IsNullOrEmpty(dto.status))
            {
                Debug.LogError($"[PlatformBridgeReceiver] Bridge response JSON parsed but is missing a 'status' field: {json}");
                return;
            }

            PlatformResult result;
            switch (dto.status)
            {
                case "Success": result = PlatformResult.Ok(dto.data); break;
                case "Failed": result = PlatformResult.Fail(dto.message); break;
                case "Cancelled": result = PlatformResult.Cancel(); break;
                case "TimedOut": result = PlatformResult.Timeout(); break;
                default: result = PlatformResult.Fail($"Unknown bridge status '{dto.status}'."); break;
            }
            Complete(dto.requestId, result);
        }

        [Serializable]
        private class BridgeResponseDto
        {
            public int requestId;
            public string status;
            public string message;
            public string data;
        }
    }
}
