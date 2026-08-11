using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class SpeakerButton : MonoBehaviour
{
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _clip;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();

        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        _button.onClick.AddListener(PlayAudio);
    }

    private void OnDisable()
    {
        _button.onClick.RemoveListener(PlayAudio);
    }

    public void PlayAudio()
    {
        if (_audioSource == null || _clip == null)
        {
            Debug.LogWarning("[SpeakerButton] AudioSource or clip not assigned.");
            return;
        }

        // Ignore spammed presses while the clip is still playing — only a fresh press after it
        // finishes is allowed to replay it.
        if (_audioSource.isPlaying) return;

        _audioSource.clip = _clip;
        _audioSource.Play();
    }
}
