using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Noosphere.Spawners
{
    [AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
    public sealed partial class MappingSpawnersCommand : LocalizedEntityCommands
    {
        [Dependency] private IEntityManager _entityManager = default!;
        [Dependency] private MappingSpawnerSystem _mappingSpawnerSystem = default!;

        public override string Command => "mappingspawners";

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            if (args.Length == 1)
                return CompletionResult.FromHintOptions(["false", "true"], "Delete spawners?");
            return CompletionResult.Empty;
        }

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var delete = true;
            if (args.Length > 0)
                delete = bool.Parse(args[0]);

            var entQuery = _entityManager.EntityQuery<MappingSpawnerComponent>(true);

            foreach (var ent in entQuery)
            {
                _mappingSpawnerSystem.SpawnEntitys(ent, delete);
            }

        }
    }
}
