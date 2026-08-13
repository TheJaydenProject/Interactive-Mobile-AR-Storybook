using UnityEngine;

/// <summary>
/// Attach to any object to make it gently bob up and down in place, like it's floating.
/// Offsets from whatever local position/rotation the object starts at, so it still works when
/// the object is parented to an AR anchor that's moving/rotating on its own.
/// </summary>
public class FloatingObject : MonoBehaviour
{
    [Header("Bob")]
    [SerializeField] private float _bobHeight = 0.02f;
    [SerializeField] private float _bobSpeed  = 1f;

    [Header("Rotate")]
    [SerializeField] private bool  _rotate = false;
    [SerializeField] private float _rotateSpeed = 30f;

    private Vector3 _startLocalPosition;
    private float   _timeOffset;

    private void Awake()
    {
        _startLocalPosition = transform.localPosition;
        // Randomized so multiple floating objects in the same scene don't bob in perfect sync.
        _timeOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        float y = Mathf.Sin(Time.time * _bobSpeed + _timeOffset) * _bobHeight;
        transform.localPosition = _startLocalPosition + new Vector3(0f, y, 0f);

        if (_rotate)
            transform.Rotate(Vector3.up, _rotateSpeed * Time.deltaTime, Space.Self);
    }
}
