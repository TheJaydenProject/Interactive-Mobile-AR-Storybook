using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Attach to XR Origin (AR Rig).
/// When the target image is detected: spawn the island/garden above the anchor, wait, then
/// fade in a grey overlay and spawn clouds.
/// Tap each cloud to pop it — the overlay clears gradually as clouds are popped.
/// </summary>
[RequireComponent(typeof(ARTrackedImageManager))]
public class Page1Manager : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;
    [SerializeField] private XRRayInteractor _rayInteractor;

    [Header("Island")]
    [SerializeField] private GameObject _islandPrefab;
    [SerializeField] private float _islandXOffset = 0.0f;
    [SerializeField] private float _islandYOffset = 0.1f;
    [SerializeField] private float _islandZOffset = 0.0f;
    [SerializeField] private float _islandSpawnDelay = 5.0f;
    [SerializeField] private float _islandScale = 1.0f;
    [SerializeField] private Vector3 _islandRotation = Vector3.zero;
    [SerializeField] private float _islandFadeOutDelay = 2.0f;
    [SerializeField] private float _islandFadeOutDuration = 1.0f;

    [Header("Overlay")]
    [SerializeField] private Image _greyOverlay;
    [SerializeField] private float _fadeInDuration = 1.0f;
    [SerializeField] private float _popFadeDuration = 0.3f;
    [SerializeField] private AudioSource _overlayFadeInAudioSource;
    [SerializeField] private AudioClip _overlayFadeInClip;
    [SerializeField] private float _overlayFadeInSfxLeadTime = 1.0f;

    [Header("Clouds")]
    [SerializeField] private GameObject _cloudPrefab;
    [SerializeField] private int _cloudCount = 5;
    [SerializeField] private float _spreadRadius = 0.15f;
    [SerializeField] private float _cloudYOffsetMin = 0.05f;
    [SerializeField] private float _cloudYOffsetMax = 0.15f;
    [SerializeField] private float _cloudScale = 1.0f;
    [SerializeField] private float _cloudXOffset = 0.0f;
    [SerializeField] private float _cloudZOffset = 0.0f;
    [SerializeField] private AudioSource _cloudPopAudioSource;
    [SerializeField] private AudioClip _cloudPopClip;
    [SerializeField] private float _cloudPopPitchMin = 0.9f;
    [SerializeField] private float _cloudPopPitchMax = 1.15f;

    [Header("Instructions")]
    [SerializeField] private GameObject _instructionGroup;
    [SerializeField] private GameObject _instructionGroup2;
    [SerializeField] private float _instructionDelay = 2.5f;
    [SerializeField] private float _cloudUnlockDelay = 1.0f;

    [Header("Finish")]
    [SerializeField] private GameObject _finishGroup;
    [SerializeField] private float _finishDelay = 1.5f;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    // How opaque the overlay starts and how low it goes before the last cloud.
    private const float OverlayMaxAlpha = 0.85f;
    private const float OverlayMinAlpha = 0.4f;

    private List<GameObject> _clouds = new();
    private int _poppedCount = 0;
    private bool _sequenceActive = false;
    private Coroutine _fadeCoroutine;
    private Coroutine _sequenceCoroutine;
    private Coroutine _instructionDelayCoroutine;
    private Coroutine _cloudUnlockDelayCoroutine;
    private Coroutine _finishDelayCoroutine;
    private Coroutine _islandFadeOutCoroutine;
    private GameObject _islandInstance;
    private Transform _lastAnchor;
    private bool _cloudsLocked;
    private int _cloudsUnlockedFrame = -1;

    // Mirrors "do I currently hold the shared AppStateManager lock" — separate from
    // _sequenceActive (which also drives tracking-loss/regain) so the cancel guard below can't
    // be fooled by a stale value. Self-heals via HandleFeatureActiveChanged since only the page
    // that holds the lock could ever observe it become inactive.
    private bool _isActive;

    // Set when the child backs out via Back; blocks this page re-triggering while its image
    // stays in view, cleared once the image leaves tracking so looking back re-arms it.
    private bool _suppressedWhileTracked;

    private void Awake()
    {
        if (_trackedImageManager == null)
            _trackedImageManager = GetComponent<ARTrackedImageManager>();

        // Make sure the overlay starts invisible.
        SetOverlayAlpha(0f);
        _greyOverlay.enabled = false;

        if (_instructionGroup != null)
            _instructionGroup.SetActive(false);

        if (_instructionGroup2 != null)
            _instructionGroup2.SetActive(false);

        if (_finishGroup != null)
            _finishGroup.SetActive(false);
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

    private void Update()
    {
        // Block cloud taps from the moment clouds spawn until the instruction group is dismissed.
        if (_cloudsLocked) return;

        // The tap that dismisses the instruction group runs its UI click handler before this
        // Update() in the same frame, so wasCompletedThisFrame is still true right as the lock
        // clears — ignore that same frame so it can't also pop a cloud underneath.
        if (Time.frameCount == _cloudsUnlockedFrame) return;

        // Only check for taps when clouds are present.
        if (_clouds.Count == 0) return;
        if (_rayInteractor == null) return;

        // wasCompletedThisFrame is true on the exact frame the tap gesture ends.
        if (!_rayInteractor.logicalSelectState.wasCompletedThisFrame) return;

        // Ask the interactor where the ray hit.
        if (!_rayInteractor.TryGetCurrentRaycast(out RaycastHit? hit, out _, out _, out _, out _)) return;
        if (hit == null) return;

        // If we hit a cloud, pop it.
        CloudBehaviour cloud = hit.Value.collider.GetComponent<CloudBehaviour>();
        cloud?.Pop();
    }

    // --- AR Tracking ---

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage image in args.added)
        {
            if (image.referenceImage.name != "page1") continue;
            StartSequence(image.transform);
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page1") continue;

            // Re-arm as soon as the image stops being solidly tracked. XR Simulation / ARCore
            // usually report Limited (not None) on look-away, so keying only off None left the
            // suppression stuck and the feature never reappeared. ponytail: clears on the first
            // non-Tracking frame — heavy tracking flicker could re-arm early; add a short debounce
            // if that shows up on-device.
            if (image.trackingState != TrackingState.Tracking)
                _suppressedWhileTracked = false;

            if (image.trackingState == TrackingState.Tracking && !_sequenceActive)
                StartSequence(image.transform);
            else if (image.trackingState == TrackingState.None && _sequenceActive)
                StopSequence();
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != "page1") continue;
            _suppressedWhileTracked = false;
            StopSequence();
        }
    }

    private void StartSequence(Transform anchor)
    {
        // Backed out of this page but still pointed at it — stay quiet until it's re-acquired.
        if (_suppressedWhileTracked) return;

        // Ignore the scan outright if another page's feature is already active; only this
        // page's own completion (below) releases the shared lock.
        if (!TryBeginFeature()) return;

        _sequenceActive = true;
        _poppedCount = 0;
        _sequenceCoroutine = StartCoroutine(SpawnIslandThenFadeInThenSpawn(anchor));
    }

    private bool TryBeginFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[Page1Manager] _appStateManager not assigned; ignoring scan.");
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

        StopSequence(); // stops the fade coroutine, despawns clouds — no partial credit exists for Page 1 anyway
        _suppressedWhileTracked = true;
        _isActive = false;
        EndFeature();
    }

    private void StopSequence()
    {
        _sequenceActive = false;

        if (_sequenceCoroutine != null)
        {
            StopCoroutine(_sequenceCoroutine);
            _sequenceCoroutine = null;
        }

        if (_instructionDelayCoroutine != null)
        {
            StopCoroutine(_instructionDelayCoroutine);
            _instructionDelayCoroutine = null;
        }

        if (_cloudUnlockDelayCoroutine != null)
        {
            StopCoroutine(_cloudUnlockDelayCoroutine);
            _cloudUnlockDelayCoroutine = null;
        }

        if (_finishDelayCoroutine != null)
        {
            StopCoroutine(_finishDelayCoroutine);
            _finishDelayCoroutine = null;
        }

        if (_finishGroup != null)
            _finishGroup.SetActive(false);

        if (_islandFadeOutCoroutine != null)
        {
            StopCoroutine(_islandFadeOutCoroutine);
            _islandFadeOutCoroutine = null;
        }

        DespawnIsland();
        DespawnClouds();
        FadeTo(0f, _popFadeDuration);
        HideInstructionGroup();
    }

    // --- Overlay ---

    private void FadeTo(float targetAlpha, float duration)
    {
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadeRoutine(targetAlpha, duration));
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        _greyOverlay.enabled = true;

        float startAlpha = _greyOverlay.color.a;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
            yield return null;
        }

        SetOverlayAlpha(targetAlpha);

        if (targetAlpha <= 0f)
            _greyOverlay.enabled = false;
    }

    private void SetOverlayAlpha(float alpha)
    {
        Color c = _greyOverlay.color;
        _greyOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }

    // --- Island ---

    private IEnumerator SpawnIslandThenFadeInThenSpawn(Transform anchor)
    {
        SpawnIsland(anchor);
        yield return new WaitForSeconds(_islandSpawnDelay);
        yield return FadeInThenSpawn(anchor);
    }

    private void SpawnIsland(Transform anchor)
    {
        DespawnIsland();

        if (_islandPrefab == null)
        {
            Debug.LogError("[Page1Manager] Island prefab not assigned.");
            return;
        }

        _lastAnchor = anchor;

        Vector3 position = anchor.position + new Vector3(_islandXOffset, _islandYOffset, _islandZOffset);
        _islandInstance = Instantiate(_islandPrefab, position, Quaternion.Euler(_islandRotation), anchor);
        _islandInstance.transform.localScale *= _islandScale;
    }

    // Right-click the component header in the Inspector (Play mode only) to re-spawn the island
    // at its current offset/scale values without waiting through the full scan sequence again.
    [ContextMenu("Respawn Island (Debug)")]
    private void DebugRespawnIsland()
    {
        if (_lastAnchor == null)
        {
            Debug.LogWarning("[Page1Manager] No anchor yet — scan the page at least once this Play session first.");
            return;
        }

        SpawnIsland(_lastAnchor);
    }

    [ContextMenu("Respawn Clouds (Debug)")]
    private void DebugRespawnClouds()
    {
        if (_lastAnchor == null)
        {
            Debug.LogWarning("[Page1Manager] No anchor yet — scan the page at least once this Play session first.");
            return;
        }

        SpawnClouds(_lastAnchor);
    }

    private void DespawnIsland()
    {
        if (_islandInstance != null)
            Destroy(_islandInstance);
        _islandInstance = null;
    }

    // --- Clouds ---

    private IEnumerator FadeInThenSpawn(Transform anchor)
    {
        PlayOverlayFadeInSfx();
        yield return new WaitForSeconds(_overlayFadeInSfxLeadTime);
        FadeTo(OverlayMaxAlpha, _fadeInDuration);
        yield return new WaitForSeconds(_fadeInDuration);
        SpawnClouds(anchor);
    }

    private void PlayOverlayFadeInSfx()
    {
        if (_overlayFadeInAudioSource == null || _overlayFadeInClip == null) return;
        _overlayFadeInAudioSource.PlayOneShot(_overlayFadeInClip);
    }

    private void SpawnClouds(Transform anchor)
    {
        DespawnClouds();

        _lastAnchor = anchor;

        if (_cloudPrefab == null)
        {
            Debug.LogError("[Page1Manager] Cloud prefab not assigned.");
            return;
        }

        if (_cloudCount <= 0) return;

        // One cloud sits centered above the anchor; the rest form a ring around it.
        SpawnCloud(anchor, new Vector3(_cloudXOffset, 0f, _cloudZOffset));

        int ringCount = _cloudCount - 1;
        if (ringCount <= 0) return;

        float angleStep = 360f / ringCount;
        float startAngle = Random.Range(0f, 360f);

        for (int i = 0; i < ringCount; i++)
        {
            float angle = startAngle + (angleStep * i);
            float rad = angle * Mathf.Deg2Rad;
            float radius = Random.Range(_spreadRadius * 0.6f, _spreadRadius);

            Vector3 horizontalOffset = new Vector3(
                Mathf.Cos(rad) * radius + _cloudXOffset,
                0f,
                Mathf.Sin(rad) * radius + _cloudZOffset
            );

            SpawnCloud(anchor, horizontalOffset);
        }

        _cloudsLocked = true;
        _instructionDelayCoroutine = StartCoroutine(ShowInstructionGroupAfterDelay());
    }

    private IEnumerator ShowInstructionGroupAfterDelay()
    {
        yield return new WaitForSeconds(_instructionDelay);
        ShowInstructionGroup();
    }

    private void SpawnCloud(Transform anchor, Vector3 horizontalOffset)
    {
        Vector3 offset = horizontalOffset + Vector3.up * Random.Range(_cloudYOffsetMin, _cloudYOffsetMax);

        GameObject cloud = Instantiate(_cloudPrefab, anchor.position + offset, Quaternion.identity, anchor);
        cloud.transform.LookAt(Camera.main.transform);
        cloud.transform.Rotate(0f, 180f, 0f);
        cloud.transform.localScale *= _cloudScale;

        CloudBehaviour behaviour = cloud.GetComponent<CloudBehaviour>();
        if (behaviour != null)
            behaviour.OnPopped += OnCloudPopped;
        else
            Debug.LogWarning("[Page1Manager] Cloud prefab missing CloudBehaviour.");

        _clouds.Add(cloud);
    }

    // --- Instructions ---

    private void ShowInstructionGroup()
    {
        if (_instructionGroup != null)
            _instructionGroup.SetActive(true);
    }

    // Wire this to a tap/click event on the FIRST instruction group so tapping it advances
    // to the second group. Clouds stay locked throughout.
    public void ShowSecondInstructionGroup()
    {
        if (_instructionGroup != null)
            _instructionGroup.SetActive(false);

        if (_instructionGroup2 != null)
            _instructionGroup2.SetActive(true);
    }

    // Wire this to a tap/click event on the SECOND instruction group so tapping it dismisses
    // both groups and unlocks the clouds.
    public void HideInstructionGroup()
    {
        if (_instructionGroup != null)
            _instructionGroup.SetActive(false);

        if (_instructionGroup2 != null)
            _instructionGroup2.SetActive(false);

        _cloudUnlockDelayCoroutine = StartCoroutine(UnlockCloudsAfterDelay());
    }

    private IEnumerator UnlockCloudsAfterDelay()
    {
        yield return new WaitForSeconds(_cloudUnlockDelay);
        _cloudsLocked = false;
        _cloudsUnlockedFrame = Time.frameCount;
    }

    private void OnCloudPopped(CloudBehaviour cloud)
    {
        cloud.OnPopped -= OnCloudPopped;
        _clouds.Remove(cloud.gameObject);
        _poppedCount++;

        PlayCloudPopSfx();

        // Step the overlay down with each pop. Goes to zero only on the last one.
        float targetAlpha = _poppedCount >= _cloudCount
            ? 0f
            : Mathf.Lerp(OverlayMaxAlpha, OverlayMinAlpha, (float)_poppedCount / (_cloudCount - 1));

        FadeTo(targetAlpha, _popFadeDuration);

        // Page 1 has no dedicated completion-sequence overlay like later pages; the grey
        // overlay finishing its fade-out is the closest thing it has to a transition.
        if (_poppedCount >= _cloudCount)
            _finishDelayCoroutine = StartCoroutine(ShowFinishGroupAfterDelay());
    }

    private void PlayCloudPopSfx()
    {
        if (_cloudPopAudioSource == null || _cloudPopClip == null) return;

        // Climbs from _cloudPopPitchMin to _cloudPopPitchMax across however many clouds there
        // are, so the last pop always lands at the top regardless of _cloudCount.
        float t = _cloudCount > 1 ? (float)(_poppedCount - 1) / (_cloudCount - 1) : 0f;
        _cloudPopAudioSource.pitch = Mathf.Lerp(_cloudPopPitchMin, _cloudPopPitchMax, t);
        _cloudPopAudioSource.PlayOneShot(_cloudPopClip);
    }

    private IEnumerator ShowFinishGroupAfterDelay()
    {
        yield return new WaitForSeconds(_finishDelay);

        if (_finishGroup != null)
            _finishGroup.SetActive(true);
    }

    // Wire this to a tap/click event on the finish group so tapping it dismisses the group,
    // removes the island, and releases the shared scan lock for the next page.
    public void HideFinishGroup()
    {
        if (_finishGroup != null)
            _finishGroup.SetActive(false);

        _islandFadeOutCoroutine = StartCoroutine(FadeOutIslandThenDestroy());
        EndFeature();
    }

    private IEnumerator FadeOutIslandThenDestroy()
    {
        yield return new WaitForSeconds(_islandFadeOutDelay);

        if (_islandInstance == null) yield break;

        // Shrinking localScale alone collapses toward the prefab's own pivot, which isn't
        // centered on its geometry (see the off-center model discussion above) — so instead
        // pull position toward the combined renderer bounds' center at the same rate the
        // scale shrinks, making it visually shrink toward its own center.
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

        DespawnIsland();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page1Manager] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    private void DespawnClouds()
    {
        foreach (GameObject cloud in _clouds)
        {
            if (cloud == null) continue;
            CloudBehaviour behaviour = cloud.GetComponent<CloudBehaviour>();
            if (behaviour != null) behaviour.OnPopped -= OnCloudPopped;
            Destroy(cloud);
        }
        _clouds.Clear();
    }
}