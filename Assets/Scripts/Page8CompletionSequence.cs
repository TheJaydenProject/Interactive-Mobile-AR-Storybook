using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Page8CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image      _completionOverlay;
    [SerializeField] private float      _fadeInDuration  = 1.5f;
    [SerializeField] private float      _holdDuration    = 1.0f;
    [SerializeField] private float      _fadeOutDuration = 1.5f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip   _voiceClip;

    [Header("References")]
    [SerializeField] private PendantManager _pendantManager;

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
        yield return StartCoroutine(FadeInOverlay());

        PlayVoiceLine();

        float waitTime = _voiceClip != null ? _voiceClip.length : _holdDuration;
        yield return new WaitForSeconds(waitTime);

        yield return StartCoroutine(FadeOutOverlay());

        if (_pendantManager != null)
            _pendantManager.CollectYellowSpark();
        else
            Debug.LogWarning("[Page8CompletionSequence] _pendantManager not assigned — CollectYellowSpark() not called.");

        if (_completionOverlay != null)
            _completionOverlay.gameObject.SetActive(false);
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
        if (_audioSource != null && _voiceClip != null)
            _audioSource.PlayOneShot(_voiceClip);
        else
            Debug.LogWarning("[Page8CompletionSequence] AudioSource or VoiceClip not assigned.");
    }

    private void SetOverlayAlpha(float alpha)
    {
        Color c = _completionOverlay.color;
        _completionOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }
}
