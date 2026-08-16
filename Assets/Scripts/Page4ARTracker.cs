using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Attach to XR Origin (AR Rig) — the same GameObject that holds ARTrackedImageManager.
/// Starts/stops the drop spawner and shows/hides the Page 4 UI based on image tracking.
/// </summary>
[RequireComponent(typeof(ARTrackedImageManager))]
public class Page4ARTracker : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;

    [Header("Page 4")]
    [SerializeField] private DropSpawner         _dropSpawner;
    [SerializeField] private Image               _catchZoneImage;
    [SerializeField] private DropMeterController _dropMeterController;

    [Header("Island")]
    [SerializeField] private GameObject _islandPrefab;
    [SerializeField] private float _islandXOffset = 0.0f;
    [SerializeField] private float _islandYOffset = 0.1f;
    [SerializeField] private float _islandZOffset = 0.0f;
    [Tooltip("Wait after the image is first detected, before capturing its pose to spawn the island — lets ARFoundation's pose estimate settle past its initial (often noisy) read.")]
    [SerializeField] private float _islandSpawnStabilizationDelay = 0.65f;
    [SerializeField] private float _islandScale = 1.0f;
    [SerializeField] private Vector3 _islandRotation = Vector3.zero;
    [SerializeField] private float _islandFadeOutDelay = 2.0f;
    [SerializeField] private float _islandFadeOutDuration = 1.0f;

    [Header("Pre-Game Sequence")]
    [Tooltip("Wait after the island spawns (lets its crying animation play out) before the instruction prompt appears.")]
    [SerializeField] private float _introWaitDuration = 9f;
    [SerializeField] private AudioSource _introVoiceAudioSource;
    [Tooltip("Plays this many seconds after the island spawns, during the intro wait.")]
    [SerializeField] private AudioClip   _introVoiceClip;
    [SerializeField] private float       _introVoiceDelay = 3.5f;
    [Tooltip("Shown after the intro wait. Needs a Graphic (e.g. Image) with Raycast Target on, under a Canvas with a GraphicRaycaster, so tap-anywhere dismissal works.")]
    [SerializeField] private GameObject _instructionPrompt;
    [Tooltip("Parent group holding the countdown text, if it's inactive by default and needs to be shown/hidden alongside the text itself.")]
    [SerializeField] private GameObject _countdownGroup;
    [SerializeField] private TMP_Text   _countdownText;
    [SerializeField] private float      _countdownStepDuration = 1f;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    // Mirrors "do I currently hold the shared AppStateManager lock" for the cancel guard.
    // Self-heals via HandleFeatureActiveChanged so a stale true can never linger past this
    // page's own completion or cancellation.
    private bool _isActive;

    // Set when the child backs out via Back; blocks this page re-triggering while its image
    // stays in view, cleared once the image leaves tracking so looking back re-arms it.
    private bool _suppressedWhileTracked;

    private Coroutine _countdownCoroutine;
    private Coroutine _introCoroutine;

    private GameObject         _islandInstance;
    private IslandVfxReference _islandVfxReference;
    private Coroutine          _islandFadeOutCoroutine;
    private Transform          _lastAnchor;

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
            Debug.LogError("[Page4ARTracker] _instructionPrompt not assigned.");
        }

        if (_countdownGroup != null) _countdownGroup.SetActive(false);
        if (_countdownText != null) _countdownText.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);

        if (_appStateManager != null)
        {
            _appStateManager.OnFeatureCancelled += HandleFeatureCancelled;
            _appStateManager.OnFeatureActiveChanged += HandleFeatureActiveChanged;
        }
    }

    private void OnDisable()
    {
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
            if (image.referenceImage.name != "page4") continue;
            if (_suppressedWhileTracked) continue; // backed out; wait for a fresh acquisition
            if (!TryBeginFeature()) continue;

            _lastAnchor = image.transform;
            BeginPreGameSequence();
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page4") continue;

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

                _lastAnchor = image.transform;
                BeginPreGameSequence();
            }
            else if (image.trackingState == TrackingState.None)
            {
                CancelPreGameSequence();
                CancelIslandReward();
                HideUI();
                if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
                else _dropSpawner.StopSpawning();
                ReleaseLockIfHeld();
            }
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != "page4") continue;
            _suppressedWhileTracked = false; // image left view → re-arm so looking back replays
            CancelPreGameSequence();
            CancelIslandReward();
            HideUI();
            if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
            else _dropSpawner.StopSpawning();
            ReleaseLockIfHeld();
        }
    }

    // Entry point once this page claims the shared lock: spawn the island immediately (Eira and
    // the Water Spirit appear right away, crying — no VFX, no shrink yet), wait for that to play
    // out, then show the instructions, then (once dismissed) the countdown, and only then the
    // actual catch-zone/meter/drops. The island persists through the whole rest of the page —
    // it isn't spawned again later; CompletionSequence just shrinks this same instance at the end.
    private void BeginPreGameSequence()
    {
        _introCoroutine = StartCoroutine(SpawnIslandThenWaitThenShowInstructions());
    }

    private IEnumerator SpawnIslandThenWaitThenShowInstructions()
    {
        // Wait before spawning so ARFoundation's pose estimate for the image has a moment to
        // settle past its initial (often noisy) read — the island is unparented and locks in
        // whatever pose it sees at spawn time, so a bad first read would otherwise be permanent.
        yield return new WaitForSeconds(_islandSpawnStabilizationDelay);

        if (_lastAnchor != null)
            SpawnIsland(_lastAnchor);
        else
            Debug.LogError("[Page4ARTracker] No anchor available — page4 was never tracked?");

        yield return new WaitForSeconds(_introVoiceDelay);
        PlayIntroVoiceLine();

        float remainingWait = Mathf.Max(0f, _introWaitDuration - _introVoiceDelay);
        yield return new WaitForSeconds(remainingWait);

        _introCoroutine = null;

        if (_instructionPrompt == null)
        {
            Debug.LogError("[Page4ARTracker] _instructionPrompt is NULL — skipping straight to countdown.");
            StartCountdown();
            yield break;
        }

        _instructionPrompt.SetActive(true);
    }

    private void PlayIntroVoiceLine()
    {
        if (_introVoiceAudioSource != null && _introVoiceClip != null)
            _introVoiceAudioSource.PlayOneShot(_introVoiceClip);
        else
            Debug.LogWarning("[Page4ARTracker] _introVoiceAudioSource or _introVoiceClip not assigned.");
    }

    // Public so it can also be wired to a Button's OnClick() in the Inspector (same pattern as
    // Page1Manager.HideFinishGroup), in addition to the runtime tap-anywhere handler below.
    public void HideInstructionPrompt()
    {
        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);
        StartCountdown();
    }

    private void StartCountdown()
    {
        _countdownCoroutine = StartCoroutine(CountdownThenStart());
    }

    private IEnumerator CountdownThenStart()
    {
        if (_countdownGroup != null) _countdownGroup.SetActive(true);
        if (_countdownText != null) _countdownText.gameObject.SetActive(true);

        for (int i = 3; i >= 1; i--)
        {
            if (_countdownText != null) _countdownText.text = i.ToString();
            yield return new WaitForSeconds(_countdownStepDuration);
        }

        if (_countdownText != null) _countdownText.text = "Go!";
        yield return new WaitForSeconds(_countdownStepDuration);

        if (_countdownGroup != null) _countdownGroup.SetActive(false);
        if (_countdownText != null) _countdownText.gameObject.SetActive(false);
        _countdownCoroutine = null;

        ShowUI();
        if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
        else _dropSpawner.StartSpawning();
    }

    // Stops/hides whatever part of the instructions-or-countdown is currently showing.
    // Called on Back cancellation and on tracking loss so nothing is left stuck on screen.
    private void CancelPreGameSequence()
    {
        if (_introCoroutine != null)
        {
            StopCoroutine(_introCoroutine);
            _introCoroutine = null;
        }

        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }

        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);
        if (_countdownGroup != null) _countdownGroup.SetActive(false);
        if (_countdownText != null) _countdownText.gameObject.SetActive(false);
    }

    // Called by CompletionSequence once the whole reward sequence has fully played out (right
    // around when it releases the shared lock), so this page doesn't immediately re-trigger
    // itself while still being continuously tracked — same suppression the Back-button path
    // already uses. Naturally re-arms once tracking is lost and regained (e.g. looking away and
    // back), same as that path, so scanning page4 again later still replays the whole thing.
    public void MarkSequenceComplete()
    {
        _suppressedWhileTracked = true;
    }

    // The glow VFX lives inside the island prefab itself (via IslandVfxReference), not as a
    // fixed scene reference, since it doesn't exist until the island is instantiated.
    public ParticleSystem GetIslandGlowVfx()
    {
        return _islandVfxReference != null ? _islandVfxReference.GlowVfx : null;
    }

    // Same story as GetIslandGlowVfx() — the Water Spirit's Animator lives inside the spawned
    // island prefab, not as a fixed scene reference.
    public Animator GetIslandWaterSpiritAnimator()
    {
        return _islandVfxReference != null ? _islandVfxReference.WaterSpiritAnimator : null;
    }

    // Called by CompletionSequence at the end of the reward sequence — fire-and-forget, doesn't
    // block on the shrink finishing.
    public void BeginIslandShrink()
    {
        if (_islandFadeOutCoroutine != null) return;
        _islandFadeOutCoroutine = StartCoroutine(FadeOutIslandThenDestroy());
    }

    private void SpawnIsland(Transform anchor)
    {
        DespawnIsland();

        if (_islandPrefab == null)
        {
            Debug.LogError("[Page4ARTracker] _islandPrefab not assigned.");
            return;
        }

        Vector3 position = anchor.position + new Vector3(_islandXOffset, _islandYOffset, _islandZOffset);
        // Spawned unparented, not attached to the tracked image's own transform — a live-tracked
        // ARTrackedImage keeps refining its pose every frame, which used to make the island
        // tilt/wobble along with the physical page instead of staying put once placed. Rotation
        // is still composed with anchor.rotation (not a bare absolute value) so it matches however
        // the physical page is actually oriented — a pure Quaternion.Euler(_islandRotation) looked
        // right in Editor/XR Simulation (whose marker sits at one fixed, canonical orientation) but
        // pointed the wrong way on-device, since the AR session's world axes are arbitrary, set by
        // wherever the phone happened to be facing when that session started, not by the page.
        _islandInstance = Instantiate(_islandPrefab, position, anchor.rotation * Quaternion.Euler(_islandRotation));
        _islandInstance.transform.localScale *= _islandScale;

        _islandVfxReference = _islandInstance.GetComponent<IslandVfxReference>();
        if (_islandVfxReference == null)
        {
            Debug.LogWarning("[Page4ARTracker] _islandPrefab has no IslandVfxReference — CompletionSequence's glow VFX won't play.");
        }
        else if (_islandVfxReference.GlowVfx == null)
        {
            Debug.LogWarning("[Page4ARTracker] _islandPrefab's IslandVfxReference has no Glow Vfx assigned — nothing to stop or play.");
        }
        else
        {
            // ParticleSystems default to "Play On Awake", so left alone this fires the instant
            // the island is instantiated — stop it immediately so it stays silent until
            // CompletionSequence explicitly plays it later.
            _islandVfxReference.GlowVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _islandVfxReference.GlowVfx.gameObject.SetActive(false);
        }
    }

    private IEnumerator FadeOutIslandThenDestroy()
    {
        yield return new WaitForSeconds(_islandFadeOutDelay);

        if (_islandInstance != null)
        {
            // Shrinking localScale alone collapses toward the prefab's own pivot, which isn't
            // necessarily centered on its geometry — so instead pull position toward the combined
            // renderer bounds' center at the same rate the scale shrinks, same as Page1Manager.
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

    private void DespawnIsland()
    {
        if (_islandInstance != null)
            Destroy(_islandInstance);
        _islandInstance = null;
        _islandVfxReference = null;
    }

    // Stops the island wherever it's at (mid-shrink or otherwise) and removes it immediately.
    // Called on Back cancellation and on tracking loss so nothing is left mid-air or mid-fade.
    private void CancelIslandReward()
    {
        if (_islandFadeOutCoroutine != null)
        {
            StopCoroutine(_islandFadeOutCoroutine);
            _islandFadeOutCoroutine = null;
        }

        DespawnIsland();
    }

    private void ShowUI()
    {
        if (_catchZoneImage == null) Debug.LogError("[Page4ARTracker] _catchZoneImage is NULL");
        else _catchZoneImage.enabled = true;

        if (_dropMeterController == null) Debug.LogError("[Page4ARTracker] _dropMeterController is NULL");
        else _dropMeterController.SetVisible(true);
    }

    // Ignore the scan outright if another page's feature is already active; only
    // CompletionSequence releases the shared lock, so tracking loss/regain mid-game won't
    // silently reclaim it.
    private bool TryBeginFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[Page4ARTracker] _appStateManager not assigned; ignoring scan.");
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

        CancelPreGameSequence();
        CancelIslandReward();

        if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
        else _dropSpawner.StopSpawning();

        DestroyActiveDrops();
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

    // Currently-falling drops aren't cleared by StopSpawning() alone (it only stops new ones
    // spawning), so they'd keep falling/registering catches after the child backs out.
    private static void DestroyActiveDrops()
    {
        DropBehaviour[] activeDrops = FindObjectsByType<DropBehaviour>();
        foreach (DropBehaviour drop in activeDrops)
            if (drop != null) Destroy(drop.gameObject);
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page4ARTracker] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    private void HideUI()
    {
        if (_catchZoneImage == null) Debug.LogError("[Page4ARTracker] _catchZoneImage is NULL");
        else _catchZoneImage.enabled = false;

        if (_dropMeterController == null) Debug.LogError("[Page4ARTracker] _dropMeterController is NULL");
        else _dropMeterController.SetVisible(false);
    }

    // Runtime-attached to _instructionPrompt so any tap on it dismisses it, without requiring
    // a Button to be set up in the Inspector (same trick Page8ShadowController uses for its
    // shadow's press-and-hold).
    private class PromptTapHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnTap;
        public void OnPointerClick(PointerEventData eventData) => OnTap?.Invoke();
    }
}
