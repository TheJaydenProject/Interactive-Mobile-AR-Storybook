using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class Page8ShadowController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image _shadowImage;

    [Header("AR References")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;
    [SerializeField] private string _targetImageName = "page8";

    [Header("Island")]
    [SerializeField] private GameObject _islandPrefab;
    [SerializeField] private float _islandXOffset = 0.0f;
    [SerializeField] private float _islandYOffset = 0.1f;
    [SerializeField] private float _islandZOffset = 0.0f;
    [Tooltip("Wait after the shadow first appears (unclickable) before the instruction prompt shows.")]
    [SerializeField] private float _shadowToPromptDelay = 0.5f;
    [SerializeField] private float _islandScale = 1.0f;
    [SerializeField] private Vector3 _islandRotation = Vector3.zero;
    [Tooltip("Wait after the island's shrink is triggered before it actually starts shrinking.")]
    [SerializeField] private float _islandFadeOutDelay = 1.5f;
    [SerializeField] private float _islandFadeOutDuration = 1.0f;

    [Header("Instructions")]
    [Tooltip("Shown after _shadowToPromptDelay. Needs a Graphic (e.g. Image) with Raycast Target on, under a Canvas with a GraphicRaycaster, so tap-anywhere dismissal works.")]
    [SerializeField] private GameObject _instructionPrompt;
    [Tooltip("Delay after the prompt is dismissed before the shadow becomes interactable (holdable).")]
    [SerializeField] private float _interactableDelay = 0.25f;

    [Header("Settings")]
    [SerializeField] private float _holdDuration = 3f;
    [SerializeField] private float _fadeDuration = 1f;
    [SerializeField] private float _preFadeShrinkDuration = 0.4f;
    [SerializeField] private AnimationCurve _morphCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Dependencies")]
    [SerializeField] private Page8CompletionSequence _completionSequence;
    [SerializeField] private PendantManager _pendantManager;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private bool _isCompleted;
    private Material _morphMaterial;
    private float _holdProgress;
    private bool _isHolding;

    // The shadow is visible as soon as the pre-shadow sequence begins, but shouldn't respond to
    // touch until the instruction prompt has been shown and dismissed — this gates
    // HandlePointerDown/Up separately from _shadowImage.enabled (which only controls visibility).
    private bool _shadowInteractable;

    private GameObject _islandInstance;
    private IslandVfxReference _islandVfxReference;
    private Coroutine _islandIntroCoroutine;
    private Coroutine _postPromptCoroutine;
    private Coroutine _islandFadeOutCoroutine;

    // Mirrors "do I currently hold the shared AppStateManager lock" for the cancel guard, and
    // also gates Update() so its per-frame hold/morph work doesn't run before the shadow is
    // actually shown.
    private bool _isActive;

    // Set when the child backs out via Back; blocks this page re-triggering while its image
    // stays in view, cleared once the image leaves tracking so looking back re-arms it.
    private bool _suppressedWhileTracked;

    private void Awake()
    {
        if (_shadowImage != null)
        {
            var handler = _shadowImage.gameObject.AddComponent<ShadowPointerHandler>();
            handler.OnPointerDownAction = HandlePointerDown;
            handler.OnPointerUpAction = HandlePointerUp;
            _shadowImage.enabled = false;

            if (_shadowImage.material != null)
            {
                _morphMaterial = new Material(_shadowImage.material);
                _shadowImage.material = _morphMaterial;
            }
        }
        else
        {
            Debug.LogWarning("[Page8ShadowController] _shadowImage is missing.");
        }

        if (_instructionPrompt != null)
        {
            _instructionPrompt.SetActive(false);
            PromptTapHandler tapHandler = _instructionPrompt.AddComponent<PromptTapHandler>();
            tapHandler.OnTap = HideInstructionPrompt;
        }
        else
        {
            Debug.LogError("[Page8ShadowController] _instructionPrompt not assigned.");
        }
    }

    private void OnEnable()
    {
        if (_trackedImageManager != null)
        {
            _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }
        else
        {
            Debug.LogWarning("[Page8ShadowController] _trackedImageManager is missing.");
        }

        if (_appStateManager != null)
        {
            _appStateManager.OnFeatureCancelled += HandleFeatureCancelled;
            _appStateManager.OnFeatureActiveChanged += HandleFeatureActiveChanged;
        }
    }

    private void OnDisable()
    {
        if (_trackedImageManager != null)
        {
            _trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        if (_appStateManager != null)
        {
            _appStateManager.OnFeatureCancelled -= HandleFeatureCancelled;
            _appStateManager.OnFeatureActiveChanged -= HandleFeatureActiveChanged;
        }
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        foreach (var trackedImage in eventArgs.added)
        {
            CheckImage(trackedImage);
        }
        foreach (var trackedImage in eventArgs.updated)
        {
            CheckImage(trackedImage);
        }
        foreach (var removed in eventArgs.removed)
        {
            // Image dropped entirely — re-arm so looking back replays. The removed collection
            // yields KeyValuePair<TrackableId, ARTrackedImage>, so reach the image via .Value.
            if (removed.Value.referenceImage.name != _targetImageName) continue;
            _suppressedWhileTracked = false;
            if (IsSequenceInProgress) CancelSequence();
            ReleaseLockIfHeld();
        }
    }

    private void CheckImage(ARTrackedImage trackedImage)
    {
        if (_isCompleted) return;
        if (trackedImage.referenceImage.name != _targetImageName) return;

        // Re-arm as soon as the image stops being solidly tracked. XR Simulation / ARCore
        // usually report Limited (not None) on look-away, so keying only off None left the
        // suppression stuck and the feature never reappeared. ponytail: clears on the first
        // non-Tracking frame — heavy tracking flicker could re-arm early; add a short debounce
        // if that shows up on-device.
        if (trackedImage.trackingState != TrackingState.Tracking)
            _suppressedWhileTracked = false;

        if (trackedImage.trackingState == TrackingState.Tracking)
        {
            // Backed out of this page but still pointed at it — stay quiet until re-acquired.
            if (_suppressedWhileTracked) return;

            // Ignore the scan outright if another page's feature is already active, or if this
            // page's own sequence is already running; only this page's own completion releases
            // the shared lock.
            if (IsSequenceInProgress) return;
            if (!TryBeginFeature()) return;

            BeginPreShadowSequence(trackedImage.transform);
        }
        else if (trackedImage.trackingState == TrackingState.None && IsSequenceInProgress)
        {
            CancelSequence();
            ReleaseLockIfHeld();
        }
    }

    // True while any stage from island spawn through the shadow hold is in progress, so the
    // tracking-update loop doesn't re-trigger a new pass over an already-running one.
    private bool IsSequenceInProgress =>
        _islandIntroCoroutine != null
        || (_instructionPrompt != null && _instructionPrompt.activeSelf)
        || _postPromptCoroutine != null
        || (_shadowImage != null && _shadowImage.enabled);

    // Entry point once this page claims the shared lock: spawn the island, show the shadow
    // immediately (visible but not yet interactable), wait, then show the instructions, and
    // only after those are dismissed (plus a short buffer) does the shadow actually start
    // responding to touch.
    private void BeginPreShadowSequence(Transform anchor)
    {
        SpawnIsland(anchor);
        ShowShadow();
        _islandIntroCoroutine = StartCoroutine(WaitThenShowInstructions());
    }

    private IEnumerator WaitThenShowInstructions()
    {
        yield return new WaitForSeconds(_shadowToPromptDelay);
        _islandIntroCoroutine = null;
        ShowInstructionPrompt();
    }

    private void ShowInstructionPrompt()
    {
        if (_instructionPrompt == null)
        {
            Debug.LogError("[Page8ShadowController] _instructionPrompt is NULL — skipping straight to interactable.");
            EnableInteraction();
            return;
        }

        _instructionPrompt.SetActive(true);
    }

    // Public so it can also be wired to a Button's OnClick() in the Inspector (same pattern as
    // Page4ARTracker/Page6SpeechController), in addition to the runtime tap-anywhere handler below.
    public void HideInstructionPrompt()
    {
        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);
        _postPromptCoroutine = StartCoroutine(EnableInteractionAfterDelay());
    }

    private IEnumerator EnableInteractionAfterDelay()
    {
        yield return new WaitForSeconds(_interactableDelay);
        _postPromptCoroutine = null;
        EnableInteraction();
    }

    private void ShowShadow()
    {
        _shadowInteractable = false;
        if (_shadowImage != null) _shadowImage.enabled = true;
    }

    private void EnableInteraction()
    {
        _shadowInteractable = true;
    }

    // --- Island ---

    private void SpawnIsland(Transform anchor)
    {
        DespawnIsland();

        if (_islandPrefab == null)
        {
            Debug.LogError("[Page8ShadowController] _islandPrefab not assigned.");
            return;
        }

        Vector3 position = anchor.position + new Vector3(_islandXOffset, _islandYOffset, _islandZOffset);
        _islandInstance = Instantiate(_islandPrefab, position, Quaternion.Euler(_islandRotation), anchor);
        _islandInstance.transform.localScale *= _islandScale;

        _islandVfxReference = _islandInstance.GetComponent<IslandVfxReference>();
        if (_islandVfxReference == null)
        {
            Debug.LogWarning("[Page8ShadowController] _islandPrefab has no IslandVfxReference — Page8CompletionSequence's glow VFX won't play.");
        }
        else if (_islandVfxReference.GlowVfx != null)
        {
            // ParticleSystems default to "Play On Awake", so left alone this fires the instant
            // the island is instantiated — stop it immediately so it stays silent until
            // Page8CompletionSequence explicitly plays it later.
            _islandVfxReference.GlowVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _islandVfxReference.GlowVfx.gameObject.SetActive(false);
        }
    }

    private void DespawnIsland()
    {
        if (_islandInstance != null)
            Destroy(_islandInstance);
        _islandInstance = null;
        _islandVfxReference = null;
    }

    // Called by Page8CompletionSequence right before it needs to play the glow VFX — the VFX
    // lives inside the island prefab itself, so it only exists once the island is spawned.
    public ParticleSystem GetIslandGlowVfx()
    {
        return _islandVfxReference != null ? _islandVfxReference.GlowVfx : null;
    }

    // Called by Page8CompletionSequence once the glow VFX has played out. Fire-and-forget — the
    // caller doesn't wait for the shrink to finish.
    public void BeginIslandShrink()
    {
        if (_islandFadeOutCoroutine != null) return;
        _islandFadeOutCoroutine = StartCoroutine(FadeOutIslandThenDestroy());
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

    // --- Scan lock ---

    private bool TryBeginFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[Page8ShadowController] _appStateManager not assigned; ignoring scan.");
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
    // the shared cancel event reaches all six page scripts.
    private void HandleFeatureCancelled()
    {
        if (!_isActive) return;

        CancelSequence();
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

    // Cancels whichever stage (island intro / instruction prompt / post-prompt wait / shadow
    // hold) is in progress and removes the island immediately. Called on Back cancellation and
    // on tracking loss, mirroring Page6SpeechController's CancelIslandAndPrompt — the island is
    // a real 3D anchor now, so a genuinely lost marker shouldn't leave it orphaned mid-air.
    private void CancelSequence()
    {
        if (_islandIntroCoroutine != null)
        {
            StopCoroutine(_islandIntroCoroutine);
            _islandIntroCoroutine = null;
        }
        if (_postPromptCoroutine != null)
        {
            StopCoroutine(_postPromptCoroutine);
            _postPromptCoroutine = null;
        }
        if (_islandFadeOutCoroutine != null)
        {
            StopCoroutine(_islandFadeOutCoroutine);
            _islandFadeOutCoroutine = null;
        }
        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);

        _isHolding = false;
        _shadowInteractable = false;
        _holdProgress = 0f; // no partial credit — reset so a future attempt starts from scratch

        if (_shadowImage != null)
        {
            _shadowImage.transform.localScale = Vector3.one;
            // Disabling it re-arms IsSequenceInProgress/CheckImage's claim gate for next time.
            _shadowImage.enabled = false;
        }

        DespawnIsland();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page8ShadowController] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    // --- Hold interaction (existing mechanic, unchanged) ---

    private void Update()
    {
        if (_isCompleted || !_isActive) return;
        if (_shadowImage == null || !_shadowImage.enabled) return;

        if (_isHolding)
        {
            _holdProgress += Time.deltaTime / _holdDuration;
        }
        else
        {
            _holdProgress -= Time.deltaTime / _holdDuration;
        }

        _holdProgress = Mathf.Clamp01(_holdProgress);

        _shadowImage.transform.localScale = Vector3.one * (1f - _holdProgress);

        if (_morphMaterial != null)
        {
            Rect rect = _shadowImage.rectTransform.rect;
            _morphMaterial.SetFloat("_RectWidth", rect.width);
            _morphMaterial.SetFloat("_RectHeight", rect.height);

            float morphTime = Mathf.Max(0f, (_holdProgress * _holdDuration) - _preFadeShrinkDuration);
            float morphDuration = Mathf.Max(0.01f, _holdDuration - _preFadeShrinkDuration);
            float morphProgress = Mathf.Clamp01(morphTime / morphDuration);
            morphProgress = _morphCurve.Evaluate(morphProgress);

            // Morph width and height to form a perfect square
            float targetSize = Mathf.Min(rect.width, rect.height);
            float currentWidth = Mathf.Lerp(rect.width, targetSize, morphProgress);
            float currentHeight = Mathf.Lerp(rect.height, targetSize, morphProgress);

            _morphMaterial.SetFloat("_DrawWidth", currentWidth);
            _morphMaterial.SetFloat("_DrawHeight", currentHeight);

            float maxRadius = targetSize * 0.5f;
            _morphMaterial.SetFloat("_Radius", Mathf.Lerp(0f, maxRadius, morphProgress));
        }

        if (_holdProgress >= 1f && !_isCompleted)
        {
            _isCompleted = true;

            if (_pendantManager != null)
                _pendantManager.CollectYellowSpark();
            else
                Debug.LogWarning("[Page8ShadowController] _pendantManager is missing.");

            StartCoroutine(FadeOutShadowThenComplete());
        }
    }

    private void HandlePointerDown()
    {
        if (_isCompleted || !_shadowInteractable) return;
        _isHolding = true;
    }

    private void HandlePointerUp()
    {
        if (_isCompleted || !_shadowInteractable) return;
        _isHolding = false;
    }

    private IEnumerator FadeOutShadowThenComplete()
    {
        yield return StartCoroutine(FadeOutShadow());

        if (_completionSequence != null)
            _completionSequence.TriggerCompletion();
        else
            Debug.LogWarning("[Page8ShadowController] _completionSequence is missing.");
    }

    private IEnumerator FadeOutShadow()
    {
        if (_shadowImage == null) yield break;

        Color startColor = _shadowImage.color;
        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(startColor.a, 0f, elapsed / _fadeDuration);
            _shadowImage.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null;
        }

        _shadowImage.enabled = false;
    }

    private class ShadowPointerHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public System.Action OnPointerDownAction;
        public System.Action OnPointerUpAction;

        public void OnPointerDown(PointerEventData eventData) => OnPointerDownAction?.Invoke();
        public void OnPointerUp(PointerEventData eventData) => OnPointerUpAction?.Invoke();
    }

    // Runtime-attached to _instructionPrompt so any tap on it dismisses it, without requiring
    // a Button to be set up in the Inspector (same trick Page4ARTracker/Page6SpeechController use).
    private class PromptTapHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnTap;
        public void OnPointerClick(PointerEventData eventData) => OnTap?.Invoke();
    }
}
