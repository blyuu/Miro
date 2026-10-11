using UnityEngine;

public class PlayerUpgradeStats : MonoBehaviour
{
    public float SprayDamageMultiplier { get; private set; } = 1f;
    public float PaintSizeMultiplier { get; private set; } = 1f;
    public float SprayRateMultiplier { get; private set; } = 1f;

    public void ApplyPressureUp()
    {
        SprayDamageMultiplier *= 1.25f;
    }

    public void ApplyWideNozzle()
    {
        PaintSizeMultiplier *= 1.25f;
    }

    public void ApplyRapidSpray()
    {
        SprayRateMultiplier *= 1.15f;
    }
}