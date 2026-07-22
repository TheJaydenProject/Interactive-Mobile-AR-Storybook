using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Page 11 completion: fades a screen overlay, then reveals the Phoenix in its "transformed"
/// state. Mirrors Page10CompletionSequence's fade skeleton. Instead of writing a spark to
/// PendantManager, it swaps the grey Phoenix placeholder to a bright rainbow-cycling material
/// (stand-in for the Rainbow Phoenix model, which drops onto the same object later).
/// </summary>
public class Page11CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image _completionOverlay;
    [Tooltip("Delay in seconds after all sparks are placed before the sequence begins.")]
    [SerializeField] private float _delayBeforeSequence = 2.0f;
    [SerializeField] private float _fadeInDuration  = 1.5f;
    [SerializeField] private float _holdDuration     = 1.0f;
    [SerializeField] private float _fadeOutDuration  = 1.5f;

    [Header("Phoenix Transformation")]
    [SerializeField] private Renderer _phoenixRenderer;
    [Tooltip("If true, the transformed Phoenix cycles hue (rainbow) instead of holding a solid colour.")]
    [SerializeField] private bool  _rainbowCycle = true;
    [SerializeField] private float _rainbowSpeed = 0.4f;

    [Header("End of Sequence")]
    [Tooltip("Objects to completely hide at the end of the sequence (e.g., the Phoenix and the sparks).")]
    [SerializeField] private GameObject[] _objectsToDisable;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private bool _triggered;
    private bool _transformed;

    // Drive the Phoenix colour via a property block so it overrides the placeholder tint
    // (also set via a property block) regardless of shared-material state.
    private MaterialPropertyBlock _mpb;
    private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        if (_completionOverlay != null)
        {
            SetOverlayAlpha(0f);
            _completionOverlay.enabled = false;
        }
    }

    private void Update()
    {
        if (_transformed && _rainbowCycle && _phoenixRenderer != null)
        {
            float hue = Mathf.Repeat(Time.time * _rainbowSpeed, 1f);
            SetPhoenixColor(Color.HSVToRGB(hue, 0.8f, 1f));
        }
    }

    private void SetPhoenixColor(Color color)
    {
        if (_phoenixRenderer == null) return;
        _mpb ??= new MaterialPropertyBlock();
        _phoenixRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(s_baseColorId, color);
        _phoenixRenderer.SetPropertyBlock(_mpb);
    }

    public void TriggerCompletion()
    {
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        // Start transforming and changing colors immediately during the delay
        TransformPhoenix();

        if (_delayBeforeSequence > 0f)
            yield return new WaitForSeconds(_delayBeforeSequence);

        yield return StartCoroutine(FadeInOverlay());

        // Hide objects while the screen is completely covered
        if (_phoenixRenderer != null)
        {
            _phoenixRenderer.gameObject.SetActive(false);
        }

        if (_objectsToDisable != null)
        {
            foreach (var obj in _objectsToDisable)
            {
                if (obj != null) obj.SetActive(false);
            }
        }

        yield return new WaitForSeconds(_holdDuration);

        yield return StartCoroutine(FadeOutOverlay());

        if (_completionOverlay != null)
            _completionOverlay.gameObject.SetActive(false);

        // Transition has fully played out — release the shared lock so scanning resumes.
        EndFeature();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page11CompletionSequence] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    private void TransformPhoenix()
    {
        _transformed = true;

        if (_phoenixRenderer == null)
        {
            Debug.LogWarning("[Page11CompletionSequence] _phoenixRenderer not assigned — transformation not shown.");
            return;
        }

        // Immediate bright colour; Update() takes over the rainbow cycle if enabled.
        SetPhoenixColor(Color.HSVToRGB(0f, 0.8f, 1f));
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
        if (_completionOverlay == null) return;
        Color c = _completionOverlay.color;
        _completionOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }
}
