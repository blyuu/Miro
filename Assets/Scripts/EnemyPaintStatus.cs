using System;
using UnityEngine;
using UnityEngine.Serialization;

// Separate from EnemyHealth on purpose (Section 16): HP decides whether the
// enemy dies: Paint Gauge decides whether it can enter Saturated and take
// part in a Chain Shatter. PlayerSpray feeds this via AddPaint(); once
// Saturated, the player's next hit calls Shatter() instead of normal damage.
[RequireComponent(typeof(EnemyHealth))]
public class EnemyPaintStatus : MonoBehaviour
{
    [SerializeField] private float maxPaint = 100f;

    [Header("Shatter")]
    [SerializeField] private float shatterRadius = 4f;
    [SerializeField] private float shatterPaintAmount = 40f;
    [SerializeField] private int shatterDamage = 0;
    [SerializeField] private LayerMask enemyLayerMask = 0;
    [SerializeField] private int maxShatterTargets = 12;

    [Header("Feedback (reuses existing renderer/material, no new shader)")]
    [SerializeField] private Renderer tintRenderer;
    [FormerlySerializedAs("saturatedTintColor")]
    [SerializeField] private Color paintColor = new Color(0.1f, 0.9f, 1f, 1f);
    // Additive glow (via EnemyHealth.SetRestingEmissive) so the paint buildup
    // reads clearly even in dark/foggy scenes where baseColorFactor alone gets
    // washed out by ambient lighting - purely visual, same accumulation value.
    [SerializeField] private Color paintGlowColor = new Color(0.1f, 0.9f, 1f, 1f);

    private EnemyHealth health;
    private BossController bossController;
    private ImpactSplatPool impactSplatPool;
    private MaterialPropertyBlock propertyBlock;
    private Color restColor;
    // baseColorFactor (not emissiveFactor): EnemyHealth's hit-flash owns emissiveFactor
    // exclusively, and BossController's own accent tint already lives on baseColorFactor -
    // sharing that key (via restColor, see Start()) composes with it instead of fighting it.
    private static readonly int BaseColorFactorId = Shader.PropertyToID("baseColorFactor");
    private static readonly int LegacyBaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int StandardColorId = Shader.PropertyToID("_Color");
    private static readonly Collider[] overlapBuffer = new Collider[16];

    public float CurrentPaint { get; private set; }
    public float MaxPaint => maxPaint;
    public bool IsSaturated { get; private set; }

    // Fired on every AddPaint so the per-enemy gauge UI can react.
    public event Action<float, float> OnPaintChanged; // (current, max)
    // Fired exactly once per saturation cycle (Section 8).
    public event Action OnSaturated;

    void Awake()
    {
        health = GetComponent<EnemyHealth>();
        bossController = GetComponent<BossController>();
        propertyBlock = new MaterialPropertyBlock();

        if (tintRenderer == null) tintRenderer = GetComponentInChildren<Renderer>();

        if (enemyLayerMask == 0) enemyLayerMask = LayerMask.GetMask("Enemy");

        var poolGo = GameObject.Find("ImpactSplatPool");
        if (poolGo != null) impactSplatPool = poolGo.GetComponent<ImpactSplatPool>();
    }

    // Start() (not Awake()): Unity guarantees every object's Awake() has already run by the
    // time any Start() runs, so this reads BossController.AccentColor safely regardless of
    // component order on the GameObject.
    void Start()
    {
        if (bossController != null)
        {
            restColor = bossController.AccentColor;
        }
        else if (tintRenderer != null && tintRenderer.sharedMaterial != null)
        {
            restColor = tintRenderer.sharedMaterial.GetColor(BaseColorFactorId);
        }
        else
        {
            restColor = Color.white;
        }
    }

    public void AddPaint(float amount)
    {
        if (IsSaturated || health == null || health.IsDead || amount <= 0f) return;

        CurrentPaint = Mathf.Min(CurrentPaint + amount, maxPaint);
        OnPaintChanged?.Invoke(CurrentPaint, maxPaint);
        UpdateVisual();

        if (CurrentPaint >= maxPaint)
        {
            IsSaturated = true;
            OnSaturated?.Invoke();
        }
    }

    // Continuous/gradual tint toward paintColor as CurrentPaint accumulates - not a one-shot
    // switch at saturation. restColor is the Boss's existing Magenta accent (or the renderer's
    // own base color for non-Boss enemies), so this intensifies rather than overwrites it.
    private void UpdateVisual()
    {
        if (tintRenderer == null) return;

        float percent = CurrentPaint / maxPaint;
        Color color = Color.Lerp(restColor, paintColor, percent);

        tintRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorFactorId, color);
        propertyBlock.SetColor(LegacyBaseColorId, color);
        propertyBlock.SetColor(StandardColorId, color);
        tintRenderer.SetPropertyBlock(propertyBlock);

        if (health != null)
        {
            health.SetRestingEmissive(Color.Lerp(Color.black, paintGlowColor, percent));
        }
    }

    // Called by PlayerSpray when a Saturated enemy takes its next hit
    // (Section 10's chosen rule - see PlayerSpray.Fire()). Propagates Paint
    // to nearby enemies, plays a reused pooled VFX burst, optionally applies
    // modest splash damage, registers the Chain, and finishes the kill
    // through the existing EnemyHealth death path (no parallel death logic).
    public void Shatter()
    {
        if (!IsSaturated) return;

        Vector3 origin = transform.position;

        if (impactSplatPool != null) impactSplatPool.PlayImpact(origin, Vector3.up);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayPaintBurst();

        int count = Physics.OverlapSphereNonAlloc(origin, shatterRadius, overlapBuffer, enemyLayerMask);
        int propagated = 0;
        for (int i = 0; i < count && propagated < maxShatterTargets; i++)
        {
            var otherStatus = overlapBuffer[i].GetComponentInParent<EnemyPaintStatus>();
            if (otherStatus == null || otherStatus == this) continue;
            if (otherStatus.health == null || otherStatus.health.IsDead) continue;

            otherStatus.AddPaint(shatterPaintAmount);
            if (shatterDamage > 0)
            {
                otherStatus.health.TakeDamage(shatterDamage);
                // Splash-killed outright by the damage itself, not by its own
                // Shatter() call (ChainShatterTracker was never told about
                // this one) - scores as a normal kill. shatterDamage is 0 by
                // default, so this is a dormant edge case, not the main path.
                if (otherStatus.health.IsDead && ScoreManager.Instance != null) ScoreManager.Instance.AddNormalKill();
            }
            propagated++;
        }

        ChainShatterTracker.Instance?.RegisterShatter();

        health.Kill();
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, shatterRadius);
    }
#endif
}
