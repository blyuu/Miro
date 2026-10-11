using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WaveHUD : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI waveText;
    [SerializeField] private TextMeshProUGUI enemiesText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI bigFlashText;
    [SerializeField] private Image panelBackground;
    [SerializeField] private int totalWaves = 3;

    private Coroutine flashRoutine;

    // Start(), not OnEnable(): Unity only guarantees every object's Awake()
    // has run before ANY object's Start() runs scene-wide - Awake-before-
    // OnEnable has no such cross-object guarantee, so subscribing here in
    // OnEnable() could silently run before ScoreManager.Awake() has set
    // Instance (no error either way - the null-check just skips the
    // subscribe), permanently missing every future OnScoreChanged after the
    // one-time initial read below. [ScoreDebug]
    void Start()
    {
        Debug.Log($"[ScoreDebug] WaveHUD.Start() - ScoreManager.Instance={(ScoreManager.Instance != null)}, scoreText={(scoreText != null)}");
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged += UpdateScore;
            UpdateScore(ScoreManager.Instance.TotalScore);
        }
        else
        {
            UpdateScore(0);
        }
    }

    void OnDisable()
    {
        if (ScoreManager.Instance != null) ScoreManager.Instance.OnScoreChanged -= UpdateScore;
    }

    public void UpdateScore(int total)
    {
        Debug.Log($"[ScoreDebug] WaveHUD.UpdateScore({total}) scoreText={(scoreText != null)}");
        if (scoreText != null) scoreText.text = "SCORE " + total;
    }

    public void SetWaveProgress(int waveNumber)
    {
        // Rich-text size/color split only (visual hierarchy) - the "WAVE"
        // word itself now comes from the Wave_Label artwork image, so this
        // field only holds the number/total. Wave number reads big and
        // magenta (no bar to carry an accent color here); "/total" stays
        // small and white. No behavior change.
        if (waveText != null) waveText.text = "<size=170%><color=#FF1A8C>" + waveNumber + "</color></size><size=75%>/" + totalWaves + "</size>";
    }

    public void UpdateAliveCount(int count)
    {
        if (enemiesText != null) enemiesText.text = "ENEMIES ×" + Mathf.Max(count, 0);
    }

    public void ShowWaveStart(int waveNumber)
    {
        SetWaveProgress(waveNumber);
        ShowFlash("WAVE " + waveNumber, 1f);
    }

    public void ShowWaveClear()
    {
        ShowFlash("WAVE CLEAR", 1f);
    }

    // Hides only the small WAVE/ENEMIES readout - WaveHUD's own GameObject
    // stays active so ShowFlash() can keep running coroutines during the
    // Boss fight (the shared flash banner is a separate sibling object).
    public void SetPanelVisible(bool visible)
    {
        if (waveText != null) waveText.gameObject.SetActive(visible);
        if (enemiesText != null) enemiesText.gameObject.SetActive(visible);
        if (panelBackground != null) panelBackground.enabled = visible;
    }

    // Shared flash banner, also reused by Boss intro/enrage feedback so no
    // second flash-text object needs to exist.
    public void ShowFlash(string text, float duration)
    {
        if (bigFlashText == null) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine(text, duration));
    }

    private IEnumerator FlashRoutine(string text, float duration)
    {
        bigFlashText.text = text;
        bigFlashText.gameObject.SetActive(true);
        yield return new WaitForSeconds(duration);
        bigFlashText.gameObject.SetActive(false);
    }
}
