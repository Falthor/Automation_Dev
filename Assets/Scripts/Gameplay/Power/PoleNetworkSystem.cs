using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Gameplay.Power
{
    /// <summary>
    /// The electric pole network (ENERGIE.md): which poles are connected, in what groups, and which
    /// of those groups currently reach a power source. Gates every power-consuming building's draw
    /// through <see cref="BuildingRuntime.PoleNetwork"/> - a building outside every fed pole's reach
    /// simply does not draw, whatever supply exists.
    ///
    /// <b>The connection rule, and it is the whole rule.</b> A pole placed within range of one or
    /// more existing poles connects to the *nearest pole of each distinct group* in range - never
    /// just the single nearest neighbour, which would leave a pole at the edge of a group unconnected
    /// to a third one also in range. One cable per distinct group found; zero cables (a fresh,
    /// isolated network of one) when none are.
    ///
    /// <b>Nothing about the graph is saved.</b> A pole's position is an ordinary building and is
    /// saved and restored like any other; the graph itself - which pole is in which group, which
    /// cables exist - is entirely a deterministic function of every pole's position and the order
    /// they were placed in. Replaying <see cref="RegisterPole"/> for each restored pole in save order
    /// (ConstructionService.CreateAndRegisterOccupant, the same chokepoint placement and restore both
    /// already go through) reconstructs the exact groups the session had, with nothing extra to
    /// persist - the same "what comes from the player is saved, what is derived is recomputed" rule
    /// WreckField's own seed-derived layout already follows.
    ///
    /// <b>A pole still under construction connects nothing and conducts nothing.</b> It occupies its
    /// cell immediately, like every chantier, but is excluded from every fed/coverage query until it
    /// is actually built - a half-built pole has no wire in it yet.
    /// </summary>
    public sealed class PoleNetworkSystem
    {
        readonly GridRuntime _grid;
        readonly PoleNetworkSettings _settings;

        readonly List<PoleRuntime> _poles = new List<PoleRuntime>();
        readonly List<(PoleRuntime A, PoleRuntime B)> _edges = new List<(PoleRuntime, PoleRuntime)>();
        readonly HashSet<int> _fedNetworks = new HashSet<int>();

        int _nextNetworkId;

        /// <summary>Every pole this system knows about, built or still under construction.</summary>
        public IReadOnlyList<PoleRuntime> Poles => _poles;

        /// <summary>Every cable, one per connection made at placement - what the view draws and what a removal walks to recompute groups.</summary>
        public IReadOnlyList<(PoleRuntime A, PoleRuntime B)> Edges => _edges;

        public int PowerRangeCells => _settings.PowerRangeCells;
        public int ConnectionRangeCells => _settings.ConnectionRangeCells;

        public PoleNetworkSystem(GridRuntime grid, PoleNetworkSettings settings)
        {
            _grid = grid;
            _settings = settings;
        }

        /// <summary>
        /// Registers a newly placed (or restored) pole and connects it to the nearest pole of every
        /// distinct group within range - see the class summary. Idempotent-unsafe by design: calling
        /// it twice for the same pole would double-connect it, so it is only ever called once, from
        /// ConstructionService.CreateAndRegisterOccupant.
        /// </summary>
        public void RegisterPole(PoleRuntime pole)
        {
            _poles.Add(pole);

            var nearestInGroup = new Dictionary<int, PoleRuntime>();
            var nearestDistance = new Dictionary<int, int>();

            foreach (PoleRuntime candidate in _poles)
            {
                if (ReferenceEquals(candidate, pole)) continue;

                int distance = ChebyshevDistance(candidate.Cell, pole.Cell);
                if (distance > ConnectionRangeCells) continue;

                if (!nearestDistance.TryGetValue(candidate.NetworkId, out int best) || distance < best)
                {
                    nearestDistance[candidate.NetworkId] = distance;
                    nearestInGroup[candidate.NetworkId] = candidate;
                }
            }

            if (nearestInGroup.Count == 0)
            {
                pole.NetworkId = _nextNetworkId++;
                return;
            }

            // The first group found becomes the surviving id; every other group found merges into
            // it, one cable per group - a pole bridging three separate networks fuses all three.
            var groupIds = new List<int>(nearestInGroup.Keys);
            int survivingId = groupIds[0];

            foreach (int groupId in groupIds)
            {
                _edges.Add((pole, nearestInGroup[groupId]));
                if (groupId != survivingId) Relabel(groupId, survivingId);
            }

            pole.NetworkId = survivingId;
        }

        void Relabel(int fromId, int toId)
        {
            foreach (PoleRuntime p in _poles)
            {
                if (p.NetworkId == fromId) p.NetworkId = toId;
            }
        }

        /// <summary>
        /// Removes a pole and every cable touching it, then recomputes every remaining pole's group
        /// from scratch by walking the remaining edges - the one real computation this system does
        /// (see the class summary). The graph is a tree by construction (one cable per group found,
        /// never a second between two poles already in the same one), so removing a pole from its
        /// middle genuinely splits it; recomputing fresh is simpler and just as cheap as detecting
        /// that case specially at the pole counts this game ever reaches.
        /// </summary>
        public void UnregisterPole(PoleRuntime pole)
        {
            _poles.Remove(pole);
            _edges.RemoveAll(edge => ReferenceEquals(edge.A, pole) || ReferenceEquals(edge.B, pole));
            RecomputeGroups();
        }

        void RecomputeGroups()
        {
            foreach (PoleRuntime p in _poles) p.NetworkId = -1;

            var adjacency = new Dictionary<PoleRuntime, List<PoleRuntime>>(_poles.Count);
            foreach (PoleRuntime p in _poles) adjacency[p] = new List<PoleRuntime>();
            foreach (var (a, b) in _edges)
            {
                adjacency[a].Add(b);
                adjacency[b].Add(a);
            }

            int nextId = 0;
            var stack = new Stack<PoleRuntime>();
            foreach (PoleRuntime start in _poles)
            {
                if (start.NetworkId != -1) continue;

                int id = nextId++;
                start.NetworkId = id;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    PoleRuntime current = stack.Pop();
                    foreach (PoleRuntime neighbour in adjacency[current])
                    {
                        if (neighbour.NetworkId != -1) continue;
                        neighbour.NetworkId = id;
                        stack.Push(neighbour);
                    }
                }
            }

            _nextNetworkId = nextId;
        }

        // ---- Fed state ----

        /// <summary>
        /// Recomputes which networks currently reach a source, once per frame - called from
        /// GameRuntime.Update() before Transport.Tick(), so every consumer's draw this frame reads a
        /// fresh answer rather than last frame's. Cheap at the pole counts this game ever reaches, and
        /// avoids every consumer's own gating check re-scanning the grid around every pole itself.
        /// </summary>
        public void Tick(float deltaTime)
        {
            _fedNetworks.Clear();

            foreach (PoleRuntime pole in _poles)
            {
                if (pole.IsUnderConstruction) continue;
                if (_fedNetworks.Contains(pole.NetworkId)) continue;
                if (IsNearPowerSource(pole.Cell)) _fedNetworks.Add(pole.NetworkId);
            }
        }

        public bool IsNetworkFed(int networkId) => _fedNetworks.Contains(networkId);

        bool IsNearPowerSource(GridCoord cell)
        {
            int r = PowerRangeCells;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    var candidate = new GridCoord(cell.X + dx, cell.Y + dy);
                    if (_grid.GetOccupant(candidate) is BuildingRuntime building && building.Definition.SuppliesPower) return true;
                }
            }
            return false;
        }

        // ---- Consumer gating ----

        /// <summary>
        /// Whether any cell of the given footprint falls within range of a built, fed pole. What
        /// BuildingRuntime.ComputeEffectivePerformance asks before drawing power at all (ENERGIE.md).
        /// </summary>
        public bool IsCovered(GridCoord origin, Vector2Int[] footprintCells)
        {
            foreach (PoleRuntime pole in _poles)
            {
                if (pole.IsUnderConstruction) continue;
                if (!IsNetworkFed(pole.NetworkId)) continue;

                foreach (Vector2Int offset in footprintCells)
                {
                    var cell = new GridCoord(origin.X + offset.x, origin.Y + offset.y);
                    if (ChebyshevDistance(pole.Cell, cell) <= PowerRangeCells) return true;
                }
            }

            return false;
        }

        static int ChebyshevDistance(GridCoord a, GridCoord b)
            => Mathf.Max(Mathf.Abs(a.X - b.X), Mathf.Abs(a.Y - b.Y));
    }
}
