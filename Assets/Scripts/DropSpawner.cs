using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns BlueDrop prefabs at timed intervals along the top of the canvas.
/// Attach to the DropSpawner child GameObject inside Page4Manager.
/// Call StartSpawning() from your AR tracking callback when the image is detected.
/// </summary>
public class DropSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject _blueDropPrefab;
    [SerializeField] private RectTransform _dropContainer;      // Assign the Canvas RectTransform
    [SerializeField] private CatchZoneController _catchZone;
    [SerializeField] private DropMeterController _dropMeter;

    [Header("Spawn Config")]
    [SerializeField] private float _spawnInterval = 1.5f;
    [SerializeField] private float _horizontalPadding = 60f;    // Keeps drops away from screen edges
    [SerializeField] private bool _autoStart = true;

    private const float MinSpawnSeparation = 150f;
    private const int   MaxRollAttempts    = 5;

    private Coroutine _spawnRoutine;
    private float     _lastSpawnX = float.MinValue;
    private bool      _complete;

    private void Start()
    {
        ValidateReferences();
        if (_autoStart)
            StartSpawning();
    }

    public void StartSpawning()
    {
        if (_complete) return;          // Completion fired — never restart
        if (_spawnRoutine != null) return;
        _spawnRoutine = StartCoroutine(SpawnLoop());
    }

    public void StopSpawning()
    {
        if (_spawnRoutine == null) return;
        StopCoroutine(_spawnRoutine);
        _spawnRoutine = null;
    }

    // Called by CompletionSequence — permanently blocks StartSpawning from that point on
    public void CompleteAndStop()
    {
        _complete = true;
        StopSpawning();
    }

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            SpawnDrop();
            yield return new WaitForSeconds(_spawnInterval);
        }
    }

    private void SpawnDrop()
    {
        if (_blueDropPrefab == null || _dropContainer == null) return;

        GameObject dropObj = Instantiate(_blueDropPrefab, _dropContainer);
        RectTransform dropRect = dropObj.GetComponent<RectTransform>();

        if (dropRect == null)
        {
            Debug.LogError("[DropSpawner] BlueDrop prefab is missing a RectTransform.", this);
            Destroy(dropObj);
            return;
        }

        // rect.xMin / xMax / yMax are pivot-agnostic — works regardless of canvas anchor setup
        Rect  container = _dropContainer.rect;
        float minX      = container.xMin + _horizontalPadding;
        float maxX      = container.xMax - _horizontalPadding;
        float spawnX    = Random.Range(minX, maxX);

        for (int i = 1; i < MaxRollAttempts; i++)
        {
            if (Mathf.Abs(spawnX - _lastSpawnX) >= MinSpawnSeparation) break;
            spawnX = Random.Range(minX, maxX);
        }

        _lastSpawnX = spawnX;
        float spawnY = container.yMax;

        dropRect.anchoredPosition = new Vector2(spawnX, spawnY);

        if (dropObj.TryGetComponent(out DropBehaviour behaviour))
            behaviour.Initialize(_catchZone, _dropMeter);
        else
            Debug.LogWarning("[DropSpawner] BlueDrop prefab is missing a DropBehaviour component.", this);
    }

    private void ValidateReferences()
    {
        if (_blueDropPrefab == null)
            Debug.LogError("[DropSpawner] _blueDropPrefab not assigned.", this);
        if (_dropContainer == null)
            Debug.LogError("[DropSpawner] _dropContainer not assigned.", this);
        if (_catchZone == null)
            Debug.LogError("[DropSpawner] _catchZone not assigned.", this);
        if (_dropMeter == null)
            Debug.LogError("[DropSpawner] _dropMeter not assigned.", this);
    }
}
