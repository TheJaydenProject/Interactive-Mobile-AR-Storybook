using UnityEngine;

/// <summary>
/// Attach directly to Page 12's island prefab and drag the Phoenix's child references into the
/// fields below while editing the prefab. Lets Page12ARTracker look these up at runtime without
/// guessing by name or hierarchy order, once the island is instantiated.
///
/// Trimmed down from Page11IslandReference — Page 12's Phoenix is a decorative preview only (no
/// draggable spark orbs, no drop-target collider to hit-test against), so only the pieces needed
/// for the rise + rainbow-cycle visuals are exposed here.
/// </summary>
public class Page12IslandReference : MonoBehaviour
{
    [Header("Phoenix")]
    [Tooltip("Plays continuously (looping) once the Phoenix rises — not a one-shot reward VFX like the other islands'.")]
    [SerializeField] private ParticleSystem _phoenixVfx;
    [SerializeField] private Transform _phoenixTransform;
    [SerializeField] private Renderer _phoenixRenderer;

    public ParticleSystem PhoenixVfx => _phoenixVfx;
    public Transform PhoenixTransform => _phoenixTransform;
    public Renderer PhoenixRenderer => _phoenixRenderer;
}
