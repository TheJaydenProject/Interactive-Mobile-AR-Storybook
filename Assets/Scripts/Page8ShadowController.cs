using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class Page8ShadowController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image _shadowImage;
    
    [Header("AR References")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;
    [SerializeField] private string _targetImageName = "page8Placeholder";

    [Header("Settings")]
    [SerializeField] private float _holdDuration = 3f;
    [SerializeField] private float _fadeDuration = 1f;
    [SerializeField] private float _preFadeShrinkDuration = 0.4f;
    [SerializeField] private AnimationCurve _morphCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    [Header("Dependencies")]
    [SerializeField] private Page8CompletionSequence _completionSequence;
    [SerializeField] private PendantManager _pendantManager;

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private bool _isCompleted;
    private Material _morphMaterial;
    private float _holdProgress;
    private bool _isHolding;

    // Mirrors "do I currently hold the shared AppStateManager lock" for the cancel guard, and
    // also gates Update() so its per-frame hold/morph work doesn't run before this page is
    // scanned (it previously ran unconditionally, same class of issue Page10OrbController had).
    private bool _isActive;

    // Set when the child backs out via Back; blocks this page re-triggering while its image
    // stays in view, cleared once the image leaves tracking so looking back re-arms it.
    private bool _suppressedWhileTracked;

    private void Awake()
    {
        if (_shadowImage != null)
        {
            var handler = _shadowImage.gameObject.AddComponent<ShadowPointerHandler>();
            handler.OnPointerDownAction = HandlePointerDown;
            handler.OnPointerUpAction = HandlePointerUp;
            _shadowImage.enabled = false;

            if (_shadowImage.material != null)
            {
                _morphMaterial = new Material(_shadowImage.material);
                _shadowImage.material = _morphMaterial;
            }
        }
        else
        {
            Debug.LogWarning("[Page8ShadowController] _shadowImage is missing.");
        }
    }

    private void OnEnable()
    {
        if (_trackedImageManager != null)
        {
            _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }
        else
        {
            Debug.LogWarning("[Page8ShadowController] _trackedImageManager is missing.");
        }

        if (_appStateManager != null)
        {
            _appStateManager.OnFeatureCancelled += HandleFeatureCancelled;
            _appStateManager.OnFeatureActiveChanged += HandleFeatureActiveChanged;
        }
    }

    private void OnDisable()
    {
        if (_trackedImageManager != null)
        {
            _trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }

        if (_appStateManager != null)
        {
            _appStateManager.OnFeatureCancelled -= HandleFeatureCancelled;
            _appStateManager.OnFeatureActiveChanged -= HandleFeatureActiveChanged;
        }
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        foreach (var trackedImage in eventArgs.added)
        {
            CheckImage(trackedImage);
        }
        foreach (var trackedImage in eventArgs.updated)
        {
            CheckImage(trackedImage);
        }
        foreach (var trackedImage in eventArgs.removed)
        {
            // Image dropped entirely — re-arm so looking back replays.
            if (trackedImage.referenceImage.name == _targetImageName)
                _suppressedWhileTracked = false;
        }
    }

    private void CheckImage(ARTrackedImage trackedImage)
    {
        if (_isCompleted) return;

        if (trackedImage.referenceImage.name == _targetImageName)
        {
            if (trackedImage.trackingState == TrackingState.Tracking)
            {
                // Backed out of this page but still pointed at it — stay quiet until re-acquired.
                if (_suppressedWhileTracked) return;

                if (_shadowImage != null && !_shadowImage.enabled)
                {
                    // Ignore the scan outright if another page's feature is already active;
                    // only this page's own completion releases the shared lock.
                    if (!TryBeginFeature()) return;
                    _shadowImage.enabled = true;
                }
            }
            else if (trackedImage.trackingState == TrackingState.None)
            {
                _suppressedWhileTracked = false; // image left view → re-arm so looking back replays
            }
        }
    }

    private bool TryBeginFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[Page8ShadowController] _appStateManager not assigned; ignoring scan.");
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

        _isHolding = false;
        _holdProgress = 0f; // no partial credit — reset so a future attempt starts from scratch

        if (_shadowImage != null)
        {
            _shadowImage.transform.localScale = Vector3.one;
            // Disabling it re-arms CheckImage()'s "!_shadowImage.enabled" claim gate for next time.
            _shadowImage.enabled = false;
        }

        _suppressedWhileTracked = true;
        _isActive = false;
        EndFeature();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page8ShadowController] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    private void Update()
    {
        if (_isCompleted || !_isActive) return;

        if (_isHolding)
        {
            _holdProgress += Time.deltaTime / _holdDuration;
        }
        else
        {
            _holdProgress -= Time.deltaTime / _holdDuration;
        }

        _holdProgress = Mathf.Clamp01(_holdProgress);

        if (_shadowImage != null)
        {
            _shadowImage.transform.localScale = Vector3.one * (1f - _holdProgress);

            if (_morphMaterial != null)
            {
                Rect rect = _shadowImage.rectTransform.rect;
                _morphMaterial.SetFloat("_RectWidth", rect.width);
                _morphMaterial.SetFloat("_RectHeight", rect.height);

                float morphTime = Mathf.Max(0f, (_holdProgress * _holdDuration) - _preFadeShrinkDuration);
                float morphDuration = Mathf.Max(0.01f, _holdDuration - _preFadeShrinkDuration);
                float morphProgress = Mathf.Clamp01(morphTime / morphDuration);
                morphProgress = _morphCurve.Evaluate(morphProgress);

                // Morph width and height to form a perfect square
                float targetSize = Mathf.Min(rect.width, rect.height);
                float currentWidth = Mathf.Lerp(rect.width, targetSize, morphProgress);
                float currentHeight = Mathf.Lerp(rect.height, targetSize, morphProgress);
                
                _morphMaterial.SetFloat("_DrawWidth", currentWidth);
                _morphMaterial.SetFloat("_DrawHeight", currentHeight);

                float maxRadius = targetSize * 0.5f;
                _morphMaterial.SetFloat("_Radius", Mathf.Lerp(0f, maxRadius, morphProgress));
            }
        }

        if (_holdProgress >= 1f && !_isCompleted)
        {
            _isCompleted = true;
            
            if (_pendantManager != null)
                _pendantManager.CollectYellowSpark();
            else
                Debug.LogWarning("[Page8ShadowController] _pendantManager is missing.");
                
            if (_completionSequence != null)
                _completionSequence.TriggerCompletion();
            else
                Debug.LogWarning("[Page8ShadowController] _completionSequence is missing.");
                
            StartCoroutine(FadeOutShadow());
        }
    }

    private void HandlePointerDown()
    {
        if (_isCompleted) return;
        _isHolding = true;
    }

    private void HandlePointerUp()
    {
        if (_isCompleted) return;
        _isHolding = false;
    }

    private IEnumerator FadeOutShadow()
    {
        if (_shadowImage == null) yield break;
        
        Color startColor = _shadowImage.color;
        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(startColor.a, 0f, elapsed / _fadeDuration);
            _shadowImage.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null;
        }
        
        _shadowImage.enabled = false;
    }

    private class ShadowPointerHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public System.Action OnPointerDownAction;
        public System.Action OnPointerUpAction;

        public void OnPointerDown(PointerEventData eventData) => OnPointerDownAction?.Invoke();
        public void OnPointerUp(PointerEventData eventData) => OnPointerUpAction?.Invoke();
    }
}
