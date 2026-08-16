using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Page6CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image      _completionOverlay;
    [SerializeField] private GameObject _page6WordsParent;
    [SerializeField] private float      _fadeInDuration  = 0.8f;
    [SerializeField] private float      _holdDuration    = 0.5f;
    [SerializeField] private float      _fadeOutDuration = 0.8f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _brickVoiceClip;
    [Tooltip("Wait after the player completes the speech, before the brick voice clip plays.")]
    [SerializeField] private float       _brickVoiceDelay = 1f;

    [Header("Spark Reward")]
    [SerializeField] private Material               _pendantMaterial;
    [SerializeField] private Color                  _sparkColor = new Color(188f / 255f, 23f / 255f, 0f); // #BC1700
    [SerializeField] private PendantManager.SparkColor _sparkType = PendantManager.SparkColor.Red;
    [Tooltip("Wait after the overlay finishes fading out, before the pendant color snaps.")]
    [SerializeField] private float _preGlowDelay = 0.5f;
    [Tooltip("Gap between the pendant color snap and the glow VFX starting.")]
    [SerializeField] private float _colorSnapToVfxDelay = 0.1f;
    [Tooltip("Fallback VFX duration used only if the island's glow VFX can't be found.")]
    [SerializeField] private float _fallbackVfxDuration = 4f;

    [Header("Island Exit")]
    [Tooltip("Also used to look up the glow VFX living inside the spawned island prefab (via IslandVfxReference), since that VFX doesn't exist until the island is spawned.")]
    [SerializeField] private Page6SpeechController _speechController;

    [Header("References")]
    [SerializeField] private PendantManager _pendantManager;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private bool _triggered;
    private Coroutine _completionCoroutine;

    private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        if (_completionOverlay != null)
        {
            SetOverlayAlpha(0f);
            _completionOverlay.enabled = false;
        }
    }

    private void OnEnable()
    {
        if (_appStateManager != null)
            _appStateManager.OnFeatureCancelled += HandleFeatureCancelled;
    }

    private void OnDisable()
    {
        if (_appStateManager != null)
            _appStateManager.OnFeatureCancelled -= HandleFeatureCancelled;
    }

    // Back/Back To Menu pressed mid-reward-sequence. Page6SpeechController's own cancel handler
    // already destroys the island and releases the shared lock — this only needs to stop the
    // coroutine before it reaches its own (now redundant, and potentially stale) EndFeature() call
    // below, and reset _triggered so a retried attempt at this page can fire the sequence again.
    private void HandleFeatureCancelled()
    {
        if (_completionCoroutine == null) return; // not currently running; nothing to cancel

        StopCoroutine(_completionCoroutine);
        _completionCoroutine = null;
        _triggered = false;

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
        _completionCoroutine = StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        if (_page6WordsParent != null)
            _page6WordsParent.SetActive(false);

        yield return new WaitForSeconds(_brickVoiceDelay);
        PlayVoiceLine();

        yield return StartCoroutine(FadeInOverlay());

        yield return new WaitForSeconds(_holdDuration);

        yield return StartCoroutine(FadeOutOverlay());

        yield return StartCoroutine(SparkRewardSequence());
    }

    // Runs right after the overlay has fully faded back out: pendant color snap + spark award,
    // glow VFX, then the island exit and final cleanup.
    private IEnumerator SparkRewardSequence()
    {
        yield return new WaitForSeconds(_preGlowDelay);

        // Must fire together — the pendant color and the spark recorded in PendantManager
        // should never be out of sync.
        SetPendantColor(_sparkColor);
        if (_pendantManager != null)
            _pendantManager.AwardSpark(_sparkType);
        else
            Debug.LogWarning("[Page6CompletionSequence] _pendantManager not assigned — AwardSpark() not called.");

        yield return new WaitForSeconds(_colorSnapToVfxDelay);

        // The glow VFX lives inside the spawned island prefab (via IslandVfxReference), not as
        // a fixed scene reference, since it doesn't exist until the island is instantiated.
        ParticleSystem glowVfx = _speechController != null ? _speechController.GetIslandGlowVfx() : null;

        float vfxDuration = _fallbackVfxDuration;
        if (glowVfx != null)
        {
            glowVfx.gameObject.SetActive(true);
            glowVfx.Play(true); // withChildren — the VFX has several nested particle systems
            vfxDuration = glowVfx.main.duration;
        }
        else
        {
            Debug.LogWarning("[Page6CompletionSequence] Island's glow VFX not found — is IslandVfxReference on the island prefab, and _speechController assigned?");
        }

        // Let the VFX play out fully before exiting — timed off its actual duration, not a
        // hardcoded value, so this still lines up if the VFX's length changes later.
        yield return new WaitForSeconds(vfxDuration);

        // Kick off the island's shrink-and-destroy — fire-and-forget, doesn't block on it
        // finishing (same exit timing as Page1Manager's original finish-group dismissal).
        if (_speechController != null)
            _speechController.BeginIslandShrink();
        else
            Debug.LogWarning("[Page6CompletionSequence] _speechController not assigned — island shrink not triggered.");

        if (glowVfx != null)
            glowVfx.gameObject.SetActive(false);

        if (_completionOverlay != null)
            _completionOverlay.gameObject.SetActive(false);

        // Sequence has fully played out — release the shared lock so scanning resumes.
        EndFeature();
    }

    private void SetPendantColor(Color color)
    {
        if (_pendantMaterial == null)
        {
            Debug.LogWarning("[Page6CompletionSequence] _pendantMaterial not assigned — pendant color not set.");
            return;
        }

        _pendantMaterial.SetColor(s_baseColorId, color);
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page6CompletionSequence] _appStateManager not assigned; scanner lock not released.");
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

    private void PlayVoiceLine()
    {
        if (_audioSource != null && _brickVoiceClip != null)
            _audioSource.PlayOneShot(_brickVoiceClip);
        else
            Debug.LogWarning("[Page6CompletionSequence] AudioSource or BrickVoiceClip not assigned.");
    }

    private void SetOverlayAlpha(float alpha)
    {
        Color c = _completionOverlay.color;
        _completionOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }
}
