using System.Numerics;
using Content.Shared._Carmine.OccluderSmooth;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Enumerators;

namespace Content.Client._Carmine.OccluderSmooth
{
    /// <summary>
    /// .2 | 2026
    /// Handles connecting wall occluders to one another for cooler occlusion, similar to IconSmoothSystem.cs
    /// Please read the big comment before porting this to your codebase, otherwise your walls will likely not work.
    /// This file is licensed under MIT. Feel free to do whatever.
    /// </summary>
    public sealed partial class OccluderSmoothSystem : EntitySystem
    {
        /*
        FOR PORTING / IMPLEMENTING:
        This ONLY works if your walls have a black "inner" part. Like full black. This is because the occluders never actually connect, so if you DON'T have that part
        full black, you will see a lot of black artifacts.

        STEP 0: EDITING YOUR SPRITES TO FIT THIS
        - your "." / SOLO state must have a rectangle that is fully black.
        - your "-" / "L" / "T" / "+"  state must extend that rectangle's sides NORTH, EAST, SOUTH, WEST to fit those shapes. Imagine you have two rectangles overlapping,
          and one rectangle extends NORTH/SOUTH, the other rectangle extends EAST/WEST. That's basically how OccluderSmooth works.

        STEP 1: DETERMINING BASE SOLO WALL OCCLUDERS
        - spawn a wall ingame with NO NEIGHBORS, ViewVariables it, go to Client Components -> OccluderComponent, and modify the bounding box until it matches the black part.
        - note down these measurements and enter them into where you find "IMPLEMENTATION STEP 1: FILL THIS IN!" later in the code

        STEP 2: NUKING OCCLUDERCOMP FROM BASE WALL
        - go to BaseWall, remove OccluderComponent and add OccluderSmoothComponent. That's it.
        - this repo made BaseWallOccluderSmooth because we still have some wall sprites that need fixing, but ideally you make all wall sprites fit this system and modify BaseWall.

        STEP 3: MODIFYING YOUR AIRLOCKS
        - go to BaseAirlock, remove OccluderComponent and add OccluderSmoothComponent. That's it.

        and you're done!

        FOR DEVELOPERS / UNDERSTANDING WHAT THIS DOES:
        All this code is very much based on IconSmooth's logic with a few additional things to accomodate rotation.

        Every OccluderSmooth wall has two entities assigned as children, "OccluderSmoothAlpha" and "OccluderSmoothBeta".
        Alpha extends to connect to NORTH/SOUTH neighbors.
        Beta extends to connect to EAST/WEST neighbors.
        This is neccessary to form T/L/+ shapes, otherwise I'd have only used one occluder.
        */
        [Dependency] private TransformSystem _transform = default!;
        [Dependency] private MapSystem _mapSystem = default!;
        [Dependency] private IEyeManager _eyeManager = default!;
        [Dependency] private OccluderSystem _occluderSystem = default!;
        [Dependency] private IEntityManager _entityManager = default!;

        private readonly Queue<EntityUid> _dirtyEntities = new();
        private readonly Queue<EntityUid> _anchorChangedEntities = new();

        private int _generation;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<OccluderSmoothComponent, AnchorStateChangedEvent>(OnAnchorChanged);
            SubscribeLocalEvent<OccluderSmoothComponent, ComponentStartup>(OnStartup);
            SubscribeLocalEvent<OccluderSmoothComponent, ComponentShutdown>(OnShutdown);
        }

        public override void FrameUpdate(float frameTime)
        {
            base.FrameUpdate(frameTime);

            var xformQuery = GetEntityQuery<TransformComponent>();
            var smoothQuery = GetEntityQuery<OccluderSmoothComponent>();

            // first process anchor state changes.
            while (_anchorChangedEntities.TryDequeue(out var uid))
            {
                if (!xformQuery.TryGetComponent(uid, out var xform))
                    continue;

                if (xform.MapID == MapId.Nullspace)
                {
                    // in null-space. Almost certainly because it left PVS. If something ever gets sent to null-space
                    // for reasons other than this (or entity deletion), then maybe we still need to update ex-neighbor
                    // smoothing here.
                    continue;
                }

                DirtyNeighbours(uid, comp: null, xform, smoothQuery);
            }


            var clientAngle = _eyeManager.CurrentEye.Rotation;
            // then, check if any existing rotations are over the threshold for dirtying.
            var smoothQ = EntityQueryEnumerator<OccluderSmoothComponent>();
            // TODO: MAKE THIS NOT CARE ABOUT ENTITIES IN SPAWNMENU
            while (smoothQ.MoveNext(out var uid, out var comp))
            {
                //Logger.Info("DIFFERENCE:" + (Math.Abs((clientAngle + _transform.GetWorldRotation(uid)).Reduced().FlipPositive() - comp.Rotation)).ToString());
                //TODO: you can do bigger (less checks) than PI / 8, but PI / 2 exactly makes it inconsistent. someone whose smarter than me can optimize this i guess
                if (Math.Abs((clientAngle + _transform.GetWorldRotation(uid) - Transform(uid).LocalRotation).Reduced().FlipPositive() - comp.Rotation) >= Math.PI / 8)
                {
                    _dirtyEntities.Enqueue(uid);
                }
            }

            // Next, update actual occluders.
            if (_dirtyEntities.Count == 0)
                return;

            _generation += 1;

            // Performance: This could be spread over multiple updates, or made parallel.
            while (_dirtyEntities.TryDequeue(out var uid))
            {
                UpdateOccluderState(uid, smoothQuery, xformQuery);
            }
        }
        private void OnStartup(EntityUid uid, OccluderSmoothComponent component, ComponentStartup args)
        {
            if (!component.Transparent)
            {
                var occluderAlpha = Spawn("OccluderSmoothOccluderAlpha", Transform(uid).Coordinates);
                component.OccluderAlpha = occluderAlpha;
                var occluderBeta = Spawn("OccluderSmoothOccluderBeta", Transform(uid).Coordinates);
                component.OccluderBeta = occluderBeta;
            }

            //set angle
            var angle = _eyeManager.CurrentEye.Rotation + _transform.GetWorldRotation(uid) - Transform(uid).LocalRotation;
            angle = angle.Reduced().FlipPositive();
            component.Rotation = angle;

            // dirty all neighbors cuz we just spawned a new wall
            var xform = Transform(uid);
            if (xform.Anchored)
            {
                component.LastPosition = TryComp<MapGridComponent>(xform.GridUid, out var grid)
                    ? (xform.GridUid.Value, _mapSystem.TileIndicesFor(xform.GridUid.Value, grid, xform.Coordinates))
                    : (null, new Vector2i(0, 0));

                DirtyNeighbours(uid, component);
            }
        }


        /// <summary>
        /// on shutdown, dirty all neighbors so they can update us missing
        /// </summary>
        private void OnShutdown(EntityUid uid, OccluderSmoothComponent component, ComponentShutdown args)
        {
            _entityManager.DeleteEntity(component.OccluderAlpha);
            _entityManager.DeleteEntity(component.OccluderBeta);
            _dirtyEntities.Enqueue(uid);
            DirtyNeighbours(uid, component);
        }

        private void OnAnchorChanged(EntityUid uid, OccluderSmoothComponent component, ref AnchorStateChangedEvent args)
        {
            if (!args.Detaching)
                _anchorChangedEntities.Enqueue(uid);
        }



        //inherited from iconsmooth
        public void DirtyNeighbours(EntityUid uid, OccluderSmoothComponent? comp = null, TransformComponent? transform = null, EntityQuery<OccluderSmoothComponent>? smoothQuery = null)
        {
            smoothQuery ??= GetEntityQuery<OccluderSmoothComponent>();
            if (!smoothQuery.Value.Resolve(uid, ref comp) || !comp.Running)
                return;

            _dirtyEntities.Enqueue(uid);

            if (!Resolve(uid, ref transform))
                return;

            Vector2i pos;

            EntityUid entityUid;

            if (transform.Anchored && TryComp<MapGridComponent>(transform.GridUid, out var grid))
            {
                entityUid = transform.GridUid.Value;
                pos = _mapSystem.CoordinatesToTile(transform.GridUid.Value, grid, transform.Coordinates);
            }
            else
            {
                // Entity is no longer valid, update around the last position it was at.
                if (comp.LastPosition is not (EntityUid gridId, Vector2i oldPos))
                    return;

                if (!TryComp(gridId, out grid))
                    return;

                entityUid = gridId;
                pos = oldPos;
            }

            // Yes, we updates ALL smoothing entities surrounding us even if they would never smooth with us.
            //cardinals
            DirtyEntities(_mapSystem.GetAnchoredEntitiesEnumerator(entityUid, grid, pos + new Vector2i(1, 0)));
            DirtyEntities(_mapSystem.GetAnchoredEntitiesEnumerator(entityUid, grid, pos + new Vector2i(-1, 0)));
            DirtyEntities(_mapSystem.GetAnchoredEntitiesEnumerator(entityUid, grid, pos + new Vector2i(0, 1)));
            DirtyEntities(_mapSystem.GetAnchoredEntitiesEnumerator(entityUid, grid, pos + new Vector2i(0, -1)));
            // //diagonals //might not be needed
            // DirtyEntities(_mapSystem.GetAnchoredEntitiesEnumerator(entityUid, grid, pos + new Vector2i(1, 1)));
            // DirtyEntities(_mapSystem.GetAnchoredEntitiesEnumerator(entityUid, grid, pos + new Vector2i(-1, -1)));
            // DirtyEntities(_mapSystem.GetAnchoredEntitiesEnumerator(entityUid, grid, pos + new Vector2i(-1, 1)));
            // DirtyEntities(_mapSystem.GetAnchoredEntitiesEnumerator(entityUid, grid, pos + new Vector2i(1, -1)));
        }

        //inherited from iconsmooth
        private void DirtyEntities(AnchoredEntitiesEnumerator entities)
        {
            // Instead of doing HasComp -> Enqueue -> TryGetComp, we will just enqueue all entities. Generally when
            // dealing with walls neighboring anchored entities will also be walls, and in those instances that will
            // require one less component fetch/check.
            while (entities.MoveNext(out var entity))
            {
                _dirtyEntities.Enqueue(entity.Value);
            }
        }


        /// <summary>
        /// Takes a specific OccluderSmoothed entity and recalculates its occluders.
        /// </summary>
        private void UpdateOccluderState(EntityUid uid,
            EntityQuery<OccluderSmoothComponent> smoothQuery,
            EntityQuery<TransformComponent> xformQuery,
            OccluderSmoothComponent? smooth = null)
        {
            TransformComponent? xform;
            Entity<MapGridComponent>? gridEntity = null;

            // INHERITED FROM ICONSMOOTH
            // The generation check prevents updating an entity multiple times per tick.
            // As it stands now, it's totally possible for something to get queued twice.
            // Generation on the component is set after an update so we can cull updates that happened this generation.
            if (!smoothQuery.Resolve(uid, ref smooth, false)
                || smooth.UpdateGeneration == _generation
                || !smooth.Enabled
                || !smooth.Running
                || smooth.Transparent)
            {
                return;
            }

            xform = xformQuery.GetComponent(uid);
            smooth.UpdateGeneration = _generation;

            if (xform.Anchored)
            {
                if (TryComp(xform.GridUid, out MapGridComponent? mapGridComp))
                {
                    gridEntity = (xform.GridUid.Value, mapGridComp);
                }
                else
                {
                    Log.Error($"Failed to calculate OccluderSmoothComponent sprite in {uid} because grid {xform.GridUid} was missing.");
                    return;
                }
            }

            if (gridEntity == null)
            {
                // entity was called to update but was not anchored - inherited from iconsmooth but should never happen
                return;
            }

            var gridUid = gridEntity.Value.Owner;
            var grid = gridEntity.Value.Comp;

            // STEP 1: GRAB ALL CONNECTIONS AROUND US AND PUT THEM INTO smooth.Connections
            var pos = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
            smooth.Connections = OccluderSmoothComponent.WallConnections.None;
            if (MatchingEntity(_mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(Direction.North)), smoothQuery))
                smooth.Connections |= OccluderSmoothComponent.WallConnections.North;
            if (MatchingEntity(_mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(Direction.East)), smoothQuery))
                smooth.Connections |= OccluderSmoothComponent.WallConnections.East;
            if (MatchingEntity(_mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(Direction.South)), smoothQuery))
                smooth.Connections |= OccluderSmoothComponent.WallConnections.South;
            if (MatchingEntity(_mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(Direction.West)), smoothQuery))
                smooth.Connections |= OccluderSmoothComponent.WallConnections.West;

            // STEP 2: GRAB TRUE CLIENT ROTATION OF THE WALL - WHICH IS SpriteSystem.Render.cs LINES 49-50
            // var angle = worldRotation + eyeRotation; // angle on-screen. Used to decide the direction of 4/8 directional RSIs
            // angle = angle.Reduced().FlipPositive();  // Reduce the angles to fix math shenanigans
            // TOOK WAYY TOO LONG TO FIND THIS .2 | 2026

            // localrotation needed to account for airlocks rotating
            var angle = _eyeManager.CurrentEye.Rotation + _transform.GetWorldRotation(uid) - Transform(uid).LocalRotation;
            angle = angle.Reduced().FlipPositive();

            smooth.Rotation = angle;

            // step 1: use smooth.Rotation to determine the rotation of the needed occluder set (SOUTH is base rotation)
            // step 2: use step1 + smooth.Connections to determine what occluder shape we need
            // step 3: apply bounding box changes to occluder + occluderchild

            var dir = angle.ToRsiDirection(RsiDirectionType.Dir4);

            var bounds = DetermineBounds(dir, smooth.Connections);

            if (smooth.OccluderAlpha == null || smooth.OccluderBeta == null)
                throw new Exception($"Entity {uid} had an OccluderSmoothComponent with null occluder children - should be OccluderSmoothAlpha and OccluderSmoothBeta.");

            // _occluderSystem.SetBoundingBox(smooth.OccluderAlpha.Value, bounds.Alpha);
            // _occluderSystem.SetBoundingBox(smooth.OccluderBeta.Value, bounds.Beta);

            _occluderSystem.SetPolygon(smooth.OccluderAlpha.Value,
            [
                bounds.Alpha.TopLeft,
                bounds.Alpha.TopRight,
                bounds.Alpha.BottomRight,
                bounds.Alpha.BottomLeft,
            ]);
            _occluderSystem.SetPolygon(smooth.OccluderBeta.Value,
            [
                bounds.Beta.TopLeft,
                bounds.Beta.TopRight,
                bounds.Beta.BottomRight,
                bounds.Beta.BottomLeft,
            ]);
        }

        /// <summary>
        /// Helper function that determines if "candidates" should smooth with a wall.
        /// Inherited from IconSmooth.
        /// </summary>
        private bool MatchingEntity(AnchoredEntitiesEnumerator candidates, EntityQuery<OccluderSmoothComponent> smoothQuery)
        {
            while (candidates.MoveNext(out var entity))
            {
                if (smoothQuery.TryGetComponent(entity, out var other) && other.Enabled)
                {
                    return true;
                }
            }
            return false;
        }
        public struct BoundingBoxPair
        {
            public Box2 Alpha;
            public Box2 Beta;
        };

        private BoundingBoxPair DetermineBounds(RsiDirection dir, OccluderSmoothComponent.WallConnections connections)
        {
            // NEIGHBOR HANDLING
            // THIS IS ROTATED LATER, BUT THESE SHOULD ALL BE FOR A WALL THAT IS POINTING "SOUTH" / NOT ROTATED AT ALL

            // EXPLANATION:
            /*
            OK SO
            there are 8 neighbors and they could be present or not present. so 2 possibilities.
            2^8 possibilities = 256 possibilities
            extremely inefficient to define them all here. instead we go smarter

            instead, if you have a neighbor EAST
            you expand your wall to connect to it, so BETA.RIGHT = 0.5
            (same for NORTH/SOUTH/EAST/WEST. ALPHA - NORTHSOUTH. BETA - EASTWEST)

            and thats all you need

            also you need to rotate neighbors based on screen rotation
            .2 | 2026
            */

            OccluderSmoothComponent.WallConnections rotatedConnections = RotateConnections(connections, dir);
            /* BOUNDING BOX FOR A SOLO WALL
            ---
            -#-
            ---
            */
            BoundingBoxPair box = new BoundingBoxPair
            {
                Alpha = new(-0.13f, 0.31f, 0.13f, 0.44f), //IMPLEMENTATION STEP 1: FILL THIS IN!
                Beta = new(-0.13f, 0.31f, 0.13f, 0.44f) //IMPLEMENTATION STEP 1: FILL THIS IN! (should be the same as alpha)
            };

            if (rotatedConnections.HasFlag(OccluderSmoothComponent.WallConnections.North))
                box.Alpha.Top = 0.5f;

            if (rotatedConnections.HasFlag(OccluderSmoothComponent.WallConnections.South))
                box.Alpha.Bottom = -0.5f;

            if (rotatedConnections.HasFlag(OccluderSmoothComponent.WallConnections.East))
                box.Beta.Right = 0.5f;

            if (rotatedConnections.HasFlag(OccluderSmoothComponent.WallConnections.West))
                box.Beta.Left = -0.5f;

            // ROTATION HANDLING

            // EXPLANATION FOR HOW WE HANDLE ROTATIONS
            /*
            we utilize 2 occluders, Alpha and Beta, because we need to do L, T, and + shapes.

            box2's are LEFT, BOTTOM, RIGHT, TOP in order
            0,0,0,0 is dead middle of a tile.
            for generic spation tiles, you have -0.5, -0.5, 0.5, 0.5 to make a full tile covered by an occluder.
            carmine's walls are, on a 1x1 wall with no neighbors, -0.25, 0.15, 0.25, 0.5

            the math here should hold for all types of walls to rotate though,
            since afaik it's, if you rotate a coordinate by θ,

            x' =  x·cos(θ) - y·sin(θ)
            y' =  x·sin(θ) + y·cos(θ)

            EX:

            SOUTH:
            -0.25, 0.15, 0.25, 0.5
            becomes
            WEST:
            -0.5, -0.25, 0.15, 0.25

            good luck .2 | 2026
            */
            // Then just:

            box.Alpha = RotateBox(box.Alpha, dir);
            box.Beta = RotateBox(box.Beta, dir);

            return box;
        }
        Box2 RotateBox(Box2 b, RsiDirection dir) => dir switch
        {
            RsiDirection.South => b,
            RsiDirection.West  => new Box2(-b.Top,   b.Left,  -b.Bottom, b.Right),
            RsiDirection.North => new Box2(-b.Right, -b.Top,  -b.Left,   -b.Bottom),
            RsiDirection.East  => new Box2(b.Bottom,   b.Left,  b.Top, b.Right),
            _                  => throw new ArgumentOutOfRangeException(nameof(dir))
        };
        OccluderSmoothComponent.WallConnections RotateConnections(OccluderSmoothComponent.WallConnections connections, RsiDirection dir)
        {
            var c = connections;
            return dir switch
            {
                RsiDirection.South => c,
                RsiDirection.West  => (c.HasFlag(OccluderSmoothComponent.WallConnections.North) ? OccluderSmoothComponent.WallConnections.East  : OccluderSmoothComponent.WallConnections.None)
                                    | (c.HasFlag(OccluderSmoothComponent.WallConnections.East)  ? OccluderSmoothComponent.WallConnections.South : OccluderSmoothComponent.WallConnections.None)
                                    | (c.HasFlag(OccluderSmoothComponent.WallConnections.South) ? OccluderSmoothComponent.WallConnections.West  : OccluderSmoothComponent.WallConnections.None)
                                    | (c.HasFlag(OccluderSmoothComponent.WallConnections.West)  ? OccluderSmoothComponent.WallConnections.North : OccluderSmoothComponent.WallConnections.None),
                RsiDirection.North => (c.HasFlag(OccluderSmoothComponent.WallConnections.North) ? OccluderSmoothComponent.WallConnections.South : OccluderSmoothComponent.WallConnections.None)
                                    | (c.HasFlag(OccluderSmoothComponent.WallConnections.East)  ? OccluderSmoothComponent.WallConnections.West  : OccluderSmoothComponent.WallConnections.None)
                                    | (c.HasFlag(OccluderSmoothComponent.WallConnections.South) ? OccluderSmoothComponent.WallConnections.North : OccluderSmoothComponent.WallConnections.None)
                                    | (c.HasFlag(OccluderSmoothComponent.WallConnections.West)  ? OccluderSmoothComponent.WallConnections.East  : OccluderSmoothComponent.WallConnections.None),
                RsiDirection.East  => (c.HasFlag(OccluderSmoothComponent.WallConnections.North) ? OccluderSmoothComponent.WallConnections.East  : OccluderSmoothComponent.WallConnections.None)
                                    | (c.HasFlag(OccluderSmoothComponent.WallConnections.East)  ? OccluderSmoothComponent.WallConnections.North : OccluderSmoothComponent.WallConnections.None)
                                    | (c.HasFlag(OccluderSmoothComponent.WallConnections.South) ? OccluderSmoothComponent.WallConnections.West  : OccluderSmoothComponent.WallConnections.None)
                                    | (c.HasFlag(OccluderSmoothComponent.WallConnections.West)  ? OccluderSmoothComponent.WallConnections.South : OccluderSmoothComponent.WallConnections.None),
                _ => throw new ArgumentOutOfRangeException(nameof(dir))
            };
        }
    }
}
