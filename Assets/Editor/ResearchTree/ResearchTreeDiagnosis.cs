using System.Collections.Generic;
using Game.Data;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// What is wrong with the tree right now, for the scene's drawing and the node inspector:
    /// ResearchTreeValidation's two checks, and the nodes placed outside their branch's sector.
    ///
    /// A sector is centred on its core, one equal share of the circle per core. Leaving it is
    /// reported, never prevented: the sectors are there to see where to place, not to constrain it.
    /// A research's branch is the core its first prerequisites lead back to.
    ///
    /// Recomputed only after something changed - an undo group closing, a write by the editor, a
    /// project change - not on every repaint.
    /// </summary>
    public static class ResearchTreeDiagnosis
    {
        public static readonly HashSet<ResearchDefinition> Cores = new HashSet<ResearchDefinition>();
        public static readonly HashSet<ResearchDefinition> InCycle = new HashSet<ResearchDefinition>();
        public static readonly HashSet<ResearchDefinition> Unreachable = new HashSet<ResearchDefinition>();
        public static readonly HashSet<ResearchDefinition> OutOfSector = new HashSet<ResearchDefinition>();

        /// <summary>The outermost ring any node sits on.</summary>
        public static int MaxTier { get; private set; }

        /// <summary>Half a sector, in degrees.</summary>
        public static float HalfSector { get; private set; } = 180f;

        static bool _valid;
        static int _undoGroup = -1;

        public static void Invalidate() => _valid = false;

        public static void Refresh()
        {
            int undoGroup = Undo.GetCurrentGroup();
            if (_valid && undoGroup == _undoGroup) return;

            _valid = true;
            _undoGroup = undoGroup;
            Cores.Clear();
            InCycle.Clear();
            Unreachable.Clear();
            OutOfSector.Clear();
            MaxTier = 1;

            ResearchDatabase database = ResearchTreeScene.Database;
            if (database == null) return;

            var tree = new List<ResearchDefinition>();
            foreach (ResearchDefinition core in database.GetCores())
            {
                if (core == null) continue;
                Cores.Add(core);
                tree.Add(core);
            }
            tree.AddRange(database.GetAll());

            InCycle.UnionWith(ResearchTreeValidation.FindCycles(tree));
            Unreachable.UnionWith(ResearchTreeValidation.FindUnreachable(database.GetCores(), database.GetAll()));

            HalfSector = Cores.Count > 0 ? 180f / Cores.Count : 180f;
            foreach (ResearchDefinition research in tree)
            {
                if (research == null) continue;
                MaxTier = Mathf.Max(MaxTier, research.Tier);
                if (Cores.Contains(research)) continue;

                ResearchDefinition core = BranchCore(research);
                if (core != null && Mathf.Abs(Mathf.DeltaAngle(research.Angle, core.Angle)) > HalfSector + 0.05f) OutOfSector.Add(research);
            }
        }

        /// <summary>Plain sentences for the node inspector - empty when nothing is wrong with this research.</summary>
        public static List<string> ProblemsOf(ResearchDefinition research)
        {
            Refresh();
            var problems = new List<string>();
            if (InCycle.Contains(research)) problems.Add("On a prerequisite cycle: it can never start, and nothing behind it can.");
            else if (Unreachable.Contains(research)) problems.Add("Can never be unlocked from the cores: a prerequisite chain does not lead back to them.");
            if (OutOfSector.Contains(research)) problems.Add("Outside its branch's sector.");
            return problems;
        }

        static ResearchDefinition BranchCore(ResearchDefinition research)
        {
            var seen = new HashSet<ResearchDefinition>();
            ResearchDefinition node = research;
            while (node != null && seen.Add(node))
            {
                if (Cores.Contains(node)) return node;
                node = FirstPrerequisite(node);
            }
            return null;
        }

        static ResearchDefinition FirstPrerequisite(ResearchDefinition research)
        {
            foreach (ResearchDefinition prerequisite in research.Prerequisites)
            {
                if (prerequisite != null) return prerequisite;
            }
            return null;
        }
    }
}
