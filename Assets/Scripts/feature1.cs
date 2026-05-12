using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Attach to XR Origin (AR Rig).
/// When the target image is detected: fade in a grey overlay, then spawn clouds.
/// Tap each cloud to pop it — the overlay clears gradually as clouds are popped.
/// </summary>
[RequireComponent(typeof(ARTrackedImageManager))]
public class Page1Manager : MonoBehaviour
{
    [Header("AR")]
    [SerializeField] private ARTrackedImageManager _trackedImageManager;
    [SerializeField] private XRRayInteractor _rayInteractor;

    [Header("Overlay")]
    [SerializeField] private Image _greyOverlay;
    [SerializeField] private float _fadeInDuration = 1.0f;
    [SerializeField] private float _popFadeDuration = 0.3f;

    [Header("Clouds")]
    [SerializeField] private GameObject _cloudPrefab;
    [SerializeField] private int _cloudCount = 5;
    [SerializeField] private float _spreadRadius = 0.15f;
    [SerializeField] private float _cloudYOffsetMin = 0.05f;
    [SerializeField] private float _cloudYOffsetMax = 0.15f;

    // How opaque the overlay starts and how low it goes before the last cloud.
    private const float OverlayMaxAlpha = 0.85f;
    private const float OverlayMinAlpha = 0.4f;

    private List<GameObject> _clouds = new();
    private int _poppedCount = 0;
    private bool _sequenceActive = false;
    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        if (_trackedImageManager == null)
            _trackedImageManager = GetComponent<ARTrackedImageManager>();

        // Make sure the overlay starts invisible.
        SetOverlayAlpha(0f);
        _greyOverlay.enabled = false;
    }

    private void OnEnable()
    {
        _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
    }

    private void OnDisable()
    {
        _trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
    }

    private void Update()
    {
        // Only check for taps when clouds are present.
        if (_clouds.Count == 0) return;
        if (_rayInteractor == null) return;

        // wasCompletedThisFrame is true on the exact frame the tap gesture ends.
        if (!_rayInteractor.logicalSelectState.wasCompletedThisFrame) return;

        // Ask the interactor where the ray hit.
        if (!_rayInteractor.TryGetCurrentRaycast(out RaycastHit? hit, out _, out _, out _, out _)) return;
        if (hit == null) return;

        // If we hit a cloud, pop it.
        CloudBehaviour cloud = hit.Value.collider.GetComponent<CloudBehaviour>();
        cloud?.Pop();
    }

    // --- AR Tracking ---

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage image in args.added)
            StartSequence(image.transform);

        foreach (ARTrackedImage image in args.updated)
        {
            if (image.trackingState == TrackingState.Tracking && !_sequenceActive)
                StartSequence(image.transform);
            else if (image.trackingState == TrackingState.None && _sequenceActive)
                StopSequence();
        }

        foreach (var removed in args.removed)
            StopSequence();
    }

    private void StartSequence(Transform anchor)
    {
        _sequenceActive = true;
        _poppedCount = 0;
        StartCoroutine(FadeInThenSpawn(anchor));
    }

    private void StopSequence()
    {
        _sequenceActive = false;
        DespawnClouds();
        FadeTo(0f, _popFadeDuration);
    }

    // --- Overlay ---

    private void FadeTo(float targetAlpha, float duration)
    {
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadeRoutine(targetAlpha, duration));
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        _greyOverlay.enabled = true;

        float startAlpha = _greyOverlay.color.a;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
            yield return null;
        }

        SetOverlayAlpha(targetAlpha);

        if (targetAlpha <= 0f)
            _greyOverlay.enabled = false;
    }

    private void SetOverlayAlpha(float alpha)
    {
        Color c = _greyOverlay.color;
        _greyOverlay.color = new Color(c.r, c.g, c.b, alpha);
    }

    // --- Clouds ---

    private IEnumerator FadeInThenSpawn(Transform anchor)
    {
        FadeTo(OverlayMaxAlpha, _fadeInDuration);
        yield return new WaitForSeconds(_fadeInDuration);
        SpawnClouds(anchor);
    }

    private void SpawnClouds(Transform anchor)
    {
        DespawnClouds();

        if (_cloudPrefab == null)
        {
            Debug.LogError("[Page1Manager] Cloud prefab not assigned.");
            return;
        }

        float angleStep = 360f / _cloudCount;

        for (int i = 0; i < _cloudCount; i++)
        {
            float angle = (angleStep * i) + Random.Range(-angleStep * 0.3f, angleStep * 0.3f);
            float rad = angle * Mathf.Deg2Rad;
            float radius = Random.Range(_spreadRadius * 0.6f, _spreadRadius);

            Vector3 offset = new Vector3(
                Mathf.Cos(rad) * radius,
                Random.Range(_cloudYOffsetMin, _cloudYOffsetMax),
                Mathf.Sin(rad) * radius
            );

            GameObject cloud = Instantiate(_cloudPrefab, anchor.position + offset, Quaternion.identity, anchor);
            cloud.transform.LookAt(Camera.main.transform);
            cloud.transform.Rotate(0f, 180f, 0f);

            CloudBehaviour behaviour = cloud.GetComponent<CloudBehaviour>();
            if (behaviour != null)
                behaviour.OnPopped += OnCloudPopped;
            else
                Debug.LogWarning("[Page1Manager] Cloud prefab missing CloudBehaviour.");

            _clouds.Add(cloud);
        }
    }

    private void OnCloudPopped(CloudBehaviour cloud)
    {
        cloud.OnPopped -= OnCloudPopped;
        _clouds.Remove(cloud.gameObject);
        _poppedCount++;

        // Step the overlay down with each pop. Goes to zero only on the last one.
        float targetAlpha = _poppedCount >= _cloudCount
            ? 0f
            : Mathf.Lerp(OverlayMaxAlpha, OverlayMinAlpha, (float)_poppedCount / (_cloudCount - 1));

        FadeTo(targetAlpha, _popFadeDuration);
    }

    private void DespawnClouds()
    {
        foreach (GameObject cloud in _clouds)
        {
            if (cloud == null) continue;
            CloudBehaviour behaviour = cloud.GetComponent<CloudBehaviour>();
            if (behaviour != null) behaviour.OnPopped -= OnCloudPopped;
            Destroy(cloud);
        }
        _clouds.Clear();
    }
}