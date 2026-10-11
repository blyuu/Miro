using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectMiro.Platform
{
    /// Editor / non-WebGL implementation of IPlatformService. This is NOT a
    /// real SDK - it never talks to a server, ad network, or leaderboard.
    /// Every call resolves after a short simulated delay with a fabricated
    /// success payload, unless ForceNextOutcome was used to request
    /// Failed/Cancelled/TimedOut for that api name - this lets gameplay/UI
    /// code exercise every PlatformResultStatus branch without a WebGL
    /// build. See IPlatformService for the overall contract.
    public class MockPlatformService : IPlatformService
    {
        public bool IsInitialized { get; private set; }

        private readonly Dictionary<string, PlatformResultStatus> _forcedOutcomes = new Dictionary<string, PlatformResultStatus>();

        /// Forces the NEXT call for apiName ("initialize"|"login"|
        /// "showRewardedAd"|"showMidgameAd"|"submitScore") to resolve with
        /// this outcome instead of Success. Consumed once, then reset to
        /// normal.
        public void ForceNextOutcome(string apiName, PlatformResultStatus outcome) => _forcedOutcomes[apiName] = outcome;

        public void Initialize(Action<PlatformResult> onComplete)
        {
            Run("initialize", result =>
            {
                IsInitialized = result.Status == PlatformResultStatus.Success;
                onComplete?.Invoke(result);
            }, () => PlatformResult.Ok(JsonUtility.ToJson(new InitializeResultData { sdkVersion = "mock-editor-1.0.0" })));
        }

        public void Login(Action<PlatformResult> onComplete)
        {
            Run("login", onComplete, () => PlatformResult.Ok(JsonUtility.ToJson(new LoginResultData { userId = "mock-user-0001", displayName = "Mock Player" })));
        }

        public void ShowRewardedAd(Action<PlatformResult> onComplete)
        {
            Run("showRewardedAd", onComplete, () => PlatformResult.Ok(JsonUtility.ToJson(new RewardedAdResultData { rewardGranted = true })));
        }

        public void ShowMidgameAd(Action<PlatformResult> onComplete)
        {
            Run("showMidgameAd", onComplete, () => PlatformResult.Ok(JsonUtility.ToJson(new MidgameAdResultData { completed = true })));
        }

        public void SubmitScore(int score, Action<PlatformResult> onComplete)
        {
            Run("submitScore", onComplete, () => PlatformResult.Ok(JsonUtility.ToJson(new SubmitScoreResultData { accepted = true, score = score })));
        }

        public void NotifyGameplayStart() => Debug.Log("[MockPlatformService] NotifyGameplayStart (no-op, Editor/Mock).");
        public void NotifyGameplayStop() => Debug.Log("[MockPlatformService] NotifyGameplayStop (no-op, Editor/Mock).");

        private void Run(string apiName, Action<PlatformResult> onComplete, Func<PlatformResult> onSuccess)
        {
            PlatformResultStatus outcome = PlatformResultStatus.Success;
            if (_forcedOutcomes.TryGetValue(apiName, out var forced))
            {
                outcome = forced;
                _forcedOutcomes.Remove(apiName);
            }
            MockServiceRunner.EnsureInstance().StartCoroutine(RunRoutine(apiName, outcome, onComplete, onSuccess));
        }

        private IEnumerator RunRoutine(string apiName, PlatformResultStatus outcome, Action<PlatformResult> onComplete, Func<PlatformResult> onSuccess)
        {
            float delay = outcome == PlatformResultStatus.TimedOut ? 0.05f : UnityEngine.Random.Range(0.2f, 0.5f);
            yield return new WaitForSecondsRealtime(delay);

            PlatformResult result;
            switch (outcome)
            {
                case PlatformResultStatus.Success: result = onSuccess(); break;
                case PlatformResultStatus.Failed: result = PlatformResult.Fail($"Mock {apiName} failed (simulated failure)."); break;
                case PlatformResultStatus.Cancelled: result = PlatformResult.Cancel(); break;
                case PlatformResultStatus.TimedOut: result = PlatformResult.Timeout(); break;
                default: result = PlatformResult.Fail("Unknown forced outcome."); break;
            }
            onComplete?.Invoke(result);
        }
    }
}
