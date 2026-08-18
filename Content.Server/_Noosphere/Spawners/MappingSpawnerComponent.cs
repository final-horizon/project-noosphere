using Content.Shared.EntityTable.EntitySelectors;

namespace Content.Server._Noosphere.Spawners;

[RegisterComponent]
public sealed partial class MappingSpawnerComponent : Component
{
    /// <summary>
    /// Table that determines what gets spawned.
    /// </summary>
    [DataField(required: true)]
    public EntityTableSelector Table = default!;

    /// <summary>
    /// Scatter of entity spawn coordinates
    /// </summary>
    [DataField]
    public float Offset = 0.2f;
}

