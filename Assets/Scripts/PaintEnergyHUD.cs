using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PaintEnergyHUD : MonoBehaviour
{
    [SerializeField] private PlayerProgression progression;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private Image fillImage;

    void Update()
    {
        if (progression == null) return;

        if (levelText != null)
            levelText.text = "LV. " + progression.Level;

        // Rich-text size split only (visual hierarchy) - current paint amount
        // reads big, required amount stays small. Color stays white; the bar
        // itself carries the cyan accent per the reference.
        if (energyText != null)
            energyText.text = "<size=150%>" + Mathf.FloorToInt(progression.CurrentEnergy) + "</size><size=75%> / " + Mathf.FloorToInt(progression.RequiredEnergy) + "</size>";

        if (fillImage != null)
            fillImage.fillAmount = progression.RequiredEnergy > 0f ? progression.CurrentEnergy / progression.RequiredEnergy : 0f;
    }
}