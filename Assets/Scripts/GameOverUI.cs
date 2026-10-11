using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI summaryText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button mainMenuButton;

    // Guards against a double-click firing two overlapping scene loads.
    private bool _transitioning;

    // panel starts inactive in the saved scene already - see GameClearUI/
    // LevelUpUI/BossHUD for why it must not be deactivated again from Awake().
    void Awake()
    {
        if (retryButton != null) retryButton.onClick.AddListener(Retry);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
    }

    public void Show(int score)
    {
        if (summaryText != null) summaryText.text = "SCORE " + score;
        if (panel != null) panel.SetActive(true);
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayGameOver();
            AudioManager.Instance.PlayGameOverMusic();
        }
    }

    private void Retry()
    {
        if (_transitioning) return;
        _transitioning = true;

        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void GoToMainMenu()
    {
        if (_transitioning) return;
        _transitioning = true;

        Time.timeScale = 1f;
        GameplayCursor.Unlock(); // GameFlowController.EndCommon() already did this - idempotent safety net.
        SceneManager.LoadScene("LobbyScene");
    }
}
