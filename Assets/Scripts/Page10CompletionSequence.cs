using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Page10CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image      _completionOverlay;
    [SerializeField] private float      _fadeInDuration  = 1.5f;
    [SerializeField] private float      _holdDuration    = 1.0f;
    [SerializeField] private float      _fadeOutDuration = 1.5f;

    [Header("Page 10 References")]
    [SerializeField] private Page10MeterController _page10MeterController;
    [SerializeField] private PendantManager _pendantManager;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private bool _triggered;

    private void Awake()
    {
        if (_completionOverlay != null)
        {
            SetOverlayAlpha(0f);
            _completionOverlay.enabled = false;
        }
    }

    public void TriggerCompletion()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        // Hide the meter when completion starts (matching Page 4's CompletionSequence)
        if (_page10MeterController != null)
            _page10MeterController.SetVisible(false);

        yield return StartCoroutine(FadeInOverlay());

        yield return new WaitForSeconds(_holdDuration);

        yield return StartCoroutine(FadeOutOverlay());

        if (_pendantManager != null)
            _pendantManager.CollectGoldSpark();
        else
            Debug.LogWarning("[Page10CompletionSequence] _pendantManager not assigned — CollectGoldSpark() not called.");

        if (_completionOverlay != null)
            _completionOverlay.gameObject.SetActive(false);

        // Transition has fully played out — release the shared lock so scanning resumes.
        EndFeature();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page10CompletionSequence] _appStateManager not assigned; scanner lock not released.");
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

    private void SetOverlayAlpha(float alpha)
    {
        Color c = _completionOverlay.color;
        _completionOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }
}
