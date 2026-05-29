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

    private void Awake()
    {
        if (_trackedImageManager == null)
            _trackedImageManager = GetComponent<ARTrackedImageManager>();

        HideUI();
    }

    private void OnEnable()
    {
        _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
    }

    private void OnDisable()
    {
        _trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage image in args.added)
        {
            if (image.referenceImage.name != "page4Placeholder") continue;
            ShowUI();
            if (_dropSpawner == null) Debug.LogError("[Page4ARTracker] _dropSpawner is NULL");
            else _dropSpawner.StartSpawning();
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page4Placeholder") continue;

            if (image.trackingState == TrackingState.Tracking)
            {
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

    private void HideUI()
    {
        if (_catchZoneImage == null) Debug.LogError("[Page4ARTracker] _catchZoneImage is NULL");
        else _catchZoneImage.enabled = false;

        if (_dropMeterController == null) Debug.LogError("[Page4ARTracker] _dropMeterController is NULL");
        else _dropMeterController.SetVisible(false);
    }
}
