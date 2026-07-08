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
            
            _activeTrackedImage = image;
            ShowUI();
            Update3DContentTransform();
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page10Placeholder") continue;

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
            if (removed.Value.referenceImage.name != "page10Placeholder") continue;
            
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

    private void ShowUI()
    {
        if (_page10MeterController == null) Debug.LogError("[Page10ARTracker] _page10MeterController is NULL");
        else _page10MeterController.SetVisible(true);

        if (_page10_3DContent != null) _page10_3DContent.SetActive(true);
    }

    private void HideUI()
    {
        if (_page10MeterController == null) Debug.LogError("[Page10ARTracker] _page10MeterController is NULL");
        else _page10MeterController.SetVisible(false);

        if (_page10_3DContent != null) _page10_3DContent.SetActive(false);
    }
}
