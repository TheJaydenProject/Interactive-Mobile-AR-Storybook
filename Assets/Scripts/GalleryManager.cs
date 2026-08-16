using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lets the player browse photos captured on Page 12 (see PhotoStorage/Page12FaceFilterScene)
/// without leaving the app. Reads from the app's own private storage, not the system gallery, so
/// there's no scoped-storage read permission to deal with.
/// </summary>
public class GalleryManager : MonoBehaviour
{
    [Header("Menu")]
    [Tooltip("Hidden while the gallery is open; shown again when it closes.")]
    [SerializeField] private GameObject _menuUIGroup;

    [Header("Gallery")]
    [SerializeField] private GameObject _galleryPanel;
    [Tooltip("The ScrollView's Content transform — needs a GridLayoutGroup for the thumbnails to lay out in a grid.")]
    [SerializeField] private Transform _gridContent;
    [Tooltip("Prefab with an Image (for the thumbnail) and a Button, both on the root object.")]
    [SerializeField] private GameObject _thumbnailPrefab;
    [Tooltip("Shown instead of the grid when there are no saved photos yet.")]
    [SerializeField] private GameObject _emptyStateGroup;

    [Header("Fullscreen Viewer")]
    [Tooltip("Needs its own close button wired to CloseFullscreenViewer().")]
    [SerializeField] private GameObject _fullscreenViewer;
    [SerializeField] private Image _fullscreenImage;

    // Tracked so they can be torn down on the next OpenGallery() call — otherwise every reopen
    // leaks the previous batch of instantiated thumbnails and loaded textures.
    private readonly List<GameObject> _thumbnails = new();
    private readonly List<Texture2D> _loadedTextures = new();

    private void Awake()
    {
        if (_galleryPanel != null) _galleryPanel.SetActive(false);
        if (_fullscreenViewer != null) _fullscreenViewer.SetActive(false);
    }

    // Wire this to the Gallery button on the main menu.
    public void OnGalleryButtonPressed()
    {
        if (_menuUIGroup != null) _menuUIGroup.SetActive(false);
        if (_galleryPanel != null) _galleryPanel.SetActive(true);

        PopulateGrid();
    }

    // Wire this to the gallery panel's own Back button.
    public void OnGalleryBackPressed()
    {
        if (_galleryPanel != null) _galleryPanel.SetActive(false);
        if (_menuUIGroup != null) _menuUIGroup.SetActive(true);

        CloseFullscreenViewer();
    }

    // Wire this to the fullscreen viewer's own close button.
    public void CloseFullscreenViewer()
    {
        if (_fullscreenViewer != null) _fullscreenViewer.SetActive(false);
    }

    private void PopulateGrid()
    {
        ClearGrid();

        if (_gridContent == null || _thumbnailPrefab == null)
        {
            Debug.LogError("[GalleryManager] _gridContent or _thumbnailPrefab not assigned.");
            return;
        }

        string[] files = Directory.GetFiles(PhotoStorage.GetDirectory(), "*.png");
        // Newest first — the timestamp is baked into the filename (see Page12FaceFilterScene).
        Array.Sort(files, (a, b) => string.CompareOrdinal(b, a));

        if (_emptyStateGroup != null) _emptyStateGroup.SetActive(files.Length == 0);

        foreach (string path in files)
        {
            // ponytail: loads each photo at full screenshot resolution with no downscaling or
            // on-disk thumbnail cache — fine for a handful of selfies, but would want real
            // thumbnail generation if photo counts grow large enough for grid load time/memory
            // to become noticeable.
            Texture2D texture = LoadTexture(path);
            if (texture == null) continue;

            _loadedTextures.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));

            GameObject thumbnail = Instantiate(_thumbnailPrefab, _gridContent);
            _thumbnails.Add(thumbnail);

            Image image = thumbnail.GetComponent<Image>();
            if (image != null) image.sprite = sprite;
            else Debug.LogWarning("[GalleryManager] _thumbnailPrefab has no Image component on its root.");

            Button button = thumbnail.GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(() => ShowFullscreen(sprite));
            else
                Debug.LogWarning("[GalleryManager] _thumbnailPrefab has no Button component on its root.");
        }
    }

    private void ShowFullscreen(Sprite sprite)
    {
        if (_fullscreenImage != null) _fullscreenImage.sprite = sprite;
        if (_fullscreenViewer != null) _fullscreenViewer.SetActive(true);
    }

    private void ClearGrid()
    {
        foreach (GameObject thumbnail in _thumbnails)
            if (thumbnail != null) Destroy(thumbnail);
        _thumbnails.Clear();

        // The Sprites created from these get cleaned up once nothing references them, but the
        // underlying Texture2D pixel data isn't freed until the texture itself is destroyed —
        // hence tracking and destroying these explicitly rather than leaving it to Sprite's GC.
        foreach (Texture2D texture in _loadedTextures)
            if (texture != null) Destroy(texture);
        _loadedTextures.Clear();
    }

    private static Texture2D LoadTexture(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GalleryManager] Failed to read '{path}': {e.Message}");
            return null;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            Debug.LogWarning($"[GalleryManager] Failed to decode image at '{path}'.");
            Destroy(texture);
            return null;
        }

        return texture;
    }
}
