using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the Page10Meter segment visibility.
/// </summary>
public class Page10MeterController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image   _parentImage;
    [SerializeField] private Image[] _segmentImages;

    [Header("Audio")]
    [SerializeField] private AudioSource _catchAudioSource;
    [SerializeField] private AudioClip   _catchSoundClip;

    [Header("Dependencies")]
    [SerializeField] private Page10CompletionSequence _completionSequence;

    private const int TotalRequired = 10;
    private int _catchCount;

    // Unlit tone: #3D362E (Dark grey-bronze)
    private static readonly Color s_unlit   = new Color(61f/255f,  54f/255f,  46f/255f);
    // Three tiers spaced across hue AND lightness so they read as distinct steps,
    // not shades of the same yellow (Page 10 = Gold Spark / Joy jar filling with light).
    // Lit low: #CC6600 (deep amber-orange, segments 0-3)
    private static readonly Color s_litLow  = new Color(204f/255f, 102f/255f,   0f/255f);
    // Lit mid: #FFB300 (saturated gold-orange, segments 4-6)
    private static readonly Color s_litMid  = new Color(255f/255f, 179f/255f,   0f/255f);
    // Lit high: #FFEB3B (bright sunny yellow, segments 7-9)
    private static readonly Color s_litHigh = new Color(255f/255f, 235f/255f,  59f/255f);

    private void Start()
    {
        _catchCount = 0;

        if (_segmentImages == null || _segmentImages.Length != TotalRequired)
        {
            Debug.LogError($"[Page10MeterController] _segmentImages must have exactly {TotalRequired} entries.", this);
            return;
        }

        foreach (Image segment in _segmentImages)
        {
            if (segment != null)
                segment.color = s_unlit;
        }
    }

    public void RegisterCatch()
    {
        if (_catchCount >= TotalRequired) return;

        LightSegment(_catchCount);
        _catchCount++;

        PlayCatchSound();

        if (_catchCount >= TotalRequired)
        {
            if (_completionSequence == null) Debug.LogError("[Page10MeterController] _completionSequence is NULL — completion not triggered");
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

    private static Color GetLitColour(int index)
    {
        if (index <= 3) return s_litLow;
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

    private void PlayCatchSound()
    {
        if (_catchAudioSource != null && _catchSoundClip != null)
            _catchAudioSource.PlayOneShot(_catchSoundClip);
    }
}
