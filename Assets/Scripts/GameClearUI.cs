using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class GameClearUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI summaryText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button mainMenuButton;

    // Guards against a double-click firing two overlapping scene loads.
    private bool _transitioning;

    // panel starts inactive in the saved scene already - not deactivated here
    // (see LevelUpUI/BossHUD: doing so from Awake() on a panel that IS this
    // script's own GameObject fires reentrantly from Show()'s SetActive(true)
    // and immediately undoes it).
    void Awake()
    {
        if (retryButton != null) retryButton.onClick.AddListener(Retry);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GoToMainMenu);
    }

    public void Show(int level, int enemiesDefeated, int score)
    {
        if (summaryText != null)
            summaryText.text = "LEVEL " + level + "\nENEMIES DEFEATED " + enemiesDefeated + "\nSCORE " + score + "\nBOSS DEFEATED";

        if (panel != null) panel.SetActive(true);
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayGameClear();
            AudioManager.Instance.PlayVictoryMusic();
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
