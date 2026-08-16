using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image  _completionOverlay;
    [SerializeField] private float  _uiFadeOutDuration = 0.5f;
    [SerializeField] private float  _fadeInDuration    = 0.8f;
    [SerializeField] private float  _holdDuration      = 0.5f;
    [SerializeField] private float  _fadeOutDuration   = 0.8f;

    [Header("Audio")]
    [SerializeField] private AudioSource _voiceAudioSource;
    [SerializeField] private AudioClip   _riverSpiritClip;

    [Header("Spark Reward")]
    [SerializeField] private Material               _pendantMaterial;
    [SerializeField] private Color                  _sparkColor = new Color(0x33 / 255f, 0x99 / 255f, 0xFF / 255f); // #3399FF
    [SerializeField] private PendantManager.SparkColor _sparkType = PendantManager.SparkColor.Blue;
    [Tooltip("Wait after the completion overlay finishes fading out, before the glow VFX starts. The island itself was already spawned back at the start of the page (Page4ARTracker), not here.")]
    [SerializeField] private float _vfxStartDelay = 2f;
    [Tooltip("Wait after the glow VFX starts, before the audio line plays.")]
    [SerializeField] private float _audioStartDelay = 1f;
    [Tooltip("Wait after both the VFX and audio line have finished, before the island shrinks.")]
    [SerializeField] private float _postRewardDelay = 1.5f;
    [Tooltip("Fallback VFX duration used only if the island's glow VFX can't be found.")]
    [SerializeField] private float _fallbackVfxDuration = 4f;

    [Header("Island Exit")]
    [Tooltip("Used to look up the already-spawned island's glow VFX (via IslandVfxReference) and to trigger its shrink at the end.")]
    [SerializeField] private Page4ARTracker _arTracker;

    [Header("Scene References")]
    [SerializeField] private DropSpawner         _dropSpawner;
    [SerializeField] private Image               _catchZoneImage;
    [SerializeField] private DropMeterController _dropMeterController;
    [SerializeField] private PendantManager      _pendantManager;

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

    // Back/Back To Menu pressed mid-reward-sequence. Page4ARTracker's own cancel handler already
    // destroys the island and releases the shared lock — this only needs to stop the coroutine
    // before it reaches its own (now redundant, and potentially stale) EndFeature() call below,
    // and reset _triggered so a retried attempt at this page can fire the sequence again.
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
        Debug.Log("[CompletionSequence] TriggerCompletion called. _triggered=" + _triggered);
        if (_triggered) return;
        _triggered = true;
        _completionCoroutine = StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        Debug.Log("[CompletionSequence] Step 1 — stopping spawner");
        if (_dropSpawner == null) Debug.LogError("[CompletionSequence] _dropSpawner is NULL");
        else _dropSpawner.CompleteAndStop();

        Debug.Log("[CompletionSequence] Step 2 — destroying drops");
        DestroyAllActiveDrops();

        Debug.Log("[CompletionSequence] Step 3 — fading out UI");
        if (_catchZoneImage == null) Debug.LogError("[CompletionSequence] _catchZoneImage is NULL");
        if (_dropMeterController == null) Debug.LogError("[CompletionSequence] _dropMeterController is NULL");
        yield return StartCoroutine(FadeOutUI());

        // Must fire together — the pendant color and the spark recorded in PendantManager
        // should never be out of sync. Happens right as the game ends, before the overlay plays.
        SetPendantColor(_sparkColor);
        if (_pendantManager != null)
            _pendantManager.AwardSpark(_sparkType);
        else
            Debug.LogWarning("[CompletionSequence] _pendantManager not assigned — AwardSpark() not called.");

        Debug.Log("[CompletionSequence] Step 4 — fading in overlay");
        if (_completionOverlay == null) Debug.LogError("[CompletionSequence] _completionOverlay is NULL");
        yield return StartCoroutine(FadeInOverlay());

        yield return new WaitForSeconds(_holdDuration);

        Debug.Log("[CompletionSequence] Step 5 — fading out overlay");
        yield return StartCoroutine(FadeOutOverlay());

        yield return StartCoroutine(SparkRewardSequence());
    }

    // Runs right after the overlay has fully faded back out: the Water Spirit stops crying,
    // glow VFX (on the island that's already been sitting there since the page started), the
    // river spirit's line, then the island exit and final cleanup.
    private IEnumerator SparkRewardSequence()
    {
        Animator waterSpiritAnimator = _arTracker != null ? _arTracker.GetIslandWaterSpiritAnimator() : null;
        if (waterSpiritAnimator != null)
            waterSpiritAnimator.SetBool("complete", true);
        else
            Debug.LogWarning("[CompletionSequence] Island's Water Spirit Animator not found — is IslandVfxReference's Water Spirit Animator assigned, and _arTracker assigned?");

        yield return new WaitForSeconds(_vfxStartDelay);

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
            Debug.LogWarning("[CompletionSequence] Island's glow VFX not found — is IslandVfxReference on the island prefab, and _arTracker assigned?");
        }

        yield return new WaitForSeconds(_audioStartDelay);

        PlayVoiceLine();
        float audioLength = _riverSpiritClip != null ? _riverSpiritClip.length : 0f;

        // Wait until both the VFX and the audio line have finished — whichever takes longer.
        // The VFX has already been running for _audioStartDelay seconds at this point, so only
        // the remainder of its duration (if any) still needs to be waited out here.
        float remainingWait = Mathf.Max(vfxDuration - _audioStartDelay, audioLength);
        yield return new WaitForSeconds(Mathf.Max(0f, remainingWait));

        yield return new WaitForSeconds(_postRewardDelay);

        // Kick off the island's shrink-and-destroy — fire-and-forget, doesn't block on it
        // finishing (same exit timing as Page1Manager's original finish-group dismissal).
        if (_arTracker != null)
        {
            _arTracker.BeginIslandShrink();
            // Stops page4 from immediately re-triggering itself (and destroying the still-shrinking
            // island) if the marker is still being tracked once the lock below releases.
            _arTracker.MarkSequenceComplete();
        }
        else
        {
            Debug.LogWarning("[CompletionSequence] _arTracker not assigned — island shrink not triggered.");
        }

        if (glowVfx != null)
            glowVfx.gameObject.SetActive(false);

        // Sequence has fully played out — release the shared lock so scanning resumes.
        EndFeature();
    }

    private void SetPendantColor(Color color)
    {
        if (_pendantMaterial == null)
        {
            Debug.LogWarning("[CompletionSequence] _pendantMaterial not assigned — pendant color not set.");
            return;
        }

        _pendantMaterial.SetColor(s_baseColorId, color);
    }

    private IEnumerator FadeOutUI()
    {
        float elapsed = 0f;
        while (elapsed < _uiFadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / _uiFadeOutDuration);

            if (_catchZoneImage != null)
            {
                Color c = _catchZoneImage.color;
                _catchZoneImage.color = new Color(c.r, c.g, c.b, alpha);
            }

            if (_dropMeterController != null)
                _dropMeterController.SetAlpha(alpha);

            yield return null;
        }

        if (_catchZoneImage != null) _catchZoneImage.enabled = false;
        if (_dropMeterController != null) _dropMeterController.SetVisible(false);

        Debug.Log("[CompletionSequence] FadeOutUI done");
    }

    private IEnumerator FadeInOverlay()
    {
        if (_completionOverlay == null) yield break;

        SetOverlayAlpha(0f);
        _completionOverlay.enabled = true;
        _completionOverlay.gameObject.SetActive(true);
        _completionOverlay.transform.SetAsLastSibling();

        float elapsed = 0f;
        while (elapsed < _fadeInDuration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(Mathf.Clamp01(elapsed / _fadeInDuration));
            yield return null;
        }

        SetOverlayAlpha(1f);
        Debug.Log("[CompletionSequence] FadeInOverlay done");
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
        Debug.Log("[CompletionSequence] FadeOutOverlay done");
    }

    private void SetOverlayAlpha(float alpha)
    {
        Color c = _completionOverlay.color;
        _completionOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }

    private void PlayVoiceLine()
    {
        if (_voiceAudioSource != null && _riverSpiritClip != null)
            _voiceAudioSource.PlayOneShot(_riverSpiritClip);
        else
            Debug.LogWarning("[CompletionSequence] Voice audio source or clip not assigned.");
    }

    private void DestroyAllActiveDrops()
    {
        DropBehaviour[] activeDrops = FindObjectsByType<DropBehaviour>();
        Debug.Log("[CompletionSequence] Destroying " + activeDrops.Length + " active drops");
        foreach (DropBehaviour drop in activeDrops)
            Destroy(drop.gameObject);
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[CompletionSequence] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }
}
