using System;
using UnityEngine;

/// <summary>
/// Owns the single shared "is a page's AR feature currently active" lock. Tracking lives
/// separately in each page's own ARTrackedImageManager listener, so there's no existing script
/// to hang this flag on — every page-side script instead holds a reference to one shared
/// AppStateManager (assigned in the Inspector, same pattern as PendantManager) so the flag is
/// never duplicated per page.
/// </summary>
public class AppStateManager : MonoBehaviour
{
    /// <summary>
    /// Fires whenever the lock changes state, so UI (the Back button) can react without
    /// polling every frame.
    /// </summary>
    public event Action<bool> OnFeatureActiveChanged;

    /// <summary>
    /// Fires when the Back button cancels whichever feature is currently active. This is a single
    /// shared, parameter-less event rather than routing through per-page references — every page
    /// script already holds an AppStateManager reference, and each one checks its own "am I the
    /// page that's actually running" state before reacting, so only the active page does anything.
    /// </summary>
    public event Action OnFeatureCancelled;

    [Header("Audio")]
    [Tooltip("Skipped when Cancel stops all currently-playing audio — e.g. the background music source and the button-click SFX source, neither of which belong to whichever page feature just got cancelled.")]
    [SerializeField] private AudioSource[] _audioSourcesExcludedFromCancelStop;

    private bool _isFeatureActive;

    public bool IsFeatureActive => _isFeatureActive;

    /// <summary>
    /// Called by a page's AR tracker when its target image is recognized. Returns false (and
    /// claims nothing) if another page's feature is already active, so the caller should ignore
    /// the scan outright rather than queue or retry it.
    /// </summary>
    public bool TryBeginFeature()
    {
        if (_isFeatureActive) return false;
        _isFeatureActive = true;
        OnFeatureActiveChanged?.Invoke(true);
        return true;
    }

    /// <summary>
    /// Called once a page's completion transition has finished, so scanning can resume.
    /// </summary>
    public void EndFeature()
    {
        _isFeatureActive = false;
        OnFeatureActiveChanged?.Invoke(false);
    }

    /// <summary>
    /// Called by the Back button. Doesn't release the lock itself — the active page's own
    /// cancellation handler stops its coroutines/spawners/audio first, then calls EndFeature(),
    /// so the lock is never released while that page's game objects are still mid-flight.
    /// </summary>
    public void CancelFeature()
    {
        if (!_isFeatureActive) return;
        StopAllFeatureAudio();
        OnFeatureCancelled?.Invoke();
    }

    // Cuts off whatever's currently playing (voice lines, SFX) the instant Back/Back To Menu is
    // pressed, so nothing keeps talking/playing over the menu or the next page — instead of
    // relying on every single page script to remember to stop its own AudioSources on cancel.
    // AudioSource.Stop() only halts playback; the clip and every other setting are untouched, so
    // the exact same sound plays completely normally the next time this (or any) page triggers it.
    private void StopAllFeatureAudio()
    {
        AudioSource[] allSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include);
        foreach (AudioSource source in allSources)
        {
            if (IsExcludedFromCancelStop(source)) continue;
            source.Stop();
        }
    }

    private bool IsExcludedFromCancelStop(AudioSource source)
    {
        if (_audioSourcesExcludedFromCancelStop == null) return false;

        foreach (AudioSource excluded in _audioSourcesExcludedFromCancelStop)
            if (excluded == source) return true;

        return false;
    }
}
