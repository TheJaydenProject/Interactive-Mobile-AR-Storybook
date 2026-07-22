using System.Collections;
using TMPro;
using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [Header("Menu UI")]
    [SerializeField] private GameObject _menuUIGroup;

    [Header("Permission Fallback")]
    [SerializeField] private GameObject _permissionDeniedGroup;
    [SerializeField] private TMP_Text   _permissionDeniedMessage;

    private bool _isRequestingPermissions;

    public void OnStartPressed()
    {
        if (_isRequestingPermissions) return;

        if (_menuUIGroup == null)
        {
            Debug.LogError("[MenuManager] _menuUIGroup not assigned; cannot proceed.");
            return;
        }

        StartCoroutine(RequestPermissionsAndProceed());
    }

    // Camera and microphone are requested together here, at the menu, rather than lazily
    // when Page 6's mic feature runs — so both prompts happen up front and a denial can be
    // handled before any AR/scanner UI loads.
    private IEnumerator RequestPermissionsAndProceed()
    {
        _isRequestingPermissions = true;

        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam | UserAuthorization.Microphone);

        bool cameraGranted = Application.HasUserAuthorization(UserAuthorization.WebCam);
        bool micGranted    = Application.HasUserAuthorization(UserAuthorization.Microphone);

        _isRequestingPermissions = false;

        if (cameraGranted && micGranted)
            ProceedToScanner();
        else
            ShowPermissionDenied(cameraGranted, micGranted);
    }

    private void ProceedToScanner()
    {
        // Deactivate rather than destroy so the menu can be restored later (e.g. if the user
        // backs out of the scanner) without reloading the scene or re-instantiating UI.
        _menuUIGroup.SetActive(false);

        // TODO: load/enable the camera scanner scene once it exists.
    }

    private void ShowPermissionDenied(bool cameraGranted, bool micGranted)
    {
        if (_permissionDeniedGroup == null)
        {
            Debug.LogWarning("[MenuManager] Camera/microphone permission denied and _permissionDeniedGroup not assigned; no fallback UI shown.");
            return;
        }

        if (_permissionDeniedMessage != null)
            _permissionDeniedMessage.text = BuildDenialMessage(cameraGranted, micGranted);

        _permissionDeniedGroup.SetActive(true);
    }

    private static string BuildDenialMessage(bool cameraGranted, bool micGranted)
    {
        if (!cameraGranted && !micGranted)
            return "Camera and microphone access are required to play. Please enable both in your device settings.";
        if (!cameraGranted)
            return "Camera access is required to play. Please enable it in your device settings.";
        return "Microphone access is required to play. Please enable it in your device settings.";
    }
}
