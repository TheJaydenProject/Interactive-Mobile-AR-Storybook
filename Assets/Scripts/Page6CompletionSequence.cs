using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Page6CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image      _completionOverlay;
    [SerializeField] private GameObject _page6WordsParent;
    [SerializeField] private float      _fadeInDuration  = 1.5f;
    [SerializeField] private float      _holdDuration    = 1.0f;
    [SerializeField] private float      _fadeOutDuration = 1.5f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _brickVoiceClip;

    [Header("References")]
    [SerializeField] private PendantManager _pendantManager;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private bool _triggered;

    private void Awake()
    {
        if (_completionOverlay == null) return;
        SetOverlayAlpha(0f);
        _completionOverlay.enabled = false;
    }

    public void TriggerCompletion()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        if (_page6WordsParent != null)
            _page6WordsParent.SetActive(false);

        yield return StartCoroutine(FadeInOverlay());

        PlayVoiceLine();

        float waitTime = _brickVoiceClip != null ? _brickVoiceClip.length : _holdDuration;
        yield return new WaitForSeconds(waitTime);

        yield return StartCoroutine(FadeOutOverlay());

        if (_pendantManager != null)
            _pendantManager.CollectRedSpark();
        else
            Debug.LogWarning("[Page6CompletionSequence] _pendantManager not assigned — CollectRedSpark() not called.");

        if (_completionOverlay != null)
            _completionOverlay.gameObject.SetActive(false);

        // Transition has fully played out — release the shared lock so scanning resumes.
        EndFeature();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page6CompletionSequence] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    private IEnumerator FadeInOverlay()
    {
        if (_completionOverlay == null) yield break;

        SetOverlayAlpha(0f);
        _completionOverlay.gameObject.SetActive(true);
        _completionOverlay.enabled = true;
        _completionOverlay.transform.SetAsLastSibling();

        float elapsed = 0f;
        while (elapsed < _fadeInDuration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(Mathf.Clamp01(elapsed / _fadeInDuration));
            yield return null;
        }
        SetOverlayAlpha(1f);
    }

    private IEnumerator FadeOutOverlay()
    {
        if (_completionOverlay == null) yield break;

        float elapsed = 0f;
        while (elapsed < _fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(1f - Mathf.Clamp01(elapsed / _fadeOutDuration));
            yield return null;
        }
        SetOverlayAlpha(0f);
        _completionOverlay.enabled = false;
    }

    private void PlayVoiceLine()
    {
        if (_audioSource != null && _brickVoiceClip != null)
            _audioSource.PlayOneShot(_brickVoiceClip);
        else
            Debug.LogWarning("[Page6CompletionSequence] AudioSource or BrickVoiceClip not assigned.");
    }

    private void SetOverlayAlpha(float alpha)
    {
        Color c = _completionOverlay.color;
        _completionOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }
}
