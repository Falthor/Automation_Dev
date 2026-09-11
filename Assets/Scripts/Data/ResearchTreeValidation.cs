using System;
using System.Collections.Generic;

namespace Game.Data
{
    /// <summary>
    /// Two checks on a research tree that no player could recover from (CONTRACTS.md §11). Pure, so
    /// they run without a scene, an editor or a run.
    ///
    /// - A <b>cycle</b> of prerequisites: a research that needs itself through any chain can never be
    ///   started, and neither can anything behind it - a permanent block of the run, and very easy to
    ///   create by linking two nodes the wrong way round.
    /// - An <b>unreachable</b> research: one no chain of prerequisites leads back to the cores.
    ///
    /// <b>Reachable is read strictly, as "can ever be unlocked".</b> Every prerequisite has to be a core
    /// or reachable itself. A research with one prerequisite on the tree and another nothing leads to
    /// has a path to the cores and still can never start, so it is reported too. A research with no
    /// prerequisite at all is not a root: only the cores are. A missing (null) prerequisite is skipped,
    /// as ResearchSystem.ArePrerequisitesMet skips it.
    /// </summary>
    public static class ResearchTreeValidation
    {
        /// <summary>Every research that is part of a prerequisite cycle - a research requiring itself included. Empty when there is none.</summary>
        public static List<ResearchDefinition> FindCycles(IEnumerable<ResearchDefinition> researches)
        {
            var result = new List<ResearchDefinition>();
            if (researches == null) return result;

            // Tarjan's strongly connected components over the prerequisite edges: every component of
            // more than one research is a cycle, and so is a single research that lists itself.
            var order = new Dictionary<ResearchDefinition, int>();
            var lowLink = new Dictionary<ResearchDefinition, int>();
            var onStack = new HashSet<ResearchDefinition>();
            var stack = new Stack<ResearchDefinition>();
            int counter = 0;

            void Visit(ResearchDefinition node)
            {
                order[node] = lowLink[node] = counter++;
                stack.Push(node);
                onStack.Add(node);

                IReadOnlyList<ResearchDefinition> prerequisites = node.Prerequisites;
                for (int i = 0; i < prerequisites.Count; i++)
                {
                    ResearchDefinition next = prerequisites[i];
                    if (next == null) continue;

                    if (!order.ContainsKey(next))
                    {
                        Visit(next);
                        lowLink[node] = Math.Min(lowLink[node], lowLink[next]);
                    }
                    else if (onStack.Contains(next))
                    {
                        lowLink[node] = Math.Min(lowLink[node], order[next]);
                    }
                }

                if (lowLink[node] != order[node]) return;

                var component = new List<ResearchDefinition>();
                ResearchDefinition member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                }
                while (!ReferenceEquals(member, node));

                if (component.Count > 1 || RequiresItself(node)) result.AddRange(component);
            }

            foreach (ResearchDefinition research in researches)
            {
                if (research != null && !order.ContainsKey(research)) Visit(research);
            }
            return result;
        }

        /// <summary>Every research in <paramref name="researches"/> that can never be unlocked from <paramref name="cores"/>, in the order given. Empty when all can.</summary>
        public static List<ResearchDefinition> FindUnreachable(IEnumerable<ResearchDefinition> cores, IEnumerable<ResearchDefinition> researches)
        {
            var reachable = new HashSet<ResearchDefinition>();
            if (cores != null)
            {
                foreach (ResearchDefinition core in cores)
                {
                    if (core != null) reachable.Add(core);
                }
            }

            var pending = new List<ResearchDefinition>();
            if (researches != null)
            {
                foreach (ResearchDefinition research in researches)
                {
                    if (research != null && !reachable.Contains(research) && !pending.Contains(research)) pending.Add(research);
                }
            }

            // Grow the reachable set until nothing more joins it. Whatever is left can never start.
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    if (!CanBeUnlocked(pending[i], reachable)) continue;

                    reachable.Add(pending[i]);
                    pending.RemoveAt(i);
                    grew = true;
                }
            }
            return pending;
        }

        static bool CanBeUnlocked(ResearchDefinition research, HashSet<ResearchDefinition> reachable)
        {
            IReadOnlyList<ResearchDefinition> prerequisites = research.Prerequisites;
            int counted = 0;
            for (int i = 0; i < prerequisites.Count; i++)
            {
                if (prerequisites[i] == null) continue;
                if (!reachable.Contains(prerequisites[i])) return false;
                counted++;
            }
            return counted > 0; // only the cores are roots
        }

        static bool RequiresItself(ResearchDefinition research)
        {
            IReadOnlyList<ResearchDefinition> prerequisites = research.Prerequisites;
            for (int i = 0; i < prerequisites.Count; i++)
            {
                if (ReferenceEquals(prerequisites[i], research)) return true;
            }
            return false;
        }
    }
}
