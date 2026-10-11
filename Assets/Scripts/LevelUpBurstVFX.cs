using UnityEngine;

public class LevelUpBurstVFX : MonoBehaviour
{
    [SerializeField] private PlayerProgression progression;
    [SerializeField] private GameObject cyanBurstPrefab;
    [SerializeField] private GameObject magentaBurstPrefab;

    void Awake()
    {
        if (progression == null) progression = GetComponent<PlayerProgression>();
        if (progression != null) progression.OnLeveledUp += HandleLeveledUp;
    }

    void OnDestroy()
    {
        if (progression != null) progression.OnLeveledUp -= HandleLeveledUp;
    }

    private void HandleLeveledUp()
    {
        if (cyanBurstPrefab != null) Instantiate(cyanBurstPrefab, transform.position, Quaternion.identity);
        if (magentaBurstPrefab != null) Instantiate(magentaBurstPrefab, transform.position, Quaternion.identity);
    }
}
