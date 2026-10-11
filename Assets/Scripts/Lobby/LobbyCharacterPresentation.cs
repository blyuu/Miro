using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Miro is otherwise completely static (no idle motion). On mouse hover over
// the actual artwork (alpha hit-testing on the Image handles the hit area -
// see MiroCharacter's Image.alphaHitTestMinimumThreshold), the whole poster
// scales up slightly and eases back down on exit. Scale only - no rotation,
// floating, breathing or parallax.
public class LobbyCharacterPresentation : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private float normalScale = 1f;
    [SerializeField] private float hoverScale = 1.04f;
    [SerializeField] private float transitionDuration = 0.2f;

    private RectTransform rt;
    private Coroutine scaleRoutine;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        rt.localScale = Vector3.one * normalScale;
    }

    public void OnPointerEnter(PointerEventData eventData) => StartScale(hoverScale);

    public void OnPointerExit(PointerEventData eventData) => StartScale(normalScale);

    private void StartScale(float target)
    {
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        scaleRoutine = StartCoroutine(ScaleRoutine(target));
    }

    private IEnumerator ScaleRoutine(float target)
    {
        float start = rt.localScale.x;
        float t = 0f;
        while (t < transitionDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / transitionDuration);
            p = 1f - (1f - p) * (1f - p); // ease-out
            rt.localScale = Vector3.one * Mathf.Lerp(start, target, p);
            yield return null;
        }
        rt.localScale = Vector3.one * target;
        scaleRoutine = null;
    }
}
