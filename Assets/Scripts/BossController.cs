using System.Linq;
using UnityEngine;

// Boss-specific orchestration on top of the existing EnemyAI/EnemyHealth -
// does not replace or duplicate their logic, only watches HP for the one-time
// Phase 2 transition and reacts to death. No separate AI framework.
public class BossController : MonoBehaviour
{
    [SerializeField] private string bossName = "CHROMA GUARDIAN";
    [SerializeField] private float phase2HealthRatio = 0.5f;
    [SerializeField] private float phase2MoveSpeedMultiplier = 1.2f;
    [SerializeField] private float phase2AttackCooldownMultiplier = 0.8f;
    [SerializeField] private float phase2DamageMultiplier = 1.25f;
    [SerializeField] private GameObject enrageBurstPrefab;

    private static readonly int BaseColorFactorId = Shader.PropertyToID("baseColorFactor");

    private EnemyHealth health;
    private EnemyAI ai;
    private BossHUD bossHUD;
    private bool phase2Triggered;

    // Exposes the tint ApplyBossTint() already computes so EnemyPaintStatus can use it as the
    // Paint Gauge's 0%-painted baseline instead of overwriting this accent outright.
    public Color AccentColor { get; private set; }

    void Awake()
    {
        health = GetComponent<EnemyHealth>();
        ai = GetComponent<EnemyAI>();

        // BossHUD's panel starts inactive in the saved scene, so plain
        // GameObject.Find("BossHUD") would silently return null here.
        bossHUD = FindInactiveByName("BossHUD")?.GetComponent<BossHUD>();

        if (health != null) health.OnDied += HandleDied;

        ApplyBossTint();
    }

    // One-time Magenta tint blended into the existing skin color, via its own
    // MaterialPropertyBlock property (baseColorFactor) - deliberately NOT
    // emissiveFactor, which EnemyHealth's hit-flash owns exclusively, so the
    // two never fight over the same override slot.
    private void ApplyBossTint()
    {
        var renderer = GetComponentInChildren<SkinnedMeshRenderer>();
        if (renderer == null || renderer.sharedMaterial == null) return;

        Color original = renderer.sharedMaterial.GetColor(BaseColorFactorId);
        Color tinted = Color.Lerp(original, new Color(1f, 0.1f, 0.55f), 0.35f);
        AccentColor = tinted;

        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor(BaseColorFactorId, tinted);
        renderer.SetPropertyBlock(block);
    }

    void Start()
    {
        if (bossHUD != null) bossHUD.Show(health, bossName);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayBossAppear();
    }

    void OnDestroy()
    {
        if (health != null) health.OnDied -= HandleDied;
    }

    void Update()
    {
        if (phase2Triggered || health == null || health.IsDead) return;

        if (health.CurrentHealth <= health.MaxHealth * phase2HealthRatio)
        {
            TriggerPhase2();
        }
    }

    private void TriggerPhase2()
    {
        phase2Triggered = true;

        if (AudioManager.Instance != null) AudioManager.Instance.PlayBossPhase2();

        if (ai != null)
        {
            ai.SetMoveSpeedMultiplier(phase2MoveSpeedMultiplier);
            ai.SetAttackCooldownMultiplier(phase2AttackCooldownMultiplier);
            ai.SetDamageMultiplier(phase2DamageMultiplier);
        }

        if (enrageBurstPrefab != null) Instantiate(enrageBurstPrefab, transform.position, Quaternion.identity);

        var hudGo = GameObject.Find("WaveHUD");
        if (hudGo != null) hudGo.GetComponent<WaveHUD>()?.ShowFlash("ENRAGED", 1.5f);
    }

    private static GameObject FindInactiveByName(string name)
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.name == name && go.scene.IsValid()) return go;
        }
        return null;
    }

    private void HandleDied(EnemyHealth deadHealth)
    {
        if (bossHUD != null) bossHUD.Hide();
        if (AudioManager.Instance != null) AudioManager.Instance.PlayBossDefeat();
        if (ScoreManager.Instance != null) ScoreManager.Instance.AddBossKill();

        if (enrageBurstPrefab != null)
        {
            // A bit bigger than the enrage/normal-enemy burst, per spec, so
            // the boss kill reads as a bigger moment - same prefab, no new VFX.
            var burst = Instantiate(enrageBurstPrefab, transform.position, Quaternion.identity);
            burst.transform.localScale *= 1.5f;
        }

        if (GameFlowController.Instance != null) GameFlowController.Instance.ShowClear();
    }
}
