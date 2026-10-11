using UnityEngine;

// Tiny persisted-settings holder for things that aren't audio (AudioManager
// owns volume) but still need to survive a scene reload via PlayerPrefs -
// currently just mouse sensitivity, applied by PlayerAimCamera on load.
public static class GameSettings
{
    private const string SensitivityPrefKey = "input.mouseSensitivity";
    public const float DefaultSensitivity = 1f;

    public static float MouseSensitivity
    {
        get => PlayerPrefs.GetFloat(SensitivityPrefKey, DefaultSensitivity);
        set => PlayerPrefs.SetFloat(SensitivityPrefKey, value);
    }
}
