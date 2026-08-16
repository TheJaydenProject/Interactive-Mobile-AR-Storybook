using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Handles Page 12 in the main scene ("Ar1"). Scanning the page12 marker spawns an island (mirrors
/// the other pages' island pattern), then runs an instructions → speaker → completion-overlay
/// sequence before revealing the "Try It On" button. Only pressing that button triggers the full
/// app process restart (via AppRestarter) into a dedicated scene ("Ar2") whose AR session starts
/// directly in front-facing/face-tracking mode. A plain scene load isn't enough here — ARCore's
/// native session layer carries state across a same-process scene reload, and that's what segfaults
/// the moment a *second* session in the same process reaches the front-facing/face-tracking config,
/// confirmed via device logcat (12/12 reproductions). Only a genuine process restart, landing Ar2 as
/// the first and only session in a fresh process, avoids it.
/// </summary>
public class Page12ARTracker : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;

    [Header("Island")]
    [SerializeField] private GameObject _islandPrefab;
    [SerializeField] private float _islandXOffset = 0.0f;
    [SerializeField] private float _islandYOffset = 0.1f;
    [SerializeField] private float _islandZOffset = 0.0f;
    [Tooltip("Wait after the image is first detected, before capturing its pose to spawn the island — lets ARFoundation's pose estimate settle past its initial (often noisy) read.")]
    [SerializeField] private float _islandSpawnStabilizationDelay = 0.65f;
    [SerializeField] private float _islandScale = 1.0f;
    [SerializeField] private Vector3 _islandRotation = Vector3.zero;

    [Header("Instructions")]
    [Tooltip("Wait after the island spawns before the instruction prompt (with its speaker button) appears.")]
    [SerializeField] private float _instructionDelay = 2.0f;
    [Tooltip("Contains its own speaker button wired to OnSpeakerButtonPressed(). Auto-dismissed (no tap needed) the instant the completion overlay starts fading in.")]
    [SerializeField] private GameObject _instructionPrompt;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _voiceClip;
    [Tooltip("Wait after the voice line finishes playing, before the completion overlay starts fading in.")]
    [SerializeField] private float _audioToOverlayDelay = 0.4f;
    [Tooltip("Fallback wait used only if the voice clip isn't assigned.")]
    [SerializeField] private float _fallbackAudioDuration = 1f;

    [Header("Completion Overlay")]
    [SerializeField] private Image _completionOverlay;
    [SerializeField] private float _fadeInDuration = 0.8f;
    [SerializeField] private float _holdDuration = 0.5f;
    [SerializeField] private float _fadeOutDuration = 0.8f;
    [Tooltip("Wait after the completion overlay finishes fading out, before the Try It On button appears.")]
    [SerializeField] private float _postOverlayDelay = 0.5f;

    [Header("Try It On")]
    [Tooltip("Shown only once the instructions → speaker → completion overlay sequence finishes. Needs OnClick() wired to OnTryItOnPressed().")]
    [SerializeField] private GameObject _tryItOnButton;

    [Header("Phoenix")]
    [Tooltip("Wait after the island spawns before the Phoenix's VFX starts and it rises. Same rainbow rise as Page 11's Phoenix, just without the spark-collection gate — it plays as soon as the island is up, in parallel with the instructions/overlay flow above.")]
    [SerializeField] private float _phoenixRiseDelay = 1.0f;
    [Tooltip("How far above its authored (initial) position the Phoenix rises.")]
    [SerializeField] private float _phoenixRiseHeight = 0.5f;
    [SerializeField] private float _phoenixRiseDuration = 2.0f;
    [Tooltip("How many degrees the Phoenix turns around its Y axis while it rises. Set to 0 for no turn.")]
    [SerializeField] private float _phoenixRiseYRotation = 360f;
    [Tooltip("Continuously cycles the Phoenix VFX's \"Glow\" child through hues, starting the instant it begins playing.")]
    [SerializeField] private float _glowRainbowSpeed = 0.4f;
    [Tooltip("Continuously cycles the Phoenix's own body color through hues, starting once it finishes rising.")]
    [SerializeField] private float _phoenixBodyRainbowSpeed = 0.4f;

    [Header("Pendant")]
    [Tooltip("Set the moment page12 is scanned — Eira's gem previewing the crown feature.")]
    [SerializeField] private Material _pendantMaterial;
    [SerializeField] private Color _pinkGemColor = new Color(1f, 0.349f, 0.4902f); // #FF597D

    [Header("Scene")]
    [Tooltip("Name of the scene to restart into once Try It On is pressed. Must be added to File > Build Settings > Scenes In Build.")]
    [SerializeField] private string _faceFilterSceneName = "Ar2";

    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    private GameObject _islandInstance;
    private Page12IslandReference _islandReference;

    // Covers island spawn through the instruction prompt appearing. Ends there — the rest of the
    // flow (audio → overlay → Try It On) is driven by OnSpeakerButtonPressed() instead, since it
    // has to wait on a real user tap rather than a fixed timer.
    private Coroutine _spawnCoroutine;
    private Coroutine _postAudioCoroutine;

    private Coroutine _phoenixRiseDelayCoroutine;
    private Coroutine _phoenixRiseCoroutine;

    // The Phoenix VFX is actually several sibling particle systems (Crystal/Glow/Sparks/Flares).
    // Only the "Glow" child's Start Color rainbow-cycles, same as Page 11. Cached once (not
    // searched every frame) when the VFX starts playing.
    private ParticleSystem _phoenixGlowVfx;

    // Hue (~240°) the Glow rainbow starts on so it always opens on blue instead of whatever
    // colour Time.time happens to land on. Cycling is measured from _glowRainbowStartTime so the
    // first frame is exactly this hue, not an arbitrary point partway through the cycle.
    private const float GlowRainbowStartHue = 0.6667f;
    private float _glowRainbowStartTime;

    // Page 11 only rainbow-cycles the Phoenix's own body once all 4 sparks are collected; Page 12
    // has no such gate, so this just flips on once the rise animation finishes.
    private bool _phoenixRisen;
    private float _phoenixBodyRainbowStartTime;
    private MaterialPropertyBlock _mpb;

    // True once the speaker button has kicked off the audio → overlay → button progression, so
    // replaying the voice line (allowed, same as SpeakerButton.cs) doesn't also restart that
    // progression a second time.
    private bool _voiceLineTriggered;

    // Guards against the restart firing twice (e.g. a double-tap on Try It On) while the process
    // restart is in flight.
    private bool _triggered;

    // Mirrors "do I currently hold the shared AppStateManager lock" for the cancel guard.
    // Self-heals via HandleFeatureActiveChanged so a stale true can never linger past this
    // page's own cancellation.
    private bool _isActive;

    // Set when the child backs out via Back; blocks this page re-triggering while its image
    // stays in view, cleared once the image leaves tracking so looking back re-arms it.
    private bool _suppressedWhileTracked;

    private static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        if (_trackedImageManager == null)
            _trackedImageManager = GetComponent<ARTrackedImageManager>();

        if (_instructionPrompt != null)
        {
            _instructionPrompt.SetActive(false);
            PromptTapHandler tapHandler = _instructionPrompt.AddComponent<PromptTapHandler>();
            tapHandler.OnTap = OnInstructionPromptTapped;
        }
        else
        {
            Debug.LogError("[Page12ARTracker] _instructionPrompt not assigned.");
        }

        if (_completionOverlay != null)
        {
            SetOverlayAlpha(0f);
            _completionOverlay.enabled = false;
        }

        if (_tryItOnButton != null) _tryItOnButton.SetActive(false);
        else Debug.LogError("[Page12ARTracker] _tryItOnButton not assigned.");
    }

    private void OnEnable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);

        if (_appStateManager != null)
        {
            _appStateManager.OnFeatureCancelled += HandleFeatureCancelled;
            _appStateManager.OnFeatureActiveChanged += HandleFeatureActiveChanged;
        }
    }

    private void OnDisable()
    {
        if (_trackedImageManager != null)
            _trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);

        if (_appStateManager != null)
        {
            _appStateManager.OnFeatureCancelled -= HandleFeatureCancelled;
            _appStateManager.OnFeatureActiveChanged -= HandleFeatureActiveChanged;
        }
    }

    private void Update()
    {
        if (_phoenixGlowVfx != null)
        {
            float hue = Mathf.Repeat(GlowRainbowStartHue + (Time.time - _glowRainbowStartTime) * _glowRainbowSpeed, 1f);
            ParticleSystem.MainModule main = _phoenixGlowVfx.main;
            main.startColor = Color.HSVToRGB(hue, 0.8f, 1f);
        }

        if (_phoenixRisen && _islandReference != null && _islandReference.PhoenixRenderer != null)
        {
            float hue = Mathf.Repeat((Time.time - _phoenixBodyRainbowStartTime) * _phoenixBodyRainbowSpeed, 1f);
            SetPhoenixColor(Color.HSVToRGB(hue, 0.8f, 1f));
        }
    }

    private void SetPhoenixColor(Color color)
    {
        if (_islandReference == null || _islandReference.PhoenixRenderer == null) return;
        _mpb ??= new MaterialPropertyBlock();
        _islandReference.PhoenixRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(s_baseColorId, color);
        _islandReference.PhoenixRenderer.SetPropertyBlock(_mpb);
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        if (_triggered) return; // restart already in flight; ignore further tracking updates

        foreach (ARTrackedImage image in args.added)
        {
            if (image.referenceImage.name != "page12") continue;
            if (_suppressedWhileTracked) continue; // backed out; wait for a fresh acquisition
            if (!TryBeginFeature()) continue;

            BeginPreGameSequence(image.transform);
        }

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.referenceImage.name != "page12") continue;

            // Re-arm as soon as the image stops being solidly tracked. XR Simulation / ARCore
            // usually report Limited (not None) on look-away, so keying only off None left the
            // suppression stuck and the feature never reappeared. ponytail: clears on the first
            // non-Tracking frame — heavy tracking flicker could re-arm early; add a short debounce
            // if that shows up on-device.
            if (image.trackingState != TrackingState.Tracking)
                _suppressedWhileTracked = false;

            if (image.trackingState == TrackingState.Tracking)
            {
                if (_suppressedWhileTracked) continue; // backed out; wait for a fresh acquisition
                if (!TryBeginFeature()) continue;

                BeginPreGameSequence(image.transform);
            }
            else if (image.trackingState == TrackingState.None)
            {
                CancelSequence();
                ReleaseLockIfHeld();
            }
        }

        foreach (var removed in args.removed)
        {
            if (removed.Value.referenceImage.name != "page12") continue;

            _suppressedWhileTracked = false; // image left view → re-arm so looking back replays
            CancelSequence();
            ReleaseLockIfHeld();
        }
    }

    // Entry point once this page claims the shared lock: spawn the island, wait, then show the
    // instruction prompt. From there the speaker button (OnSpeakerButtonPressed) drives the rest.
    private void BeginPreGameSequence(Transform anchor)
    {
        SetPendantColor(_pinkGemColor);
        _spawnCoroutine = StartCoroutine(SpawnIslandThenShowInstructions(anchor));
    }

    private void SetPendantColor(Color color)
    {
        if (_pendantMaterial == null)
        {
            Debug.LogWarning("[Page12ARTracker] _pendantMaterial not assigned — pendant color not set.");
            return;
        }

        _pendantMaterial.SetColor(s_baseColorId, color);
    }

    private IEnumerator SpawnIslandThenShowInstructions(Transform anchor)
    {
        // Wait before spawning so ARFoundation's pose estimate for the image has a moment to
        // settle past its initial (often noisy) read — the island is unparented and locks in
        // whatever pose it sees at spawn time, so a bad first read would otherwise be permanent.
        yield return new WaitForSeconds(_islandSpawnStabilizationDelay);
        if (anchor == null)
        {
            _spawnCoroutine = null; // tracked image removed while stabilizing
            yield break;
        }

        SpawnIsland(anchor);

        // Phoenix rise/VFX runs on its own timer in parallel — independent of the
        // instructions/audio/overlay flow below.
        _phoenixRiseDelayCoroutine = StartCoroutine(WaitThenStartPhoenixRise());

        yield return new WaitForSeconds(_instructionDelay);

        if (_instructionPrompt == null)
        {
            Debug.LogError("[Page12ARTracker] _instructionPrompt is NULL — skipping straight to Try It On.");
            if (_tryItOnButton != null) _tryItOnButton.SetActive(true);
            _spawnCoroutine = null;
            yield break;
        }

        _instructionPrompt.SetActive(true);
        // Only cleared once the whole spawn → instructions flow has actually finished — clearing
        // it earlier (e.g. right after SpawnIsland) left CancelSequence() with nothing to stop
        // during the _instructionDelay wait, so Back To Menu couldn't cancel it and the prompt
        // could still pop up on top of the menu after backing out.
        _spawnCoroutine = null;
    }

    // Wired to the instruction prompt's speaker button OnClick(). Replayable like any other
    // SpeakerButton (ignored while already playing), but only the first successful play kicks off
    // the audio → overlay → Try It On progression.
    public void OnSpeakerButtonPressed()
    {
        if (_audioSource == null || _voiceClip == null)
        {
            Debug.LogWarning("[Page12ARTracker] _audioSource or _voiceClip not assigned.");
            return;
        }

        if (_audioSource.isPlaying) return;

        _audioSource.clip = _voiceClip;
        _audioSource.Play();

        if (_voiceLineTriggered) return;
        _voiceLineTriggered = true;

        _postAudioCoroutine = StartCoroutine(WaitForAudioThenShowOverlay());
    }

    // Runtime tap-anywhere-to-dismiss, same trick every other page's instruction prompt uses — a
    // tap on the speaker button itself is consumed by that Button first and never reaches this.
    // Manual skip path on top of the normal audio → overlay → button progression: jumps straight
    // to the Try It On button instead of waiting through the voice line and overlay.
    private void OnInstructionPromptTapped()
    {
        if (_instructionPrompt == null || !_instructionPrompt.activeSelf) return;

        if (_postAudioCoroutine != null)
        {
            StopCoroutine(_postAudioCoroutine);
            _postAudioCoroutine = null;
        }

        if (_audioSource != null) _audioSource.Stop();

        _instructionPrompt.SetActive(false);
        if (_tryItOnButton != null) _tryItOnButton.SetActive(true);
    }

    private IEnumerator WaitForAudioThenShowOverlay()
    {
        float audioLength = _voiceClip != null ? _voiceClip.length : _fallbackAudioDuration;
        yield return new WaitForSeconds(audioLength);

        yield return new WaitForSeconds(_audioToOverlayDelay);

        // yield return StartCoroutine(...) chains this coroutine to CompletionOverlaySequence(),
        // so keeping _postAudioCoroutine's reference alive through the whole chain (only clearing
        // it once everything is truly done) means CancelSequence()'s StopCoroutine() call can
        // still cancel the overlay/button-reveal even if Back To Menu is pressed mid-fade.
        yield return StartCoroutine(CompletionOverlaySequence());
        _postAudioCoroutine = null;
    }

    private IEnumerator CompletionOverlaySequence()
    {
        // Auto-dismissed the instant the overlay starts fading in — no tap needed, unlike every
        // other page's instruction prompt.
        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);

        yield return StartCoroutine(FadeInOverlay());
        yield return new WaitForSeconds(_holdDuration);
        yield return StartCoroutine(FadeOutOverlay());

        yield return new WaitForSeconds(_postOverlayDelay);

        if (_tryItOnButton != null) _tryItOnButton.SetActive(true);
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

    private IEnumerator WaitThenStartPhoenixRise()
    {
        yield return new WaitForSeconds(_phoenixRiseDelay);
        _phoenixRiseDelayCoroutine = null;
        StartPhoenixRise();
    }

    // Starts the Phoenix's VFX looping and animates it up from below into its authored resting
    // position — same rise as Page 11's, just triggered right after spawn instead of waiting on
    // sparks. The VFX keeps playing (and both rainbow cycles keep running) for as long as the
    // island exists.
    private void StartPhoenixRise()
    {
        if (_islandReference == null)
        {
            Debug.LogWarning("[Page12ARTracker] No island reference — Phoenix rise/VFX skipped.");
            return;
        }

        if (_islandReference.PhoenixVfx != null)
        {
            _islandReference.PhoenixVfx.gameObject.SetActive(true);
            _islandReference.PhoenixVfx.Play(true); // withChildren; loops until the island despawns

            _phoenixGlowVfx = null;
            foreach (ParticleSystem ps in _islandReference.PhoenixVfx.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.gameObject.name == "Glow")
                {
                    _phoenixGlowVfx = ps;
                    break;
                }
            }

            if (_phoenixGlowVfx == null)
                Debug.LogWarning("[Page12ARTracker] No child named 'Glow' found under the Phoenix VFX — its Start Color won't rainbow-cycle.");
            else
                _glowRainbowStartTime = Time.time;
        }
        else
        {
            Debug.LogWarning("[Page12ARTracker] Island's Phoenix VFX not assigned on Page12IslandReference.");
        }

        if (_islandReference.PhoenixTransform != null)
            _phoenixRiseCoroutine = StartCoroutine(RisePhoenix(_islandReference.PhoenixTransform));
        else
            Debug.LogWarning("[Page12ARTracker] Island's Phoenix transform not assigned on Page12IslandReference — rise animation skipped.");
    }

    private IEnumerator RisePhoenix(Transform phoenix)
    {
        Vector3 startLocalPos = phoenix.localPosition; // authored (initial) position from the prefab
        Vector3 targetLocalPos = startLocalPos + Vector3.up * _phoenixRiseHeight;

        // Interpolate the Y degree offset directly rather than Slerp-ing between quaternions —
        // Slerp always takes the shortest path, so a 360° turn (or any exact multiple of it)
        // would be indistinguishable from no turn at all and silently do nothing.
        Quaternion startRotation = phoenix.localRotation;

        float elapsed = 0f;
        while (elapsed < _phoenixRiseDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _phoenixRiseDuration);
            phoenix.localPosition = Vector3.Lerp(startLocalPos, targetLocalPos, t);

            float yOffset = Mathf.Lerp(0f, _phoenixRiseYRotation, t);
            phoenix.localRotation = startRotation * Quaternion.Euler(0f, yOffset, 0f);
            yield return null;
        }

        phoenix.localPosition = targetLocalPos;
        phoenix.localRotation = startRotation * Quaternion.Euler(0f, _phoenixRiseYRotation, 0f);
        _phoenixRiseCoroutine = null;

        // Once fully risen, start the Phoenix's own body rainbow-cycling too — Page 11 gates this
        // behind spark collection; Page 12 has no such gate, so it kicks in right as the rise ends.
        _phoenixRisen = true;
        _phoenixBodyRainbowStartTime = Time.time;
    }

    // Wired to the Try It On button's OnClick().
    public void OnTryItOnPressed()
    {
        if (_triggered) return;
        _triggered = true;

        Debug.Log($"[Page12ARTracker] Try It On pressed — restarting into '{_faceFilterSceneName}'.");
        AppRestarter.RestartIntoScene(_faceFilterSceneName);
    }

    // --- Island ---

    private void SpawnIsland(Transform anchor)
    {
        DespawnIsland();

        if (_islandPrefab == null)
        {
            Debug.LogError("[Page12ARTracker] _islandPrefab not assigned.");
            return;
        }

        Vector3 position = anchor.position + new Vector3(_islandXOffset, _islandYOffset, _islandZOffset);
        // Spawned unparented, not attached to the tracked image's own transform — a live-tracked
        // ARTrackedImage keeps refining its pose every frame, which used to make the island
        // tilt/wobble along with the physical page instead of staying put once placed. Rotation
        // is still composed with anchor.rotation (not a bare absolute value) so it matches however
        // the physical page is actually oriented — a pure Quaternion.Euler(_islandRotation) looked
        // right in Editor/XR Simulation (whose marker sits at one fixed, canonical orientation) but
        // pointed the wrong way on-device, since the AR session's world axes are arbitrary, set by
        // wherever the phone happened to be facing when that session started, not by the page.
        _islandInstance = Instantiate(_islandPrefab, position, anchor.rotation * Quaternion.Euler(_islandRotation));
        _islandInstance.transform.localScale *= _islandScale;

        _islandReference = _islandInstance.GetComponent<Page12IslandReference>();
        if (_islandReference == null)
        {
            Debug.LogWarning("[Page12ARTracker] _islandPrefab has no Page12IslandReference — Phoenix rise/VFX won't work.");
        }
        else if (_islandReference.PhoenixVfx != null)
        {
            // ParticleSystems default to "Play On Awake", so left alone this fires the instant
            // the island is instantiated — stop it immediately so it stays silent until
            // StartPhoenixRise() explicitly plays it later.
            _islandReference.PhoenixVfx.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _islandReference.PhoenixVfx.gameObject.SetActive(false);
        }
    }

    private void DespawnIsland()
    {
        if (_islandInstance != null)
            Destroy(_islandInstance);
        _islandInstance = null;
        _islandReference = null;
        _phoenixGlowVfx = null;
        _phoenixRisen = false;
    }

    // --- Scan lock ---

    // Ignore the scan outright if another page's feature is already active; only Back
    // cancellation or tracking loss releases the lock (no completion sequence on this page).
    private bool TryBeginFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[Page12ARTracker] _appStateManager not assigned; ignoring scan.");
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
    // the shared cancel event reaches every page script.
    private void HandleFeatureCancelled()
    {
        if (!_isActive) return;

        CancelSequence();
        _suppressedWhileTracked = true;
        _isActive = false;
        EndFeature();
    }

    // Releases the shared lock on genuine tracking loss (not the Back button — HandleFeatureCancelled
    // already covers that), guarded on _isActive so this page can never release a lock another page
    // holds. Without this, TryBeginFeature() would keep failing forever after a single look-away,
    // even though _suppressedWhileTracked's own comment promises "looking back replays."
    private void ReleaseLockIfHeld()
    {
        if (!_isActive) return;
        _isActive = false;
        EndFeature();
    }

    // Stops whichever stage (spawn wait / instructions / audio wait / overlay / button shown) is
    // in progress and removes the island immediately. Called on Back cancellation and on tracking
    // loss so nothing is left stuck.
    private void CancelSequence()
    {
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }
        if (_postAudioCoroutine != null)
        {
            StopCoroutine(_postAudioCoroutine);
            _postAudioCoroutine = null;
        }
        if (_phoenixRiseDelayCoroutine != null)
        {
            StopCoroutine(_phoenixRiseDelayCoroutine);
            _phoenixRiseDelayCoroutine = null;
        }
        if (_phoenixRiseCoroutine != null)
        {
            // Without this, the rise kept animating a Transform that DespawnIsland() was about
            // to destroy, throwing a MissingReferenceException on the next frame.
            StopCoroutine(_phoenixRiseCoroutine);
            _phoenixRiseCoroutine = null;
        }

        if (_audioSource != null) _audioSource.Stop();
        _voiceLineTriggered = false;

        if (_instructionPrompt != null) _instructionPrompt.SetActive(false);
        if (_completionOverlay != null)
        {
            SetOverlayAlpha(0f);
            _completionOverlay.enabled = false;
        }
        if (_tryItOnButton != null) _tryItOnButton.SetActive(false);

        DespawnIsland();
    }

    private void EndFeature()
    {
        if (_appStateManager == null)
        {
            Debug.LogWarning("[Page12ARTracker] _appStateManager not assigned; scanner lock not released.");
            return;
        }
        _appStateManager.EndFeature();
    }

    // Runtime-attached to _instructionPrompt so any tap on it dismisses it, without requiring
    // a Button to be set up in the Inspector (same trick Page4ARTracker/Page6SpeechController use).
    private class PromptTapHandler : MonoBehaviour, IPointerClickHandler
    {
        public System.Action OnTap;
        public void OnPointerClick(PointerEventData eventData) => OnTap?.Invoke();
    }
}
