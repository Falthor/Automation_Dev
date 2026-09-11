using System.Collections.Generic;
using Game.Data;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Where each node of the research network sits (GDD §5.4). Pure - no element, no state - so the
    /// placement rules can be checked without a panel.
    ///
    /// The cores share the circle in equal sectors, the first centred up and to the left. Inside a
    /// sector, a node's children split its angular interval in proportion to how many leaves each
    /// carries, and a child never leaves its parent's interval: that is what guarantees no two
    /// synapses cross, and why nothing is ever placed by hand. The radius is the tier times a fixed
    /// step, so the distance from the centre reads as progression.
    ///
    /// A research hangs under its first prerequisite that is itself on the network (a core or a
    /// listed research); one with none is an orphan and is not placed. A core with nothing under it
    /// gets a few unknown nodes instead, so an unpowered branch still occupies its sector.
    /// </summary>
    public static class NeuralLayout
    {
        /// <summary>Where the first core's sector is centred. Screen space, y down, so -90 is straight up and -150 is up and to the left.</summary>
        public const float FirstCoreAngleDegrees = -150f;

        public struct Placement
        {
            /// <summary>The core or research on this node; null for an unknown node.</summary>
            public ResearchDefinition Definition;

            /// <summary>Index of the node this one hangs from, in the same list; -1 for a core, which hangs from the centre.</summary>
            public int ParentIndex;

            public int Tier;

            /// <summary>The angular interval this node and everything under it may use, in radians.</summary>
            public float SectorStart;
            public float SectorEnd;

            /// <summary>Offset from the centre, screen space (y down).</summary>
            public Vector2 Position;

            public bool IsCore => ParentIndex < 0;
            public float Angle => (SectorStart + SectorEnd) * 0.5f;
        }

        /// <summary>Cores first, in the order given, each followed by everything under it.</summary>
        public static List<Placement> Compute(IReadOnlyList<ResearchDefinition> cores, IReadOnlyList<ResearchDefinition> researches,
            float ringStep, int unknownNodesPerEmptyCore)
        {
            var placements = new List<Placement>();
            var onNetwork = new HashSet<ResearchDefinition>();
            var coreList = new List<ResearchDefinition>();

            for (int i = 0; i < cores.Count; i++)
            {
                if (cores[i] != null && onNetwork.Add(cores[i])) coreList.Add(cores[i]);
            }
            for (int i = 0; i < researches.Count; i++)
            {
                if (researches[i] != null) onNetwork.Add(researches[i]);
            }

            var children = new Dictionary<ResearchDefinition, List<ResearchDefinition>>();
            for (int i = 0; i < researches.Count; i++)
            {
                ResearchDefinition research = researches[i];
                if (research == null || coreList.Contains(research)) continue;

                ResearchDefinition parent = LayoutParent(research, onNetwork);
                if (parent == null) continue; // an orphan: nothing on the network leads to it

                if (!children.TryGetValue(parent, out List<ResearchDefinition> list)) children[parent] = list = new List<ResearchDefinition>();
                if (!list.Contains(research)) list.Add(research);
            }

            if (coreList.Count == 0) return placements;

            float width = 2f * Mathf.PI / coreList.Count;
            float start = FirstCoreAngleDegrees * Mathf.Deg2Rad - width * 0.5f;
            var placed = new HashSet<ResearchDefinition>(coreList);

            for (int c = 0; c < coreList.Count; c++)
            {
                float sectorStart = start + c * width;
                int coreIndex = placements.Count;
                placements.Add(Make(coreList[c], -1, 1, sectorStart, sectorStart + width, ringStep));

                if (children.ContainsKey(coreList[c])) PlaceChildren(coreList[c], coreIndex, placements, children, placed, ringStep);
                else PlaceUnknown(coreIndex, placements, unknownNodesPerEmptyCore, ringStep);
            }
            return placements;
        }

        /// <summary>The first prerequisite that is itself on the network. A node with several parents is placed under one and linked to the others by its synapses alone.</summary>
        static ResearchDefinition LayoutParent(ResearchDefinition research, HashSet<ResearchDefinition> onNetwork)
        {
            IReadOnlyList<ResearchDefinition> prerequisites = research.Prerequisites;
            for (int i = 0; i < prerequisites.Count; i++)
            {
                ResearchDefinition prerequisite = prerequisites[i];
                if (prerequisite != null && !ReferenceEquals(prerequisite, research) && onNetwork.Contains(prerequisite)) return prerequisite;
            }
            return null;
        }

        static void PlaceChildren(ResearchDefinition parent, int parentIndex, List<Placement> placements,
            Dictionary<ResearchDefinition, List<ResearchDefinition>> children, HashSet<ResearchDefinition> placed, float ringStep)
        {
            if (!children.TryGetValue(parent, out List<ResearchDefinition> list)) return;

            Placement parentPlacement = placements[parentIndex];
            int total = 0;
            for (int i = 0; i < list.Count; i++) total += Leaves(list[i], children, 0);

            float span = parentPlacement.SectorEnd - parentPlacement.SectorStart;
            float cursor = parentPlacement.SectorStart;
            for (int i = 0; i < list.Count; i++)
            {
                ResearchDefinition child = list[i];
                float share = span * Leaves(child, children, 0) / total;
                if (!placed.Add(child)) { cursor += share; continue; }

                // Never on its parent's ring or inside it, whatever the asset says: the radius is the
                // progression, and a child is always further along than what it hangs from.
                int tier = Mathf.Max(child.Tier, parentPlacement.Tier + 1);
                int index = placements.Count;
                placements.Add(Make(child, parentIndex, tier, cursor, cursor + share, ringStep));
                cursor += share;
                PlaceChildren(child, index, placements, children, placed, ringStep);
            }
        }

        /// <summary>A leaf counts one; anything else counts the leaves under it. The depth guard only protects against a data loop, which a tree cannot have.</summary>
        static int Leaves(ResearchDefinition node, Dictionary<ResearchDefinition, List<ResearchDefinition>> children, int depth)
        {
            if (depth > 64 || !children.TryGetValue(node, out List<ResearchDefinition> list) || list.Count == 0) return 1;

            int total = 0;
            for (int i = 0; i < list.Count; i++) total += Leaves(list[i], children, depth + 1);
            return total;
        }

        static void PlaceUnknown(int coreIndex, List<Placement> placements, int count, float ringStep)
        {
            Placement core = placements[coreIndex];
            float share = (core.SectorEnd - core.SectorStart) / Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                float start = core.SectorStart + i * share;
                placements.Add(Make(null, coreIndex, core.Tier + 1, start, start + share, ringStep));
            }
        }

        static Placement Make(ResearchDefinition definition, int parentIndex, int tier, float start, float end, float ringStep)
        {
            float angle = (start + end) * 0.5f;
            return new Placement
            {
                Definition = definition,
                ParentIndex = parentIndex,
                Tier = tier,
                SectorStart = start,
                SectorEnd = end,
                Position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (tier * ringStep)
            };
        }
    }
}
