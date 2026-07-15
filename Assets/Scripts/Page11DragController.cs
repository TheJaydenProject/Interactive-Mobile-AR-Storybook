using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

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
    [Header("Sparks (order must match Spark Names)")]
    [SerializeField] private Transform[] _sparks;

    // Parallel to _sparks. Names must match PendantManager's spark strings ("Blue"/"Red"/"Yellow"/"Gold").
    [SerializeField] private string[] _sparkNames = { "Blue", "Red", "Yellow", "Gold" };

    [Header("Drop Target")]
    [SerializeField] private Collider _phoenixCollider;
    [Tooltip("Sparks dock at the Phoenix position plus a small ring offset of this radius.")]
    [SerializeField] private float _dockRadius = 0.18f;
    [Tooltip("Scales up the Phoenix placeholder square slightly relative to everything else.")]
    [SerializeField] private float _phoenixScaleMultiplier = 1.15f;
    [Tooltip("Globally scales down the Phoenix and all Sparks. Adjust this to match Page 10's size.")]
    [SerializeField] private float _globalScaleMultiplier = 0.3f;

    [Header("Scatter Settings")]
    [Tooltip("How much to randomly scatter the initial positions of the sparks.")]
    [SerializeField] private float _scatterRadius = 0.05f;

    [Header("Floating Motion")]
    [SerializeField] private float _floatAmplitude = 0.01f;
    [SerializeField] private float _floatFrequency = 1.5f;
    [SerializeField] private float _driftAmplitude = 0.005f;

    [Header("Dependencies")]
    [SerializeField] private PendantManager           _pendantManager;
    [SerializeField] private Page11CompletionSequence _completionSequence;

    [Header("Debug")]
    [Tooltip("When true, skips the collected-spark check so all four can be dragged even if not earned yet. For isolated Page 11 testing.")]
    [SerializeField] private bool _debugUnlockAllSparks;

    // Lines spoken when each spark is applied (placeholder logging until VO/text assets exist).
    private static readonly Dictionary<string, string> s_sparkLines = new Dictionary<string, string>
    {
        { "Blue",   "Sadness helps you care" },
        { "Red",    "Anger gives strength"    },
        { "Yellow", "Fear helps you be brave" },
        { "Gold",   "Joy gives you energy"    },
    };

    private Vector3[] _initialLocalPositions;
    private float[]   _phaseOffsets;
    private bool[]    _applied;
    private bool[]    _slotOccupied;
    private int       _appliedCount;

    private int      _heldIndex = -1;
    private Collider _heldCollider;
    private float    _heldCameraDistance;
    private Vector2  _lastPointerPos;

    private void Awake()
    {
        // Dynamically scale the dock radius so spheres always hug the Phoenix tightly no matter the size
        _dockRadius *= _globalScaleMultiplier;

        if (_phoenixCollider != null)
        {
            _phoenixCollider.transform.localScale *= (_phoenixScaleMultiplier * _globalScaleMultiplier);
        }

        if (_sparks == null) return;

        _initialLocalPositions = new Vector3[_sparks.Length];
        _phaseOffsets = new float[_sparks.Length];
        _applied = new bool[_sparks.Length];
        _slotOccupied = new bool[_sparks.Length];

        for (int i = 0; i < _sparks.Length; i++)
        {
            if (_sparks[i] != null)
            {
                _sparks[i].localScale *= _globalScaleMultiplier;
                
                // Pull their initial positions closer to the center proportionally to the scale down
                _sparks[i].localPosition *= _globalScaleMultiplier;
                
                // Apply a small 3D random scatter so they appear at different height levels
                Vector3 rand = Random.insideUnitSphere * _scatterRadius;
                _sparks[i].localPosition += rand;
                
                _initialLocalPositions[i] = _sparks[i].localPosition;
                _phaseOffsets[i] = Random.Range(0f, Mathf.PI * 2f);
            }
        }

        if (_sparkNames == null || _sparkNames.Length != _sparks.Length)
            Debug.LogWarning("[Page11DragController] _sparkNames length does not match _sparks length; gating may misbehave.", this);
    }

    private void Update()
    {
        if (Camera.main == null) return;

        if (_heldIndex == -1) HandlePickup();
        else                  HandleDragAndRelease();

        UpdateFloatingMotion();
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
        if (!TryGetPointerDown(out Vector2 screenPos)) return;

        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        int index = IndexOfSpark(hit.collider.gameObject);
        if (index == -1 || _applied[index]) return;

        if (!IsSparkUnlocked(index))
        {
            Debug.Log($"[Page11DragController] '{_sparkNames[index]}' spark not collected yet — drag ignored.");
            return;
        }

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
        // Held spark's collider is disabled, so a hit here is the Phoenix (or nothing).
        if (Physics.Raycast(ray, out RaycastHit hit))
            return hit.collider == _phoenixCollider;

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
                    Vector3 localOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _dockRadius;
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
                spark.position = bestPos;
            }
            else
            {
                // Fallback
                float angle = index * (Mathf.PI * 2f / Mathf.Max(1, _sparks.Length));
                Vector3 localOffset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _dockRadius;
                Vector3 worldOffset = _phoenixCollider.transform.rotation * localOffset;
                spark.position = _phoenixCollider.transform.position + worldOffset;
            }
        }

        string name = index < _sparkNames.Length ? _sparkNames[index] : "?";
        string line = s_sparkLines.TryGetValue(name, out string l) ? l : "(no line)";
        Debug.Log($"[Page11DragController] {name} spark applied — \"{line}\"");

        if (_appliedCount >= _sparks.Length)
        {
            if (_completionSequence == null)
                Debug.LogError("[Page11DragController] _completionSequence is NULL — completion not triggered.");
            else
                _completionSequence.TriggerCompletion();
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
