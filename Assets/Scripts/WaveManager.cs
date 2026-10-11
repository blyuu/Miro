using System.Collections;
using ProjectMiro.Platform;
using UnityEngine;
using UnityEngine.AI;

public class WaveManager : MonoBehaviour
{
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private WaveHUD waveHUD;

    [SerializeField] private GameObject bossPrefab;
    [SerializeField] private Transform bossSpawnPoint;
    [SerializeField] private float bossIntroDelay = 2f;

    // Design targets for simultaneous active enemies on the MainGame subway
    // (a single wave's count is also its simultaneous-alive ceiling, since
    // the next wave doesn't start until this one is fully cleared):
    // early ~4-6, mid ~6-10, later ~8-12.
    [SerializeField] private int[] waveEnemyCounts = { 5, 8, 10 };
    [SerializeField] private float startDelay = 1.5f;
    [SerializeField] private float interWaveDelay = 2f;
    [SerializeField] private float spawnInterval = 0.35f;

    // isOnNavMesh alone doesn't guarantee a spawn can actually reach the
    // Player - the subway's decorative props (boxes/crates/pipes) got swept
    // into the same NavigationStatic bake as the floor, so their tops carry
    // tiny disconnected NavMesh islands. A candidate is only accepted once
    // NavMesh.CalculatePath to the Player comes back PathComplete.
    [SerializeField] private int maxJitterAttemptsPerPoint = 4;
    [SerializeField] private float jitterRadius = 3f;

    private int aliveEnemies;
    private bool allEnemiesSpawned;
    private int lastSpawnPointIndex = -1;
    private bool midgameAdShown;

    private int totalSpawned;
    private int totalDied;

    public int TotalSpawned => totalSpawned;
    public int TotalDied => totalDied;

    void Start()
    {
        if (waveHUD != null) waveHUD.SetWaveProgress(0);
        StartCoroutine(RunLoop());
    }

    private IEnumerator RunLoop()
    {
        yield return new WaitForSeconds(startDelay);

        PlatformServiceProvider.Current.NotifyGameplayStart();
        if (AudioManager.Instance != null) AudioManager.Instance.PlayBattleMusic();

        for (int waveIndex = 0; waveIndex < waveEnemyCounts.Length; waveIndex++)
        {
            if (PlayerIsDead()) yield break;

            if (waveHUD != null) waveHUD.ShowWaveStart(waveIndex + 1);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayWaveStart();

            yield return StartCoroutine(SpawnWave(waveEnemyCounts[waveIndex]));

            while (!(allEnemiesSpawned && aliveEnemies <= 0))
            {
                if (PlayerIsDead()) yield break;
                yield return null;
            }

            if (waveHUD != null) waveHUD.ShowWaveClear();
            if (AudioManager.Instance != null) AudioManager.Instance.PlayWaveClear();

            bool isLastWave = waveIndex == waveEnemyCounts.Length - 1;

            // One midgame ad per run, at the midpoint of the wave count
            // (never on the last wave - that transitions straight into the
            // boss, not a natural ad break). No enemies are alive here (this
            // wave just fully cleared), so this never costs the player an
            // unfair hit. Result is ignored either way - PlatformResult's own
            // timeout guarantees this always resolves.
            if (!midgameAdShown && !isLastWave && waveIndex == (waveEnemyCounts.Length - 1) / 2)
            {
                midgameAdShown = true;
                yield return StartCoroutine(ShowMidgameAdRoutine());
            }

            if (!isLastWave) yield return new WaitForSeconds(interWaveDelay);
        }

        if (PlayerIsDead()) yield break;

        if (waveHUD != null) waveHUD.ShowFlash("BOSS INCOMING", bossIntroDelay);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayBossMusic();
        yield return new WaitForSeconds(bossIntroDelay);

        if (PlayerIsDead()) yield break;

        if (waveHUD != null) waveHUD.SetPanelVisible(false);
        SpawnBoss();
    }

    // Spawns exactly one Boss instance and hands off entirely - BossController
    // and EnemyHealth own everything from here (phases, death, Clear flow).
    private void SpawnBoss()
    {
        if (bossPrefab == null) return;

        Vector3 pos;
        if (bossSpawnPoint == null || !TryGetReachableSpawnPosition(bossSpawnPoint, out pos))
        {
            // Only one designated boss spawn point exists (no fallback list
            // to try, unlike regular waves) - the boss must spawn regardless,
            // so fall back to a plain NavMesh sample rather than skip it.
            pos = bossSpawnPoint != null ? bossSpawnPoint.position : transform.position;
            if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas))
                pos = hit.position;
            Debug.LogWarning("[WaveManager] Boss spawn point has no reachable path to the Player after retries - spawning at best-effort position anyway.");
        }

        var go = Instantiate(bossPrefab, pos, Quaternion.identity);

        var agent = go.GetComponent<NavMeshAgent>();
        if (agent != null && !agent.isOnNavMesh)
            Debug.LogWarning($"[WaveManager] Spawned boss at {pos} is not on the NavMesh - it will not chase.", go);

        var ai = go.GetComponent<EnemyAI>();
        if (ai != null) ai.BeginChase();
    }

    private IEnumerator ShowMidgameAdRoutine()
    {
        bool done = false;
        PlatformServiceProvider.Current.ShowMidgameAd(_ => done = true);
        while (!done) yield return null;
    }

    private bool PlayerIsDead()
    {
        return playerHealth != null && playerHealth.IsDead;
    }

    private IEnumerator SpawnWave(int count)
    {
        allEnemiesSpawned = false;

        for (int i = 0; i < count; i++)
        {
            if (PlayerIsDead())
            {
                allEnemiesSpawned = true;
                yield break;
            }

            SpawnOne();
            yield return new WaitForSeconds(spawnInterval);
        }

        allEnemiesSpawned = true;
    }

    private void SpawnOne()
    {
        if (enemyPrefab == null) return;

        Transform firstPoint = PickSpawnPoint();
        Vector3 pos = default;
        bool found = firstPoint != null && TryGetReachableSpawnPosition(firstPoint, out pos);

        if (!found && spawnPoints != null)
        {
            for (int i = 0; i < spawnPoints.Length && !found; i++)
            {
                if (spawnPoints[i] == null || spawnPoints[i] == firstPoint) continue;
                found = TryGetReachableSpawnPosition(spawnPoints[i], out pos);
            }
        }

        if (!found)
        {
            Debug.LogWarning("[WaveManager] No spawn point produced a position with a complete NavMesh path to the Player - skipping this spawn.");
            return;
        }

        var go = Instantiate(enemyPrefab, pos, Quaternion.identity);

        var agent = go.GetComponent<NavMeshAgent>();
        if (agent != null) agent.avoidancePriority = Random.Range(30, 71);

        var ai = go.GetComponent<EnemyAI>();
        if (ai != null) ai.BeginChase();

        var health = go.GetComponent<EnemyHealth>();
        if (health != null) health.OnDied += HandleEnemyDied;

        aliveEnemies++;
        totalSpawned++;
        if (waveHUD != null) waveHUD.UpdateAliveCount(aliveEnemies);
    }

    private Transform PickSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return null;
        if (spawnPoints.Length == 1) return spawnPoints[0];

        int index;
        do { index = Random.Range(0, spawnPoints.Length); }
        while (index == lastSpawnPointIndex);

        lastSpawnPointIndex = index;
        return spawnPoints[index];
    }

    // isOnNavMesh only proves the sampled point sits on SOME NavMesh surface
    // - it says nothing about whether that surface is the main floor or a
    // disconnected island on top of a box/crate. Jitters a few candidates
    // around the spawn point and only accepts one with a verified
    // PathComplete route to the Player, bounded so this can never loop.
    private bool TryGetReachableSpawnPosition(Transform spawnPoint, out Vector3 result)
    {
        Vector3 playerPos = playerHealth != null ? playerHealth.transform.position : transform.position;
        var path = new NavMeshPath();

        for (int attempt = 0; attempt < maxJitterAttemptsPerPoint; attempt++)
        {
            Vector3 candidate = spawnPoint.position;
            if (attempt > 0)
            {
                Vector2 offset = Random.insideUnitCircle * jitterRadius;
                candidate += new Vector3(offset.x, 0f, offset.y);
            }

            if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas))
                continue;

            if (NavMesh.CalculatePath(hit.position, playerPos, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                result = hit.position;
                return true;
            }
        }

        result = default;
        return false;
    }

    private void HandleEnemyDied(EnemyHealth deadHealth)
    {
        deadHealth.OnDied -= HandleEnemyDied;
        aliveEnemies--;
        totalDied++;
        if (waveHUD != null) waveHUD.UpdateAliveCount(aliveEnemies);
    }
}
