using System;
using System.Collections;
using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private Renderer hitFlashRenderer;
    [SerializeField] private Color hitFlashColor = Color.white;
    [SerializeField] private float hitFlashDuration = 0.08f;

    // TakeDamage fires on every landed spray tick (as fast as 0.04s apart
    // with Rapid Spray) - throttle the hit SFX itself so sustained spraying
    // on one enemy doesn't stack into noise (paired with PlayerSpray's own
    // splash-SFX cooldown).
    [SerializeField] private float hitSfxCooldown = 0.09f;
    private float nextHitSfxTime;

    private int currentHealth;
    private bool hasDied;
    private MaterialPropertyBlock propertyBlock;
    private Coroutine flashRoutine;
    // The emissiveFactor value FlashRoutine settles back to once a flash ends -
    // defaults to black, but EnemyPaintStatus drives it toward a glow color as
    // paint accumulates, so the two features share this slot without fighting.
    private Color restingEmissive = Color.black;
    private static readonly int EmissiveFactorId = Shader.PropertyToID("emissiveFactor");

    public bool IsDead => currentHealth <= 0;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    public event Action<EnemyHealth> OnDied;

    void Awake()
    {
        currentHealth = maxHealth;
        propertyBlock = new MaterialPropertyBlock();

        if (hitFlashRenderer == null)
            hitFlashRenderer = GetComponentInChildren<Renderer>();
    }

    // Used by EnemyPaintStatus.Shatter() to finish a Saturated enemy through
    // the exact same death path as normal damage (OnDied, SetActive(false),
    // hasDied guard) - no parallel death logic.
    public void Kill() => TakeDamage(currentHealth);

    public void TakeDamage(int amount)
    {
        if (currentHealth <= 0) return;

        currentHealth -= amount;

        if (AudioManager.Instance != null && Time.time >= nextHitSfxTime)
        {
            nextHitSfxTime = Time.time + hitSfxCooldown;
            AudioManager.Instance.PlayEnemyHit();
        }

        if (hitFlashRenderer != null)
        {
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine());
        }

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            gameObject.SetActive(false);

            if (!hasDied)
            {
                hasDied = true;
                if (AudioManager.Instance != null) AudioManager.Instance.PlayEnemyDeath();
                OnDied?.Invoke(this);
            }
        }
    }

    private IEnumerator FlashRoutine()
    {
        hitFlashRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(EmissiveFactorId, hitFlashColor);
        hitFlashRenderer.SetPropertyBlock(propertyBlock);

        yield return new WaitForSeconds(hitFlashDuration);

        propertyBlock.SetColor(EmissiveFactorId, restingEmissive);
        hitFlashRenderer.SetPropertyBlock(propertyBlock);
        flashRoutine = null;
    }

    // Lets EnemyPaintStatus drive a persistent glow (via emissiveFactor) that
    // intensifies with paint accumulation, without ever interrupting an
    // in-progress hit-flash: if a flash is currently playing, this only
    // updates the value FlashRoutine reverts to, not the live override.
    public void SetRestingEmissive(Color color)
    {
        restingEmissive = color;

        if (flashRoutine == null && hitFlashRenderer != null)
        {
            hitFlashRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(EmissiveFactorId, restingEmissive);
            hitFlashRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}