using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Page10CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image      _completionOverlay;
    [SerializeField] private float      _fadeInDuration  = 0.8f;
    [SerializeField] private float      _holdDuration    = 0.5f;
    [SerializeField] private float      _fadeOutDuration = 0.8f;

    [Header("Timing")]
    [Tooltip("Wait right after all orbs are collected (pendant already snapped gold), before the first voice line and completion overlay sequence begins.")]
    [SerializeField] private float _postCollectionDelay = 0.5f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Plays _voiceLineLeadTime seconds before the completion overlay starts fading in.")]
    [SerializeField] private AudioClip   _voiceClip;
    [Tooltip("Wait after the first voice line plays, before the completion overlay starts fading in.")]
    [SerializeField] private float       _voiceLineLeadTime = 0.1f;
    [Tooltip("Second voice line. Plays exactly _voiceLineGap seconds after the first voice line finishes (never less, regardless of clip length), timed so the glow VFX starts _vfxDelayAfterVoice seconds after this one begins.")]
    [SerializeField] private AudioClip   _secondVoiceClip;
    [Tooltip("Guaranteed silence between the first voice line ending and the second voice line starting.")]
    [SerializeField] private float       _voiceLineGap = 2.0f;

    [Header("VFX")]
    [Tooltip("Wait after the second voice line starts, before the glow VFX starts.")]
    [SerializeField] private float _vfxDelayAfterVoice = 0.5f;
    [Tooltip("Fallback VFX duration used only if the island's glow VFX can't be found.")]
    [SerializeField] private float _fallbackVfxDuration = 4f;

    [Header("Gemstone Reward")]
    [Tooltip("The pendant gemstone's material — snapped to _sparkColor the instant all orbs are collected.")]
    [SerializeField] private Material _pendantMaterial;
    [SerializeField] private Color    _sparkColor = new Color(232f / 255f, 185f / 255f, 35f / 255f); // #E8B923

    [Header("Page 10 References")]
    [SerializeField] private Page10MeterController _page10MeterController;
    [SerializeField] private PendantManager _pendantManager;

    [Header("Island Exit")]
    [Tooltip("Used to look up the glow VFX living inside the spawned island prefab (via IslandVfxReference) and to trigger its shrink at the end.")]
    [SerializeField] private Page10ARTracker _arTracker;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private bool _triggered;

    private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        if (_completionOverlay != null)
        {
            SetOverlayAlpha(0f);
            _completionOverlay.enabled = false;
        }
    }

    public void TriggerCompletion()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        // Must fire together, right as the last orb is collected — the pendant color and the
        // spark recorded in PendantManager should never be out of sync.
        SetPendantColor(_sparkColor);
        if (_pendantManager != null)
            _pendantManager.CollectGoldSpark();
        else
            Debug.LogWarning("[Page10CompletionSequence] _pendantManager not assigned — CollectGoldSpark() not called.");

        // Hide the meter when completion starts (matching Page 4's CompletionSequence)
        if (_page10MeterController != null)
            _page10MeterController.SetVisible(false);

        yield return new WaitForSeconds(_postCollectionDelay);

        // First voice line fires _voiceLineLeadTime seconds before the overlay starts fading in.
        PlayVoiceLine();
        float voiceLineLength = _voiceClip != null ? _voiceClip.length : 0f;
        yield return new WaitForSeconds(_voiceLineLeadTime);

        yield return StartCoroutine(FadeInOverlay());

        yield return new WaitForSeconds(_holdDuration);

        yield return StartCoroutine(FadeOutOverlay());

        if (_completionOverlay != null)
            _completionOverlay.gameObject.SetActive(false);

        // The overlay's fixed fade sequence (lead time + fade in + hold + fade out) already ate
        // into the gap between the two voice lines — only wait out whatever's left so the second
        // line always starts exactly _voiceLineGap seconds after the first one actually finishes,
        // no matter how long that first clip is.
        float overlayCycleDuration = _voiceLineLeadTime + _fadeInDuration + _holdDuration + _fadeOutDuration;
        float remainingGap = (voiceLineLength + _voiceLineGap) - overlayCycleDuration;
        if (remainingGap > 0f)
            yield return new WaitForSeconds(remainingGap);

        PlaySecondVoiceLine();
        float secondVoiceLineLength = _secondVoiceClip != null ? _secondVoiceClip.length : 0f;

        yield return new WaitForSeconds(_vfxDelayAfterVoice);

        // The glow VFX lives inside the spawned island prefab (via IslandVfxReference), not as
        // a fixed scene reference, since it doesn't exist until the island is instantiated.
        ParticleSystem glowVfx = _arTracker != null ? _arTracker.GetIslandGlowVfx() : null;

        float vfxDuration = _fallbackVfxDuration;
        if (glowVfx != null)
        {
            glowVfx.gameObject.SetActive(true);
            glowVfx.Play(true); // withChildren — the VFX has several nested particle systems
            vfxDuration = glowVfx.main.duration;
        }
        else
        {
            Debug.LogWarning("[Page10CompletionSequence] Island's glow VFX not found — is IslandVfxReference on the island prefab, and _arTracker assigned?");
        }

        // Wait until both the VFX and the second voice line have finished — whichever takes
        // longer. The voice line has already been running for _vfxDelayAfterVoice seconds at
        // this point, so only its remainder (if any) still needs to be waited out here.
        float remainingWait = Mathf.Max(vfxDuration, secondVoiceLineLength - _vfxDelayAfterVoice);
        yield return new WaitForSeconds(Mathf.Max(0f, remainingWait));

        if (glowVfx != null)
            glowVfx.gameObject.SetActive(false);

        // Kick off the island's shrink-and-destroy — fire-and-forget, doesn't block on it
        // finishing (same exit timing as the other pages' island rewards). The wait before the
        // shrink actually starts lives inside BeginIslandShrink's own delay, not here.
        if (_arTracker != null)
        {
            _arTracker.BeginIslandShrink();
            // Stops page10 from immediately re-triggering itself (and destroying the
            // still-shrinking island) if the marker is still being tracked once the lock below
            // releases.
            _arTracker.MarkSequenceComplete();
        }
        else
        {
            Debug.LogWarning("[Page10CompletionSequence] _arTracker not assigned — island shrink not triggered.");
        }

        // Transition has fully played out — release the shared lock so scanning resumes.
        EndFeature();
    }

    private void SetPendantColor(Color color)
    {
        if (_pendantMaterial == null)
        {
            Debug.LogWarning("[Page10CompletionSequence] _pendantMaterial not assigned — pendant color not set.");
            return;
        }

        _pendantMaterial.SetColor(s_baseColorId, color);
    }

    private void PlayVoiceLine()
    {
        if (_audioSource != null && _voiceClip != null)
            _audioSource.PlayOneShot(_voiceClip);
        else
            Debug.LogWarning("[Page10CompletionSequence] AudioSource or VoiceClip not assigned.");
    }

    private void PlaySecondVoiceLine()
    {
        if (_audioSource != null && _secondVoiceClip != null)
            _audioSource.PlayOneShot(_secondVoiceClip);
        else
            Debug.LogWarning("[Page10CompletionSequence] AudioSource or SecondVoiceClip not assigned.");
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page10CompletionSequence] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    private IEnumerator FadeInOverlay()
    {
        if (_completionOverlay == null) yield break;

        SetOverlayAlpha(0f);
        _completionOverlay.gameObject.SetActive(true);
        _completionOverlay.enabled = true;
        _completionOverlay.transform.SetAsLastSibling();

        float elapsed = 0f;
        while (elapsed < _fadeInDuration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(Mathf.Clamp01(elapsed / _fadeInDuration));
            yield return null;
        }
        SetOverlayAlpha(1f);
    }

    private IEnumerator FadeOutOverlay()
    {
        if (_completionOverlay == null) yield break;

        float elapsed = 0f;
        while (elapsed < _fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(1f - Mathf.Clamp01(elapsed / _fadeOutDuration));
            yield return null;
        }
        SetOverlayAlpha(0f);
        _completionOverlay.enabled = false;
    }

    private void SetOverlayAlpha(float alpha)
    {
        Color c = _completionOverlay.color;
        _completionOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }
}
