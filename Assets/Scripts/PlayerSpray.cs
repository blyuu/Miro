using System;
using UnityEngine;

public class PlayerSpray : MonoBehaviour
{
    [SerializeField] private Transform sprayOrigin;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private PaintDecalPool decalPool;
    [SerializeField] private ImpactSplatPool impactSplatPool;

    // SprayVFX container (rotated as one unit toward AimDirection each
    // frame) holding the two visual layers: FineMist (existing spray
    // particle, kept as-is) + CoreDroplets (new, bigger paint blobs).
    [SerializeField] private Transform sprayVfxRoot;
    [SerializeField] private ParticleSystem fineMist;
    [SerializeField] private ParticleSystem coreDroplets;

    [SerializeField] private PlayerUpgradeStats upgradeStats;

    // Fire() can tick as fast as minFireInterval (0.04s = 25/s with Rapid
    // Spray) - playing a splash SFX on every single tick turns into an
    // indistinct wall of noise, so the SFX itself is throttled independently
    // of the raycast/damage rate.
    [SerializeField] private float sfxCooldown = 0.09f;
    private float nextSfxTime;

    // Fired whenever a Fire() tick actually lands on an EnemyHealth - purely
    // for presentation (Crosshair hit feedback). Does not affect damage/paint.
    public event Action OnEnemyHit;

    [SerializeField] private float maxAimDistance = 50f;
    [SerializeField] private float fireInterval = 0.1f;
    [SerializeField] private float minFireInterval = 0.04f;
    [SerializeField] private int damage = 10;

    // Tuned so sustained accurate spraying saturates an ordinary enemy in
    // roughly 1-2s at the default fireInterval (Section 7).
    [SerializeField] private float paintPerSprayHit = 7f;

    // Below this, (aimPoint - muzzle) is too short/unstable to define a
    // direction (e.g. camera ray hit something almost at the muzzle) - fall
    // back to the camera's own forward instead of risking a flipped spray.
    [SerializeField] private float minimumAimDistance = 0.3f;

    [Header("Debug (Editor only)")]
    [SerializeField] private bool drawAimGizmos = false;

    private MiroInputReader input;
    private float fireTimer;
    private int aimMask;

    // Single Source of Truth: computed once per frame (LateUpdate, after the
    // Animator has posed the hand bone this frame so sprayOrigin.position is
    // the real on-screen muzzle position) and consumed by both the VFX
    // orientation below and Fire()'s hit detection - and read externally by
    // PlayerMovement for aim-facing rotation.
    public Vector3 AimPoint { get; private set; }
    public Vector3 AimDirection { get; private set; } = Vector3.forward;

    void Awake()
    {
        input = GetComponent<MiroInputReader>();

        if (aimCamera == null && Camera.main != null)
            aimCamera = Camera.main;

        if (upgradeStats == null) upgradeStats = GetComponent<PlayerUpgradeStats>();

        aimMask = ~(1 << gameObject.layer);
    }

    // LateUpdate (not Update): sprayOrigin is parented under the hand bone,
    // which the Animator repositions during its own per-frame pass between
    // Update and LateUpdate. Computing/applying aim here - after that pass -
    // is what lets the VFX orientation actually stick instead of being
    // immediately overwritten by the bone's animated rotation.
    void LateUpdate()
    {
        UpdateAim();

        bool spraying = input.SprayHeld;

        if (sprayVfxRoot != null)
        {
            // Rotating the shared parent orients both FineMist and
            // CoreDroplets together in a single call.
            sprayVfxRoot.rotation = Quaternion.LookRotation(AimDirection, Vector3.up);
        }

        SetEmitting(fineMist, spraying);
        SetEmitting(coreDroplets, spraying);
        if (AudioManager.Instance != null) AudioManager.Instance.SetSprayLooping(spraying);

        if (!spraying)
        {
            fireTimer = 0f;
            return;
        }

        fireTimer -= Time.deltaTime;
        if (fireTimer > 0f) return;

        float rateMultiplier = upgradeStats != null ? upgradeStats.SprayRateMultiplier : 1f;
        float effectiveInterval = rateMultiplier > 0f ? fireInterval / rateMultiplier : fireInterval;
        fireTimer = Mathf.Max(effectiveInterval, minFireInterval);

        Fire();
    }

    // Phase A: Camera -> AimPoint, then Muzzle -> AimPoint -> AimDirection.
    private void UpdateAim()
    {
        if (aimCamera == null || sprayOrigin == null) return;

        Ray camRay = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 aimPoint;

        if (Physics.Raycast(camRay, out RaycastHit camHit, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore))
        {
            aimPoint = camHit.point;
        }
        else
        {
            aimPoint = camRay.origin + camRay.direction * maxAimDistance;
        }

        Vector3 toAim = aimPoint - sprayOrigin.position;
        Vector3 aimDir = toAim.magnitude < minimumAimDistance ? camRay.direction : toAim.normalized;

        AimPoint = aimPoint;
        AimDirection = aimDir;
    }

    private static void SetEmitting(ParticleSystem ps, bool emitting)
    {
        if (ps == null) return;
        if (emitting && !ps.isEmitting) ps.Play();
        else if (!emitting && ps.isEmitting) ps.Stop();
    }

    // Phase C: hit detection reuses the exact same AimDirection the VFX is
    // facing (Single Source of Truth) - this raycast already naturally
    // respects walls/obstructions between the muzzle and the target (Phase 14).
    void Fire()
    {
        if (sprayOrigin == null) return;

        if (Physics.Raycast(sprayOrigin.position, AimDirection, out RaycastHit sprayHit, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore))
        {
            // Short Impact Splat on whatever was hit - enemy, wall or ground
            // alike. Pooled, not Instantiate/Destroy (see ImpactSplatPool).
            if (impactSplatPool != null) impactSplatPool.PlayImpact(sprayHit.point, sprayHit.normal);

            if (AudioManager.Instance != null && Time.time >= nextSfxTime)
            {
                nextSfxTime = Time.time + sfxCooldown;
                AudioManager.Instance.PlaySprayImpact();
            }

            var enemy = sprayHit.collider.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
            {
                var paintStatus = enemy.GetComponent<EnemyPaintStatus>();

                // A Saturated enemy's next hit triggers Shatter instead of
                // normal damage (Section 10) - Shatter finishes the kill
                // itself through EnemyHealth.Kill().
                if (paintStatus != null && paintStatus.IsSaturated)
                {
                    paintStatus.Shatter();
                }
                else
                {
                    float damageMultiplier = upgradeStats != null ? upgradeStats.SprayDamageMultiplier : 1f;
                    int finalDamage = Mathf.RoundToInt(damage * damageMultiplier);
                    enemy.TakeDamage(finalDamage);
                    if (enemy.IsDead)
                    {
                        Debug.Log($"[ScoreDebug] normal kill - ScoreManager.Instance={(ScoreManager.Instance != null)}");
                        if (ScoreManager.Instance != null) ScoreManager.Instance.AddNormalKill();
                    }
                    if (paintStatus != null) paintStatus.AddPaint(paintPerSprayHit);
                }

                OnEnemyHit?.Invoke();
                return;
            }

            var paintable = sprayHit.collider.GetComponentInParent<PaintableSurface>();
            if (paintable != null && decalPool != null)
            {
                float sizeMultiplier = upgradeStats != null ? upgradeStats.PaintSizeMultiplier : 1f;
                decalPool.PlacePaint(sprayHit.point, sprayHit.normal, sizeMultiplier);
            }
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!drawAimGizmos || aimCamera == null || sprayOrigin == null) return;

        Gizmos.color = Color.cyan;
        Ray camRay = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Gizmos.DrawLine(camRay.origin, camRay.origin + camRay.direction * maxAimDistance);

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(AimPoint, 0.1f);

        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(sprayOrigin.position, AimPoint);
    }
#endif
}