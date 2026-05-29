using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reads the device accelerometer each frame and moves the catch zone bar horizontally.
/// Attach to the CatchZone GameObject inside the Canvas.
/// Only X position is updated — Y stays fixed where you set it in the Inspector.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CatchZoneController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _sensitivity = 150f;     // Canvas units moved per (m/s²) of tilt per second
    [SerializeField] private float _deadZone    = 0.08f;    // Ignores tiny accelerometer noise below this threshold
    [SerializeField] private bool  _invertX     = false;    // Flip if the bar moves the wrong direction on device

    private RectTransform _rectTransform;
    private Accelerometer _accelerometer;
    private float _leftClamp;
    private float _rightClamp;

    // Exposes the RectTransform for AABB overlap checks in DropBehaviour
    public RectTransform CatchRect => _rectTransform;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    private void Start()
    {
        InitAccelerometer();
        ComputeClampBounds();
    }

    private void InitAccelerometer()
    {
        // GetDevice<T>() is the required new Input System API per project spec
        _accelerometer = InputSystem.GetDevice<Accelerometer>();

        if (_accelerometer != null)
        {
            InputSystem.EnableDevice(_accelerometer);
        }
        else
        {
            Debug.LogWarning("[CatchZoneController] No accelerometer found. Catch zone will be stationary.", this);
        }
    }

    private void ComputeClampBounds()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[CatchZoneController] No parent Canvas found.", this);
            return;
        }

        RectTransform canvasRect   = canvas.GetComponent<RectTransform>();
        float         catchHalfW   = _rectTransform.rect.width / 2f;
        float         canvasHalfW  = canvasRect.rect.width     / 2f;

        // Keep the bar fully on screen — its centre cannot go past the canvas edge minus its own half-width
        _leftClamp  = -canvasHalfW + catchHalfW;
        _rightClamp =  canvasHalfW - catchHalfW;
    }

    private void Update()
    {
        if (_accelerometer == null) return;

        float rawTilt = _accelerometer.acceleration.ReadValue().x;

        // Dead zone prevents the bar drifting when the phone is resting flat
        if (Mathf.Abs(rawTilt) < _deadZone) rawTilt = 0f;
        if (_invertX) rawTilt = -rawTilt;

        // Velocity-based: tilt angle drives speed, not absolute position.
        // Holding the phone at an angle makes the bar slide continuously — natural for a catch game.
        float newX = _rectTransform.anchoredPosition.x + rawTilt * _sensitivity * Time.deltaTime;
        newX = Mathf.Clamp(newX, _leftClamp, _rightClamp);

        _rectTransform.anchoredPosition = new Vector2(newX, _rectTransform.anchoredPosition.y);
    }

    private void OnDestroy()
    {
        if (_accelerometer != null && _accelerometer.enabled)
            InputSystem.DisableDevice(_accelerometer);
    }
}
