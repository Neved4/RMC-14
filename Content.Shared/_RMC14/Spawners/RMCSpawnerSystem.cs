using Content.Shared._RMC14.Evacuation;
using Content.Shared._RMC14.Xenonids.Acid;
using Content.Shared._RMC14.Xenonids.Spray;
using Content.Shared.Coordinates;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._RMC14.Spawners;

public sealed class RMCSpawnerSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly EntityWhitelistSystem _entityWhitelist =
        default!;
    [Dependency] private readonly SharedEvacuationSystem _evacuation = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedXenoAcidSystem _xenoAcid = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpawnOnInteractComponent, InteractHandEvent>(
            OnSpawnOnInteractHand);
    }

    private void OnSpawnOnInteractHand(
        Entity<SpawnOnInteractComponent> ent,
        ref InteractHandEvent args)
    {
        if (_net.IsClient)
            return;

        var user = args.User;
        if (TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent))
            return;

        if (_entityWhitelist.IsBlacklistPass(ent.Comp.Blacklist, user))
            return;

        if (ent.Comp.RequireEvacuation && !_evacuation.IsEvacuationInProgress())
        {
            // TODO RMC14 code red or above
            _popup.PopupEntity(
                Loc.GetString("rmc-sentry-not-emergency", ("deployer", ent)),
                ent,
                user);
            return;
        }

        var spawned = SpawnAtPosition(
            ent.Comp.Spawn,
            ent.Owner.ToCoordinates());
        TransferAcid(ent.Owner, spawned);
        if (ent.Comp.Popup is { } popup)
            _popup.PopupEntity(
                Loc.GetString(popup, ("spawned", spawned)),
                ent,
                user);

        _audio.PlayPvs(ent.Comp.Sound, spawned);

        QueueDel(ent);
    }

    private void TransferAcid(EntityUid source, EntityUid target)
    {
        if (TryComp<TimedCorrodingComponent>(source, out var timed))
        {
            _xenoAcid.ApplyAcid(
                timed.AcidPrototype,
                timed.Strength,
                target,
                timed.Dps,
                timed.LightDps,
                timed.CorrodesAt,
                true);
            _xenoAcid.RemoveAcid(source);
        }

        if (TryComp<DamageableCorrodingComponent>(source, out var damageable))
        {
            RemComp<DamageableCorrodingComponent>(source);
            if (Exists(damageable.Acid))
                _transform.SetParent(damageable.Acid, target);

            var applied = EnsureComp<DamageableCorrodingComponent>(target);
            applied.Acid = damageable.Acid;
            applied.Dps = damageable.Dps;
            applied.Damage = damageable.Damage;
            applied.Strength = damageable.Strength;
            applied.NextDamageAt = damageable.NextDamageAt;
            applied.AcidExpiresAt = damageable.AcidExpiresAt;
            Dirty(target, applied);
        }

        if (TryComp<SprayAcidedComponent>(source, out var spray))
        {
            RemComp<SprayAcidedComponent>(source);
            var applied = EnsureComp<SprayAcidedComponent>(target);
            applied.Damage = spray.Damage;
            applied.ExpireAt = spray.ExpireAt;
            applied.NextDamageAt = spray.NextDamageAt;
            applied.DamageEvery = spray.DamageEvery;
            Dirty(target, applied);
        }
    }
}
