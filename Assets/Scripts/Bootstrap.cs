using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach to a GameObject in a dedicated "Bootstrap" scene that has NO AR components at all, and
/// make it the actual first scene in Build Settings. Its only job: redirect to Ar1 on a normal
/// launch, or to Ar2 if AppRestarter just relaunched the process specifically to land there.
///
/// This has to be a separate, AR-component-free scene rather than just checking this at the top
/// of Ar1 — if Ar1 loaded first and then redirected to Ar2, Ar1's own ARSession would still
/// initialize as the first session in the process before the redirect happened, recreating the
/// exact same-process-second-session bug this whole restart mechanism exists to avoid.
/// </summary>
public class Bootstrap : MonoBehaviour
{
    [SerializeField] private string _defaultSceneName = "Ar1";

    private void Start()
    {
        string pendingTarget = AppRestarter.ConsumePendingBootTarget();
        string sceneToLoad = string.IsNullOrEmpty(pendingTarget) ? _defaultSceneName : pendingTarget;
        Debug.Log($"[Bootstrap] Loading '{sceneToLoad}' (pendingTarget was '{pendingTarget}', default is '{_defaultSceneName}').");
        SceneManager.LoadScene(sceneToLoad);
    }
}
