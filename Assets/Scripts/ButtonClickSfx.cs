using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Auto-wires a click SFX onto every Button in the scene at startup, instead of hand-adding a
/// PlayOneShot call to every OnClick() method across every script — most of this app's buttons
/// (instruction prompts, Try It On, Back To Menu, etc.) start inactive, so this searches inactive
/// objects too. Add one of these per scene (Ar1, Ar2) since Buttons don't carry across the full
/// process restart between them.
///
/// Doesn't cover Buttons instantiated at runtime — there aren't any in this codebase; every UI
/// panel is a fixed scene object toggled via SetActive, not spawned.
/// </summary>
public class ButtonClickSfx : MonoBehaviour
{
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _clickClip;

    [Tooltip("Buttons to skip — e.g. the Ar2 shutter button, which already has its own dedicated capture SFX and shouldn't also play a generic click.")]
    [SerializeField] private Button[] _excludedButtons;

    private void Start()
    {
        if (_audioSource == null || _clickClip == null)
        {
            Debug.LogWarning("[ButtonClickSfx] _audioSource or _clickClip not assigned — no click SFX will play.");
            return;
        }

        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include);
        foreach (Button button in buttons)
        {
            if (IsExcluded(button)) continue;
            button.onClick.AddListener(PlayClickSfx);
        }
    }

    private bool IsExcluded(Button button)
    {
        if (_excludedButtons == null) return false;

        foreach (Button excluded in _excludedButtons)
            if (excluded == button) return true;

        return false;
    }

    private void PlayClickSfx()
    {
        _audioSource.PlayOneShot(_clickClip);
    }
}
