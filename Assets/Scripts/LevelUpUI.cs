using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class LevelUpUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button pressureUpButton;
    [SerializeField] private Button wideNozzleButton;
    [SerializeField] private Button rapidSprayButton;
    [SerializeField] private UIJuice panelJuice;
    [SerializeField] private UIJuice[] cardJuice;

    private Action<UpgradeType> onChosen;
    private bool choiceMade;

    void Awake()
    {
        // panel starts inactive in the saved scene already. Deactivating it
        // again here is redundant, and actively harmful: panel is the same
        // GameObject this component lives on, so when it starts inactive,
        // Awake() is deferred until the first SetActive(true) - i.e. it fires
        // reentrantly from inside Show(), which would immediately undo it.
        if (pressureUpButton != null) pressureUpButton.onClick.AddListener(() => Choose(UpgradeType.PressureUp, pressureUpButton.GetComponent<UIJuice>()));
        if (wideNozzleButton != null) wideNozzleButton.onClick.AddListener(() => Choose(UpgradeType.WideNozzle, wideNozzleButton.GetComponent<UIJuice>()));
        if (rapidSprayButton != null) rapidSprayButton.onClick.AddListener(() => Choose(UpgradeType.RapidSpray, rapidSprayButton.GetComponent<UIJuice>()));
    }

    public void Show(Action<UpgradeType> callback)
    {
        onChosen = callback;
        choiceMade = false;

        if (panel != null) panel.SetActive(true);

        if (panelJuice != null) panelJuice.PlayAppear();
        if (cardJuice != null)
        {
            for (int i = 0; i < cardJuice.Length; i++)
            {
                if (cardJuice[i] != null) cardJuice[i].PlayAppear(0.05f * i);
            }
        }

        Time.timeScale = 0f;
        GameplayCursor.Unlock();
    }

    // Called by GameFlowController when Clear/Game Over must take over as the
    // final state - hides the panel without applying the pending upgrade or
    // touching Time.timeScale/cursor (the caller owns those in that case).
    public void ForceClose()
    {
        choiceMade = true;
        onChosen = null;
        if (panel != null) panel.SetActive(false);
    }

    private void Choose(UpgradeType type, UIJuice chosenCardJuice)
    {
        if (choiceMade) return;
        choiceMade = true;

        if (AudioManager.Instance != null) AudioManager.Instance.PlayUpgradeSelect();

        if (chosenCardJuice != null)
        {
            chosenCardJuice.PlayPop();
            StartCoroutine(FinishChoice(type));
        }
        else
        {
            FinishChoiceImmediate(type);
        }
    }

    // Lets the chosen card's pop play out (using unscaled time, same as
    // UIJuice) before the panel closes - purely cosmetic delay, same end
    // state as FinishChoiceImmediate.
    private IEnumerator FinishChoice(UpgradeType type)
    {
        yield return new WaitForSecondsRealtime(0.12f);
        FinishChoiceImmediate(type);
    }

    private void FinishChoiceImmediate(UpgradeType type)
    {
        if (panel != null) panel.SetActive(false);

        Time.timeScale = 1f;
        GameplayCursor.Lock();

        var callback = onChosen;
        onChosen = null;
        callback?.Invoke(type);
    }
}