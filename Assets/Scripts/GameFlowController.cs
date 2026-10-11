using ProjectMiro.Platform;
using UnityEngine;

// Single source of truth for "the run has ended" - Clear and Game Over are
// mutually exclusive and each only fire once. Does not replace WaveManager /
// BossController / EnemyAI; it only decides which end-of-run panel wins and
// performs the one shared pause/unlock/lockout sequence.
public class GameFlowController : MonoBehaviour
{
    public static GameFlowController Instance { get; private set; }

    [SerializeField] private PlayerProgression progression;
    [SerializeField] private LevelUpUI levelUpUI;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private WaveManager waveManager;
    [SerializeField] private GameClearUI clearUI;
    [SerializeField] private GameOverUI gameOverUI;
    [SerializeField] private ReviveUI reviveUI;

    private bool ended;
    // One offer per run, whether it's accepted, declined, or the ad itself
    // fails/is cancelled - prevents farming infinite revives by repeatedly
    // dying and re-watching ads.
    private bool reviveOffered;

    public bool IsEnded => ended;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnFatalHit += HandleFatalHit;
            playerHealth.OnDied += HandlePlayerDied;
        }
    }

    void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnFatalHit -= HandleFatalHit;
            playerHealth.OnDied -= HandlePlayerDied;
        }
    }

    // Fires the instant a hit would be fatal, BEFORE death is finalized (see
    // PlayerHealth.OnFatalHit). Offers one rewarded-ad revive per run; every
    // other path (already offered, no ReviveUI wired, Clear already fired)
    // falls straight through to the exact same immediate
    // ResolveFatalHit(false) -> OnDied -> HandlePlayerDied chain that used to
    // run unconditionally, so a project without ReviveUI set up behaves
    // identically to before this feature existed.
    private void HandleFatalHit()
    {
        if (ended || reviveOffered || reviveUI == null)
        {
            playerHealth.ResolveFatalHit(false);
            return;
        }

        reviveOffered = true;
        reviveUI.Show(OnReviveResolved);
    }

    private void OnReviveResolved(bool revived)
    {
        if (playerHealth != null) playerHealth.ResolveFatalHit(revived);
    }

    public void ShowClear()
    {
        if (ended) return;
        ended = true;

        EndCommon();

        int enemiesDefeated = waveManager != null ? waveManager.TotalDied : 0;
        int level = progression != null ? progression.Level : 1;
        int score = ScoreManager.Instance != null ? ScoreManager.Instance.TotalScore : 0;
        if (clearUI != null) clearUI.Show(level, enemiesDefeated, score);
    }

    private void HandlePlayerDied()
    {
        if (ended) return;
        ended = true;

        EndCommon();

        int score = ScoreManager.Instance != null ? ScoreManager.Instance.TotalScore : 0;
        if (gameOverUI != null) gameOverUI.Show(score);
    }

    private void EndCommon()
    {
        if (progression != null) progression.LockOut();
        if (levelUpUI != null) levelUpUI.ForceClose();

        Time.timeScale = 0f;
        GameplayCursor.Unlock();

        PlatformServiceProvider.Current.NotifyGameplayStop();
    }
}
