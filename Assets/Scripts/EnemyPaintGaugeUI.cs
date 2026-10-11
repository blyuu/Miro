using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Small world-space gauge above an enemy's head (Section 9) - deliberately
// dumb: it only visualizes EnemyPaintStatus, it never changes Paint state
// itself. Shown while Paint is non-zero / was just touched, then hidden
// again after a short delay to stay subtle.
public class EnemyPaintGaugeUI : MonoBehaviour
{
    [SerializeField] private EnemyPaintStatus paintStatus;
    [SerializeField] private Image fillImage;
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color saturatedColor = new Color(0.1f, 0.95f, 1f, 1f);
    [SerializeField] private float hideDelay = 2f;

    private Canvas canvas;
    private Camera facingCamera;
    private Coroutine hideRoutine;

    void Awake()
    {
        canvas = GetComponent<Canvas>();
        if (paintStatus == null) paintStatus = GetComponentInParent<EnemyPaintStatus>();
        facingCamera = Camera.main;
        if (canvas != null) canvas.enabled = false;
        if (fillImage != null) fillImage.fillAmount = 0f;
    }

    void OnEnable()
    {
        if (paintStatus != null)
        {
            paintStatus.OnPaintChanged += HandlePaintChanged;
            paintStatus.OnSaturated += HandleSaturated;
        }
    }

    void OnDisable()
    {
        if (paintStatus != null)
        {
            paintStatus.OnPaintChanged -= HandlePaintChanged;
            paintStatus.OnSaturated -= HandleSaturated;
        }
    }

    void LateUpdate()
    {
        if (canvas == null || !canvas.enabled) return;
        if (facingCamera == null) facingCamera = Camera.main;
        if (facingCamera == null) return;
        transform.rotation = Quaternion.LookRotation(transform.position - facingCamera.transform.position);
    }

    private void HandlePaintChanged(float current, float max)
    {
        if (fillImage != null) fillImage.fillAmount = max > 0f ? current / max : 0f;

        if (canvas != null) canvas.enabled = true;
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private void HandleSaturated()
    {
        if (fillImage != null) fillImage.color = saturatedColor;
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(hideDelay);
        if (canvas != null) canvas.enabled = false;
        hideRoutine = null;
    }
}
