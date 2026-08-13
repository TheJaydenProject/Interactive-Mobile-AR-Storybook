using UnityEngine;

/// <summary>
/// Controls the Ar2 scene (Page 12's face filter). This scene's own AR session is configured to
/// start directly in User-facing/face-tracking mode (set on its ARCameraManager in the Inspector,
/// with an ARFaceManager already enabled) — no camera switching happens in this scene at all.
/// Attach your filter content (e.g. the crown) as the ARFaceManager's own "Face Prefab" field.
///
/// Returning to Ar1 goes through a full process restart (AppRestarter), not a plain scene load —
/// we've only confirmed the front-facing config crashes as a second same-process session; going
/// back to Ar1's world-tracking config afterward is untested, so this stays on the safe, confirmed
/// path (every AR session always the first and only one in its process) in both directions.
/// </summary>
public class Page12FaceFilterScene : MonoBehaviour
{
    [Tooltip("Name of the main scene to restart into. Must be added to File > Build Settings > Scenes In Build.")]
    [SerializeField] private string _mainSceneName = "Ar1";

    // Guards against a rapid double-tap on Back calling AppRestarter.RestartIntoScene() twice
    // while the process kill is in flight.
    private bool _triggered;

    // Wire this to a Back/Done button's OnClick().
    public void ReturnToMainScene()
    {
        if (_triggered) return;
        _triggered = true;
        AppRestarter.RestartIntoScene(_mainSceneName);
    }

    // Wire this to a capture button, if you want one here.
    public void CaptureScreenshot()
    {
        ScreenCapture.CaptureScreenshot($"page12_selfie_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
    }
}
