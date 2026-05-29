using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracks how many drops have been caught and lights up 10 segment Images in order.
/// Triggers CompletionSequence when all 10 are lit.
/// Attach to the DropMeter GameObject inside the Canvas.
/// </summary>
public class DropMeterController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image   _parentImage;
    [SerializeField] private Image[] _segmentImages;

    [Header("Audio")]
    [SerializeField] private AudioSource _catchAudioSource;
    [SerializeField] private AudioClip   _catchSoundClip;

    [Header("Dependencies")]
    [SerializeField] private CompletionSequence _completionSequence;

    private const int TotalRequired = 10;

    // Unlit and three lit tiers, cached to avoid per-frame allocation
    private static readonly Color s_unlit   = new Color( 10f/255f,  26f/255f,  58f/255f); // #0A1A3A
    private static readonly Color s_litLow  = new Color(  0f/255f,  85f/255f, 255f/255f); // #0055FF  segments 1–3
    private static readonly Color s_litMid  = new Color(  0f/255f, 170f/255f, 255f/255f); // #00AAFF  segments 4–7
    private static readonly Color s_litHigh = new Color(  0f/255f, 238f/255f, 255f/255f); // #00EEFF  segments 8–10

    private int _catchCount;

    private void Start()
    {
        _catchCount = 0;

        if (_segmentImages == null || _segmentImages.Length != TotalRequired)
        {
            Debug.LogError($"[DropMeterController] _segmentImages must have exactly {TotalRequired} entries.", this);
            return;
        }

        foreach (Image segment in _segmentImages)
        {
            if (segment != null)
                segment.color = s_unlit;
        }
    }

    /// <summary>
    /// Called by DropBehaviour on each successful catch.
    /// </summary>
    public void RegisterCatch()
    {
        // Guard: all segments already lit, ignore stale calls
        if (_catchCount >= TotalRequired) return;

        LightSegment(_catchCount);
        _catchCount++;

        PlayCatchSound();

        if (_catchCount >= TotalRequired)
        {
            if (_completionSequence == null) Debug.LogError("[DropMeterController] _completionSequence is NULL — completion not triggered");
            else _completionSequence.TriggerCompletion();
        }
    }

    private void LightSegment(int index)
    {
        if (_segmentImages == null || index >= _segmentImages.Length) return;

        Image segment = _segmentImages[index];
        if (segment != null)
            segment.color = GetLitColour(index);
    }

    // Segments 0–2 → tier 1, 3–6 → tier 2, 7–9 → tier 3
    private static Color GetLitColour(int index)
    {
        if (index <= 2) return s_litLow;
        if (index <= 6) return s_litMid;
        return s_litHigh;
    }

    public void SetVisible(bool visible)
    {
        if (_parentImage != null) _parentImage.enabled = visible;

        if (_segmentImages == null) return;
        foreach (Image segment in _segmentImages)
        {
            if (segment != null)
                segment.enabled = visible;
        }
    }

    public void SetAlpha(float alpha)
    {
        if (_parentImage != null)
        {
            Color c = _parentImage.color;
            _parentImage.color = new Color(c.r, c.g, c.b, alpha);
        }

        if (_segmentImages == null) return;
        foreach (Image segment in _segmentImages)
        {
            if (segment == null) continue;
            Color c = segment.color;
            segment.color = new Color(c.r, c.g, c.b, alpha);
        }
    }

    private void PlayCatchSound()
    {
        if (_catchAudioSource != null && _catchSoundClip != null)
            _catchAudioSource.PlayOneShot(_catchSoundClip);
    }
}
