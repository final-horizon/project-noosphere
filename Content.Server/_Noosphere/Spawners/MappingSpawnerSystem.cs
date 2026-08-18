using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Shared.EntityTable;
using Robust.Server.GameObjects;
using Robust.Shared.Random;

namespace Content.Server._Noosphere.Spawners;

public sealed partial class MappingSpawnerSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private EntityTableSystem _entityTable = default!;
    [Dependency] private TransformSystem _xform = default!;
    [Dependency] private RandomDecalSpawnerSystem _decalSpawner = default!;
    public override void Initialize()
    {
        base.Initialize();
    }

    public void SpawnEntitys(MappingSpawnerComponent comp, bool delete)
    {

        var xform = Transform(comp.Owner);
        var coords = _xform.GetMapCoordinates(comp.Owner, xform);
        var rotation = _xform.GetWorldRotation(xform);
        var offset = comp.Offset;

        var spawns = _entityTable.GetSpawns(comp.Table);
        foreach (var proto in spawns)
        {
            var vOffset = _robustRandom.NextVector2(-offset, offset);
            var trueCoords = coords.Offset(vOffset);

            var spawnedEnt = Spawn(proto, trueCoords, rotation: rotation);
            if (HasComp<RandomDecalSpawnerComponent>(spawnedEnt))
            {
                _decalSpawner.TrySpawn(spawnedEnt);
                QueueDel(spawnedEnt);
            }
        }
        if (delete)
            QueueDel(comp.Owner);
    }
}
