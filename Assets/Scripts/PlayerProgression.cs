using System;
using UnityEngine;

public enum UpgradeType
{
    PressureUp,
    WideNozzle,
    RapidSpray
}

public class PlayerProgression : MonoBehaviour
{
    [SerializeField] private LevelUpUI levelUpUI;
    [SerializeField] private PlayerUpgradeStats upgradeStats;

    [SerializeField] private int level = 1;
    [SerializeField] private float currentEnergy = 0f;
    [SerializeField] private float requiredEnergy = 30f;

    private int pendingLevelUps;
    private bool upgradeUIActive;
    private bool lockedOut;

    public int Level => level;
    public float CurrentEnergy => currentEnergy;
    public float RequiredEnergy => requiredEnergy;

    public event Action OnLeveledUp;

    void Awake()
    {
        if (upgradeStats == null) upgradeStats = GetComponent<PlayerUpgradeStats>();
    }

    public void AddEnergy(int amount)
    {
        AddEnergy((float)amount);
    }

    public void AddEnergy(float amount)
    {
        currentEnergy += amount;

        while (currentEnergy >= requiredEnergy)
        {
            currentEnergy -= requiredEnergy;
            level++;
            requiredEnergy = 30f + (level - 1) * 20f;
            pendingLevelUps++;
            if (AudioManager.Instance != null) AudioManager.Instance.PlayPlayerLevelUp();
            OnLeveledUp?.Invoke();
        }

        TryShowNextLevelUp();
    }

    private void TryShowNextLevelUp()
    {
        if (lockedOut || pendingLevelUps <= 0 || upgradeUIActive || levelUpUI == null) return;

        upgradeUIActive = true;
        levelUpUI.Show(OnUpgradeChosen);
    }

    // Called once the run ends (Clear or Game Over) so no new Level Up panel
    // can open over the end-of-run UI.
    public void LockOut()
    {
        lockedOut = true;
    }

    private void OnUpgradeChosen(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.PressureUp: upgradeStats.ApplyPressureUp(); break;
            case UpgradeType.WideNozzle: upgradeStats.ApplyWideNozzle(); break;
            case UpgradeType.RapidSpray: upgradeStats.ApplyRapidSpray(); break;
        }

        pendingLevelUps--;
        upgradeUIActive = false;

        TryShowNextLevelUp();
    }

    [ContextMenu("Debug Add 10 Energy")]
    private void DebugAdd10Energy()
    {
        AddEnergy(10);
    }

    public void DebugAddEnergy(int amount)
    {
        AddEnergy(amount);
    }
}