using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Page8CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image _completionOverlay;
    [Tooltip("Wait after the shadow overlay disappears, before the completion overlay fades in.")]
    [SerializeField] private float _overlayDelay    = 0.5f;
    [SerializeField] private float _fadeInDuration  = 0.8f;
    [SerializeField] private float _holdDuration    = 0.5f;
    [SerializeField] private float _fadeOutDuration = 0.8f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _voiceClip;
    [Tooltip("Wait after the completion overlay fades out, before the voice line plays.")]
    [SerializeField] private float       _audioLineDelay = 0.5f;
    [Tooltip("Fallback wait used only if the voice clip isn't assigned.")]
    [SerializeField] private float       _fallbackAudioDuration = 1f;

    [Header("VFX")]
    [Tooltip("Wait after the voice line ends, before the glow VFX starts.")]
    [SerializeField] private float _vfxStartDelay = 1f;
    [Tooltip("Fallback VFX duration used only if the island's glow VFX can't be found.")]
    [SerializeField] private float _fallbackVfxDuration = 4f;

    [Header("Island Exit")]
    [Tooltip("Used to look up the glow VFX living inside the spawned island prefab (via IslandVfxReference) and to trigger its shrink at the end.")]
    [SerializeField] private Page8ShadowController _shadowController;

    [Header("Gemstone Reward")]
    [Tooltip("The pendant gemstone's material — snapped to _sparkColor right after the completion overlay fades out.")]
    [SerializeField] private Material _pendantMaterial;
    [SerializeField] private Color    _sparkColor = new Color(1f, 212f / 255f, 0f); // #FFD400

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private bool _triggered;

    private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        if (_completionOverlay == null) return;
        SetOverlayAlpha(0f);
        _completionOverlay.enabled = false;
    }

    public void TriggerCompletion()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(CompletionRoutine());
    }

    // Called by Page8ShadowController once the shadow overlay has fully faded out from the
    // held-to-completion interaction. Runs the reward beats in order: completion overlay,
    // gemstone colour snap, voice line, glow VFX, then the island's shrink-and-destroy exit —
    // mirrors Page6CompletionSequence's structure. Note PendantManager.CollectYellowSpark() is
    // called up front by Page8ShadowController the moment the hold completes, not here; this
    // routine only drives the gemstone's visible colour.
    private IEnumerator CompletionRoutine()
    {
        yield return new WaitForSeconds(_overlayDelay);
        yield return StartCoroutine(FadeInOverlay());

        yield return new WaitForSeconds(_holdDuration);

        yield return StartCoroutine(FadeOutOverlay());

        // Gemstone glows yellow to mark the spark now that the overlay has cleared.
        SetPendantColor(_sparkColor);

        yield return new WaitForSeconds(_audioLineDelay);
        PlayVoiceLine();

        float audioLength = _voiceClip != null ? _voiceClip.length : _fallbackAudioDuration;
        yield return new WaitForSeconds(audioLength);

        yield return new WaitForSeconds(_vfxStartDelay);

        // The glow VFX lives inside the spawned island prefab (via IslandVfxReference), not as
        // a fixed scene reference, since it doesn't exist until the island is instantiated.
        ParticleSystem glowVfx = _shadowController != null ? _shadowController.GetIslandGlowVfx() : null;

        float vfxDuration = _fallbackVfxDuration;
        if (glowVfx != null)
        {
            glowVfx.gameObject.SetActive(true);
            glowVfx.Play(true); // withChildren — the VFX has several nested particle systems
            vfxDuration = glowVfx.main.duration;
        }
        else
        {
            Debug.LogWarning("[Page8CompletionSequence] Island's glow VFX not found — is IslandVfxReference on the island prefab, and _shadowController assigned?");
        }

        yield return new WaitForSeconds(vfxDuration);

        if (glowVfx != null)
            glowVfx.gameObject.SetActive(false);

        if (_completionOverlay != null)
            _completionOverlay.gameObject.SetActive(false);

        // Kick off the island's shrink-and-destroy — fire-and-forget, doesn't block on it
        // finishing (same exit timing as Page4/Page6's island rewards). The wait before the
        // shrink actually starts lives inside BeginIslandShrink's own delay, not here.
        if (_shadowController != null)
            _shadowController.BeginIslandShrink();
        else
            Debug.LogWarning("[Page8CompletionSequence] _shadowController not assigned — island shrink not triggered.");

        // Sequence has fully played out — release the shared lock so scanning resumes.
        EndFeature();
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

    private void SetPendantColor(Color color)
    {
        if (_pendantMaterial == null)
        {
            Debug.LogWarning("[Page8CompletionSequence] _pendantMaterial not assigned — pendant color not set.");
            return;
        }

        _pendantMaterial.SetColor(s_baseColorId, color);
    }

    private void PlayVoiceLine()
    {
        if (_audioSource != null && _voiceClip != null)
            _audioSource.PlayOneShot(_voiceClip);
        else
            Debug.LogWarning("[Page8CompletionSequence] AudioSource or VoiceClip not assigned.");
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page8CompletionSequence] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }
}
