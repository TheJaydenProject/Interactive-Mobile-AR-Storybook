using System.IO;
using UnityEngine;

/// <summary>
/// Shared location for photos saved to the app's own private storage, in addition to the system
/// gallery — Application.persistentDataPath is fully owned by this app, so the in-app Gallery can
/// read photos back from here with no scoped-storage read permissions to deal with. Written to by
/// Page12FaceFilterScene (in the Ar2 scene) and read by GalleryManager (in Ar1) — the folder name
/// is defined once here rather than duplicated as a literal string in both.
/// </summary>
public static class PhotoStorage
{
    private const string FolderName = "Photos";

    public static string GetDirectory()
    {
        string path = Path.Combine(Application.persistentDataPath, FolderName);
        Directory.CreateDirectory(path); // no-op if it already exists
        return path;
    }
}
