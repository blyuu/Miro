namespace ProjectMiro.Platform
{
    public enum PlatformResultStatus
    {
        Success,
        Failed,
        Cancelled,
        TimedOut,
    }

    /// Uniform async result shape every IPlatformService call resolves with.
    /// `Data`, when present, is a raw JSON string the caller can parse into
    /// the matching *ResultData type from PlatformDataModels.cs - kept as a
    /// string (not `object`) because JsonUtility, used on both the Mock and
    /// WebGL paths, has no good way to represent an arbitrary payload shape.
    public class PlatformResult
    {
        public PlatformResultStatus Status { get; private set; }
        public string Message { get; private set; }
        public string Data { get; private set; }

        public static PlatformResult Ok(string data = null) =>
            new PlatformResult { Status = PlatformResultStatus.Success, Data = data };

        public static PlatformResult Fail(string message) =>
            new PlatformResult { Status = PlatformResultStatus.Failed, Message = message };

        public static PlatformResult Cancel() =>
            new PlatformResult { Status = PlatformResultStatus.Cancelled };

        public static PlatformResult Timeout() =>
            new PlatformResult { Status = PlatformResultStatus.TimedOut, Message = "Request timed out." };
    }
}
