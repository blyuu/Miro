using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BossHUD : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private Image fillImage;

    private EnemyHealth boss;

    // panel starts inactive in the saved scene already (same object this
    // component lives on) - do NOT SetActive(false) it here. Doing so would
    // defer this Awake() until the first Show()'s SetActive(true), making it
    // fire reentrantly and undo that activation (see LevelUpUI's history).

    public void Show(EnemyHealth bossHealth, string bossName)
    {
        boss = bossHealth;
        if (nameText != null) nameText.text = bossName;
        if (panel != null) panel.SetActive(true);
    }

    public void Hide()
    {
        boss = null;
        if (panel != null) panel.SetActive(false);
    }

    void Update()
    {
        if (boss == null) return;

        if (fillImage != null)
            fillImage.fillAmount = boss.MaxHealth > 0 ? (float)boss.CurrentHealth / boss.MaxHealth : 0f;

        if (hpText != null)
            hpText.text = boss.CurrentHealth + " / " + boss.MaxHealth;
    }
}
