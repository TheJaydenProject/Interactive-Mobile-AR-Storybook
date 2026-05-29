using UnityEngine;

/// <summary>
/// Moves a drop downward each frame and detects overlap with the catch zone.
/// Attach to the BlueDrop prefab. Initialize() must be called by DropSpawner after instantiation.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class DropBehaviour : MonoBehaviour
{
    [SerializeField] private float _fallSpeed = 200f;   // Canvas units per second; tune in playtesting

    private RectTransform _rectTransform;
    private CatchZoneController _catchZone;
    private DropMeterController _dropMeter;
    private float _destroyBelowY;
    private bool _isCaught;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        // Safe fallback so a drop without Initialize() still exits the screen eventually
        _destroyBelowY = -3000f;
    }

    /// <summary>
    /// Called by DropSpawner immediately after instantiation to wire up dependencies.
    /// </summary>
    public void Initialize(CatchZoneController catchZone, DropMeterController dropMeter)
    {
        _catchZone = catchZone;
        _dropMeter = dropMeter;

        // Destroy the drop once it fully clears the bottom of its container
        if (transform.parent is RectTransform parentRect)
            _destroyBelowY = parentRect.rect.yMin - _rectTransform.rect.height;
    }

    private void Update()
    {
        if (_isCaught) return;

        Fall();
        CheckOutOfBounds();
        CheckCatch();
    }

    private void Fall()
    {
        Vector2 pos = _rectTransform.anchoredPosition;
        pos.y -= _fallSpeed * Time.deltaTime;
        _rectTransform.anchoredPosition = pos;
    }

    private void CheckOutOfBounds()
    {
        if (_rectTransform.anchoredPosition.y < _destroyBelowY)
            Destroy(gameObject);
    }

    private void CheckCatch()
    {
        if (_catchZone == null) return;
        if (!OverlapsWith(_catchZone.CatchRect)) return;

        _isCaught = true;                   // Prevent Update() re-entering before Destroy completes
        if (_dropMeter == null) Debug.LogError("[DropBehaviour] _dropMeter is NULL — catch not registered");
        else _dropMeter.RegisterCatch();
        Destroy(gameObject);
    }

    // AABB overlap between this drop and the catch zone rect.
    // Both are direct children of the Canvas with the same pivot setup, so
    // comparing anchoredPosition values is valid — they share the same local space.
    private bool OverlapsWith(RectTransform other)
    {
        if (other == null) return false;

        Vector2 myPos    = _rectTransform.anchoredPosition;
        Vector2 otherPos = other.anchoredPosition;

        float myHalfW    = _rectTransform.rect.width  / 2f;
        float myHalfH    = _rectTransform.rect.height / 2f;
        float otherHalfW = other.rect.width  / 2f;
        float otherHalfH = other.rect.height / 2f;

        return Mathf.Abs(myPos.x - otherPos.x) < myHalfW + otherHalfW
            && Mathf.Abs(myPos.y - otherPos.y) < myHalfH + otherHalfH;
    }
}
