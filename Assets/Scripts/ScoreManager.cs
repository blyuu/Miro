using System;
using UnityEngine;

// Run-scoped score counter - NOT persistent across scenes like AudioManager;
// a fresh instance is created per MainGame scene load (Awake), exactly like
// ChainShatterTracker, so Retry/scene reload naturally resets it to zero.
//
// Scoring rule (the point of this file): a Shatter kill is worth
// shatterChainPoints * the CURRENT chain count, not a flat amount - so a
// 3-Shatter chain scores 1+2+3 = 6x shatterChainPoints, not 3x. Killing the
// same number of enemies by slowly saturating and shattering them one at a
// time (chain resets between each, since chainTimeout elapses) scores far
// less than popping them in one fast chain. This is what rewards "모아서 한
// 번에 터트리기" over picking enemies off individually - see
// ChainShatterTracker.RegisterShatter(), the single call site that reports a
// Shatter here.
//
// Feeds WaveHUD's live counter (via OnScoreChanged) and the Game Clear/Game
// Over summary (GameFlowController reads TotalScore once a run ends). Also
// the number PlatformServiceProvider.Current.SubmitScore would eventually
// submit, once a CrazyGames leaderboard + encryption key exist - see
// Assets/Scripts/Platform/CRAZYGAMES_INTEGRATION.md.
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("Points")]
    [Tooltip("A normal spray kill - the enemy died from plain damage, never reached a Shatter.")]
    [SerializeField] private int normalKillPoints = 10;
    [Tooltip("Per-Shatter base, multiplied by the CURRENT chain count (ChainShatterTracker) - see this file's header comment for why that's chosen over a flat per-Shatter amount.")]
    [SerializeField] private int shatterChainPoints = 20;
    [Tooltip("Flat bonus the instant the Boss dies.")]
    [SerializeField] private int bossKillPoints = 500;

    public int TotalScore { get; private set; }

    // Fired on every change with the new running total - WaveHUD subscribes
    // to drive its live counter.
    public event Action<int> OnScoreChanged;

    void Awake()
    {
        Instance = this;
    }

    public void AddNormalKill() => Add(normalKillPoints);

    public void AddBossKill() => Add(bossKillPoints);

    // chainCount is the value ChainShatterTracker.RegisterShatter() just
    // computed for the Shatter that triggered this (1 = first in a new
    // chain, 2 = second within chainTimeout of the last, ...).
    public void AddShatterKill(int chainCount) => Add(shatterChainPoints * Mathf.Max(chainCount, 1));

    private void Add(int amount)
    {
        TotalScore += amount;
        Debug.Log($"[ScoreDebug] ScoreManager.Add({amount}) -> TotalScore={TotalScore}, subscriberCount={OnScoreChanged?.GetInvocationList().Length ?? 0}");
        OnScoreChanged?.Invoke(TotalScore);
    }
}
