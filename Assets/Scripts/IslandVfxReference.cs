using UnityEngine;

/// <summary>
/// Attach directly to an island prefab and drag the correct child references (ParticleSystem,
/// Animator, etc.) into the fields below while editing the prefab. Lets code that spawns the
/// island (e.g. Page4ARTracker, Page6SpeechController) look these up at runtime without guessing
/// by name or hierarchy order — useful since island prefabs can contain several nested objects.
/// </summary>
public class IslandVfxReference : MonoBehaviour
{
    [SerializeField] private ParticleSystem _glowVfx;
    [Tooltip("Optional: the Water Spirit's Animator, if this island has one.")]
    [SerializeField] private Animator _waterSpiritAnimator;

    public ParticleSystem GlowVfx => _glowVfx;
    public Animator WaterSpiritAnimator => _waterSpiritAnimator;
}
