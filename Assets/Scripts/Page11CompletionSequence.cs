using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Page 11 completion: fades a screen overlay, then reveals the Phoenix in its "transformed"
/// state. Instead of writing a spark to PendantManager, it swaps the grey Phoenix placeholder to
/// a bright rainbow-cycling material (stand-in for the Rainbow Phoenix model, which drops onto
/// the same object later).
/// </summary>
public class Page11CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image _completionOverlay;
    [Tooltip("Delay in seconds after all sparks are placed before the sequence begins.")]
    [SerializeField] private float _delayBeforeSequence = 17.5f;
    [SerializeField] private float _fadeInDuration  = 0.8f;
    [SerializeField] private float _holdDuration     = 0.5f;
    [SerializeField] private float _fadeOutDuration  = 0.8f;
    [Tooltip("If true, the overlay itself cycles hue while visible (fade-in/hold/fade-out), on top of its own alpha fade.")]
    [SerializeField] private bool  _overlayRainbowCycle = true;
    [SerializeField] private float _overlayRainbowSpeed = 0.4f;

    [Header("Phoenix Transformation")]
    [Tooltip("If true, the transformed Phoenix cycles hue (rainbow) instead of holding a solid colour.")]
    [SerializeField] private bool  _rainbowCycle = true;
    [SerializeField] private float _rainbowSpeed = 0.4f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Second voice line overall — plays this many seconds after the Phoenix starts turning rainbow, independent of Delay Before Sequence.")]
    [SerializeField] private AudioClip   _voiceClip;
    [SerializeField] private float       _voiceLineDelay = 1.5f;

    [Header("End of Sequence")]
    [Tooltip("Objects to completely hide at the end of the sequence (e.g., the sparks).")]
    [SerializeField] private GameObject[] _objectsToDisable;

    [Header("Island Exit")]
    [Tooltip("Used to look up the spawned island's Phoenix renderer/VFX and to trigger its shrink at the end.")]
    [SerializeField] private Page11ARTracker _arTracker;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private bool _triggered;
    private bool _transformed;

    // The Phoenix renderer lives inside the spawned island prefab (via Page11IslandReference),
    // not as a fixed scene reference, since it doesn't exist until the island is instantiated.
    // The Glow VFX's own rainbow-cycling is handled separately by Page11ARTracker, starting the
    // moment it begins playing rather than waiting for completion.
    private Renderer _phoenixRenderer;

    private MaterialPropertyBlock _mpb;
    private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        if (_completionOverlay != null)
        {
            SetOverlayAlpha(0f);
            _completionOverlay.enabled = false;
        }
    }

    private void Update()
    {
        if (_transformed && _rainbowCycle && _phoenixRenderer != null)
        {
            float hue = Mathf.Repeat(Time.time * _rainbowSpeed, 1f);
            SetPhoenixColor(Color.HSVToRGB(hue, 0.8f, 1f));
        }

        // Cycles the overlay's RGB only — alpha is left alone here so the fade in/hold/fade out
        // coroutines (which also write to _completionOverlay.color) keep full control of it.
        // enabled is only true while the overlay is actually mid-fade or held, so this naturally
        // stops doing anything once it's fully faded out.
        if (_overlayRainbowCycle && _completionOverlay != null && _completionOverlay.enabled)
        {
            float overlayHue = Mathf.Repeat(Time.time * _overlayRainbowSpeed, 1f);
            Color rgb = Color.HSVToRGB(overlayHue, 0.8f, 1f);
            Color current = _completionOverlay.color;
            _completionOverlay.color = new Color(rgb.r, rgb.g, rgb.b, current.a);
        }
    }

    private void SetPhoenixColor(Color color)
    {
        if (_phoenixRenderer == null) return;
        _mpb ??= new MaterialPropertyBlock();
        _phoenixRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(s_baseColorId, color);
        _phoenixRenderer.SetPropertyBlock(_mpb);
    }

    public void TriggerCompletion()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        TransformPhoenix();

        // Stop the Phoenix's rise VFX (and its Glow rainbow-cycling, driven by Page11ARTracker)
        // the instant all 4 sparks are attached, rather than waiting for the reveal later.
        ParticleSystem phoenixVfx = _arTracker != null ? _arTracker.GetPhoenixVfx() : null;
        if (phoenixVfx != null)
            phoenixVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Runs on its own timer, in parallel with the wait below, so it fires 1.5s after the
        // rainbow starts regardless of how long Delay Before Sequence is set to.
        StartCoroutine(PlayVoiceLineAfterDelay());

        if (_delayBeforeSequence > 0f)
            yield return new WaitForSeconds(_delayBeforeSequence);

        yield return StartCoroutine(FadeInOverlay());

        // The Phoenix stays visible and keeps rainbow-cycling (Update() keeps driving it as long
        // as _phoenixRenderer is non-null) all the way through until the island itself shrinks
        // and destroys it — only the sparks get hidden here, not the Phoenix.

        // The sparks' orbit celebration has no fixed duration of its own — it runs until told to
        // stop, so it stays in sync with however long the rainbow colour cycle above actually ran.
        // Stop it before hiding the sparks so the coroutine isn't still writing positions to
        // GameObjects that are now inactive.
        if (_arTracker != null)
        {
            _arTracker.StopSparkOrbit();
            _arTracker.HideSparks();
        }

        if (_objectsToDisable != null)
        {
            foreach (var obj in _objectsToDisable)
            {
                if (obj != null) obj.SetActive(false);
            }
        }

        yield return new WaitForSeconds(_holdDuration);

        yield return StartCoroutine(FadeOutOverlay());

        if (_completionOverlay != null)
            _completionOverlay.gameObject.SetActive(false);

        // Kick off the island's shrink-and-destroy — fire-and-forget, doesn't block on it
        // finishing (same exit timing as the other pages' island rewards).
        if (_arTracker != null)
        {
            _arTracker.BeginIslandShrink();
            // Stops page11 from immediately re-triggering itself (and destroying the
            // still-shrinking island) if the marker is still being tracked once the lock below
            // releases.
            _arTracker.MarkSequenceComplete();
        }
        else
        {
            Debug.LogWarning("[Page11CompletionSequence] _arTracker not assigned — island shrink not triggered.");
        }

        // Transition has fully played out — release the shared lock so scanning resumes.
        EndFeature();
    }

    private IEnumerator PlayVoiceLineAfterDelay()
    {
        yield return new WaitForSeconds(_voiceLineDelay);
        PlayVoiceLine();
    }

    private void PlayVoiceLine()
    {
        if (_audioSource != null && _voiceClip != null)
            _audioSource.PlayOneShot(_voiceClip);
        else
            Debug.LogWarning("[Page11CompletionSequence] AudioSource or VoiceClip not assigned.");
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page11CompletionSequence] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    private void TransformPhoenix()
    {
        _transformed = true;

        _phoenixRenderer = _arTracker != null ? _arTracker.GetPhoenixRenderer() : null;
        if (_phoenixRenderer == null)
        {
            Debug.LogWarning("[Page11CompletionSequence] Island's Phoenix renderer not found — is Page11IslandReference on the island prefab, and _arTracker assigned?");
            return;
        }

        // Immediate bright colour; Update() takes over the rainbow cycle if enabled.
        SetPhoenixColor(Color.HSVToRGB(0f, 0.8f, 1f));
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
        if (_completionOverlay == null) return;
        Color c = _completionOverlay.color;
        _completionOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }
}
