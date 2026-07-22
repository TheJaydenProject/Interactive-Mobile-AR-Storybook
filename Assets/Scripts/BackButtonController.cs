using UnityEngine;

/// <summary>
/// Shows a single shared Back button whenever any page's feature is active, and lets the child
/// cancel it. Doesn't know which page is running — it just asks AppStateManager to cancel, and
/// whichever page is actually active is responsible for stopping itself.
/// </summary>
public class BackButtonController : MonoBehaviour
{
    [Header("Scan Lock")]
    [SerializeField] private AppStateManager _appStateManager;

    [Header("UI")]
    [SerializeField] private GameObject _backButton;

    private void OnEnable()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[BackButtonController] _appStateManager not assigned.");
            return;
        }

        _appStateManager.OnFeatureActiveChanged += HandleFeatureActiveChanged;

        // Sync immediately in case a feature was already active before this subscribed.
        HandleFeatureActiveChanged(_appStateManager.IsFeatureActive);
    }

    private void OnDisable()
    {
        if (_appStateManager != null)
            _appStateManager.OnFeatureActiveChanged -= HandleFeatureActiveChanged;
    }

    private void HandleFeatureActiveChanged(bool isActive)
    {
        if (_backButton == null)
        {
            Debug.LogError("[BackButtonController] _backButton not assigned.");
            return;
        }
        _backButton.SetActive(isActive);
    }

    public void OnBackPressed()
    {
        if (_appStateManager == null)
        {
            Debug.LogError("[BackButtonController] _appStateManager not assigned; cannot cancel.");
            return;
        }
        _appStateManager.CancelFeature();
    }
}
