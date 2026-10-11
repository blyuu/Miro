using UnityEngine;

namespace ProjectMiro.Platform
{
    /// Tiny runtime-only (never placed in a Scene asset) MonoBehaviour that
    /// exists solely to host MockPlatformService's coroutines, since
    /// coroutines require a MonoBehaviour to run on.
    internal class MockServiceRunner : MonoBehaviour
    {
        private static MockServiceRunner _instance;

        public static MockServiceRunner EnsureInstance()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("MockPlatformServiceRunner (Mock SDK)");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<MockServiceRunner>();
            return _instance;
        }
    }
}
