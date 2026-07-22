using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Handles AR tracking for Page 11, showing/hiding the Page 11 3D content
/// (Phoenix + 4 draggable sparks) based on the page11Placeholder target.
/// Mirrors Page10ARTracker, minus the 2D meter reference (Page 11 has no screen-space UI).
/// </summary>
public class Page11ARTracker : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;

    [Header("Page 11")]
    [SerializeField] private GameObject _page11_3DContent;
    [SerializeField] private Page11DragController _dragController;

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
            if (_page11_3DContent != null)
            {
                _page11_3DContent.transform.position = _activeTrackedImage.transform.position;
                _page11_3DContent.transform.rotation = _activeTrackedImage.transform.rotation;
            }
        }
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage image in args.added)
        {
            if (image.referenceImage.name != "page11Placeholder") continue;
            if (_suppressedWhileTracked) continue; // backed out; wait for a fresh acquisition
            if (!TryBeginFeature()) continue;

            _activeTrackedImage = image;
            ShowUI();
            Update3DContentTransform();
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page11Placeholder") continue;

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
                _suppressedWhileTracked = false; // image left view → re-arm so looking back replays
                if (_activeTrackedImage == image)
                    _activeTrackedImage = null;
                HideUI();
            }
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != "page11Placeholder") continue;

            _suppressedWhileTracked = false; // image left view → re-arm so looking back replays
            if (_activeTrackedImage == removed.Value)
                _activeTrackedImage = null;
            HideUI();
        }
    }

    private void Update3DContentTransform()
    {
        if (_activeTrackedImage != null && _page11_3DContent != null)
        {
            _page11_3DContent.transform.position = _activeTrackedImage.transform.position;
            _page11_3DContent.transform.rotation = _activeTrackedImage.transform.rotation;
        }
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

        if (_dragController == null) Debug.LogError("[Page11ARTracker] _dragController is NULL");
        else _dragController.CancelDrag();

        HideUI();
        _suppressedWhileTracked = true;
        _activeTrackedImage = null;
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
        if (_page11_3DContent != null) _page11_3DContent.SetActive(true);

        if (_dragController == null) Debug.LogError("[Page11ARTracker] _dragController is NULL");
        else _dragController.SetActive(true);
    }

    private void HideUI()
    {
        if (_page11_3DContent != null) _page11_3DContent.SetActive(false);

        if (_dragController == null) Debug.LogError("[Page11ARTracker] _dragController is NULL");
        else _dragController.SetActive(false);
    }
}
