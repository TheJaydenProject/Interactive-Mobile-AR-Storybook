using System;
using System.Collections;
using UnityEngine;

public class CloudBehaviour : MonoBehaviour
{
    public event Action<CloudBehaviour> OnPopped;

    [SerializeField] private float _spawnDuration = 0.4f;
    [SerializeField] private float _popDuration = 0.3f;
    [SerializeField] private AnimationCurve _popCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Idle Float")]
    [SerializeField] private float _bobHeight = 0.022f;
    [SerializeField] private float _bobSpeed = 0.8f;
    [SerializeField] private float _driftAmount = 0.012f;
    [SerializeField] private float _driftSpeed = 0.5f;

    private bool _isPopping = false;
    private Vector3 _fullScale;

    private void Start()
    {
        _fullScale = transform.localScale;
        transform.localScale = Vector3.zero;
        StartCoroutine(SpawnIn());
    }

    public void Pop()
    {
        if (_isPopping) return;
        StartCoroutine(PopAndDestroy());
    }

    private IEnumerator SpawnIn()
    {
        float elapsed = 0f;

        while (elapsed < _spawnDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _spawnDuration);
            transform.localScale = _fullScale * t;
            yield return null;
        }

        transform.localScale = _fullScale;
        StartCoroutine(IdleFloat());
    }

    private IEnumerator PopAndDestroy()
    {
        _isPopping = true;
        OnPopped?.Invoke(this);

        Vector3 originalScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < _popDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / _popDuration;
            transform.localScale = originalScale * _popCurve.Evaluate(t);
            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator IdleFloat()
    {
        float seed = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Vector3 startPos = transform.localPosition;

        while (true)
        {
            float yOffset = Mathf.Sin(Time.time * _bobSpeed + seed) * _bobHeight;
            // Different frequency than the vertical bob so the path traces a lazy figure-eight
            // instead of a straight diagonal line.
            float xOffset = Mathf.Cos(Time.time * _driftSpeed + seed) * _driftAmount;
            transform.localPosition = startPos + new Vector3(xOffset, yOffset, 0f);
            yield return null;
        }
    }
}