using UnityEngine;
using UnityEngine.UI;

// Only one playable character exists (MIRO), so SELECT and BACK do the same
// thing - close the panel and return to the Main Menu. Start already always
// loads MIRO regardless; this screen exists purely so the Lobby's CHARACTER
// button leads somewhere real instead of a stub log.
public class CharacterSelectPanelController : MonoBehaviour
{
    [SerializeField] private Button selectButton;
    [SerializeField] private Button backButton;

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private GameObject mainMenuRoot;

    void OnEnable()
    {
        if (selectButton != null) selectButton.onClick.AddListener(OnSelectClicked);
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
    }

    void OnDisable()
    {
        if (selectButton != null) selectButton.onClick.RemoveListener(OnSelectClicked);
        if (backButton != null) backButton.onClick.RemoveListener(OnBackClicked);
    }

    private void OnSelectClicked()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUiClick();
        ReturnToMainMenu();
    }

    private void OnBackClicked()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUiPanelClose();
        ReturnToMainMenu();
    }

    private void ReturnToMainMenu()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (mainMenuRoot != null) mainMenuRoot.SetActive(true);
    }
}
