using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

// Project's only persistent object - lives for the whole app lifetime (first
// created in LobbyScene, survives every SceneManager.LoadScene after that).
// Deliberately small: one shared PlayOneShot source for all SFX (it already
// layers overlapping calls without any pooling), one looping source for the
// continuous Spray sound, one source for music. No per-enemy/per-event
// AudioSources anywhere, EXCEPT EnemyAI's own positionalAudioSource - that
// one is intentionally per-instance/3D-spatial (so each enemy's move/attack
// sound pans and attenuates from its own position), scaled by
// EffectiveSfxGain below so it still respects the Master/SFX sliders. It is
// NOT centralized here on purpose.
//
// This is also the single source of truth for WHICH clip plays for WHICH
// gameplay event - every other script calls one of the named Play* methods
// below instead of holding its own AudioClip field, so every sound in the
// game can be found and swapped from Inspector slots on this one component.
// Callers that need their own per-instance throttling (e.g. a hit-cooldown
// timer) still own that timer locally and simply call the Play* method when
// the cooldown allows it - this class only owns clip selection and playback,
// never gameplay timing.
//
// Volume sliders drive AudioSource.volume directly rather than an AudioMixer
// exposed parameter: the slider is mapped onto a dB range (SliderToLinear)
// and converted back to a linear gain, which is what actually gives the
// "natural loudness" feel the brief asks for - a linear 0..1 slider applied
// straight to volume would bunch most of the perceived loudness change into
// the first few percent of the drag. The AudioSources are still routed
// through the Master/Music/SFX groups in MainMixer.mixer for proper signal
// grouping; only the live volume math bypasses the mixer's own fader.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Mixer / Sources")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource sprayLoopSource;
    [SerializeField] private AudioSource musicSource;

    [Header("Volume")]
    [SerializeField] private float minDb = -40f;
    [SerializeField] private float maxDb = 0f;

    [Header("BGM")]
    [Tooltip("Plays while the Lobby/Main Menu is shown.")]
    [SerializeField] private AudioClip lobbyBgm;
    [Tooltip("Plays once the first wave starts (WaveManager.RunLoop).")]
    [SerializeField] private AudioClip battleBgm;
    [Tooltip("Extra volume multiplier applied only to Battle Bgm, on top of the Music slider - lower this if the battle track is mixed louder than the others.")]
    [Range(0f, 1f)]
    [SerializeField] private float battleBgmVolumeScale = 0.6f;
    [Tooltip("Plays during the \"BOSS INCOMING\" intro, through the boss fight.")]
    [SerializeField] private AudioClip bossBgm;
    [Tooltip("Plays on the Game Clear screen (boss defeated).")]
    [SerializeField] private AudioClip victoryBgm;
    [Tooltip("Plays on the Game Over screen (player died).")]
    [SerializeField] private AudioClip gameOverBgm;
    [Tooltip("Crossfade duration (seconds) when switching to a different BGM clip. 0 = instant swap.")]
    [Range(0f, 3f)]
    [SerializeField] private float musicFadeSeconds = 0.4f;

    [Header("Player SFX")]
    [Tooltip("Optional one-shot stinger the instant Spray starts. Leave empty to skip - the continuous Spray Loop below is the main sound.")]
    [SerializeField] private AudioClip sprayStartClip;
    [Tooltip("Continuous sound while Spray is held - looped on its own AudioSource, started/stopped automatically.")]
    [SerializeField] private AudioClip sprayLoopClip;
    [Tooltip("Optional one-shot stinger the instant Spray stops. Leave empty to skip.")]
    [SerializeField] private AudioClip sprayStopClip;
    [Tooltip("Random pool - plays when the Player takes damage.")]
    [SerializeField] private AudioClip[] playerHitClips;

    [Header("Combat / Enemy SFX")]
    [Tooltip("Random pool - plays on every Spray tick that lands on something (enemy, wall, or paintable surface).")]
    [SerializeField] private AudioClip[] sprayImpactClips;
    [Tooltip("Plays when a Saturated enemy Shatters (EnemyPaintStatus.Shatter).")]
    [SerializeField] private AudioClip paintBurstClip;
    [Tooltip("Plays on every Shatter that extends the current Chain (ChainShatterTracker.RegisterShatter).")]
    [SerializeField] private AudioClip chainReactionClip;
    [Tooltip("Random pool - a regular enemy taking damage.")]
    [SerializeField] private AudioClip[] enemyHitClips;
    [Tooltip("Random pool - a regular enemy dying.")]
    [SerializeField] private AudioClip[] enemyDeathClips;
    [Tooltip("Plays when the Boss first spawns in.")]
    [SerializeField] private AudioClip bossAppearClip;
    [Tooltip("Plays once, when the Boss crosses into Phase 2/Enraged.")]
    [SerializeField] private AudioClip bossPhase2Clip;
    [Tooltip("Plays when the Boss dies.")]
    [SerializeField] private AudioClip bossDefeatClip;

    [Header("Game / UI SFX")]
    [Tooltip("Random pool - menu button mouse-over.")]
    [SerializeField] private AudioClip[] uiHoverClips;
    [Tooltip("Random pool - menu button / confirm click (Lobby buttons, Character Select).")]
    [SerializeField] private AudioClip[] uiClickClips;
    [Tooltip("Plays when a Lobby sub-panel (Settings/Character Select) opens.")]
    [SerializeField] private AudioClip uiPanelOpenClip;
    [Tooltip("Plays when a Lobby sub-panel (Settings/Character Select) closes. Shared by both panels - was three separate, identically-valued fields before this change.")]
    [SerializeField] private AudioClip uiPanelCloseClip;
    [Tooltip("Random pool - plays when a new wave starts.")]
    [SerializeField] private AudioClip[] waveStartClips;
    [Tooltip("Plays once a wave is fully cleared.")]
    [SerializeField] private AudioClip waveClearClip;
    [Tooltip("Random pool - plays when the player's energy bar fills and levels up (before the upgrade-choice panel shows).")]
    [SerializeField] private AudioClip[] playerLevelUpClips;
    [Tooltip("Random pool - plays when the player picks a card on the Level Up upgrade panel.")]
    [SerializeField] private AudioClip[] upgradeSelectClips;
    [Tooltip("Plays once, on the Game Clear screen.")]
    [SerializeField] private AudioClip gameClearClip;
    [Tooltip("Plays once, on the Game Over screen.")]
    [SerializeField] private AudioClip gameOverClip;

    private const string MasterPrefKey = "audio.master";
    private const string MusicPrefKey = "audio.music";
    private const string SfxPrefKey = "audio.sfx";

    public const float DefaultVolume = 1f;

    private float masterGain = 1f;
    private float musicGain = 1f;
    private float sfxGain = 1f;

    private Coroutine musicFadeRoutine;
    private float currentMusicVolumeScale = 1f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // sprayLoopSource.clip used to be set directly on the AudioSource
        // component in the Inspector (invisible unless you expanded
        // AudioManager's own child object) - now it's just another slot on
        // this script like everything else.
        if (sprayLoopSource != null) sprayLoopSource.clip = sprayLoopClip;

        ApplyMasterVolume(PlayerPrefs.GetFloat(MasterPrefKey, DefaultVolume));
        ApplyMusicVolume(PlayerPrefs.GetFloat(MusicPrefKey, DefaultVolume));
        ApplySfxVolume(PlayerPrefs.GetFloat(SfxPrefKey, DefaultVolume));
    }

    public void PlaySfx(AudioClip clip, float volumeScale = 1f, float pitchJitter = 0.05f)
    {
        if (clip == null || sfxSource == null) return;

        sfxSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        sfxSource.PlayOneShot(clip, volumeScale * masterGain * sfxGain);
    }

    // Picks one clip at random from a small variant pool (2-4 entries) so a
    // frequently-repeated event doesn't sound identical every time.
    public void PlaySfxRandom(AudioClip[] clips, float volumeScale = 1f, float pitchJitter = 0.05f)
    {
        if (clips == null || clips.Length == 0) return;
        PlaySfx(clips[Random.Range(0, clips.Length)], volumeScale, pitchJitter);
    }

    // --- Named events - every other script calls one of these instead of
    // holding its own AudioClip field. Each is a thin wrapper around
    // PlaySfx/PlaySfxRandom over this class's own Inspector slots, so the
    // volume/pitch tuning choices already made below don't need to be
    // re-decided at every call site. ---

    public void PlayPlayerHit() => PlaySfxRandom(playerHitClips, 1f);
    public void PlaySprayImpact() => PlaySfxRandom(sprayImpactClips, 0.4f);
    public void PlayPaintBurst() => PlaySfx(paintBurstClip);
    public void PlayChainReaction() => PlaySfx(chainReactionClip);
    public void PlayEnemyHit() => PlaySfxRandom(enemyHitClips, 0.6f);
    public void PlayEnemyDeath() => PlaySfxRandom(enemyDeathClips);
    public void PlayBossAppear() => PlaySfx(bossAppearClip);
    public void PlayBossPhase2() => PlaySfx(bossPhase2Clip);
    public void PlayBossDefeat() => PlaySfx(bossDefeatClip);
    public void PlayUiHover() => PlaySfxRandom(uiHoverClips, 0.6f);
    public void PlayUiClick() => PlaySfxRandom(uiClickClips);
    public void PlayUiPanelOpen() => PlaySfx(uiPanelOpenClip);
    public void PlayUiPanelClose() => PlaySfx(uiPanelCloseClip);
    public void PlayWaveStart() => PlaySfxRandom(waveStartClips);
    public void PlayWaveClear() => PlaySfx(waveClearClip);
    public void PlayPlayerLevelUp() => PlaySfxRandom(playerLevelUpClips);
    public void PlayUpgradeSelect() => PlaySfxRandom(upgradeSelectClips);
    public void PlayGameClear() => PlaySfx(gameClearClip);
    public void PlayGameOver() => PlaySfx(gameOverClip);

    public void PlayLobbyMusic() => PlayMusic(lobbyBgm);
    public void PlayBattleMusic() => PlayMusic(battleBgm, true, battleBgmVolumeScale);
    public void PlayBossMusic() => PlayMusic(bossBgm);
    public void PlayVictoryMusic() => PlayMusic(victoryBgm);
    public void PlayGameOverMusic() => PlayMusic(gameOverBgm);

    // Spray is held/released every frame via PlayerSpray - Play()/Stop() are
    // no-ops when already in the requested state, so this is safe to call
    // every frame without restarting the loop. No clip assigned is a valid
    // no-op state, not a bug. One-shot start/stop stingers (if assigned) only
    // fire on the actual state transition, never every frame.
    public void SetSprayLooping(bool playing)
    {
        if (sprayLoopSource == null) return;

        bool wasPlaying = sprayLoopSource.isPlaying;

        if (playing && !wasPlaying)
        {
            if (sprayLoopSource.clip != null) sprayLoopSource.Play();
            PlaySfx(sprayStartClip);
        }
        else if (!playing && wasPlaying)
        {
            sprayLoopSource.Stop();
            PlaySfx(sprayStopClip);
        }
    }

    // Idempotent (calling with the clip that's already playing is a no-op,
    // so e.g. WaveManager can safely call this once per wave without
    // restarting/stacking Battle BGM) and crossfades on an actual change -
    // single musicSource, so "crossfade" here means fade the current clip
    // out, swap, then fade the new one in, rather than true dual-source
    // blending. clip == null fades out and stops (no music). trackVolumeScale
    // is an extra per-track multiplier (e.g. battleBgmVolumeScale) on top of
    // the Music slider - kept as the currently-playing track's own volume so
    // RefreshVolumes() (a slider drag) rescales against the right track
    // rather than silently reverting to full volume.
    public void PlayMusic(AudioClip clip, bool loop = true, float trackVolumeScale = 1f)
    {
        if (musicSource == null) return;
        if (musicSource.clip == clip && (musicSource.isPlaying || clip == null))
        {
            currentMusicVolumeScale = trackVolumeScale;
            return;
        }

        if (musicFadeRoutine != null) StopCoroutine(musicFadeRoutine);
        currentMusicVolumeScale = trackVolumeScale;

        if (musicFadeSeconds <= 0f)
        {
            musicSource.clip = clip;
            musicSource.loop = loop;
            musicSource.volume = masterGain * musicGain * currentMusicVolumeScale;
            if (clip != null) musicSource.Play(); else musicSource.Stop();
            return;
        }

        musicFadeRoutine = StartCoroutine(CrossfadeMusic(clip, loop));
    }

    private IEnumerator CrossfadeMusic(AudioClip clip, bool loop)
    {
        float targetVolume = masterGain * musicGain * currentMusicVolumeScale;
        float half = musicFadeSeconds * 0.5f;

        if (musicSource.isPlaying)
        {
            float startVolume = musicSource.volume;
            for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
            {
                musicSource.volume = Mathf.Lerp(startVolume, 0f, t / half);
                yield return null;
            }
        }

        musicSource.volume = 0f;
        musicSource.clip = clip;
        musicSource.loop = loop;

        if (clip == null)
        {
            musicSource.Stop();
            musicFadeRoutine = null;
            yield break;
        }

        musicSource.Play();
        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(0f, targetVolume, t / half);
            yield return null;
        }
        musicSource.volume = targetVolume;
        musicFadeRoutine = null;
    }

    public void ApplyMasterVolume(float slider01)
    {
        masterGain = SliderToLinear(slider01);
        PlayerPrefs.SetFloat(MasterPrefKey, Mathf.Clamp01(slider01));
        RefreshVolumes();
    }

    public void ApplyMusicVolume(float slider01)
    {
        musicGain = SliderToLinear(slider01);
        PlayerPrefs.SetFloat(MusicPrefKey, Mathf.Clamp01(slider01));
        RefreshVolumes();
    }

    public void ApplySfxVolume(float slider01)
    {
        sfxGain = SliderToLinear(slider01);
        PlayerPrefs.SetFloat(SfxPrefKey, Mathf.Clamp01(slider01));
        RefreshVolumes();
    }

    // For scripts that need their own positional (3D) AudioSource instead of
    // the shared PlaySfx one - e.g. EnemyAI's movement/combat cues - so their
    // manual volume math still respects the Master/SFX sliders the same way
    // PlaySfx does.
    public float EffectiveSfxGain => masterGain * sfxGain;

    public float GetMasterVolume() => PlayerPrefs.GetFloat(MasterPrefKey, DefaultVolume);
    public float GetMusicVolume() => PlayerPrefs.GetFloat(MusicPrefKey, DefaultVolume);
    public float GetSfxVolume() => PlayerPrefs.GetFloat(SfxPrefKey, DefaultVolume);

    private void RefreshVolumes()
    {
        // Only rescales the music source when no fade is actively driving its
        // volume - otherwise a slider drag mid-crossfade would fight the
        // coroutine's own Lerp.
        if (musicSource != null && musicFadeRoutine == null) musicSource.volume = masterGain * musicGain * currentMusicVolumeScale;
        if (sprayLoopSource != null) sprayLoopSource.volume = masterGain * sfxGain;
        // sfxSource has no persistent "volume" to set - PlaySfx folds
        // masterGain/sfxGain into each PlayOneShot call's volumeScale instead.
    }

    // Slider position (0..1) is linear in dB-space (minDb..maxDb), not in
    // amplitude - this is what makes equal slider movement sound like roughly
    // equal loudness change, matching how a real mixer fader behaves.
    private float SliderToLinear(float slider01)
    {
        slider01 = Mathf.Clamp01(slider01);
        if (slider01 <= 0.0001f) return 0f;

        float db = Mathf.Lerp(minDb, maxDb, slider01);
        return Mathf.Pow(10f, db / 20f);
    }
}
