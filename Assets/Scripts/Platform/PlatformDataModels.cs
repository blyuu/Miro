using System;

namespace ProjectMiro.Platform
{
    // Plain [Serializable] payload shapes for PlatformResult.Data, matching
    // what ProjectMiroBridge.jslib / MockPlatformService fabricate for each
    // API. Parse with JsonUtility.FromJson<T>(result.Data).

    [Serializable]
    public class InitializeResultData
    {
        public string sdkVersion;
    }

    [Serializable]
    public class LoginResultData
    {
        public string userId;
        public string displayName;
    }

    [Serializable]
    public class RewardedAdResultData
    {
        public bool rewardGranted;
    }

    [Serializable]
    public class MidgameAdResultData
    {
        public bool completed;
    }

    [Serializable]
    public class SubmitScoreResultData
    {
        public bool accepted;
        public int score;
    }

    /// Success payload for CrazyGamesPlatformService.Initialize - matches
    /// what CrazyGamesBridge.jslib reports after `CrazyGames.SDK.init()`
    /// resolves. environment is one of "local" | "crazygames" (never
    /// "disabled" - that case is reported as a Failed PlatformResult instead
    /// of a Success with this payload).
    [Serializable]
    public class CrazyGamesInitResultData
    {
        public string environment;
    }
}
