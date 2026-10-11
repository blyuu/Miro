using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHpHUD : MonoBehaviour
{
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private Image fillImage;

    void Update()
    {
        if (playerHealth == null) return;
        // Rich-text size split only (visual hierarchy) - current HP reads
        // big, max HP stays small - no behavior change. Color stays white;
        // the bar itself carries the magenta accent per the reference.
        if (hpText != null) hpText.text = "<size=150%>" + playerHealth.CurrentHealth + "</size><size=75%> / " + playerHealth.MaxHealth + "</size>";
        if (fillImage != null) fillImage.fillAmount = playerHealth.MaxHealth > 0 ? (float)playerHealth.CurrentHealth / playerHealth.MaxHealth : 0f;
    }
}
