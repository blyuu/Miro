using System;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int reviveHealth = 50;

    private int currentHealth;
    private bool pendingFatalHit;

    public bool IsDead { get; private set; }

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    public event Action OnDied;

    // Fired once, the instant a hit would be fatal - BEFORE death is
    // finalized (IsDead is still false, OnDied has not fired yet). Gives a
    // listener (GameFlowController) one chance to offer a revive - e.g. a
    // rewarded ad - and decide the outcome via ResolveFatalHit() instead of
    // death being unconditional. If nothing is listening, the hit is simply
    // never resolved and the player stays at 0 HP forever - callers that
    // care about game-over must always resolve a fatal hit one way or
    // another.
    public event Action OnFatalHit;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        if (IsDead || pendingFatalHit) return;

        currentHealth -= amount;

        // Player Hit volume (1.0, vs EnemyHealth's 0.6) is deliberate - meant
        // to read as a stronger impact than Enemy Hit.
        if (AudioManager.Instance != null) AudioManager.Instance.PlayPlayerHit();

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            pendingFatalHit = true;
            OnFatalHit?.Invoke();
        }
    }

    // Resolves a pending fatal hit (see OnFatalHit). revived=false finalizes
    // death exactly as TakeDamage used to do unconditionally (IsDead=true,
    // OnDied fires). revived=true restores health and lets the run continue -
    // OnDied never fires, and since nothing else observed IsDead becoming
    // true, WaveManager's own "yield break on PlayerIsDead()" loop was never
    // actually exited (Time.timeScale was 0 for the whole decision, so it was
    // merely paused mid-iteration) and simply keeps running once resumed.
    public void ResolveFatalHit(bool revived)
    {
        if (!pendingFatalHit) return;
        pendingFatalHit = false;

        if (revived)
        {
            currentHealth = Mathf.Min(reviveHealth, maxHealth);
        }
        else
        {
            IsDead = true;
            OnDied?.Invoke();
        }
    }
}
