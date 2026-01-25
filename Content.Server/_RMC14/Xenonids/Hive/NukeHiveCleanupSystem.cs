using Content.Server.Nuke;
using Content.Shared._RMC14.Xenonids.Construction.EggMorpher;
using Content.Shared._RMC14.Xenonids.ManageHive.Boons;
using Robust.Shared.Map.Components;

namespace Content.Server._RMC14.Xenonids.Hive;

public sealed class NukeHiveCleanupSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NukeExplodedEvent>(OnNukeExploded);
    }

    private void OnNukeExploded(NukeExplodedEvent ev)
    {
        if (ev.OwningStation == null)
            return;

        if (!TryComp(ev.OwningStation.Value, out TransformComponent? gridXform))
            return;

        var mapId = gridXform.MapID;

        var eggMorphers = EntityQueryEnumerator<EggMorpherComponent,
            TransformComponent>();
        while (eggMorphers.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            Del(uid);
        }

        var cocoons = EntityQueryEnumerator<HiveKingCocoonComponent,
            TransformComponent>();
        while (cocoons.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            Del(uid);
        }
    }
}
