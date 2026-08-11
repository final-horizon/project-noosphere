using Content.Server.Administration;
using Content.Shared._CMU14.ZLevels.Core.Components;
using Content.Shared._CMU14.ZLevels.Weather;
using Content.Shared.Administration;
using Content.Shared.Weather;
using NetCord.Gateway.Voice;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Prototypes;

namespace Content.Server._CMU14.ZLevels.Weather;

[AdminCommand(AdminFlags.Fun)]
public sealed partial class CMUWeatherCommand : LocalizedCommands
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IComponentFactory _compFactory = default!;

    public override string Command => "znetwork-weather";
    public override string Description => "Sets weather for all maps in zNetwork";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Loc.GetString("cmd-weather-error-no-arguments"));
            return;
        }

        // get the target
        EntityUid? target;

        if (!NetEntity.TryParse(args[0], out var targetNet) ||
            !_entities.TryGetEntity(targetNet, out target))
        {
            shell.WriteError($"Unable to find entity {args[0]}");
            return;
        }

        if (!_entities.TryGetComponent<CMUZLevelsNetworkComponent>(target, out var levelComp))
        {
            shell.WriteError($"Target entity doesnt have CMUZLevelsNetworkComponent {args[0]}");
            return;
        }

        //Weather Proto parsing
        EntProtoId weatherProto = args[1];
        if (!_proto.TryIndex(weatherProto, out _))
        {
            shell.WriteError(Loc.GetString("cmd-weather-error-unknown-proto"));
            return;
        }

        //Time parsing
        TimeSpan? endTime = null;
        if (args.Length == 3)
        {
            var curTime = _timing.CurTime;
            if (int.TryParse(args[2], out var durationInt))
            {
                endTime = curTime + TimeSpan.FromSeconds(durationInt);
            }
            else
            {
                shell.WriteError(Loc.GetString("cmd-weather-error-wrong-time"));
                return;
            }
        }

        _entities.System<CMUWeatherSystem>().SetWeather((target.Value, levelComp), weatherProto, endTime);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = new List<CompletionOption>();
            var query = _entities.EntityQueryEnumerator<CMUZLevelsNetworkComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out var meta))
            {
                options.Add(new CompletionOption(_entities.GetNetEntity(uid).ToString(), meta.EntityName));
            }
            return CompletionResult.FromHintOptions(options, "zNetwork net entity");
        }

        if (args.Length == 2)
        {
            var options = new List<CompletionOption>();
            foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
            {
                if (!proto.HasComponent<WeatherStatusEffectComponent>(_compFactory))
                    continue;

                options.Add(new CompletionOption(proto.ID, proto.Name));
            }

            options.Add(new CompletionOption("null", Loc.GetString("cmd-weather-null")));
            return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-weather-hint"));
        }

        if (args.Length == 3)
        {
            return CompletionResult.FromHint("Duration in seconds");
        }

        return CompletionResult.Empty;
    }
}
