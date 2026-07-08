using UnityEngine;
using UnityEngine.InputSystem;

public class Page10OrbController : MonoBehaviour
{
    [Header("Orbs")]
    [SerializeField] private Transform[] _orbs;

    [Header("Floating Motion")]
    [SerializeField] private float _floatAmplitude = 0.03f;
    [SerializeField] private float _floatFrequency = 1.5f;
    [SerializeField] private float _driftAmplitude = 0.015f;

    [Header("Dependencies")]
    [SerializeField] private Page10MeterController _meterController;

    private Vector3[] _initialLocalPositions;
    private float[] _phaseOffsets;

    private void Awake()
    {
        if (_orbs != null)
        {
            _initialLocalPositions = new Vector3[_orbs.Length];
            _phaseOffsets = new float[_orbs.Length];

            for (int i = 0; i < _orbs.Length; i++)
            {
                if (_orbs[i] != null)
                {
                    _initialLocalPositions[i] = _orbs[i].localPosition;
                    _phaseOffsets[i] = Random.Range(0f, Mathf.PI * 2f);
                }
            }
        }
    }

    private void Update()
    {
        // Temporary log to confirm Update is running
        Debug.Log("[Page10OrbController] Update is running.");
        
        HandleInput();
        UpdateFloatingMotion();
    }

    private void HandleInput()
    {
        if (Camera.main == null) return;

        bool hasInput = false;
        Vector2 screenPos = Vector2.zero;

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
            hasInput = true;
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPos = Mouse.current.position.ReadValue();
            hasInput = true;
        }

        if (hasInput)
        {
            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            bool hitSomething = Physics.Raycast(ray, out RaycastHit hit);
            Debug.Log($"[Page10OrbController] Raycast fired at {screenPos}. Hit something? {hitSomething}");

            if (hitSomething)
            {
                Debug.Log($"[Page10OrbController] Raycast hit: {hit.collider.gameObject.name}");

                if (_orbs == null) return;

                foreach (var orb in _orbs)
                {
                    if (orb != null && hit.collider.gameObject == orb.gameObject)
                    {
                        if (orb.gameObject.activeSelf)
                        {
                            orb.gameObject.SetActive(false);
                            
                            if (_meterController != null)
                            {
                                _meterController.RegisterCatch();
                            }
                            else
                            {
                                Debug.LogWarning("[Page10OrbController] _meterController is missing.");
                            }
                        }
                        break;
                    }
                }
            }
        }
    }

    private void UpdateFloatingMotion()
    {
        if (_orbs == null) return;

        float time = Time.time;

        for (int i = 0; i < _orbs.Length; i++)
        {
            if (_orbs[i] != null && _orbs[i].gameObject.activeSelf)
            {
                float phase = _phaseOffsets[i];
                Vector3 initialPos = _initialLocalPositions[i];

                float yOffset = Mathf.Sin(time * _floatFrequency + phase) * _floatAmplitude;
                float xOffset = Mathf.Cos(time * (_floatFrequency * 0.7f) + phase) * _driftAmplitude;
                float zOffset = Mathf.Sin(time * (_floatFrequency * 0.8f) + phase) * _driftAmplitude;

                _orbs[i].localPosition = initialPos + new Vector3(xOffset, yOffset, zOffset);
            }
        }
    }
}
