using System;
using UnityEngine;

// Lightweight consecutive-Shatter counter (Section 14) - not a combo/score
// system, just: how many Shatters happened within chainTimeout of each
// other. Resets after a gap with no Shatter.
public class ChainShatterTracker : MonoBehaviour
{
    public static ChainShatterTracker Instance { get; private set; }

    [SerializeField] private float chainTimeout = 2.5f;

    private int chainCount;
    private float lastShatterTime = -999f;

    // Fired on every Shatter with the current running chain count
    // (1 = first Shatter, 2 = "CHAIN x2", ...).
    public event Action<int> OnChainUpdated;

    void Awake()
    {
        Instance = this;
    }

    public void RegisterShatter()
    {
        if (Time.time - lastShatterTime > chainTimeout) chainCount = 0;
        chainCount++;
        lastShatterTime = Time.time;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayChainReaction();
        Debug.Log($"[ScoreDebug] shatter kill chain={chainCount} - ScoreManager.Instance={(ScoreManager.Instance != null)}");
        if (ScoreManager.Instance != null) ScoreManager.Instance.AddShatterKill(chainCount);
        OnChainUpdated?.Invoke(chainCount);
    }
}
