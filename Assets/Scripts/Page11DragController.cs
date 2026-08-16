using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives all four draggable sparks on Page 11 from a single controller (mirrors the
/// one-controller-plus-array pattern of Page10OrbController). The player drags each earned
/// spark onto the Phoenix; once all four are applied the completion sequence transforms it.
///
/// Gating (Option A): a spark's collected state is checked live at pointer-down against
/// PendantManager.GetCollectedSparks(). A spark whose colour was not earned on pages 4/6/8/10
/// stays visible but is not draggable.
/// </summary>
public class Page11DragController : MonoBehaviour
{
    // The four draggable spark orbs and the Phoenix drop-target collider live inside the spawned
    // island prefab (via Page11IslandReference), so they arrive at runtime through Initialize()
    // rather than being fixed scene references.
    private Transform[] _sparks;
    private Collider    _phoenixCollider;

    [Header("Sparks")]
    // Parallel to _sparks. Names must match PendantManager's spark strings ("Blue"/"Red"/"Yellow"/"Gold").
    [SerializeField] private string[] _sparkNames = { "Blue", "Red", "Yellow", "Gold" };

    [Header("Drop Target")]
    [Tooltip("Sparks dock at the Phoenix position plus a small ring offset of this radius.")]
    [SerializeField] private float _dockRadius = 0.18f;
    [Tooltip("Scales the Phoenix up slightly relative to the sparks around it. The island's overall size is controlled separately by Page11ARTracker's Island Scale.")]
    [SerializeField] private float _phoenixScaleMultiplier = 1.15f;

    [Header("Scatter Settings")]
    [Tooltip("How much to randomly scatter the initial positions of the sparks.")]
    [SerializeField] private float _scatterRadius = 0.05f;

    [Header("Floating Motion")]
    [SerializeField] private float _floatAmplitude = 0.01f;
    [SerializeField] private float _floatFrequency = 1.5f;
    [SerializeField] private float _driftAmplitude = 0.005f;

    [Header("Celebration")]
    [Tooltip("Max degrees per second the sparks orbit around the Phoenix during the celebration, reached after ramping up. Runs until StopOrbitCelebration() is called (Page11CompletionSequence calls it the moment the Phoenix stops being visible), not a fixed duration, so it stays in sync with the rainbow colour cycle.")]
    [SerializeField] private float _celebrationOrbitSpeed = 120f;
    [Tooltip("How long it takes the orbit to ease up from a standstill to full speed.")]
    [SerializeField] private float _celebrationRampUpDuration = 1.5f;

    private Coroutine _orbitCoroutine;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Plays the instant the Blue spark is dropped on the Phoenix.")]
    [SerializeField] private AudioClip _blueSparkVoiceClip;
    [Tooltip("Plays the instant the Red spark is dropped on the Phoenix.")]
    [SerializeField] private AudioClip _redSparkVoiceClip;
    [Tooltip("Plays the instant the Yellow spark is dropped on the Phoenix.")]
    [SerializeField] private AudioClip _yellowSparkVoiceClip;
    [Tooltip("Plays the instant the Gold spark is dropped on the Phoenix.")]
    [SerializeField] private AudioClip _goldSparkVoiceClip;

    [Header("Dependencies")]
    [SerializeField] private PendantManager           _pendantManager;
    [SerializeField] private Page11CompletionSequence _completionSequence;

    [Header("Debug")]
    [Tooltip("When true, skips the collected-spark check so all four can be dragged even if not earned yet. For isolated Page 11 testing.")]
    [SerializeField] private bool _debugUnlockAllSparks;

    private Vector3[] _initialLocalPositions;
    private float[]   _phaseOffsets;
    private bool[]    _applied;
    private bool[]    _slotOccupied;
    private int[]     _sparkSlotIndex;
    private int       _appliedCount;

    private int      _heldIndex = -1;
    private Collider _heldCollider;
    private float    _heldCameraDistance;
    private Vector2  _lastPointerPos;

    // Blocks picking up a new spark for the duration of another spark's voice line, set/cleared
    // by PlaySparkVoiceLine's coroutine.
    private bool _sparkVoiceLineBusy;
    private Coroutine _voiceLineBusyCoroutine;

    // Lives on the always-active tracker GameObject (same as Page10OrbController before its
    // perf fix), not under the page's 3D content, so Update() must be told explicitly when
    // Page 11 is actually being shown — otherwise dragging/tapping sparks works before the
    // page is ever scanned.
    private bool _isActive;

    public void SetActive(bool active)
    {
        _isActive = active;
    }

    // Called by Page11ARTracker right after it spawns (or despawns, passing nulls) the island —
    // the sparks and Phoenix collider live inside the island prefab itself (via
    // Page11IslandReference), so this controller can't hold fixed references to them; it has to
    // be told which instance is actually live. Resets all per-spark state for the new set.
    public void Initialize(Transform[] sparks, Collider phoenixCollider)
    {
        _sparks = sparks;
        _phoenixCollider = phoenixCollider;

        _heldIndex = -1;
        _heldCollider = null;
        _sparkVoiceLineBusy = false;
        _appliedCount = 0;

        // Scales the Phoenix up slightly relative to the sparks around it — the whole island
        // (Phoenix included) is already sized correctly via Page11ARTracker's Island Scale, so
        // this is purely a relative Phoenix-vs-sparks size ratio, not a global shrink.
        if (_phoenixCollider != null)
            _phoenixCollider.transform.localScale *= _phoenixScaleMultiplier;

        if (_sparks == null)
        {
            _initialLocalPositions = null;
            _phaseOffsets = null;
            _applied = null;
            _slotOccupied = null;
            _sparkSlotIndex = null;
            return;
        }

        _initialLocalPositions = new Vector3[_sparks.Length];
        _phaseOffsets = new float[_sparks.Length];
        _applied = new bool[_sparks.Length];
        _slotOccupied = new bool[_sparks.Length];
        _sparkSlotIndex = new int[_sparks.Length];

        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparks[i] != null)
            {
                // Apply a small random scatter so they don't sit in a perfect ring
                Vector3 rand = Random.insideUnitSphere * _scatterRadius;
                _sparks[i].localPosition += rand;

                // Lift each spark to the Phoenix's height so they float around the cube instead
                // of collapsing toward the parent origin below it (the localPosition *= scale
                // above pulls everything down). World-space match, so it holds even if the
                // Phoenix and sparks are re-parented differently later.
                if (_phoenixCollider != null)
                {
                    Vector3 world = _sparks[i].position;
                    world.y = _phoenixCollider.transform.position.y;
                    _sparks[i].position = world;
                }

                _initialLocalPositions[i] = _sparks[i].localPosition;
                _phaseOffsets[i] = Random.Range(0f, Mathf.PI * 2f);
            }
        }

        if (_sparkNames == null || _sparkNames.Length != _sparks.Length)
            Debug.LogWarning("[Page11DragController] _sparkNames length does not match _sparks length; gating may misbehave.", this);
    }

    private void Update()
    {
        // Floats the sparks the instant the island spawns (Initialize() populates _sparks),
        // independent of _isActive — dragging stays gated behind instruction dismissal, but the
        // idle bob shouldn't wait for that.
        UpdateFloatingMotion();

        if (!_isActive) return;
        if (Camera.main == null) return;

        if (_heldIndex == -1) HandlePickup();
        else                  HandleDragAndRelease();
    }

    // Called by Page11ARTracker (relaying from Page11CompletionSequence) once the screen is
    // fully covered by the completion overlay, so the sparks disappear alongside the Phoenix
    // instead of staying visible after the reveal.
    public void HideSparks()
    {
        if (_sparks == null) return;
        foreach (Transform spark in _sparks)
            if (spark != null) spark.gameObject.SetActive(false);
    }

    /// <summary>
    /// Called by Page11ARTracker when the Back button cancels Page 11 mid-drag. Releases
    /// whatever spark is currently held back to its resting spot (mirrors a failed drop) so
    /// nothing is left stuck mid-air or with its collider disabled for the next attempt.
    /// </summary>
    public void CancelDrag()
    {
        // Runs unconditionally, not just when a spark is currently held — a voice line can still
        // be playing (and blocking pickup) even after the held spark that triggered it was
        // already released, since _heldIndex resets right after ApplySpark() starts this timer.
        if (_voiceLineBusyCoroutine != null)
        {
            StopCoroutine(_voiceLineBusyCoroutine);
            _voiceLineBusyCoroutine = null;
        }
        _sparkVoiceLineBusy = false;

        if (_heldIndex == -1) return;

        ReturnSpark(_heldIndex);
        _heldCollider = null;
        _heldIndex = -1;
    }

    private void UpdateFloatingMotion()
    {
        if (_sparks == null) return;

        float time = Time.time;

        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparks[i] == null) continue;
            
            // Don't float the spark we are currently dragging
            if (i == _heldIndex) continue;
            
            // Keep them still once they are snapped to the Phoenix
            if (_applied[i]) continue;

            float phase = _phaseOffsets[i];
            Vector3 basePos = _initialLocalPositions[i];

            float yOffset = Mathf.Sin(time * _floatFrequency + phase) * _floatAmplitude;
            float xOffset = Mathf.Cos(time * (_floatFrequency * 0.7f) + phase) * _driftAmplitude;
            float zOffset = Mathf.Sin(time * (_floatFrequency * 0.8f) + phase) * _driftAmplitude;

            _sparks[i].localPosition = basePos + new Vector3(xOffset, yOffset, zOffset);
        }
    }

    private void HandlePickup()
    {
        if (_sparkVoiceLineBusy) return;
        if (!TryGetPointerDown(out Vector2 screenPos)) return;

        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        int index = IndexOfSpark(hit.collider.gameObject);
        if (index == -1 || _applied[index]) return;

        if (!IsSparkUnlocked(index))
            return;

        // One-time height correction, not every frame: sync this spark's floating "home" height
        // to wherever the Phoenix currently is, in case it rose after this spark's initial
        // position was captured. Also fixes where it lands if the drop misses and it returns.
        if (_phoenixCollider != null && _sparks[index].parent != null)
            _initialLocalPositions[index].y = _sparks[index].parent.InverseTransformPoint(_phoenixCollider.transform.position).y;

        _heldIndex = index;
        _heldCameraDistance = Vector3.Distance(Camera.main.transform.position, _sparks[index].position);

        // Disable the held spark's collider so it never blocks the release raycast to the Phoenix.
        _heldCollider = hit.collider;
        _heldCollider.enabled = false;
    }

    private void HandleDragAndRelease()
    {
        Transform spark = _sparks[_heldIndex];

        if (TryGetPointerPosition(out Vector2 screenPos))
        {
            _lastPointerPos = screenPos;
            if (spark != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(screenPos);
                spark.position = ray.GetPoint(_heldCameraDistance);
            }
        }

        if (!TryGetPointerUp()) return;

        // Resolve against the pointer's last known position (release frame reports not-pressed).
        bool validDrop = ResolveDrop(_lastPointerPos);

        if (validDrop) ApplySpark(_heldIndex);
        else           ReturnSpark(_heldIndex);

        _heldCollider = null;
        _heldIndex = -1;
    }

    private bool ResolveDrop(Vector2 screenPos)
    {
        if (_phoenixCollider == null)
        {
            Debug.LogWarning("[Page11DragController] _phoenixCollider not assigned — cannot validate drop.");
            return false;
        }

        Ray ray = Camera.main.ScreenPointToRay(screenPos);

        // RaycastAll, not a single Raycast: once other sparks are docked and orbiting the
        // Phoenix, one of them can sit between the camera and the Phoenix collider along this
        // ray. A single Raycast would report only that nearer spark and wrongly fail the drop —
        // sending the held spark back to its own resting spot, which can look like it "snapped"
        // onto whichever spark got in the way. Any hit on the Phoenix along the ray counts.
        RaycastHit[] hits = Physics.RaycastAll(ray);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == _phoenixCollider) return true;
        }

        return false;
    }

    private void ApplySpark(int index)
    {
        _applied[index] = true;
        _appliedCount++;

        Transform spark = _sparks[index];
        if (spark != null && _phoenixCollider != null)
        {
            int closestSlot = -1;
            float minDistance = float.MaxValue;
            Vector3 bestPos = spark.position;

            for (int i = 0; i < _sparks.Length; i++)
            {
                if (!_slotOccupied[i])
                {
                    float angle = i * (Mathf.PI * 2f / Mathf.Max(1, _sparks.Length));
                    // Ring stands in the local X-Y plane (all sparks share the same local Z),
                    // like Saturn's rings tilted 90° up, instead of the flat X-Z "same height" ring.
                    Vector3 localOffset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * _dockRadius;
                    Vector3 worldOffset = _phoenixCollider.transform.rotation * localOffset;
                    Vector3 slotPos = _phoenixCollider.transform.position + worldOffset;

                    float dist = Vector3.Distance(spark.position, slotPos);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        closestSlot = i;
                        bestPos = slotPos;
                    }
                }
            }

            if (closestSlot != -1)
            {
                _slotOccupied[closestSlot] = true;
                _sparkSlotIndex[index] = closestSlot;
                spark.position = bestPos;
            }
            else
            {
                // Fallback
                _sparkSlotIndex[index] = index;
                float angle = index * (Mathf.PI * 2f / Mathf.Max(1, _sparks.Length));
                Vector3 localOffset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * _dockRadius;
                Vector3 worldOffset = _phoenixCollider.transform.rotation * localOffset;
                spark.position = _phoenixCollider.transform.position + worldOffset;
            }
        }

        string name = index < _sparkNames.Length ? _sparkNames[index] : "?";
        PlaySparkVoiceLine(name);

        if (_appliedCount >= _sparks.Length)
        {
            _orbitCoroutine = StartCoroutine(OrbitCelebration());

            if (_completionSequence == null)
                Debug.LogError("[Page11DragController] _completionSequence is NULL — completion not triggered.");
            else
                _completionSequence.TriggerCompletion();
        }
    }

    // Called by Page11ARTracker (relaying from Page11CompletionSequence) the moment the Phoenix
    // stops being visible, so the orbit ends at the same time the rainbow colour cycle does
    // instead of on its own separate fixed timer.
    public void StopOrbitCelebration()
    {
        if (_orbitCoroutine != null)
        {
            StopCoroutine(_orbitCoroutine);
            _orbitCoroutine = null;
        }
    }

    // Once all 4 sparks are docked, keeps them all orbiting together around the Phoenix (along
    // the same vertical ring they're already docked on) indefinitely, until StopOrbitCelebration()
    // is called — not a fixed duration, so it can stay in sync with however long the rainbow
    // colour cycle actually runs for.
    private IEnumerator OrbitCelebration()
    {
        if (_phoenixCollider == null) yield break;

        float ringAngleStep = Mathf.PI * 2f / Mathf.Max(1, _sparks.Length);
        float elapsed = 0f;
        float orbitOffsetDegrees = 0f;

        while (true)
        {
            // Ease the angular speed up from a standstill to full speed instead of snapping
            // straight to it — quadratic so it really does start slow, not just linearly ramp.
            float speedT = _celebrationRampUpDuration > 0f ? Mathf.Clamp01(elapsed / _celebrationRampUpDuration) : 1f;
            float currentSpeed = Mathf.Lerp(0f, _celebrationOrbitSpeed, speedT * speedT);
            orbitOffsetDegrees += currentSpeed * Time.deltaTime;
            elapsed += Time.deltaTime;

            float orbitOffset = orbitOffsetDegrees * Mathf.Deg2Rad;

            if (_phoenixCollider == null) yield break;

            for (int i = 0; i < _sparks.Length; i++)
            {
                if (_sparks[i] == null || !_applied[i]) continue;

                float angle = (_sparkSlotIndex[i] * ringAngleStep) + orbitOffset;
                Vector3 localOffset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * _dockRadius;
                Vector3 worldOffset = _phoenixCollider.transform.rotation * localOffset;
                _sparks[i].position = _phoenixCollider.transform.position + worldOffset;
            }

            yield return null;
        }
    }

    private void PlaySparkVoiceLine(string sparkName)
    {
        AudioClip clip = GetSparkVoiceClip(sparkName);
        if (_audioSource != null && clip != null)
        {
            _audioSource.PlayOneShot(clip);
            // AudioSource.isPlaying doesn't reliably reflect PlayOneShot clips, so track the
            // "busy" window with our own timer instead of relying on it.
            _voiceLineBusyCoroutine = StartCoroutine(BlockPickupWhileVoiceLinePlays(clip.length));
        }
        else
        {
            Debug.LogWarning($"[Page11DragController] AudioSource or voice clip for '{sparkName}' spark not assigned.");
        }
    }

    // Blocks picking up any spark for the duration of whichever spark's voice line just started,
    // so the player can't yank the next one out mid-line.
    private IEnumerator BlockPickupWhileVoiceLinePlays(float duration)
    {
        _sparkVoiceLineBusy = true;
        yield return new WaitForSeconds(duration);
        _sparkVoiceLineBusy = false;
        _voiceLineBusyCoroutine = null;
    }

    private AudioClip GetSparkVoiceClip(string sparkName)
    {
        switch (sparkName)
        {
            case "Blue":   return _blueSparkVoiceClip;
            case "Red":    return _redSparkVoiceClip;
            case "Yellow": return _yellowSparkVoiceClip;
            case "Gold":   return _goldSparkVoiceClip;
            default:       return null;
        }
    }

    private void ReturnSpark(int index)
    {
        Transform spark = _sparks[index];
        if (spark != null)
            spark.localPosition = _initialLocalPositions[index];

        if (_heldCollider != null)
            _heldCollider.enabled = true;
    }

    private bool IsSparkUnlocked(int index)
    {
        if (_debugUnlockAllSparks) return true;

        if (_pendantManager == null)
        {
            Debug.LogWarning("[Page11DragController] _pendantManager not assigned — treating all sparks as locked.");
            return false;
        }

        if (index >= _sparkNames.Length) return false;
        return _pendantManager.GetCollectedSparks().Contains(_sparkNames[index]);
    }

    private int IndexOfSpark(GameObject go)
    {
        if (_sparks == null) return -1;
        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparks[i] != null && _sparks[i].gameObject == go)
                return i;
        }
        return -1;
    }

    // --- Input System helpers (same touch/mouse pattern as Page10OrbController) ---

    private static bool TryGetPointerDown(out Vector2 position)
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            position = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            position = Mouse.current.position.ReadValue();
            return true;
        }
        position = Vector2.zero;
        return false;
    }

    private static bool TryGetPointerUp()
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            return true;
        if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            return true;
        return false;
    }

    private static bool TryGetPointerPosition(out Vector2 position)
    {
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
        {
            position = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            position = Mouse.current.position.ReadValue();
            return true;
        }
        position = Vector2.zero;
        return false;
    }
}
// Forced recompile to fix Unity serialization layout mismatch
