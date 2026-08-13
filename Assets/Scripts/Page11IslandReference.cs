using UnityEngine;

/// <summary>
/// Attach directly to Page 11's island prefab and drag the correct child references into the
/// fields below while editing the prefab. Lets Page11ARTracker look these up at runtime without
/// guessing by name or hierarchy order, once the island is instantiated.
///
/// Kept as its own component instead of extending IslandVfxReference — Page 11's island carries
/// a different shape of content (draggable spark orbs, a continuously-looping Phoenix VFX, and
/// the Phoenix's own transform/renderer for the rise-and-recolor sequence) than the other pages'
/// islands, which just need a one-shot glow VFX and an optional water spirit animator.
/// </summary>
public class Page11IslandReference : MonoBehaviour
{
    [Header("Sparks")]
    [Tooltip("Assigned individually by colour (rather than a plain array) so the Inspector can't get the order mixed up with Page11DragController's Spark Names.")]
    [SerializeField] private Transform _blueSpark;
    [SerializeField] private Transform _redSpark;
    [SerializeField] private Transform _yellowSpark;
    [SerializeField] private Transform _goldSpark;

    [Header("Phoenix")]
    [Tooltip("Plays continuously (looping) once the Phoenix rises — not a one-shot reward VFX like the other islands'.")]
    [SerializeField] private ParticleSystem _phoenixVfx;
    [SerializeField] private Transform _phoenixTransform;
    [SerializeField] private Renderer _phoenixRenderer;
    [Tooltip("The Phoenix's drop-target collider, used by Page11DragController to detect a successful spark drop.")]
    [SerializeField] private Collider _phoenixCollider;

    // Order must match Page11DragController's _sparkNames = { "Blue", "Red", "Yellow", "Gold" }.
    public Transform[] Sparks => new[] { _blueSpark, _redSpark, _yellowSpark, _goldSpark };
    public ParticleSystem PhoenixVfx => _phoenixVfx;
    public Transform PhoenixTransform => _phoenixTransform;
    public Renderer PhoenixRenderer => _phoenixRenderer;
    public Collider PhoenixCollider => _phoenixCollider;
}
