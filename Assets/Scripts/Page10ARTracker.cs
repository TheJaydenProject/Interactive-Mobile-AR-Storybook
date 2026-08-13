using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Handles AR tracking for Page 10, spawning the island and managing UI visibility based on the
/// page10 target — mirrors Page4ARTracker's island spawn/instruction-prompt/shrink pattern.
/// </summary>
public class Page10ARTracker : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;

    [Header("Page 10")]
    [SerializeField] private Page10MeterController _page10MeterController;
    [SerializeField] private Page10OrbController _orbController;

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
    [Tooltip("Wait after the island spawns before the instruction prompt appears.")]
    [SerializeField] private float _islandSpawnDelay = 1.0f;
    [Tooltip("Shown after the island spawn delay. Needs a Graphic (e.g. Image) with Raycast Target on, under a Canvas with a GraphicRaycaster, so tap-anywhere dismissal works.")]
    [SerializeField] private GameObject _instructionPrompt;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private GameObject _islandInstance;
    private IslandVfxReference _islandVfxReference;
    private Coroutine _islandFadeOutCoroutine;
    private Coroutine _introCoroutine;

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
            tapHandler.OnTap = HideInstructionPrompt;
        }
        else
        {
            Debug.LogError("[Page10ARTracker] _instructionPrompt not assigned.");
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
            if (image.referenceImage.name != "page10") continue;
            if (_suppressedWhileTracked) continue; // backed out; wait for a fresh acquisition
            if (!TryBeginFeature()) continue;

            BeginPreGameSequence(image.transform);
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page10") continue;

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
            }
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != "page10") continue;

            _suppressedWhileTracked = false; // image left view → re-arm so looking back replays
            CancelPreGameSequence();
            CancelIslandReward();
            HideUI();
        }
    }

    // Entry point once this page claims the shared lock: spawn the island immediately, wait,
    // then show the instructions, and only after those are dismissed does the meter/orbs
    // actually appear for the catching game.
    private void BeginPreGameSequence(Transform anchor)
    {
        SpawnIsland(anchor);
        _introCoroutine = StartCoroutine(WaitThenShowInstructions());
    }

    private IEnumerator WaitThenShowInstructions()
    {
        yield return new WaitForSeconds(_islandSpawnDelay);
        _introCoroutine = null;
        ShowInstructionPrompt();
    }

    private void ShowInstructionPrompt()
    {
        if (_instructionPrompt == null)
        {
            Debug.LogError("[Page10ARTracker] _instructionPrompt is NULL — skipping straight to the game.");
            ShowUI();
            return;
        }

        _instructionPrompt.SetActive(true);
    }

    // Public so it can also be wired to a Button's OnClick() in the Inspector (same pattern as
    // Page4ARTracker/Page6SpeechController), in addition to the runtime tap-anywhere handler below.
    public void HideInstructionPrompt()
    {
        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);
        ShowUI();
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

        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);
    }

    // --- Island ---

    private void SpawnIsland(Transform anchor)
    {
        DespawnIsland();

        if (_islandPrefab == null)
        {
            Debug.LogError("[Page10ARTracker] _islandPrefab not assigned.");
            return;
        }

        Vector3 position = anchor.position + new Vector3(_islandXOffset, _islandYOffset, _islandZOffset);
        _islandInstance = Instantiate(_islandPrefab, position, Quaternion.Euler(_islandRotation), anchor);
        _islandInstance.transform.localScale *= _islandScale;

        _islandVfxReference = _islandInstance.GetComponent<IslandVfxReference>();
        if (_islandVfxReference == null)
        {
            Debug.LogWarning("[Page10ARTracker] _islandPrefab has no IslandVfxReference — Page10CompletionSequence's glow VFX won't play, and no orbs will be catchable.");
        }
        else
        {
            if (_islandVfxReference.GlowVfx != null)
            {
                // ParticleSystems default to "Play On Awake", so left alone this fires the instant
                // the island is instantiated — stop it immediately so it stays silent until
                // Page10CompletionSequence explicitly plays it later.
                _islandVfxReference.GlowVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _islandVfxReference.GlowVfx.gameObject.SetActive(false);
            }

            if (_orbController == null) Debug.LogError("[Page10ARTracker] _orbController is NULL");
            else _orbController.SetOrbs(_islandVfxReference.Orbs);
        }
    }

    private void DespawnIsland()
    {
        if (_islandInstance != null)
            Destroy(_islandInstance);
        _islandInstance = null;
        _islandVfxReference = null;

        if (_orbController != null)
            _orbController.SetOrbs(null);
    }

    // Called by Page10CompletionSequence right before it needs to play the glow VFX — the VFX
    // lives inside the island prefab itself, so it only exists once the island is spawned.
    public ParticleSystem GetIslandGlowVfx()
    {
        return _islandVfxReference != null ? _islandVfxReference.GlowVfx : null;
    }

    // Called by Page10CompletionSequence once the reward sequence reaches its island-exit step.
    // Fire-and-forget — the caller doesn't wait for the shrink to finish.
    public void BeginIslandShrink()
    {
        if (_islandFadeOutCoroutine != null) return;
        _islandFadeOutCoroutine = StartCoroutine(FadeOutIslandThenDestroy());
    }

    // Called by Page10CompletionSequence right before it releases the shared lock, so this page
    // doesn't immediately re-trigger itself (spawning a fresh island and orbs mid-shrink) while
    // still being continuously tracked — same suppression Page4ARTracker uses. Naturally re-arms
    // once tracking is lost and regained, so scanning page10 again later still replays it.
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
    // Page10CompletionSequence releases the shared lock, so tracking loss/regain mid-game
    // won't silently reclaim it.
    private bool TryBeginFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[Page10ARTracker] _appStateManager not assigned; ignoring scan.");
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
    // the shared cancel event reaches all six page scripts. Orb catches don't award partial
    // credit either way (the Gold spark only fires at 10/10 in Page10CompletionSequence).
    private void HandleFeatureCancelled()
    {
        if (!_isActive) return;

        CancelPreGameSequence();
        CancelIslandReward();
        HideUI(); // stops the orb controller's Update loop
        _suppressedWhileTracked = true;
        _isActive = false;
        EndFeature();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page10ARTracker] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    private void ShowUI()
    {
        if (_page10MeterController == null) Debug.LogError("[Page10ARTracker] _page10MeterController is NULL");
        else _page10MeterController.SetVisible(true);

        if (_orbController == null) Debug.LogError("[Page10ARTracker] _orbController is NULL");
        else _orbController.SetActive(true);
    }

    private void HideUI()
    {
        if (_page10MeterController == null) Debug.LogError("[Page10ARTracker] _page10MeterController is NULL");
        else _page10MeterController.SetVisible(false);

        if (_orbController == null) Debug.LogError("[Page10ARTracker] _orbController is NULL");
        else _orbController.SetActive(false);
    }

    // Runtime-attached to _instructionPrompt so any tap on it dismisses it, without requiring
    // a Button to be set up in the Inspector (same trick Page4ARTracker/Page6SpeechController use).
    private class PromptTapHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnTap;
        public void OnPointerClick(PointerEventData eventData) => OnTap?.Invoke();
    }
}
