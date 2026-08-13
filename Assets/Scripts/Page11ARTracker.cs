using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Handles AR tracking for Page 11, spawning the island (Phoenix + 4 draggable spark orbs baked
/// in via Page11IslandReference) and managing UI visibility based on the page11 target — mirrors
/// Page10ARTracker's island spawn/instruction-prompt/shrink pattern.
/// </summary>
public class Page11ARTracker : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;

    [Header("Page 11")]
    [SerializeField] private Page11DragController _dragController;

    [Header("Island")]
    [SerializeField] private GameObject _islandPrefab;
    [SerializeField] private float _islandXOffset = 0.0f;
    [SerializeField] private float _islandYOffset = 0.1f;
    [SerializeField] private float _islandZOffset = 0.0f;
    [SerializeField] private float _islandScale = 1.0f;
    [SerializeField] private Vector3 _islandRotation = Vector3.zero;
    [Tooltip("Wait after the island's shrink is triggered before it actually starts shrinking.")]
    [SerializeField] private float _islandFadeOutDelay = 1.5f;
    [SerializeField] private float _islandFadeOutDuration = 1.0f;

    [Header("Instructions")]
    [Tooltip("Wait after the first voice line finishes playing before the first instruction prompt appears.")]
    [SerializeField] private float _instructionsDelayAfterVoiceLine = 0.7f;
    [Tooltip("Needs a Graphic (e.g. Image) with Raycast Target on, under a Canvas with a GraphicRaycaster, so tap-anywhere dismissal works.")]
    [SerializeField] private GameObject _instructionPrompt;
    [Tooltip("Shown immediately (no delay) once the first instruction prompt is dismissed. Same tap-anywhere requirements as the first.")]
    [SerializeField] private GameObject _instructionPrompt2;

    [Header("Phoenix Rise")]
    [Tooltip("Wait after the island spawns before the Phoenix's VFX starts and it rises.")]
    [SerializeField] private float _islandSpawnDelay = 1.0f;
    [Tooltip("How far above its authored (initial) position the Phoenix rises.")]
    [SerializeField] private float _phoenixRiseHeight = 0.5f;
    [SerializeField] private float _phoenixRiseDuration = 2.0f;
    [Tooltip("How many degrees the Phoenix turns around its Y axis while it rises. Set to 0 for no turn.")]
    [SerializeField] private float _phoenixRiseYRotation = 360f;
    [Tooltip("Continuously cycles the Phoenix VFX's \"Glow\" child through hues, starting the instant it begins playing (not gated behind completion).")]
    [SerializeField] private float _glowRainbowSpeed = 0.4f;

    [Header("Audio")]
    [Tooltip("Plays the instant the Phoenix starts rising. First of several voice lines planned across the page — more will get their own AudioSource/AudioClip pair at their own trigger points as they're added.")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _voiceClip;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private GameObject _islandInstance;
    private Page11IslandReference _islandReference;
    private Coroutine _islandFadeOutCoroutine;
    private Coroutine _introCoroutine;
    private Coroutine _instructionsDelayCoroutine;
    private Coroutine _phoenixRiseCoroutine;

    // The Phoenix VFX is actually several sibling particle systems (Crystal/Glow/Sparks/Flares).
    // Only the "Glow" child's Start Color rainbow-cycles — Crystal/Sparks/Flares are left alone.
    // Cached once (not searched every frame) when the VFX starts playing.
    private ParticleSystem _phoenixGlowVfx;

    // Hue (~240°) the Glow rainbow starts on so it always opens on blue instead of whatever
    // colour Time.time happens to land on. Cycling is measured from _glowRainbowStartTime so the
    // first frame is exactly this hue, not an arbitrary point partway through the cycle.
    private const float GlowRainbowStartHue = 0.6667f;
    private float _glowRainbowStartTime;

    // Mirrors "do I currently hold the shared AppStateManager lock" for the cancel guard.
    // Self-heals via HandleFeatureActiveChanged so a stale true can never linger past this
    // page's own completion or cancellation.
    private bool _isActive;

    // Set when the child backs out via Back; blocks this page re-triggering while its image
    // stays in view, cleared once the image leaves tracking so looking back re-arms it.
    private bool _suppressedWhileTracked;

    private void Awake()
    {
        if (_trackedImageManager == null)
            _trackedImageManager = GetComponent<ARTrackedImageManager>();

        HideUI();

        if (_instructionPrompt != null)
        {
            _instructionPrompt.SetActive(false);
            PromptTapHandler tapHandler = _instructionPrompt.AddComponent<PromptTapHandler>();
            tapHandler.OnTap = ShowSecondInstructionPrompt;
        }
        else
        {
            Debug.LogError("[Page11ARTracker] _instructionPrompt not assigned.");
        }

        if (_instructionPrompt2 != null)
        {
            _instructionPrompt2.SetActive(false);
            PromptTapHandler tapHandler2 = _instructionPrompt2.AddComponent<PromptTapHandler>();
            tapHandler2.OnTap = HideInstructionPrompt;
        }
        else
        {
            Debug.LogError("[Page11ARTracker] _instructionPrompt2 not assigned.");
        }
    }

    private void OnEnable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);

        if (_appStateManager != null)
        {
            _appStateManager.OnFeatureCancelled += HandleFeatureCancelled;
            _appStateManager.OnFeatureActiveChanged += HandleFeatureActiveChanged;
        }
    }

    private void Update()
    {
        if (_phoenixGlowVfx == null) return;

        float hue = Mathf.Repeat(GlowRainbowStartHue + (Time.time - _glowRainbowStartTime) * _glowRainbowSpeed, 1f);
        ParticleSystem.MainModule main = _phoenixGlowVfx.main;
        main.startColor = Color.HSVToRGB(hue, 0.8f, 1f);
    }

    private void OnDisable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);

        if (_appStateManager != null)
        {
            _appStateManager.OnFeatureCancelled -= HandleFeatureCancelled;
            _appStateManager.OnFeatureActiveChanged -= HandleFeatureActiveChanged;
        }
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage image in args.added)
        {
            if (image.referenceImage.name != "page11") continue;
            if (_suppressedWhileTracked) continue; // backed out; wait for a fresh acquisition
            if (!TryBeginFeature()) continue;

            BeginPreGameSequence(image.transform);
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page11") continue;

            // Re-arm as soon as the image stops being solidly tracked. XR Simulation / ARCore
            // usually report Limited (not None) on look-away, so keying only off None left the
            // suppression stuck and the feature never reappeared. ponytail: clears on the first
            // non-Tracking frame — heavy tracking flicker could re-arm early; add a short debounce
            // if that shows up on-device.
            if (image.trackingState != TrackingState.Tracking)
                _suppressedWhileTracked = false;

            if (image.trackingState == TrackingState.Tracking)
            {
                if (_suppressedWhileTracked) continue; // backed out; wait for a fresh acquisition
                if (!TryBeginFeature()) continue;

                BeginPreGameSequence(image.transform);
            }
            else if (image.trackingState == TrackingState.None)
            {
                CancelPreGameSequence();
                CancelIslandReward();
                HideUI();
                ReleaseLockIfHeld();
            }
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != "page11") continue;

            _suppressedWhileTracked = false; // image left view → re-arm so looking back replays
            CancelPreGameSequence();
            CancelIslandReward();
            HideUI();
            ReleaseLockIfHeld();
        }
    }

    // Entry point once this page claims the shared lock: spawn the island (Phoenix + sparks
    // become visible but not yet draggable — see HideInstructionPrompt for when that flips on),
    // wait, then start the Phoenix rise. The instructions no longer gate the rise — they show up
    // later, after the first voice line.
    private void BeginPreGameSequence(Transform anchor)
    {
        SpawnIsland(anchor);
        _introCoroutine = StartCoroutine(WaitThenStartPhoenixRise());
    }

    private IEnumerator WaitThenStartPhoenixRise()
    {
        yield return new WaitForSeconds(_islandSpawnDelay);
        _introCoroutine = null;
        StartPhoenixRise();
    }

    private void ShowInstructionPrompt()
    {
        if (_instructionPrompt == null)
        {
            Debug.LogError("[Page11ARTracker] _instructionPrompt is NULL — skipping the instructions entirely.");
            return;
        }

        _instructionPrompt.SetActive(true);
    }

    // Wired to the first prompt's tap handler. Dismisses it and seamlessly shows the second
    // prompt — no delay between them.
    public void ShowSecondInstructionPrompt()
    {
        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);

        if (_instructionPrompt2 == null)
        {
            Debug.LogError("[Page11ARTracker] _instructionPrompt2 is NULL.");
            return;
        }

        _instructionPrompt2.SetActive(true);
    }

    // Wired to the second prompt's tap handler. Public so it can also be wired to a Button's
    // OnClick() in the Inspector (same pattern as Page4ARTracker/Page6SpeechController). Sparks
    // only become draggable once this fires, not the instant the island spawns.
    public void HideInstructionPrompt()
    {
        if (_instructionPrompt2 != null) _instructionPrompt2.SetActive(false);
        ShowUI();
    }

    // Starts the Phoenix's VFX looping (kept playing — not stopped — through the whole drag
    // phase; Page11CompletionSequence decides what happens to it after that), animates the
    // Phoenix up from below into its authored resting position, and queues the instructions to
    // appear a short delay after the voice line playing alongside it finishes.
    private void StartPhoenixRise()
    {
        PlayVoiceLine();

        float voiceLineLength = _voiceClip != null ? _voiceClip.length : 0f;
        _instructionsDelayCoroutine = StartCoroutine(WaitThenShowInstructions(voiceLineLength + _instructionsDelayAfterVoiceLine));

        if (_islandReference == null)
        {
            Debug.LogWarning("[Page11ARTracker] No island reference — Phoenix rise/VFX skipped.");
            return;
        }

        if (_islandReference.PhoenixVfx != null)
        {
            _islandReference.PhoenixVfx.gameObject.SetActive(true);
            _islandReference.PhoenixVfx.Play(true); // withChildren; loops until explicitly stopped

            _phoenixGlowVfx = null;
            foreach (ParticleSystem ps in _islandReference.PhoenixVfx.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.gameObject.name == "Glow")
                {
                    _phoenixGlowVfx = ps;
                    break;
                }
            }

            if (_phoenixGlowVfx == null)
                Debug.LogWarning("[Page11ARTracker] No child named 'Glow' found under the Phoenix VFX — its Start Color won't rainbow-cycle.");
            else
                _glowRainbowStartTime = Time.time;
        }
        else
        {
            Debug.LogWarning("[Page11ARTracker] Island's Phoenix VFX not assigned on Page11IslandReference.");
        }

        if (_islandReference.PhoenixTransform != null)
            _phoenixRiseCoroutine = StartCoroutine(RisePhoenix(_islandReference.PhoenixTransform));
        else
            Debug.LogWarning("[Page11ARTracker] Island's Phoenix transform not assigned on Page11IslandReference — rise animation skipped.");
    }

    private void PlayVoiceLine()
    {
        if (_audioSource != null && _voiceClip != null)
            _audioSource.PlayOneShot(_voiceClip);
        else
            Debug.LogWarning("[Page11ARTracker] AudioSource or VoiceClip not assigned.");
    }

    private IEnumerator WaitThenShowInstructions(float delay)
    {
        yield return new WaitForSeconds(delay);
        _instructionsDelayCoroutine = null;
        ShowInstructionPrompt();
    }

    private IEnumerator RisePhoenix(Transform phoenix)
    {
        Vector3 startLocalPos = phoenix.localPosition; // authored (initial) position from the prefab
        Vector3 targetLocalPos = startLocalPos + Vector3.up * _phoenixRiseHeight;

        // Interpolate the Y degree offset directly rather than Slerp-ing between quaternions —
        // Slerp always takes the shortest path, so a 360° turn (or any exact multiple of it)
        // would be indistinguishable from no turn at all and silently do nothing.
        Quaternion startRotation = phoenix.localRotation;

        float elapsed = 0f;
        while (elapsed < _phoenixRiseDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _phoenixRiseDuration);
            phoenix.localPosition = Vector3.Lerp(startLocalPos, targetLocalPos, t);

            float yOffset = Mathf.Lerp(0f, _phoenixRiseYRotation, t);
            phoenix.localRotation = startRotation * Quaternion.Euler(0f, yOffset, 0f);
            yield return null;
        }

        phoenix.localPosition = targetLocalPos;
        phoenix.localRotation = startRotation * Quaternion.Euler(0f, _phoenixRiseYRotation, 0f);
        _phoenixRiseCoroutine = null;
    }

    // Stops whichever part of the island-intro-through-instructions is currently in progress.
    // Called on Back cancellation and on tracking loss so nothing is left stuck on screen.
    private void CancelPreGameSequence()
    {
        if (_introCoroutine != null)
        {
            StopCoroutine(_introCoroutine);
            _introCoroutine = null;
        }
        if (_instructionsDelayCoroutine != null)
        {
            StopCoroutine(_instructionsDelayCoroutine);
            _instructionsDelayCoroutine = null;
        }
        if (_phoenixRiseCoroutine != null)
        {
            // Without this, the rise kept animating a Transform that CancelIslandReward() was
            // about to destroy, throwing a MissingReferenceException on the next frame.
            StopCoroutine(_phoenixRiseCoroutine);
            _phoenixRiseCoroutine = null;
        }

        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);
        if (_instructionPrompt2 != null) _instructionPrompt2.SetActive(false);
    }

    // --- Island ---

    private void SpawnIsland(Transform anchor)
    {
        DespawnIsland();

        if (_islandPrefab == null)
        {
            Debug.LogError("[Page11ARTracker] _islandPrefab not assigned.");
            return;
        }

        Vector3 position = anchor.position + new Vector3(_islandXOffset, _islandYOffset, _islandZOffset);
        _islandInstance = Instantiate(_islandPrefab, position, Quaternion.Euler(_islandRotation), anchor);
        _islandInstance.transform.localScale *= _islandScale;

        _islandReference = _islandInstance.GetComponent<Page11IslandReference>();
        if (_islandReference == null)
        {
            Debug.LogWarning("[Page11ARTracker] _islandPrefab has no Page11IslandReference — no sparks, Phoenix rise, or VFX will work.");
        }
        else
        {
            if (_islandReference.PhoenixVfx != null)
            {
                // ParticleSystems default to "Play On Awake", so left alone this fires the instant
                // the island is instantiated — stop it immediately so it stays silent until the
                // Phoenix rise explicitly plays it later.
                _islandReference.PhoenixVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _islandReference.PhoenixVfx.gameObject.SetActive(false);
            }

            if (_dragController == null) Debug.LogError("[Page11ARTracker] _dragController is NULL");
            else _dragController.Initialize(_islandReference.Sparks, _islandReference.PhoenixCollider);
        }
    }

    private void DespawnIsland()
    {
        if (_islandInstance != null)
            Destroy(_islandInstance);
        _islandInstance = null;
        _islandReference = null;
        _phoenixGlowVfx = null;

        if (_dragController != null)
            _dragController.Initialize(null, null);
    }

    // Called by Page11CompletionSequence to look up the spawned island's Phoenix renderer (for
    // the rainbow colour cycle) — it doesn't exist until the island is instantiated.
    public Renderer GetPhoenixRenderer()
    {
        return _islandReference != null ? _islandReference.PhoenixRenderer : null;
    }

    // Called by Page11CompletionSequence once the screen is fully covered, so the sparks
    // disappear alongside the Phoenix instead of staying visible after the reveal.
    public void HideSparks()
    {
        if (_dragController != null)
            _dragController.HideSparks();
    }

    // Called by Page11CompletionSequence to stop the Phoenix's looping VFX once the drag phase
    // is over and the reveal sequence takes over.
    public ParticleSystem GetPhoenixVfx()
    {
        return _islandReference != null ? _islandReference.PhoenixVfx : null;
    }

    // Called by Page11CompletionSequence the moment the Phoenix stops being visible, so the
    // sparks' orbit celebration ends in sync with however long the rainbow colour cycle ran for.
    public void StopSparkOrbit()
    {
        if (_dragController != null)
            _dragController.StopOrbitCelebration();
    }

    // Called by Page11CompletionSequence once the reward sequence reaches its island-exit step.
    // Fire-and-forget — the caller doesn't wait for the shrink to finish.
    public void BeginIslandShrink()
    {
        if (_islandFadeOutCoroutine != null) return;
        _islandFadeOutCoroutine = StartCoroutine(FadeOutIslandThenDestroy());
    }

    // Called by Page11CompletionSequence right before it releases the shared lock, so this page
    // doesn't immediately re-trigger itself (and destroy the still-shrinking island) if the
    // marker is still being tracked once the lock releases — same pattern as Page4/6/10.
    public void MarkSequenceComplete()
    {
        _suppressedWhileTracked = true;
    }

    private IEnumerator FadeOutIslandThenDestroy()
    {
        yield return new WaitForSeconds(_islandFadeOutDelay);

        if (_islandInstance != null)
        {
            Renderer[] renderers = _islandInstance.GetComponentsInChildren<Renderer>();
            Vector3 centerPoint = _islandInstance.transform.position;
            if (renderers.Length > 0)
            {
                Bounds combinedBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    combinedBounds.Encapsulate(renderers[i].bounds);
                centerPoint = combinedBounds.center;
            }

            Vector3 startScale = _islandInstance.transform.localScale;
            Vector3 startPosition = _islandInstance.transform.position;
            float elapsed = 0f;

            while (elapsed < _islandFadeOutDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _islandFadeOutDuration);
                float scaleFactor = 1f - t;

                _islandInstance.transform.localScale = startScale * scaleFactor;
                _islandInstance.transform.position = Vector3.Lerp(centerPoint, startPosition, scaleFactor);
                yield return null;
            }
        }

        DespawnIsland();
        _islandFadeOutCoroutine = null;
    }

    // Stops the island wherever it's at and removes it immediately. Called on Back cancellation
    // and on tracking loss so nothing is left mid-air or mid-fade.
    private void CancelIslandReward()
    {
        if (_islandFadeOutCoroutine != null)
        {
            StopCoroutine(_islandFadeOutCoroutine);
            _islandFadeOutCoroutine = null;
        }

        DespawnIsland();
    }

    // Ignore the scan outright if another page's feature is already active; only
    // Page11CompletionSequence releases the shared lock, so tracking loss/regain mid-game
    // won't silently reclaim it.
    private bool TryBeginFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[Page11ARTracker] _appStateManager not assigned; ignoring scan.");
            return false;
        }
        bool claimed = _appStateManager.TryBeginFeature();
        if (claimed) _isActive = true;
        return claimed;
    }

    private void HandleFeatureActiveChanged(bool isActive)
    {
        if (!isActive) _isActive = false;
    }

    // Back button pressed mid-feature. Only react if this page is the one actually running —
    // the shared cancel event reaches all six page scripts. Page 11 never awards a PendantManager
    // spark for partial progress (only the Phoenix transform, gated on all 4 sparks applied), so
    // there's no credit to worry about beyond releasing whatever spark is mid-drag.
    private void HandleFeatureCancelled()
    {
        if (!_isActive) return;

        CancelPreGameSequence();
        CancelIslandReward();

        if (_dragController == null) Debug.LogError("[Page11ARTracker] _dragController is NULL");
        else _dragController.CancelDrag();

        HideUI();
        _suppressedWhileTracked = true;
        _isActive = false;
        EndFeature();
    }

    // Releases the shared lock on genuine tracking loss (not the Back button — HandleFeatureCancelled
    // already covers that), guarded on _isActive so this page can never release a lock another page
    // holds. Without this, TryBeginFeature() would keep failing forever after a single look-away,
    // even though _suppressedWhileTracked's own comment promises "looking back replays."
    private void ReleaseLockIfHeld()
    {
        if (!_isActive) return;
        _isActive = false;
        EndFeature();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page11ARTracker] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    private void ShowUI()
    {
        if (_dragController == null) Debug.LogError("[Page11ARTracker] _dragController is NULL");
        else _dragController.SetActive(true);
    }

    private void HideUI()
    {
        if (_dragController == null) Debug.LogError("[Page11ARTracker] _dragController is NULL");
        else _dragController.SetActive(false);
    }

    // Runtime-attached to _instructionPrompt so any tap on it dismisses it, without requiring
    // a Button to be set up in the Inspector (same trick Page4ARTracker/Page6SpeechController use).
    private class PromptTapHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnTap;
        public void OnPointerClick(PointerEventData eventData) => OnTap?.Invoke();
    }
}
