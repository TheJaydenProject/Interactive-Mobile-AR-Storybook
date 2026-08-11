using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class Page6SpeechController : MonoBehaviour, ISpeechToTextListener
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;
    [SerializeField] private string _imageTargetName = "page6";

    [Header("Words")]
    [SerializeField] private GameObject _page6WordsParent;
    [SerializeField] private GameObject _textBackgroundOverlay;
    [SerializeField] private float      _wordsFadeInDuration = 0.5f;
    [SerializeField] private TMP_Text[] _wordLabels;

    [Header("Island")]
    [SerializeField] private GameObject _islandPrefab;
    [SerializeField] private float _islandXOffset = 0.0f;
    [SerializeField] private float _islandYOffset = 0.1f;
    [SerializeField] private float _islandZOffset = 0.0f;
    [SerializeField] private float _islandSpawnDelay = 5.0f;
    [SerializeField] private float _islandScale = 1.0f;
    [SerializeField] private Vector3 _islandRotation = Vector3.zero;
    [SerializeField] private float _islandFadeOutDelay = 2.0f;
    [SerializeField] private float _islandFadeOutDuration = 1.0f;

    [Header("Instructions")]
    [Tooltip("Shown after the island spawn delay. Needs a Graphic (e.g. Image) with Raycast Target on, under a Canvas with a GraphicRaycaster, so tap-anywhere dismissal works.")]
    [SerializeField] private GameObject _instructionPrompt;
    [Tooltip("Delay after the prompt is dismissed before the words/mic listening actually starts.")]
    [SerializeField] private float _postPromptDelay = 1f;

    [Header("Completion")]
    [SerializeField] private Page6CompletionSequence _completionSequence;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    [Header("Debug")]
    [Tooltip("Editor/PC testing: skips the mic/speech-matching step entirely and completes the phrase immediately once reached, so everything after it (island, VFX, pendant, overlay) can be tested without a working mic.")]
    [SerializeField] private bool _debugSkipSpeechRecognition;

    private static readonly Color LitColor   = new Color(1f,         68f / 255f, 0f, 1f); // #FF4400
    private static readonly Color UnlitColor = new Color(51f / 255f, 17f / 255f, 0f, 1f); // #331100

    private static readonly string[] TargetWords =
    {
        "let", "your", "anger", "go", "and",
        "breathe", "in", "and", "out", "with", "me"
    };

    private int       _nextWordIndex;
    // _nextWordIndex at the moment the current SpeechToText session started. Each session's
    // transcript starts empty — it never contains words confirmed in an earlier session — so
    // CheckWords must only replay words confirmed since this point, not since word 0.
    private int       _sessionStartWordIndex;
    private bool      _isListening;
    private bool      _completionTriggered;
    private Coroutine _restartCoroutine;
    private Coroutine _fadeInCoroutine;

    private GameObject         _islandInstance;
    private IslandVfxReference _islandVfxReference;
    private Coroutine  _islandIntroCoroutine;
    private Coroutine  _islandFadeOutCoroutine;
    private Coroutine  _postPromptCoroutine;

    // Mirrors "do I currently hold the shared AppStateManager lock" for the cancel guard.
    // Self-heals via HandleFeatureActiveChanged so a stale true can never linger past this
    // page's own completion or cancellation.
    private bool _isActive;

    // Set when the child backs out via Back; blocks this page re-triggering while its image
    // stays in view, cleared once the image leaves tracking so looking back re-arms it.
    private bool _suppressedWhileTracked;

    private void Awake()
    {
        if (_instructionPrompt != null)
        {
            _instructionPrompt.SetActive(false);
            PromptTapHandler tapHandler = _instructionPrompt.AddComponent<PromptTapHandler>();
            tapHandler.OnTap = HideInstructionPrompt;
        }
        else
        {
            Debug.LogError("[Page6SpeechController] _instructionPrompt not assigned.");
        }
    }

    private void Start()
    {
        _page6WordsParent?.SetActive(false);
        _textBackgroundOverlay?.SetActive(false);
        SpeechToText.Initialize("en-US");
        ResetWordColors();
    }

    private void OnEnable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        else
            Debug.LogError("[Page6SpeechController] ARTrackedImageManager not assigned.");

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

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage image in args.added)
        {
            if (image.referenceImage.name != _imageTargetName) continue;
            StartListening(image.transform);
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != _imageTargetName) continue;

            // Re-arm as soon as the image stops being solidly tracked. XR Simulation / ARCore
            // usually report Limited (not None) on look-away, so keying only off None left the
            // suppression stuck and the feature never reappeared. ponytail: clears on the first
            // non-Tracking frame — heavy tracking flicker could re-arm early; add a short debounce
            // if that shows up on-device.
            if (image.trackingState != TrackingState.Tracking)
                _suppressedWhileTracked = false;

            if (image.trackingState == TrackingState.Tracking)
            {
                if (!IsFeatureInProgress) StartListening(image.transform);
            }
            else if (image.trackingState == TrackingState.None && IsFeatureInProgress)
            {
                CancelIslandAndPrompt();
                StopListening();
            }
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != _imageTargetName) continue;
            _suppressedWhileTracked = false;
            CancelIslandAndPrompt();
            StopListening();
        }
    }

    // True while any stage of the intro-through-listening sequence is in progress, so the
    // tracking-update loop doesn't re-trigger a new pass over an already-running one.
    private bool IsFeatureInProgress =>
        _islandIntroCoroutine != null
        || (_instructionPrompt != null && _instructionPrompt.activeSelf)
        || _postPromptCoroutine != null
        || _isListening;

    // Entry point once this page claims the shared lock: spawn the island, wait, show the
    // instructions, and only after those are dismissed (plus a short buffer) does the words/mic
    // listening sequence actually start — mirrors Page1Manager/Page4ARTracker's island + prompt
    // intro, minus any clouds/overlay/countdown.
    public void StartListening(Transform anchor)
    {
        if (_completionTriggered) return;
        // Backed out of this page but still pointed at it — stay quiet until it's re-acquired.
        if (_suppressedWhileTracked) return;
        // Ignore the scan outright if another page's feature is already active; only this
        // page's own completion releases the shared lock.
        if (!TryBeginFeature()) return;

        _islandIntroCoroutine = StartCoroutine(SpawnIslandThenShowInstructions(anchor));
    }

    private IEnumerator SpawnIslandThenShowInstructions(Transform anchor)
    {
        SpawnIsland(anchor);
        yield return new WaitForSeconds(_islandSpawnDelay);

        _islandIntroCoroutine = null;
        ShowInstructionPrompt();
    }

    private void ShowInstructionPrompt()
    {
        if (_instructionPrompt == null)
        {
            Debug.LogError("[Page6SpeechController] _instructionPrompt is NULL — skipping straight to listening.");
            BeginListening();
            return;
        }

        _instructionPrompt.SetActive(true);
    }

    // Public so it can also be wired to a Button's OnClick() in the Inspector (same pattern as
    // Page4ARTracker.HideInstructionPrompt), in addition to the runtime tap-anywhere handler below.
    public void HideInstructionPrompt()
    {
        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);
        _postPromptCoroutine = StartCoroutine(BeginListeningAfterDelay());
    }

    private IEnumerator BeginListeningAfterDelay()
    {
        yield return new WaitForSeconds(_postPromptDelay);
        _postPromptCoroutine = null;
        BeginListening();
    }

    private void BeginListening()
    {
        if (_debugSkipSpeechRecognition)
        {
            TriggerCompletion();
            return;
        }

        _fadeInCoroutine = StartCoroutine(FadeInWords());
        _isListening = true;
        SpeechToText.RequestPermissionAsync(( permission ) => OnPermissionResult(permission));
    }

    private void SpawnIsland(Transform anchor)
    {
        DespawnIsland();

        if (_islandPrefab == null)
        {
            Debug.LogError("[Page6SpeechController] _islandPrefab not assigned.");
            return;
        }

        Vector3 position = anchor.position + new Vector3(_islandXOffset, _islandYOffset, _islandZOffset);
        _islandInstance = Instantiate(_islandPrefab, position, Quaternion.Euler(_islandRotation), anchor);
        _islandInstance.transform.localScale *= _islandScale;

        _islandVfxReference = _islandInstance.GetComponent<IslandVfxReference>();
        if (_islandVfxReference == null)
        {
            Debug.LogWarning("[Page6SpeechController] _islandPrefab has no IslandVfxReference — Page6CompletionSequence's glow VFX won't play.");
        }
        else if (_islandVfxReference.GlowVfx != null)
        {
            // ParticleSystems default to "Play On Awake", so left alone this fires the instant
            // the island is instantiated — stop it immediately so it stays silent until
            // Page6CompletionSequence explicitly plays it later.
            _islandVfxReference.GlowVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _islandVfxReference.GlowVfx.gameObject.SetActive(false);
        }
    }

    private void DespawnIsland()
    {
        if (_islandInstance != null)
            Destroy(_islandInstance);
        _islandInstance = null;
        _islandVfxReference = null;
    }

    // Called by Page6CompletionSequence right before it needs to play the glow VFX — the VFX
    // lives inside the island prefab itself, so it only exists once the island is spawned.
    public ParticleSystem GetIslandGlowVfx()
    {
        return _islandVfxReference != null ? _islandVfxReference.GlowVfx : null;
    }

    // Called by Page6CompletionSequence once the spark reward sequence reaches its island-exit
    // step. Fire-and-forget — the caller doesn't wait for the shrink to finish, matching
    // Page1Manager's original exit (the lock releases immediately; the shrink plays out after).
    public void BeginIslandShrink()
    {
        if (_islandFadeOutCoroutine != null) return;
        _islandFadeOutCoroutine = StartCoroutine(FadeOutIslandThenDestroy());
    }

    private IEnumerator FadeOutIslandThenDestroy()
    {
        yield return new WaitForSeconds(_islandFadeOutDelay);

        if (_islandInstance != null)
        {
            Renderer[] renderers = _islandInstance.GetComponentsInChildren<Renderer>();
            Vector3 centerPoint = _islandInstance.transform.position;
            if (renderers.Length > 0)
            {
                Bounds combinedBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    combinedBounds.Encapsulate(renderers[i].bounds);
                centerPoint = combinedBounds.center;
            }

            Vector3 startScale = _islandInstance.transform.localScale;
            Vector3 startPosition = _islandInstance.transform.position;
            float elapsed = 0f;

            while (elapsed < _islandFadeOutDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _islandFadeOutDuration);
                float scaleFactor = 1f - t;

                _islandInstance.transform.localScale = startScale * scaleFactor;
                _islandInstance.transform.position = Vector3.Lerp(centerPoint, startPosition, scaleFactor);
                yield return null;
            }
        }

        DespawnIsland();
        _islandFadeOutCoroutine = null;
    }

    private bool TryBeginFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[Page6SpeechController] _appStateManager not assigned; ignoring scan.");
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
    // the shared cancel event reaches all six page scripts.
    private void HandleFeatureCancelled()
    {
        if (!_isActive) return;

        CancelIslandAndPrompt(); // no partial credit — TriggerCompletion() was never reached
        StopListening();
        _suppressedWhileTracked = true;
        _isActive = false;
        EndFeature();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page6SpeechController] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    // Cancels whichever pre-listening stage (island intro / instruction prompt / post-prompt
    // wait) is in progress and removes the island immediately, plus stops an in-progress shrink.
    // Only called on actual cancellation (Back button, tracking loss) — never on successful
    // completion, since the island needs to survive through Page6CompletionSequence's reward
    // sequence for its own shrink-and-destroy exit at the end.
    private void CancelIslandAndPrompt()
    {
        if (_islandIntroCoroutine != null)
        {
            StopCoroutine(_islandIntroCoroutine);
            _islandIntroCoroutine = null;
        }
        if (_postPromptCoroutine != null)
        {
            StopCoroutine(_postPromptCoroutine);
            _postPromptCoroutine = null;
        }
        if (_islandFadeOutCoroutine != null)
        {
            StopCoroutine(_islandFadeOutCoroutine);
            _islandFadeOutCoroutine = null;
        }
        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);
        DespawnIsland();
    }

    // Stops mic listening and hides the words UI. Called both on successful completion and on
    // cancellation — does NOT touch the island; see CancelIslandAndPrompt() for that.
    public void StopListening()
    {
        _isListening = false;
        if (_restartCoroutine != null)
        {
            StopCoroutine(_restartCoroutine);
            _restartCoroutine = null;
        }
        if (_fadeInCoroutine != null)
        {
            StopCoroutine(_fadeInCoroutine);
            _fadeInCoroutine = null;
        }
        SpeechToText.ForceStop();
        _page6WordsParent?.SetActive(false);
        _textBackgroundOverlay?.SetActive(false);
    }

    private void OnPermissionResult(SpeechToText.Permission permission)
    {
        if (permission != SpeechToText.Permission.Granted)
        {
            Debug.LogWarning("[Page6SpeechController] Microphone permission denied or unavailable.");
            return;
        }
        if (!_isListening) return;

        _sessionStartWordIndex = _nextWordIndex;
        if (!SpeechToText.Start(this))
            Debug.LogWarning("[Page6SpeechController] Speech recognition session could not be started.");
    }

    // --- ISpeechToTextListener ---

    public void OnReadyForSpeech() { }

    public void OnBeginningOfSpeech() { }

    public void OnVoiceLevelChanged(float normalizedLevel) { }

    public void OnPartialResultReceived(string spokenText)
    {
        CheckWords(spokenText);
    }

    public void OnResultReceived(string spokenText, int? errorCode)
    {
        if (errorCode != null)
            Debug.LogWarning($"[Page6SpeechController] Speech session ended with error: {errorCode}");

        CheckWords(spokenText);

        if (!_completionTriggered && _isListening)
            _restartCoroutine = StartCoroutine(RestartAssoonAsReady());
    }

    // --- Word detection ---

    private void CheckWords(string transcript)
    {
        if (_completionTriggered) return;
        if (string.IsNullOrEmpty(transcript)) return;

        string lower = transcript.ToLowerInvariant();
        int searchPos = 0;

        // Replay words confirmed *during this session* to establish the correct character
        // offset, so duplicate words like "and" are matched at the right position in sequence.
        // Words confirmed in an earlier session aren't in this session's transcript at all, so
        // only replay from where this session started, not from word 0.
        for (int i = _sessionStartWordIndex; i < _nextWordIndex; i++)
        {
            int pos = FindWord(lower, TargetWords[i], searchPos);
            if (pos < 0) return; // transcript no longer contains expected chain; wait for next partial
            searchPos = pos + TargetWords[i].Length;
        }

        while (_nextWordIndex < TargetWords.Length)
        {
            int pos = FindWord(lower, TargetWords[_nextWordIndex], searchPos);
            if (pos < 0) break;

            SetWordLit(_nextWordIndex);
            searchPos = pos + TargetWords[_nextWordIndex].Length;
            _nextWordIndex++;
        }

        if (_nextWordIndex >= TargetWords.Length)
            TriggerCompletion();
    }

    // Finds the first whole-word occurrence of `word` in `text` at or after `startIndex`.
    // Word boundaries are enforced: the matched characters must not be adjacent to letters.
    private static int FindWord(string text, string word, int startIndex)
    {
        int pos = startIndex;
        while (pos <= text.Length - word.Length)
        {
            int found = text.IndexOf(word, pos, StringComparison.Ordinal);
            if (found < 0) return -1;

            bool startBound = found == 0 || !char.IsLetter(text[found - 1]);
            bool endBound   = found + word.Length >= text.Length || !char.IsLetter(text[found + word.Length]);

            if (startBound && endBound) return found;
            pos = found + 1;
        }
        return -1;
    }

    private void TriggerCompletion()
    {
        if (_completionTriggered) return;
        _completionTriggered = true;
        StopListening();

        if (_completionSequence != null)
            _completionSequence.TriggerCompletion();
        else
            Debug.LogWarning("[Page6SpeechController] _completionSequence not assigned.");
    }

    private IEnumerator RestartAssoonAsReady()
    {
        // Wait only as long as the native side needs to actually finish tearing down the
        // previous session, instead of a flat delay — shrinks the "nothing is listening" gap
        // between sessions as much as possible, so a mid-phrase pause is less likely to swallow
        // a word entirely.
        while (SpeechToText.IsBusy())
            yield return null;

        if (!_isListening || _completionTriggered) yield break;

        // Permission was already granted for this StartListening() call and doesn't change
        // mid-session — skipping the RequestPermissionAsync round-trip on restart shaves more
        // off the gap.
        _sessionStartWordIndex = _nextWordIndex;
        if (!SpeechToText.Start(this))
            Debug.LogWarning("[Page6SpeechController] Speech recognition session could not be restarted.");
    }

    // --- Visuals ---

    private IEnumerator FadeInWords()
    {
        if (_page6WordsParent != null) _page6WordsParent.SetActive(true);
        if (_textBackgroundOverlay != null) _textBackgroundOverlay.SetActive(true);

        Image overlayImage = _textBackgroundOverlay != null ? _textBackgroundOverlay.GetComponent<Image>() : null;
        Color overlayTargetColor = overlayImage != null ? overlayImage.color : Color.white;

        if (_wordLabels != null)
            foreach (TMP_Text label in _wordLabels)
                if (label != null) label.color = new Color(UnlitColor.r, UnlitColor.g, UnlitColor.b, 0f);

        if (overlayImage != null)
            overlayImage.color = new Color(overlayTargetColor.r, overlayTargetColor.g, overlayTargetColor.b, 0f);

        float elapsed = 0f;
        while (elapsed < _wordsFadeInDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _wordsFadeInDuration);

            if (_wordLabels != null)
                foreach (TMP_Text label in _wordLabels)
                    if (label != null)
                        label.color = new Color(UnlitColor.r, UnlitColor.g, UnlitColor.b, Mathf.Lerp(0f, UnlitColor.a, t));

            if (overlayImage != null)
                overlayImage.color = new Color(overlayTargetColor.r, overlayTargetColor.g, overlayTargetColor.b, Mathf.Lerp(0f, overlayTargetColor.a, t));

            yield return null;
        }

        if (_wordLabels != null)
            foreach (TMP_Text label in _wordLabels)
                if (label != null) label.color = UnlitColor;

        if (overlayImage != null)
            overlayImage.color = overlayTargetColor;
    }

    private void ResetWordColors()
    {
        if (_wordLabels == null) return;
        foreach (TMP_Text label in _wordLabels)
        {
            if (label != null) label.color = UnlitColor;
        }
    }

    private void SetWordLit(int index)
    {
        if (_wordLabels == null || index >= _wordLabels.Length) return;
        if (_wordLabels[index] != null)
            _wordLabels[index].color = LitColor;
    }

    // Runtime-attached to _instructionPrompt so any tap on it dismisses it, without requiring
    // a Button to be set up in the Inspector (same trick Page4ARTracker uses for its prompt).
    private class PromptTapHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnTap;
        public void OnPointerClick(PointerEventData eventData) => OnTap?.Invoke();
    }
}
