﻿using Content.Shared._RMC14.Pulling;
using Content.Shared._RMC14.Shields;
using Content.Shared._RMC14.Weapons.Melee;
using Content.Shared.Coordinates;
using Content.Shared.Movement.Systems;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._RMC14.Xenonids.Cleave;

public sealed class XenoCleaveSystem : EntitySystem
{
    [Dependency] private readonly XenoSystem _xeno = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly VanguardShieldSystem _vanguard = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly RMCPullingSystem _rmcPulling = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedRMCMeleeWeaponSystem _rmcMelee = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<XenoCleaveComponent, XenoCleaveActionEvent>(OnCleaveAction);

        SubscribeLocalEvent<CleaveRootedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshCleaveRooted);
        SubscribeLocalEvent<CleaveRootedComponent, ComponentRemove>(OnCleaveRootedRemoved);
    }

    private void OnCleaveAction(Entity<XenoCleaveComponent> xeno, ref XenoCleaveActionEvent args)
    {
        if (!_xeno.CanAbilityAttackTarget(xeno, args.Target))
            return;

        if (args.Handled)
            return;

        if (_net.IsServer)
            _audio.PlayPvs(xeno.Comp.Sound, xeno);

        var buffed = _vanguard.ShieldBuff(xeno);

        args.Handled = true;

        _rmcMelee.DoLunge(xeno, args.Target);

        if (args.Flings)
        {
            var flingRange = buffed ? xeno.Comp.FlingDistanceBuffed : xeno.Comp.FlingDistance;
            _rmcPulling.TryStopAllPullsFromAndOn(args.Target);

            //From fling
            var origin = _transform.GetMapCoordinates(xeno);
            var target = _transform.GetMapCoordinates(args.Target);
            var diff = target.Position - origin.Position;
            diff = diff.Normalized() * flingRange;

            if (_net.IsServer)
            {
                _throwing.TryThrow(args.Target, diff, 10);

                SpawnAttachedTo(xeno.Comp.FlingEffect, args.Target.ToCoordinates());
            }
        }
        else
        {
            var rootTime = buffed ? xeno.Comp.RootTimeBuffed : xeno.Comp.RootTime;
            var root = EnsureComp<CleaveRootedComponent>(args.Target);
            root.ExpiresAt = _timing.CurTime + rootTime;
            _speed.RefreshMovementSpeedModifiers(args.Target);

            if (_net.IsServer)
            {
                SpawnAttachedTo(xeno.Comp.RootEffect, args.Target.ToCoordinates());
                SpawnAttachedTo(buffed ? xeno.Comp.RootStatusEffectBuffed : xeno.Comp.RootStatusEffect, args.Target.ToCoordinates());
            }
        }
    }
    private void OnRefreshCleaveRooted(Entity<CleaveRootedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(0, 0);
    }

    private void OnCleaveRootedRemoved(Entity<CleaveRootedComponent> ent, ref ComponentRemove args)
    {
        if(!TerminatingOrDeleted(ent))
            _speed.RefreshMovementSpeedModifiers(ent);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;

        var rooted = EntityQueryEnumerator<CleaveRootedComponent>();

        while (rooted.MoveNext(out var uid, out var root))
        {
            if (root.ExpiresAt > time)
                continue;

            RemCompDeferred<CleaveRootedComponent>(uid);
        }
    }
}
