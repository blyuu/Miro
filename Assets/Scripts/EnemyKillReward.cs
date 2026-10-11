using UnityEngine;

public class EnemyKillReward : MonoBehaviour
{
    [SerializeField] private EnemyHealth health;
    [SerializeField] private int paintEnergyReward = 10;
    [SerializeField] private GameObject deathBurstPrefab;

    private PlayerProgression progression;

    void Awake()
    {
        if (health == null) health = GetComponent<EnemyHealth>();

        var player = GameObject.Find("Player");
        if (player != null) progression = player.GetComponent<PlayerProgression>();

        if (health != null) health.OnDied += HandleDied;
    }

    void OnDestroy()
    {
        if (health != null) health.OnDied -= HandleDied;
    }

    private void HandleDied(EnemyHealth deadHealth)
    {
        if (progression != null) progression.AddEnergy(paintEnergyReward);
        if (deathBurstPrefab != null) Instantiate(deathBurstPrefab, transform.position, Quaternion.identity);
    }
}