using System.Collections;
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

    [Header("Music")]
    [Tooltip("Looping BGM for the crown/mirror scene. No narration ever plays here, so it's safe to run the whole time — starts automatically and needs no explicit stop, since the only way out of this scene is a full process restart back to Ar1 (see class summary).")]
    [SerializeField] private AudioSource _bgmSource;

    [Header("Selfie Capture")]
    [SerializeField] private string _albumName = "EiraAR";
    [SerializeField] private string _fileNamePrefix = "eira_selfie";
    [Tooltip("Hidden for one frame during capture so they don't end up in the saved photo (e.g. the shutter button itself).")]
    [SerializeField] private GameObject[] _uiToHideDuringCapture;
    [SerializeField] private AudioSource _shutterAudioSource;
    [SerializeField] private AudioClip _shutterSfx;
    [Tooltip("Minimum seconds between captures, so mashing the shutter button doesn't spam the gallery.")]
    [SerializeField] private float _captureCooldown = 1f;

    private float _lastCaptureTime = -Mathf.Infinity;

    // Guards against a rapid double-tap on Back calling AppRestarter.RestartIntoScene() twice
    // while the process kill is in flight.
    private bool _triggered;

    private void Start()
    {
        if (_bgmSource != null) _bgmSource.Play();
    }

    // Wire this to a Back/Done button's OnClick().
    public void ReturnToMainScene()
    {
        if (_triggered) return;
        _triggered = true;
        AppRestarter.RestartIntoScene(_mainSceneName);
    }

    // Wire this to the shutter button's OnClick().
    public void CaptureScreenshot()
    {
        if (Time.time - _lastCaptureTime < _captureCooldown) return;
        _lastCaptureTime = Time.time;

        if (_shutterAudioSource != null && _shutterSfx != null)
            _shutterAudioSource.PlayOneShot(_shutterSfx);

        StartCoroutine(CaptureAndSaveRoutine());
    }

    private IEnumerator CaptureAndSaveRoutine()
    {
        SetUiVisible(false);

        // Wait a frame so the hidden UI is actually gone from the render before grabbing the screenshot.
        yield return new WaitForEndOfFrame();

        Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();

        SetUiVisible(true);

        if (screenshot == null)
        {
            Debug.LogError("[Page12FaceFilterScene] Screenshot capture returned null.");
            yield break;
        }

        string fileName = $"{_fileNamePrefix}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";

        NativeGallery.SaveImageToGallery(screenshot, _albumName, fileName, (bool success, string savedPath) =>
        {
            if (success)
                Debug.Log($"[Page12FaceFilterScene] Saved to {savedPath}");
            else
                Debug.LogWarning("[Page12FaceFilterScene] Gallery save failed or permission denied.");
        });

        // CaptureScreenshotAsTexture allocates a new Texture2D each call; must destroy it manually or it leaks.
        Destroy(screenshot);
    }

    private void SetUiVisible(bool visible)
    {
        if (_uiToHideDuringCapture == null) return;

        foreach (GameObject uiElement in _uiToHideDuringCapture)
        {
            if (uiElement != null)
                uiElement.SetActive(visible);
        }
    }
}
