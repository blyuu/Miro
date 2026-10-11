using System;
using UnityEngine;
using UnityEngine.UI;
using ProjectMiro.Platform;

// Shown once per run by GameFlowController.HandleFatalHit(), BEFORE death is
// finalized - offers a CrazyGames rewarded ad as a one-time continue. Mirrors
// LevelUpUI's Show()/Time.timeScale/GameplayCursor pattern (pause + unlock
// while up, resume + re-lock the instant a choice resolves).
public class ReviveUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button watchAdButton;
    [SerializeField] private Button declineButton;

    private Action<bool> onResolved;
    private bool resolved;

    void Awake()
    {
        if (watchAdButton != null) watchAdButton.onClick.AddListener(OnWatchAdClicked);
        if (declineButton != null) declineButton.onClick.AddListener(OnDeclineClicked);
    }

    public void Show(Action<bool> callback)
    {
        onResolved = callback;
        resolved = false;

        if (panel != null) panel.SetActive(true);

        Time.timeScale = 0f;
        GameplayCursor.Unlock();
    }

    private void OnWatchAdClicked()
    {
        if (resolved) return;

        // Disable both buttons while the ad request is in flight - the Mock
        // bridge resolves in 0.2-0.5s (WaitForSecondsRealtime, unaffected by
        // Time.timeScale=0), the real CrazyGames one can take longer, and
        // either way a second click shouldn't fire a second ad request.
        if (watchAdButton != null) watchAdButton.interactable = false;
        if (declineButton != null) declineButton.interactable = false;

        PlatformServiceProvider.Current.ShowRewardedAd(result =>
        {
            Finish(result.Status == PlatformResultStatus.Success);
        });
    }

    private void OnDeclineClicked()
    {
        if (resolved) return;
        Finish(false);
    }

    private void Finish(bool revived)
    {
        if (resolved) return;
        resolved = true;

        if (watchAdButton != null) watchAdButton.interactable = true;
        if (declineButton != null) declineButton.interactable = true;
        if (panel != null) panel.SetActive(false);

        Time.timeScale = 1f;
        GameplayCursor.Lock();

        var callback = onResolved;
        onResolved = null;
        callback?.Invoke(revived);
    }
}
