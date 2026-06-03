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
    [SerializeField] private float _deadZone = 0.08f;  // Ignores tiny accelerometer noise below this threshold
    [SerializeField] private bool  _invertX  = false;  // Flip if the bar moves the wrong direction on device

    private float _maxSpeed     = 840f;
    private float _acceleration = 1680f;
    private float _deceleration = 2520f;

    private RectTransform _rectTransform;
    private Accelerometer _accelerometer;
    private float         _leftClamp;
    private float         _rightClamp;
    private float         _currentVelocity;

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
        if (_invertX) rawTilt = -rawTilt;

        if (Mathf.Abs(rawTilt) > _deadZone)
        {
            // Tilt detected — accelerate velocity toward max speed in the tilt direction
            float targetVelocity = Mathf.Sign(rawTilt) * _maxSpeed;
            _currentVelocity = Mathf.MoveTowards(_currentVelocity, targetVelocity, _acceleration * Time.deltaTime);
        }
        else
        {
            // No tilt — decelerate back to zero
            _currentVelocity = Mathf.MoveTowards(_currentVelocity, 0f, _deceleration * Time.deltaTime);
        }

        _currentVelocity = Mathf.Clamp(_currentVelocity, -_maxSpeed, _maxSpeed);

        float newX = _rectTransform.anchoredPosition.x + _currentVelocity * Time.deltaTime;
        newX = Mathf.Clamp(newX, _leftClamp, _rightClamp);

        _rectTransform.anchoredPosition = new Vector2(newX, _rectTransform.anchoredPosition.y);
    }

    private void OnDestroy()
    {
        if (_accelerometer != null && _accelerometer.enabled)
            InputSystem.DisableDevice(_accelerometer);
    }
}
