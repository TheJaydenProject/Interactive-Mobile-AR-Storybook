using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class Page6SpeechController : MonoBehaviour, ISpeechToTextListener
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;
    [SerializeField] private string _imageTargetName = "page6Placeholder";

    [Header("Words")]
    [SerializeField] private TMP_Text[] _wordLabels;

    [Header("Completion")]
    [SerializeField] private Page6CompletionSequence _completionSequence;

    private static readonly Color LitColor   = new Color(1f,         68f / 255f, 0f, 1f); // #FF4400
    private static readonly Color UnlitColor = new Color(51f / 255f, 17f / 255f, 0f, 1f); // #331100

    private static readonly string[] TargetWords =
    {
        "let", "your", "anger", "go", "and",
        "breathe", "in", "and", "out", "with", "me"
    };

    private int       _nextWordIndex;
    private bool      _isListening;
    private bool      _completionTriggered;
    private Coroutine _restartCoroutine;

    private void Start()
    {
        SpeechToText.Initialize("en-US");
        ResetWordColors();
    }

    private void OnEnable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        else
            Debug.LogError("[Page6SpeechController] ARTrackedImageManager not assigned.");
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
            if (image.referenceImage.name != _imageTargetName) continue;
            StartListening();
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != _imageTargetName) continue;

            if (image.trackingState == TrackingState.Tracking && !_isListening)
                StartListening();
            else if (image.trackingState == TrackingState.None && _isListening)
                StopListening();
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != _imageTargetName) continue;
            StopListening();
        }
    }

    public void StartListening()
    {
        if (_completionTriggered) return;
        _isListening = true;
        SpeechToText.RequestPermissionAsync(( permission ) => OnPermissionResult(permission));
    }

    public void StopListening()
    {
        _isListening = false;
        if (_restartCoroutine != null)
        {
            StopCoroutine(_restartCoroutine);
            _restartCoroutine = null;
        }
        SpeechToText.ForceStop();
    }

    private void OnPermissionResult(SpeechToText.Permission permission)
    {
        if (permission != SpeechToText.Permission.Granted)
        {
            Debug.LogWarning("[Page6SpeechController] Microphone permission denied or unavailable.");
            return;
        }
        if (!_isListening) return;
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
            _restartCoroutine = StartCoroutine(RestartAfterDelay());
    }

    // --- Word detection ---

    private void CheckWords(string transcript)
    {
        if (_completionTriggered) return;
        if (string.IsNullOrEmpty(transcript)) return;

        string lower = transcript.ToLowerInvariant();
        int searchPos = 0;

        // Replay already-confirmed words to establish the correct character offset,
        // so duplicate words like "and" are matched at the right position in sequence.
        for (int i = 0; i < _nextWordIndex; i++)
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

    private IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSeconds(0.5f);
        if (_isListening && !_completionTriggered)
            SpeechToText.RequestPermissionAsync(( permission ) => OnPermissionResult(permission));
    }

    // --- Visuals ---

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
}
