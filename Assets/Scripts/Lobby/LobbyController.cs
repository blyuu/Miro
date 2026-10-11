using UnityEngine;
using UnityEngine.SceneManagement;

// Entry point for the main lobby. Only talks to Unity's SceneManager,
// AudioManager and the two sub-panels below - no gameplay systems are
// referenced here, keeping the lobby fully independent of the Gameplay
// scene/scripts.
public class LobbyController : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "MainGame";

    [SerializeField] private GameObject mainMenuRoot;
    [SerializeField] private GameObject characterSelectPanel;
    [SerializeField] private GameObject settingsPanel;

    void Start()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayLobbyMusic();
    }

    public void OnStartClicked()
    {
        // Must be the very first thing this method does: browsers only honor
        // a Fullscreen API request made synchronously within a trusted user
        // gesture's own call stack. Calling it here (before anything else,
        // definitely before any scene load) keeps it inside that same click,
        // where calling it "late" - e.g. after SceneManager.LoadScene - would
        // already be a different call stack and get silently rejected. PC
        // never requests fullscreen (FullscreenBridge itself also no-ops
        // outside WebGL); a failed/rejected/unavailable request here (no
        // fullscreen support, inside a restrictive iframe, etc.) never blocks
        // the line below - game start is never gated on fullscreen succeeding.
        if (PlatformInputDetector.IsMobileBrowser) FullscreenBridge.RequestFullscreen();

        SceneManager.LoadScene(gameplaySceneName);
    }

    public void OnCharacterClicked()
    {
        OpenPanel(characterSelectPanel);
    }

    public void OnSettingsClicked()
    {
        OpenPanel(settingsPanel);
    }

    public void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OpenPanel(GameObject panel)
    {
        if (panel == null) return;

        if (mainMenuRoot != null) mainMenuRoot.SetActive(false);
        panel.SetActive(true);

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUiPanelOpen();
    }
}
