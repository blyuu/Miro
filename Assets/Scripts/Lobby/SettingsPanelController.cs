using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Drives the Settings panel's 4 sliders (Master/Music/SFX volume + Mouse
// Sensitivity). Volume sliders apply live via AudioManager; sensitivity is
// persisted the same way (PlayerPrefs via GameSettings) and only takes
// effect the next time MainGame loads (PlayerAimCamera applies it on Awake),
// since the Cinemachine rig it scales lives in that scene, not the Lobby.
public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider sensitivitySlider;

    [SerializeField] private TextMeshProUGUI masterValueText;
    [SerializeField] private TextMeshProUGUI musicValueText;
    [SerializeField] private TextMeshProUGUI sfxValueText;
    [SerializeField] private TextMeshProUGUI sensitivityValueText;

    [SerializeField] private Button resetButton;
    [SerializeField] private Button backButton;

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private GameObject mainMenuRoot;

    void OnEnable()
    {
        Debug.Log("[DIAG] SettingsPanelController.OnEnable: start");
        RefreshFromSaved();
        Debug.Log("[DIAG] SettingsPanelController.OnEnable: RefreshFromSaved done");

        masterSlider.onValueChanged.AddListener(OnMasterChanged);
        musicSlider.onValueChanged.AddListener(OnMusicChanged);
        sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        if (resetButton != null) resetButton.onClick.AddListener(OnResetClicked);
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
        Debug.Log("[DIAG] SettingsPanelController.OnEnable: listeners attached, end");
    }

    void OnDisable()
    {
        masterSlider.onValueChanged.RemoveListener(OnMasterChanged);
        musicSlider.onValueChanged.RemoveListener(OnMusicChanged);
        sfxSlider.onValueChanged.RemoveListener(OnSfxChanged);
        sensitivitySlider.onValueChanged.RemoveListener(OnSensitivityChanged);
        if (resetButton != null) resetButton.onClick.RemoveListener(OnResetClicked);
        if (backButton != null) backButton.onClick.RemoveListener(OnBackClicked);
    }

    private void RefreshFromSaved()
    {
        Debug.Log("[DIAG] RefreshFromSaved: start");
        var am = AudioManager.Instance;
        Debug.Log("[DIAG] RefreshFromSaved: got AudioManager.Instance = " + (am != null));
        float master = am != null ? am.GetMasterVolume() : AudioManager.DefaultVolume;
        Debug.Log("[DIAG] RefreshFromSaved: master=" + master);
        float music = am != null ? am.GetMusicVolume() : AudioManager.DefaultVolume;
        Debug.Log("[DIAG] RefreshFromSaved: music=" + music);
        float sfx = am != null ? am.GetSfxVolume() : AudioManager.DefaultVolume;
        Debug.Log("[DIAG] RefreshFromSaved: sfx=" + sfx);
        float sensitivity = GameSettings.MouseSensitivity;
        Debug.Log("[DIAG] RefreshFromSaved: sensitivity=" + sensitivity);

        masterSlider.SetValueWithoutNotify(master);
        Debug.Log("[DIAG] RefreshFromSaved: masterSlider set");
        musicSlider.SetValueWithoutNotify(music);
        Debug.Log("[DIAG] RefreshFromSaved: musicSlider set");
        sfxSlider.SetValueWithoutNotify(sfx);
        Debug.Log("[DIAG] RefreshFromSaved: sfxSlider set");
        sensitivitySlider.SetValueWithoutNotify(sensitivity);
        Debug.Log("[DIAG] RefreshFromSaved: sensitivitySlider set");

        UpdateValueText(masterValueText, master);
        Debug.Log("[DIAG] RefreshFromSaved: masterValueText set");
        UpdateValueText(musicValueText, music);
        Debug.Log("[DIAG] RefreshFromSaved: musicValueText set");
        UpdateValueText(sfxValueText, sfx);
        Debug.Log("[DIAG] RefreshFromSaved: sfxValueText set");
        UpdateSensitivityText(sensitivity);
        Debug.Log("[DIAG] RefreshFromSaved: sensitivityValueText set, RefreshFromSaved end");
    }

    private void OnMasterChanged(float v)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.ApplyMasterVolume(v);
        UpdateValueText(masterValueText, v);
    }

    private void OnMusicChanged(float v)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.ApplyMusicVolume(v);
        UpdateValueText(musicValueText, v);
    }

    private void OnSfxChanged(float v)
    {
        if (AudioManager.Instance != null) AudioManager.Instance.ApplySfxVolume(v);
        UpdateValueText(sfxValueText, v);
    }

    private void OnSensitivityChanged(float v)
    {
        GameSettings.MouseSensitivity = v;
        UpdateSensitivityText(v);
    }

    private static void UpdateValueText(TextMeshProUGUI text, float slider01)
    {
        if (text != null) text.text = Mathf.RoundToInt(slider01 * 100f).ToString();
    }

    private void UpdateSensitivityText(float value)
    {
        if (sensitivityValueText != null) sensitivityValueText.text = value.ToString("0.0") + "x";
    }

    private void OnResetClicked()
    {
        var am = AudioManager.Instance;
        if (am != null)
        {
            am.ApplyMasterVolume(AudioManager.DefaultVolume);
            am.ApplyMusicVolume(AudioManager.DefaultVolume);
            am.ApplySfxVolume(AudioManager.DefaultVolume);
        }
        GameSettings.MouseSensitivity = GameSettings.DefaultSensitivity;

        RefreshFromSaved();
    }

    private void OnBackClicked()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (mainMenuRoot != null) mainMenuRoot.SetActive(true);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayUiPanelClose();
    }
}
