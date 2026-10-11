using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Unified Mouse Hover behaviour for every menu entry (START/CHARACTER/
// SETTINGS/EXIT all use the exact same logic, no per-button special case):
//
// Normal: dark transparent background, pink paint stroke hidden, white text/icon.
// Hover:  pink paint stroke shown, dark background hidden, black text/icon, nudge right.
//
// There is no "selected" state - nothing stays permanently pink. Only one
// button is pink at a time: whichever one the mouse is currently over.
public class LobbyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private TextMeshProUGUI iconText;
    [SerializeField] private Image[] iconGraphics;
    [SerializeField] private GameObject paintStroke;
    [SerializeField] private Image hoverBackground;

    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color hoverColor = Color.black;
    [SerializeField] private Color normalBackgroundColor = new Color(0f, 0f, 0f, 0.18f);
    [SerializeField] private float hoverOffsetX = 10f;

    private RectTransform rt;
    private Vector2 baseAnchoredPos;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        baseAnchoredPos = rt.anchoredPosition;
        ApplyNormal();
    }

    private void ApplyNormal()
    {
        SetContentColor(normalColor);
        if (paintStroke != null) paintStroke.SetActive(false);
        if (hoverBackground != null)
        {
            hoverBackground.gameObject.SetActive(true);
            hoverBackground.color = normalBackgroundColor;
        }
        rt.anchoredPosition = baseAnchoredPos;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetContentColor(hoverColor);
        if (paintStroke != null) paintStroke.SetActive(true);
        if (hoverBackground != null) hoverBackground.gameObject.SetActive(false);
        rt.anchoredPosition = baseAnchoredPos + new Vector2(hoverOffsetX, 0f);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUiHover();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ApplyNormal();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUiClick();
    }

    private void SetContentColor(Color color)
    {
        if (label != null) label.color = color;
        if (iconText != null) iconText.color = color;
        if (iconGraphics != null)
        {
            for (int i = 0; i < iconGraphics.Length; i++)
            {
                if (iconGraphics[i] != null) iconGraphics[i].color = color;
            }
        }
    }
}
