using System.Collections;
using UnityEngine;

// Lightweight scale-punch animation for UI elements. Runs on unscaledDeltaTime
// so it still plays while Time.timeScale == 0 (e.g. during the Level Up pause).
public class UIJuice : MonoBehaviour
{
    private RectTransform rt;
    private Vector3 baseScale;
    private Coroutine routine;
    private bool initialized;

    void Awake()
    {
        EnsureInit();
    }

    // GameObjects added while their hierarchy was inactive defer Awake() until
    // the first SetActive(true) - which is exactly when PlayAppear() tends to
    // be called from. Guard here so we never depend on Awake() ordering.
    private void EnsureInit()
    {
        if (initialized) return;
        rt = GetComponent<RectTransform>();
        baseScale = rt.localScale;
        initialized = true;
    }

    public void PlayAppear(float delay = 0f)
    {
        EnsureInit();
        if (!gameObject.activeInHierarchy) return;
        Restart(AppearRoutine(delay));
    }

    // Quick overshoot-then-settle punch for a confirmed selection (e.g. an
    // upgrade card the player just chose) - same curve shape as AppearRoutine
    // but starting from the current scale instead of zero.
    public void PlayPop()
    {
        EnsureInit();
        if (!gameObject.activeInHierarchy) return;
        Restart(PopRoutine());
    }

    private void Restart(IEnumerator r)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(r);
    }

    private IEnumerator AppearRoutine(float delay)
    {
        rt.localScale = Vector3.zero;
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);

        float t = 0f;
        const float dur = 0.25f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / dur);
            float scale = p < 0.6f
                ? Mathf.Lerp(0f, 1.08f, p / 0.6f)
                : Mathf.Lerp(1.08f, 1f, (p - 0.6f) / 0.4f);
            rt.localScale = baseScale * scale;
            yield return null;
        }
        rt.localScale = baseScale;
    }

    private IEnumerator PopRoutine()
    {
        float t = 0f;
        const float dur = 0.2f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / dur);
            float scale = p < 0.5f
                ? Mathf.Lerp(1f, 1.15f, p / 0.5f)
                : Mathf.Lerp(1.15f, 1f, (p - 0.5f) / 0.5f);
            rt.localScale = baseScale * scale;
            yield return null;
        }
        rt.localScale = baseScale;
    }
}
