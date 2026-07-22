using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Handles AR tracking for Page 10, managing UI visibility based on the page10Placeholder target.
/// </summary>
public class Page10ARTracker : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;

    [Header("Page 10")]
    [SerializeField] private Page10MeterController _page10MeterController;
    [SerializeField] private GameObject _page10_3DContent;
    [SerializeField] private Page10OrbController _orbController;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private ARTrackedImage _activeTrackedImage;

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

    private void Update()
    {
        if (_activeTrackedImage != null && _activeTrackedImage.trackingState == TrackingState.Tracking)
        {
            if (_page10_3DContent != null)
            {
                _page10_3DContent.transform.position = _activeTrackedImage.transform.position;
                _page10_3DContent.transform.rotation = _activeTrackedImage.transform.rotation;
            }
        }
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage image in args.added)
        {
            if (image.referenceImage.name != "page10Placeholder") continue;
            if (_suppressedWhileTracked) continue; // backed out; wait for a fresh acquisition
            if (!TryBeginFeature()) continue;

            _activeTrackedImage = image;
            ShowUI();
            Update3DContentTransform();
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page10Placeholder") continue;

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

                _activeTrackedImage = image;
                ShowUI();
                Update3DContentTransform();
            }
            else if (image.trackingState == TrackingState.None)
            {
                if (_activeTrackedImage == image)
                    _activeTrackedImage = null;
                HideUI();
            }
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != "page10Placeholder") continue;

            _suppressedWhileTracked = false; // image left view → re-arm so looking back replays
            if (_activeTrackedImage == removed.Value)
                _activeTrackedImage = null;
            HideUI();
        }
    }

    private void Update3DContentTransform()
    {
        if (_activeTrackedImage != null && _page10_3DContent != null)
        {
            _page10_3DContent.transform.position = _activeTrackedImage.transform.position;
            _page10_3DContent.transform.rotation = _activeTrackedImage.transform.rotation;
        }
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

        HideUI(); // stops the orb controller's Update loop and hides the 3D content
        _suppressedWhileTracked = true;
        _activeTrackedImage = null;
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

        if (_page10_3DContent != null) _page10_3DContent.SetActive(true);
    }

    private void HideUI()
    {
        if (_page10MeterController == null) Debug.LogError("[Page10ARTracker] _page10MeterController is NULL");
        else _page10MeterController.SetVisible(false);

        if (_orbController == null) Debug.LogError("[Page10ARTracker] _orbController is NULL");
        else _orbController.SetActive(false);

        if (_page10_3DContent != null) _page10_3DContent.SetActive(false);
    }
}
