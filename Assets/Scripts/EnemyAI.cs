using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    private enum State { Idle, Chase, Attack, Dead }

    [SerializeField] private Transform target;
    [SerializeField] private Animator animator;
    [SerializeField] private EnemyHealth health;

    [SerializeField] private float detectionRange = 12f;
    [SerializeField] private float attackRange = 1.8f;
    [SerializeField] private float stoppingDistance = 1.5f;
    [SerializeField] private float moveSpeed = 3.2f;
    [SerializeField] private float attackCooldown = 1.1f;
    [SerializeField] private int attackDamage = 10;
    [SerializeField] private float attackHitNormalizedTime = 0.5f;
    [SerializeField] private float rotationSpeed = 10f;

    // Dedicated 3D-spatial AudioSource (separate from AudioManager's shared
    // flat sfxSource) so movement/combat cues actually attenuate with
    // distance and give some left/right/behind positioning - important for
    // "can the player notice an off-screen attack" per the brief. Volume is
    // still scaled by AudioManager.EffectiveSfxGain so the Master/SFX sliders
    // apply the same as everywhere else.
    [SerializeField] private AudioSource positionalAudioSource;

    [Header("Movement SFX (quiet, distance + rate limited)")]
    [SerializeField] private AudioClip[] moveClips;
    [SerializeField] private float moveVolume = 0.18f;
    [SerializeField] private float movePitchJitter = 0.05f;
    [SerializeField] private float moveStepInterval = 0.55f;
    [SerializeField] private float moveAudibleDistance = 18f;
    [SerializeField] private float moveSpeedThreshold = 0.3f;
    // Shared across every EnemyAI instance - without this, a 5-10 enemy wave
    // all mid-step on the same frame would stack into a volume spike even
    // though each enemy's own cue is quiet.
    [SerializeField] private float globalMoveSfxCooldown = 0.12f;
    private static float nextGlobalMoveSfxTime;
    private float moveSfxTimer;

    [Header("Combat SFX")]
    [SerializeField] private AudioClip windupClip;
    [SerializeField] private AudioClip[] attackClips;
    [SerializeField] private float windupVolume = 0.35f;
    [SerializeField] private float attackVolume = 0.5f;
    [SerializeField] private float combatPitchJitter = 0.05f;
    // Lower pitch reads as a heavier anticipation cue without needing a
    // separate low-pitched recording.
    [SerializeField] private float windupBasePitch = 0.85f;

    private NavMeshAgent agent;
    private State state = State.Idle;
    private float cooldownTimer;
    private bool pendingHit;
    private bool hitDelivered;

    // Runtime modifiers for boss phase transitions - applied multiplicatively
    // on top of the base-designed values above, no new framework needed.
    private float moveSpeedMultiplier = 1f;
    private float attackCooldownMultiplier = 1f;
    private float damageMultiplier = 1f;

    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly int AttackParam = Animator.StringToHash("Attack");
    private static readonly int DeadParam = Animator.StringToHash("Dead");

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.speed = moveSpeed;
        agent.stoppingDistance = stoppingDistance;

        if (health == null) health = GetComponent<EnemyHealth>();

        if (target == null)
        {
            var player = GameObject.Find("Player");
            if (player != null) target = player.transform;
        }
    }

    // Called by WaveManager right after a wave-spawned enemy is placed on
    // the NavMesh. Wave spawns are a deliberate, timed encounter (not a
    // stealth ambush) and can land anywhere from a few meters to well over
    // 100m from the player on the subway's scale, far past detectionRange -
    // so without this they would sit in Idle forever waiting for the player
    // to wander into range. Skips straight to Chase regardless of distance.
    public void BeginChase()
    {
        if (state == State.Idle) state = State.Chase;
    }

    void Update()
    {
        if (state == State.Dead) return;

        if (health != null && health.IsDead)
        {
            EnterDead();
            return;
        }

        if (target == null || animator == null) return;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        switch (state)
        {
            case State.Idle:
                animator.SetFloat(SpeedParam, 0f);
                if (distance <= detectionRange)
                {
                    state = State.Chase;
                }
                break;

            case State.Chase:
                if (distance <= attackRange)
                {
                    state = State.Attack;
                    agent.isStopped = true;
                    // Small random offset so enemies entering Attack in the same
                    // frame (e.g. a wave crowd) don't all land their first hit
                    // simultaneously.
                    cooldownTimer = Random.Range(0f, 0.35f) * attackCooldownMultiplier;
                    break;
                }

                agent.isStopped = false;
                agent.SetDestination(target.position);
                animator.SetFloat(SpeedParam, agent.velocity.magnitude);
                FaceDirection(agent.velocity.sqrMagnitude > 0.01f ? agent.velocity : toTarget);
                UpdateMovementAudio(distance);
                break;

            case State.Attack:
                agent.isStopped = true;
                animator.SetFloat(SpeedParam, 0f);
                FaceDirection(toTarget);

                if (distance > attackRange * 1.2f)
                {
                    state = State.Chase;
                    agent.isStopped = false;
                    break;
                }

                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f)
                {
                    cooldownTimer = attackCooldown * attackCooldownMultiplier;
                    animator.SetTrigger(AttackParam);
                    pendingHit = true;
                    hitDelivered = false;
                    PlayWindupSfx();
                }

                if (pendingHit)
                {
                    var info = animator.GetCurrentAnimatorStateInfo(0);
                    if (info.IsName("Attack") && !hitDelivered && info.normalizedTime >= attackHitNormalizedTime)
                    {
                        hitDelivered = true;
                        pendingHit = false;
                        PlayAttackSfx();

                        Vector3 toTargetNow = target.position - transform.position;
                        toTargetNow.y = 0f;
                        if (toTargetNow.magnitude <= attackRange)
                        {
                            var playerHealth = target.GetComponent<PlayerHealth>();
                            if (playerHealth != null) playerHealth.TakeDamage(Mathf.RoundToInt(attackDamage * damageMultiplier));
                        }
                    }
                }
                break;
        }
    }

    private void UpdateMovementAudio(float distanceToPlayer)
    {
        if (positionalAudioSource == null || moveClips == null || moveClips.Length == 0) return;
        if (distanceToPlayer > moveAudibleDistance) return;
        if (agent.velocity.magnitude < moveSpeedThreshold) return;
        if (Time.timeScale <= 0f) return;

        moveSfxTimer -= Time.deltaTime;
        if (moveSfxTimer > 0f) return;
        if (Time.time < nextGlobalMoveSfxTime) return;

        moveSfxTimer = moveStepInterval;
        nextGlobalMoveSfxTime = Time.time + globalMoveSfxCooldown;

        float gain = AudioManager.Instance != null ? AudioManager.Instance.EffectiveSfxGain : 1f;
        positionalAudioSource.pitch = 1f + Random.Range(-movePitchJitter, movePitchJitter);
        positionalAudioSource.PlayOneShot(moveClips[Random.Range(0, moveClips.Length)], moveVolume * gain);
    }

    private void PlayWindupSfx()
    {
        if (positionalAudioSource == null || windupClip == null) return;

        float gain = AudioManager.Instance != null ? AudioManager.Instance.EffectiveSfxGain : 1f;
        positionalAudioSource.pitch = windupBasePitch + Random.Range(-combatPitchJitter, combatPitchJitter);
        positionalAudioSource.PlayOneShot(windupClip, windupVolume * gain);
    }

    private void PlayAttackSfx()
    {
        if (positionalAudioSource == null || attackClips == null || attackClips.Length == 0) return;

        float gain = AudioManager.Instance != null ? AudioManager.Instance.EffectiveSfxGain : 1f;
        positionalAudioSource.pitch = 1f + Random.Range(-combatPitchJitter, combatPitchJitter);
        positionalAudioSource.PlayOneShot(attackClips[Random.Range(0, attackClips.Length)], attackVolume * gain);
    }

    private void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    public void SetMoveSpeedMultiplier(float multiplier)
    {
        moveSpeedMultiplier = multiplier;
        agent.speed = moveSpeed * moveSpeedMultiplier;
    }

    public void SetAttackCooldownMultiplier(float multiplier) => attackCooldownMultiplier = multiplier;

    public void SetDamageMultiplier(float multiplier) => damageMultiplier = multiplier;

    private void EnterDead()
    {
        state = State.Dead;
        agent.isStopped = true;
        if (animator != null) animator.SetBool(DeadParam, true);
    }
}