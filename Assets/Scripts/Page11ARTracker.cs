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

    private ARTrackedImage _activeTrackedImage;

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
    }

    private void OnDisable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
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

            _activeTrackedImage = image;
            ShowUI();
            Update3DContentTransform();
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page11Placeholder") continue;

            if (image.trackingState == TrackingState.Tracking)
            {
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
            if (removed.Value.referenceImage.name != "page11Placeholder") continue;

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

    private void ShowUI()
    {
        if (_page11_3DContent != null) _page11_3DContent.SetActive(true);
    }

    private void HideUI()
    {
        if (_page11_3DContent != null) _page11_3DContent.SetActive(false);
    }
}
