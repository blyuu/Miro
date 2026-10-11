using System.Collections;
using UnityEngine;

// Small isolated Chain feedback (Section 14) - kept separate from the
// existing WaveHUD/BigFlashText so it can pop independently without
// fighting wave-start/clear messages for the same label.
public class ChainShatterUI : MonoBehaviour
{
    [SerializeField] private ChainShatterTracker tracker;
    [SerializeField] private TMPro.TextMeshProUGUI label;
    [SerializeField] private float displayDuration = 1.2f;
    [SerializeField] private float popScale = 1.2f;
    [SerializeField] private float popDuration = 0.15f;

    private Coroutine routine;

    void Awake()
    {
        if (label != null) label.gameObject.SetActive(false);
    }

    // Start (not Awake/OnEnable) so ChainShatterTracker.Instance is
    // guaranteed set regardless of scene script execution order.
    void Start()
    {
        if (tracker == null) tracker = ChainShatterTracker.Instance;
        if (tracker != null) tracker.OnChainUpdated += HandleChainUpdated;
    }

    void OnDestroy()
    {
        if (tracker != null) tracker.OnChainUpdated -= HandleChainUpdated;
    }

    private void HandleChainUpdated(int count)
    {
        if (label == null) return;
        label.text = count <= 1 ? "SHATTER" : ("CHAIN x" + count);

        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(ShowRoutine());
    }

    private IEnumerator ShowRoutine()
    {
        label.gameObject.SetActive(true);
        label.transform.localScale = Vector3.one * popScale;

        float t = 0f;
        while (t < popDuration)
        {
            t += Time.unscaledDeltaTime;
            label.transform.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, t / popDuration);
            yield return null;
        }
        label.transform.localScale = Vector3.one;

        yield return new WaitForSeconds(displayDuration);

        label.gameObject.SetActive(false);
        routine = null;
    }
}
