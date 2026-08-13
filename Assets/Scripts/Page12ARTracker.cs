using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Handles Page 12 in the main scene ("Ar1"). Scanning the page12 marker triggers a full app
/// process restart (via AppRestarter) landing in a dedicated scene ("Ar2") whose AR session starts
/// directly in front-facing/face-tracking mode. A plain scene load isn't enough here — ARCore's
/// native session layer carries state across a same-process scene reload, and that's what segfaults
/// the moment a *second* session in the same process reaches the front-facing/face-tracking config,
/// confirmed via device logcat (12/12 reproductions). Only a genuine process restart, landing Ar2 as
/// the first and only session in a fresh process, avoids it.
/// </summary>
public class Page12ARTracker : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;

    [Header("Scene")]
    [Tooltip("Name of the scene to restart into when page12 is scanned. Must be added to File > Build Settings > Scenes In Build.")]
    [SerializeField] private string _faceFilterSceneName = "Ar2";

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    // Guards against firing twice (e.g. two ARTrackedImage update events in the same batch) while
    // the process restart is in flight.
    private bool _triggered;

    private void Awake()
    {
        if (_trackedImageManager == null)
            _trackedImageManager = GetComponent<ARTrackedImageManager>();
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

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage image in args.added)
        {
            if (image.referenceImage.name != "page12") continue;
            TryLoadFaceFilterScene();
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page12") continue;
            if (image.trackingState == TrackingState.Tracking)
                TryLoadFaceFilterScene();
        }
    }

    private void TryLoadFaceFilterScene()
    {
        if (_triggered) return;

        // Don't yank the scene away mid-page if another page's feature is currently running.
        if (_appStateManager != null && _appStateManager.IsFeatureActive) return;

        _triggered = true;
        Debug.Log($"[Page12ARTracker] page12 scanned — restarting into '{_faceFilterSceneName}'.");
        AppRestarter.RestartIntoScene(_faceFilterSceneName);
    }
}
