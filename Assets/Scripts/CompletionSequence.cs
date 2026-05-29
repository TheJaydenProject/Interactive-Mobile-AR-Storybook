using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CompletionSequence : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image  _completionOverlay;
    [SerializeField] private float  _uiFadeOutDuration = 0.5f;
    [SerializeField] private float  _fadeInDuration    = 1.5f;
    [SerializeField] private float  _holdDuration      = 1.0f;
    [SerializeField] private float  _fadeOutDuration   = 1.5f;

    [Header("Audio")]
    [SerializeField] private AudioSource _voiceAudioSource;
    [SerializeField] private AudioClip   _riverSpiritClip;

    [Header("Scene References")]
    [SerializeField] private DropSpawner         _dropSpawner;
    [SerializeField] private Image               _catchZoneImage;
    [SerializeField] private DropMeterController _dropMeterController;
    [SerializeField] private Canvas              _arCanvas;
    [SerializeField] private PendantManager      _pendantManager;

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
        Debug.Log("[CompletionSequence] TriggerCompletion called. _triggered=" + _triggered);
        if (_triggered) return;
        _triggered = true;
        StartCoroutine(CompletionRoutine());
    }

    private IEnumerator CompletionRoutine()
    {
        Debug.Log("[CompletionSequence] Step 1 — stopping spawner");
        if (_dropSpawner == null) Debug.LogError("[CompletionSequence] _dropSpawner is NULL");
        else _dropSpawner.CompleteAndStop();

        Debug.Log("[CompletionSequence] Step 2 — destroying drops");
        DestroyAllActiveDrops();

        Debug.Log("[CompletionSequence] Step 3 — fading out UI");
        if (_catchZoneImage == null) Debug.LogError("[CompletionSequence] _catchZoneImage is NULL");
        if (_dropMeterController == null) Debug.LogError("[CompletionSequence] _dropMeterController is NULL");
        yield return StartCoroutine(FadeOutUI());

        Debug.Log("[CompletionSequence] Step 4 — fading in overlay");
        if (_completionOverlay == null) Debug.LogError("[CompletionSequence] _completionOverlay is NULL");
        yield return StartCoroutine(FadeInOverlay());

        Debug.Log("[CompletionSequence] Step 5 — playing voice line");
        PlayVoiceLine();

        if (_riverSpiritClip != null)
            yield return new WaitForSeconds(_riverSpiritClip.length);
        else
            yield return new WaitForSeconds(_holdDuration);

        Debug.Log("[CompletionSequence] Step 6 — fading out overlay");
        yield return StartCoroutine(FadeOutOverlay());

        Debug.Log("[CompletionSequence] Step 7 — cleanup");
        NotifyPendantManager();
        Cleanup();
    }

    private IEnumerator FadeOutUI()
    {
        float elapsed = 0f;
        while (elapsed < _uiFadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / _uiFadeOutDuration);

            if (_catchZoneImage != null)
            {
                Color c = _catchZoneImage.color;
                _catchZoneImage.color = new Color(c.r, c.g, c.b, alpha);
            }

            if (_dropMeterController != null)
                _dropMeterController.SetAlpha(alpha);

            yield return null;
        }

        if (_catchZoneImage != null) _catchZoneImage.enabled = false;
        if (_dropMeterController != null) _dropMeterController.SetVisible(false);

        Debug.Log("[CompletionSequence] FadeOutUI done");
    }

    private IEnumerator FadeInOverlay()
    {
        if (_completionOverlay == null) yield break;

        SetOverlayAlpha(0f);
        _completionOverlay.enabled = true;
        _completionOverlay.gameObject.SetActive(true);
        _completionOverlay.transform.SetAsLastSibling();

        float elapsed = 0f;
        while (elapsed < _fadeInDuration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(Mathf.Clamp01(elapsed / _fadeInDuration));
            yield return null;
        }

        SetOverlayAlpha(1f);
        Debug.Log("[CompletionSequence] FadeInOverlay done");
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
        Debug.Log("[CompletionSequence] FadeOutOverlay done");
    }

    private void SetOverlayAlpha(float alpha)
    {
        Color c = _completionOverlay.color;
        _completionOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }

    private void PlayVoiceLine()
    {
        if (_voiceAudioSource != null && _riverSpiritClip != null)
            _voiceAudioSource.PlayOneShot(_riverSpiritClip);
        else
            Debug.LogWarning("[CompletionSequence] Voice audio source or clip not assigned.");
    }

    private void DestroyAllActiveDrops()
    {
        DropBehaviour[] activeDrops = FindObjectsByType<DropBehaviour>();
        Debug.Log("[CompletionSequence] Destroying " + activeDrops.Length + " active drops");
        foreach (DropBehaviour drop in activeDrops)
            Destroy(drop.gameObject);
    }

    private void NotifyPendantManager()
    {
        if (_pendantManager != null)
            _pendantManager.CollectBlueSpark();
        else
            Debug.LogWarning("[CompletionSequence] _pendantManager is NULL — CollectBlueSpark() not called.");
    }

    private void Cleanup()
    {
        if (_arCanvas == null) Debug.LogError("[CompletionSequence] _arCanvas is NULL");
        else _arCanvas.gameObject.SetActive(false);

        gameObject.SetActive(false);
    }
}
