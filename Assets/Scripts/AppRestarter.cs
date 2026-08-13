using UnityEngine;

/// <summary>
/// Forces a full Android process kill-and-relaunch, landing in a specific scene on the fresh
/// process — not a Unity scene reload. Needed specifically for the Ar1/Ar2 transition: ARCore's
/// native session layer (ArPresto) carries state across a same-process scene reload that a plain
/// SceneManager.LoadScene() can't clear, and that's what was segfaulting the moment a second
/// session in the same process reached the front-facing/face-tracking config (0x3), confirmed via
/// device logcat. Only a genuine process restart resets that native state.
/// </summary>
public static class AppRestarter
{
    private const string BootTargetKey = "AppRestarter.BootTargetScene";

    /// <summary>
    /// The scene the Bootstrap scene should load on the next launch, or empty if none is pending.
    /// Cleared by whoever reads it (see Bootstrap.cs) so a normal subsequent launch isn't affected.
    /// </summary>
    public static string ConsumePendingBootTarget()
    {
        string target = PlayerPrefs.GetString(BootTargetKey, string.Empty);
        Debug.Log($"[AppRestarter] ConsumePendingBootTarget read '{target}' from PlayerPrefs (empty means no pending restart, or a normal launch).");
        if (!string.IsNullOrEmpty(target))
        {
            PlayerPrefs.DeleteKey(BootTargetKey);
            PlayerPrefs.Save();
        }
        return target;
    }

    /// <summary>
    /// Saves which scene to boot into, then kills and relaunches the app process. Android-only —
    /// no-ops with a warning on other platforms (Editor testing can't reproduce this at all, since
    /// XR Simulation doesn't run real ARCore native code).
    /// </summary>
    public static void RestartIntoScene(string sceneName)
    {
        Debug.Log($"[AppRestarter] RestartIntoScene('{sceneName}') called.");
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            // Written only once we're actually about to attempt the relaunch, and rolled back in
            // the catch below if it fails — otherwise a failed/no-op call (e.g. tested in the
            // Editor, where this whole block never runs) leaves a stale flag on disk that
            // Bootstrap would incorrectly act on during a later, genuinely normal launch.
            PlayerPrefs.SetString(BootTargetKey, sceneName);
            PlayerPrefs.Save();
            Debug.Log($"[AppRestarter] Saved '{sceneName}' to PlayerPrefs and called Save(). Readback check: '{PlayerPrefs.GetString(BootTargetKey, "<empty>")}'.");

            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject packageManager = currentActivity.Call<AndroidJavaObject>("getPackageManager"))
            {
                string packageName = currentActivity.Call<string>("getPackageName");
                Debug.Log($"[AppRestarter] Package name: '{packageName}'.");
                AndroidJavaObject intent = packageManager.Call<AndroidJavaObject>("getLaunchIntentForPackage", packageName);

                if (intent == null)
                {
                    Debug.LogError("[AppRestarter] getLaunchIntentForPackage returned null — can't relaunch.");
                    PlayerPrefs.DeleteKey(BootTargetKey);
                    PlayerPrefs.Save();
                    return;
                }

                // FLAG_ACTIVITY_NEW_TASK (0x10000000) | FLAG_ACTIVITY_CLEAR_TASK (0x00008000) —
                // standard combination for "relaunch this app fresh," discarding the existing task.
                intent.Call<AndroidJavaObject>("addFlags", 0x10008000);
                Debug.Log("[AppRestarter] Calling startActivity...");
                currentActivity.Call("startActivity", intent);
                Debug.Log("[AppRestarter] startActivity returned. Sleeping 300ms before killProcess.");

                // Give the PlayerPrefs disk write and the new launch Intent a brief moment to
                // actually land before killing this process. Killing it immediately risks a race
                // where PlayerPrefs.Save() hasn't finished committing to disk yet, which would
                // silently lose the boot-target flag — Bootstrap then reads nothing and falls back
                // to its default scene instead of the intended target. We're about to kill the
                // whole process anyway, so blocking briefly here costs nothing.
                System.Threading.Thread.Sleep(300);

                using (AndroidJavaClass processClass = new AndroidJavaClass("android.os.Process"))
                {
                    int pid = processClass.CallStatic<int>("myPid");
                    Debug.Log($"[AppRestarter] Killing process {pid} now.");
                    processClass.CallStatic("killProcess", pid);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AppRestarter] Failed to restart the app process: {e}");
            PlayerPrefs.DeleteKey(BootTargetKey);
            PlayerPrefs.Save();
        }
#else
        Debug.LogWarning("[AppRestarter] Process restart is Android-only and can't be tested in the Editor — nothing happened.");
#endif
    }
}
