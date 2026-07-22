using UnityEngine;
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

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

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
            if (image.referenceImage.name != "page4Placeholder") continue;
            if (_suppressedWhileTracked) continue; // backed out; wait for a fresh acquisition
            if (!TryBeginFeature()) continue;

            ShowUI();
            if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
            else _dropSpawner.StartSpawning();
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page4Placeholder") continue;

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

                ShowUI();
                if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
                else _dropSpawner.StartSpawning();
            }
            else if (image.trackingState == TrackingState.None)
            {
                HideUI();
                if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
                else _dropSpawner.StopSpawning();
            }
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != "page4Placeholder") continue;
            _suppressedWhileTracked = false; // image left view → re-arm so looking back replays
            HideUI();
            if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
            else _dropSpawner.StopSpawning();
        }
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

        if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
        else _dropSpawner.StopSpawning();

        DestroyActiveDrops();
        HideUI();

        _suppressedWhileTracked = true;
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
}
