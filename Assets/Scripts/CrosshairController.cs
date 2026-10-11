using System.Collections;
using UnityEngine;

// Minimal PAINTPUNK reticle feedback. The Crosshair GameObject's own Image
// IS the center dot (no extra wrapper); four Tick children spread outward a
// few pixels while spraying, and the center dot gets a very short pop on a
// confirmed enemy hit. Nothing here is gameplay-affecting - pure Canvas UI.
public class CrosshairController : MonoBehaviour
{
    [SerializeField] private MiroInputReader input;
    [SerializeField] private PlayerSpray spray;

    [SerializeField] private RectTransform centerDot;
    [SerializeField] private RectTransform tickTop;
    [SerializeField] private RectTransform tickBottom;
    [SerializeField] private RectTransform tickLeft;
    [SerializeField] private RectTransform tickRight;

    [SerializeField] private float normalTickDistance = 6f;
    [SerializeField] private float sprayTickDistance = 11f;
    [SerializeField] private float spreadSpeed = 60f; // px/sec

    [SerializeField] private float hitFlashScale = 1.15f;
    [SerializeField] private float hitFlashDuration = 0.1f;

    private float currentTickDistance;
    private Coroutine hitFlashRoutine;

    void Awake()
    {
        if (centerDot == null) centerDot = GetComponent<RectTransform>();
        currentTickDistance = normalTickDistance;

        if (input == null || spray == null)
        {
            var player = GameObject.Find("Player");
            if (player != null)
            {
                if (input == null) input = player.GetComponent<MiroInputReader>();
                if (spray == null) spray = player.GetComponent<PlayerSpray>();
            }
        }
    }

    void OnEnable()
    {
        if (spray != null) spray.OnEnemyHit += HandleEnemyHit;
    }

    void OnDisable()
    {
        if (spray != null) spray.OnEnemyHit -= HandleEnemyHit;
    }

    void Update()
    {
        bool spraying = input != null && input.SprayHeld;
        float target = spraying ? sprayTickDistance : normalTickDistance;
        currentTickDistance = Mathf.MoveTowards(currentTickDistance, target, spreadSpeed * Time.deltaTime);

        SetTick(tickTop, Vector2.up);
        SetTick(tickBottom, Vector2.down);
        SetTick(tickLeft, Vector2.left);
        SetTick(tickRight, Vector2.right);
    }

    private void SetTick(RectTransform tick, Vector2 direction)
    {
        if (tick == null) return;
        tick.anchoredPosition = direction * currentTickDistance;
    }

    private void HandleEnemyHit()
    {
        if (centerDot == null) return;
        if (hitFlashRoutine != null) StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = StartCoroutine(HitFlashRoutine());
    }

    private IEnumerator HitFlashRoutine()
    {
        float t = 0f;
        while (t < hitFlashDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = t / hitFlashDuration;
            float s = p < 0.5f ? Mathf.Lerp(1f, hitFlashScale, p / 0.5f) : Mathf.Lerp(hitFlashScale, 1f, (p - 0.5f) / 0.5f);
            centerDot.localScale = Vector3.one * s;
            yield return null;
        }
        centerDot.localScale = Vector3.one;
        hitFlashRoutine = null;
    }
}
