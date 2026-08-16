using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class MenuManager : MonoBehaviour
{
    [Header("Menu UI")]
    [SerializeField] private GameObject _menuUIGroup;

    [Tooltip("Reset to a clean slate the instant Start is pressed, so a new session never inherits sparks collected in a previous playthrough.")]
    [SerializeField] private PendantManager _pendantManager;

    [Header("Name Entry")]
    [Tooltip("Shown after Start is pressed, before the Welcome screen. Needs a name input field and a Continue button wired to OnNameSubmitted().")]
    [SerializeField] private GameObject _nameEntryGroup;
    [SerializeField] private TMP_InputField _nameInputField;

    [Header("Welcome")]
    [Tooltip("Shown after a name is submitted, before the instructions screen. Appears instantly (no fade) — needs a Continue button wired to OnWelcomeContinuePressed().")]
    [SerializeField] private GameObject _welcomeGroup;
    [SerializeField] private TMP_Text    _welcomeMessage;
    [Tooltip("A sub-element within the Welcome group (e.g. its message/decoration) that fades in once the Welcome group is already showing. No CanvasGroup needed on it, one is added automatically at runtime.")]
    [SerializeField] private GameObject _welcomeFadeInGroup;
    [Tooltip("Kept slow/gentle on purpose — this is a subtle fade, not an attention-grabbing one.")]
    [SerializeField] private float       _welcomeFadeInDuration = 1.2f;

    [Header("Instructions")]
    [Tooltip("Shown after the Welcome screen, before the camera/mic permission prompts. Needs its own Continue button wired to OnInstructionsContinuePressed().")]
    [SerializeField] private GameObject _instructionUIGroup;

    [Header("Permission Fallback")]
    [SerializeField] private GameObject _permissionDeniedGroup;
    [SerializeField] private TMP_Text   _permissionDeniedMessage;

    [Header("AR Scanning")]
    [Tooltip("Disabled at Awake so pages can't be scanned behind the menu; re-enabled once Start is pressed and permissions are granted.")]
    [SerializeField] private ARSession _arSession;

    [Header("Back To Menu")]
    [Tooltip("Icon/button shown the whole time the camera/scanner is active — unlike BackButtonController's cancel button, this isn't gated behind a page feature being active. Needs OnClick() wired to OnBackToMenuPressed().")]
    [SerializeField] private GameObject _backToMenuButton;

    [Tooltip("Used to stop whichever page feature (if any) is mid-interaction when Back To Menu is pressed, same as BackButtonController's cancel button. Also used to pause/resume Idle Bgm Source around page features once the camera is open.")]
    [SerializeField] private AppStateManager _appStateManager;

    [Header("Music")]
    [Tooltip("Single looping BGM track that plays continuously for the whole app session (menu, instructions, and idle scanning all share it). Muted whenever a page feature is active (minigame/narration running), unmuted the rest of the time — never stopped/restarted.")]
    [SerializeField] private AudioSource _bgmSource;

    private bool _isRequestingPermissions;
    private CanvasGroup _welcomeCanvasGroup;
    private Coroutine _welcomeFadeCoroutine;

    private void Awake()
    {
        // QualitySettings.vSyncCount is ignored on Android, so frame rate has to be set here instead.
        Application.targetFrameRate = 60;

        if (_arSession != null) _arSession.enabled = false;
        else Debug.LogError("[MenuManager] _arSession not assigned; AR scanning will not be gated behind Start.");

        if (_backToMenuButton != null) _backToMenuButton.SetActive(false);
        if (_nameEntryGroup != null) _nameEntryGroup.SetActive(false);
        if (_welcomeGroup != null) _welcomeGroup.SetActive(false);

        if (_welcomeFadeInGroup != null)
        {
            _welcomeCanvasGroup = _welcomeFadeInGroup.GetComponent<CanvasGroup>();
            if (_welcomeCanvasGroup == null)
                _welcomeCanvasGroup = _welcomeFadeInGroup.AddComponent<CanvasGroup>();

            _welcomeCanvasGroup.alpha = 0f;
        }

        if (_bgmSource != null) _bgmSource.Play();
    }

    private void OnEnable()
    {
        if (_appStateManager != null)
            _appStateManager.OnFeatureActiveChanged += HandleFeatureActiveChangedForBgm;
    }

    private void OnDisable()
    {
        if (_appStateManager != null)
            _appStateManager.OnFeatureActiveChanged -= HandleFeatureActiveChangedForBgm;
    }

    // Mute instead of Stop/Play — the track just keeps looping in the background the whole time,
    // so there's never a restart pop/gap when a page feature ends.
    private void HandleFeatureActiveChangedForBgm(bool isFeatureActive)
    {
        if (_bgmSource != null) _bgmSource.mute = isFeatureActive;
    }

    public void OnStartPressed()
    {
        if (_menuUIGroup == null)
        {
            Debug.LogError("[MenuManager] _menuUIGroup not assigned; cannot proceed.");
            return;
        }

        if (_nameEntryGroup == null)
        {
            Debug.LogError("[MenuManager] _nameEntryGroup not assigned; cannot proceed.");
            return;
        }

        if (_pendantManager != null) _pendantManager.ResetSparks();
        else Debug.LogWarning("[MenuManager] _pendantManager not assigned; sparks won't reset for this session.");

        _menuUIGroup.SetActive(false);
        _nameEntryGroup.SetActive(true);
    }

    // Wire this to the name entry screen's own Continue button.
    public void OnNameSubmitted()
    {
        if (_nameEntryGroup == null)
        {
            Debug.LogError("[MenuManager] _nameEntryGroup not assigned; cannot proceed.");
            return;
        }

        if (_welcomeGroup == null)
        {
            Debug.LogError("[MenuManager] _welcomeGroup not assigned; cannot proceed.");
            return;
        }

        string enteredName = _nameInputField != null ? _nameInputField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(enteredName)) enteredName = "friend"; // left blank — still let them through

        if (_welcomeMessage != null)
            _welcomeMessage.text = $"Welcome, {enteredName}! Your journey starts now.";

        _nameEntryGroup.SetActive(false);
        _welcomeGroup.SetActive(true); // instant — no fade on the panel itself

        if (_welcomeFadeCoroutine != null) StopCoroutine(_welcomeFadeCoroutine);
        _welcomeFadeCoroutine = StartCoroutine(FadeInWelcome());
    }

    // Fades in only _welcomeFadeInGroup (a sub-element), once the Welcome panel itself is already
    // showing — the panel switch (SetActive above) is instant, this is just a subtle accent on top.
    private IEnumerator FadeInWelcome()
    {
        if (_welcomeCanvasGroup == null)
        {
            _welcomeFadeCoroutine = null;
            yield break;
        }

        _welcomeCanvasGroup.alpha = 0f;

        float elapsed = 0f;
        while (elapsed < _welcomeFadeInDuration)
        {
            elapsed += Time.deltaTime;
            _welcomeCanvasGroup.alpha = Mathf.Clamp01(elapsed / _welcomeFadeInDuration);
            yield return null;
        }

        _welcomeCanvasGroup.alpha = 1f;
        _welcomeFadeCoroutine = null;
    }

    // Wire this to the Welcome screen's own Continue button.
    public void OnWelcomeContinuePressed()
    {
        if (_welcomeGroup == null)
        {
            Debug.LogError("[MenuManager] _welcomeGroup not assigned; cannot proceed.");
            return;
        }

        if (_instructionUIGroup == null)
        {
            Debug.LogError("[MenuManager] _instructionUIGroup not assigned; cannot proceed.");
            return;
        }

        // In case Continue is tapped before the fade-in finishes.
        if (_welcomeFadeCoroutine != null)
        {
            StopCoroutine(_welcomeFadeCoroutine);
            _welcomeFadeCoroutine = null;
        }

        _welcomeGroup.SetActive(false);
        _instructionUIGroup.SetActive(true);
    }

    // Wire this to the instruction screen's own Continue/Got It button.
    public void OnInstructionsContinuePressed()
    {
        if (_isRequestingPermissions) return;

        if (_instructionUIGroup == null)
        {
            Debug.LogError("[MenuManager] _instructionUIGroup not assigned; cannot proceed.");
            return;
        }

        _instructionUIGroup.SetActive(false);
        StartCoroutine(RequestPermissionsAndProceed());
    }

    // Camera and microphone are requested together here, at the menu, rather than lazily
    // when Page 6's mic feature runs — so both prompts happen up front and a denial can be
    // handled before any AR/scanner UI loads. Gallery-write is requested here too (for Page 12's
    // selfie capture) but isn't gated on — a denial there shouldn't block the rest of the book,
    // only the save attempt later, which already logs/handles a failure on its own.
    private IEnumerator RequestPermissionsAndProceed()
    {
        _isRequestingPermissions = true;

        bool galleryPermissionResolved = false;
        NativeGallery.RequestPermissionAsync(
            permission => galleryPermissionResolved = true,
            NativeGallery.PermissionType.Write,
            NativeGallery.MediaType.Image);
        yield return new WaitUntil(() => galleryPermissionResolved);

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

        if (_arSession != null) _arSession.enabled = true;
        else Debug.LogError("[MenuManager] _arSession not assigned; cannot enable AR scanning.");

        if (_backToMenuButton != null) _backToMenuButton.SetActive(true);
    }

    // Wire this to the back-to-menu icon/button shown while the camera/scanner is active.
    public void OnBackToMenuPressed()
    {
        // Stop whichever page feature is mid-interaction (island, minigame, coroutines, etc.)
        // before leaving — same cancel path BackButtonController uses. CancelFeature() is a
        // no-op if nothing's active, so this is safe to call unconditionally.
        if (_appStateManager != null) _appStateManager.CancelFeature();
        else Debug.LogWarning("[MenuManager] _appStateManager not assigned; an in-progress page feature (if any) won't be stopped.");

        if (_arSession != null) _arSession.enabled = false;
        else Debug.LogError("[MenuManager] _arSession not assigned; cannot disable AR scanning.");

        if (_menuUIGroup != null) _menuUIGroup.SetActive(true);
        else Debug.LogError("[MenuManager] _menuUIGroup not assigned; cannot show menu.");

        if (_backToMenuButton != null) _backToMenuButton.SetActive(false);
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
