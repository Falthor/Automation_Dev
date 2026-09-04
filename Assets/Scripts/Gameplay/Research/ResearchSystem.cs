using System;
using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Compute;

namespace Game.Gameplay.Research
{
    /// <summary>
    /// CU/absorption research model (CONTRACTS.md §11, TASK_02_REFONTE_RECHERCHE.md). A research
    /// defines a total CU cost and an absorption-rate ceiling, never a duration - the duration is
    /// the consequence of how fast the active research can actually draw CU out of the shared
    /// reserve: cost / min(AbsorptionRatePerSecond, what the reserve currently gives). One active
    /// research at a time; the rest wait in a reorderable queue and start automatically in order.
    /// Progress is a running CU total, never rolled back - at zero reserve the draw is simply
    /// zero that tick, which is what makes "pause without loss" a natural consequence of the
    /// model rather than a special case.
    /// </summary>
    public sealed class ResearchSystem
    {
        readonly ComputeSystem _computeSystem;
        readonly HashSet<string> _unlocked = new HashSet<string>();
        readonly List<ResearchDefinition> _queue = new List<ResearchDefinition>();

        public ResearchDefinition ActiveResearch { get; private set; }

        /// <summary>CU absorbed into ActiveResearch so far - 0 when nothing is active.</summary>
        public float AbsorbedCu { get; private set; }

        public event Action<string> ResearchCompleted;

        public ResearchSystem(ComputeSystem computeSystem)
        {
            _computeSystem = computeSystem;
        }

        public bool HasActiveResearch() => ActiveResearch != null;
        public ResearchDefinition GetActiveResearch() => ActiveResearch;

        /// <summary>Fraction of ActiveResearch's cost absorbed so far, in [0, 1]. 0 when nothing is active.</summary>
        public float GetProgress() => ActiveResearch != null && ActiveResearch.CuCost > 0f ? AbsorbedCu / ActiveResearch.CuCost : 0f;

        /// <summary>Seconds remaining for ActiveResearch at its own absorption ceiling - "au débit courant", not corrected for reserve depletion. 0 when nothing is active or the ceiling is 0.</summary>
        public float GetEstimatedSecondsRemaining()
        {
            if (ActiveResearch == null || ActiveResearch.AbsorptionRatePerSecond <= 0f) return 0f;
            return Math.Max(0f, ActiveResearch.CuCost - AbsorbedCu) / ActiveResearch.AbsorptionRatePerSecond;
        }

        public IReadOnlyList<ResearchDefinition> GetQueue() => _queue;

        public bool IsUnlocked(string researchId) => researchId != null && _unlocked.Contains(researchId);

        /// <summary>Every unlocked research id, for the save/load system (CONTRACTS.md §14). No other consumer should need to enumerate this - query IsUnlocked(id) instead.</summary>
        public IEnumerable<string> GetUnlockedIds() => _unlocked;

        /// <summary>Whether every one of this research's prerequisites is already completed. Always true for a research with none.</summary>
        public bool ArePrerequisitesMet(ResearchDefinition research)
        {
            if (research == null) return true;

            foreach (ResearchDefinition prerequisite in research.Prerequisites)
            {
                if (prerequisite != null && !IsUnlocked(prerequisite.Id)) return false;
            }
            return true;
        }

        /// <summary>Whether research may be queued (or started) right now: not null, not already unlocked, not already active or queued, and every prerequisite already completed. Does not check CU availability - queuing never requires CU up front, only starting the cycle does per-tick absorption.</summary>
        public bool CanQueue(ResearchDefinition research)
        {
            return research != null
                && !IsUnlocked(research.Id)
                && !ReferenceEquals(research, ActiveResearch)
                && !_queue.Contains(research)
                && ArePrerequisitesMet(research);
        }

        /// <summary>Starts research immediately if nothing is active, otherwise appends it to the back of the queue. False (no-op) when CanQueue(research) is false.</summary>
        public bool Enqueue(ResearchDefinition research)
        {
            if (!CanQueue(research)) return false;

            if (ActiveResearch == null)
            {
                ActiveResearch = research;
                AbsorbedCu = 0f;
            }
            else
            {
                _queue.Add(research);
            }
            return true;
        }

        /// <summary>Removes a pending (not yet active) entry from the queue. False if it wasn't queued.</summary>
        public bool Dequeue(ResearchDefinition research) => _queue.Remove(research);

        /// <summary>Moves a queued entry from one position to another, both 0-based indices into GetQueue(). False (no-op) on an out-of-range index.</summary>
        public bool ReorderQueue(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _queue.Count || toIndex < 0 || toIndex >= _queue.Count) return false;
            if (fromIndex == toIndex) return true;

            ResearchDefinition moved = _queue[fromIndex];
            _queue.RemoveAt(fromIndex);
            _queue.Insert(toIndex, moved);
            return true;
        }

        /// <summary>Abandons the active research, discarding whatever CU it already absorbed - matches ProductionBuildingRuntime's own "switching abandons the cycle without refunding" precedent. The queue is untouched; its head starts on the next Tick.</summary>
        public void CancelActive()
        {
            ActiveResearch = null;
            AbsorbedCu = 0f;
        }

        public void Tick(float deltaTime)
        {
            if (ActiveResearch == null)
            {
                if (_queue.Count == 0) return;

                ActiveResearch = _queue[0];
                _queue.RemoveAt(0);
                AbsorbedCu = 0f;
                return; // starts absorbing next tick, same one-frame-lag convention as Power/Compute.
            }

            float remaining = ActiveResearch.CuCost - AbsorbedCu;
            float wanted = Math.Min(remaining, ActiveResearch.AbsorptionRatePerSecond * deltaTime);
            AbsorbedCu += _computeSystem.SpendUpTo(wanted);

            if (AbsorbedCu < ActiveResearch.CuCost) return;

            string completedId = ActiveResearch.Id;
            _unlocked.Add(completedId);
            ActiveResearch = null;
            AbsorbedCu = 0f;
            ResearchCompleted?.Invoke(completedId);
        }

        /// <summary>Restores a previously-captured snapshot (CONTRACTS.md §14). Used only by the save/load system - never by gameplay code, which drives state through Enqueue/Tick instead.</summary>
        public void RestoreState(ResearchDefinition activeResearch, float absorbedCu, IEnumerable<ResearchDefinition> queue, IEnumerable<string> unlockedIds)
        {
            ActiveResearch = activeResearch;
            AbsorbedCu = activeResearch != null ? absorbedCu : 0f;

            _queue.Clear();
            if (queue != null)
            {
                foreach (ResearchDefinition research in queue)
                {
                    if (research != null) _queue.Add(research);
                }
            }

            _unlocked.Clear();
            if (unlockedIds == null) return;
            foreach (string id in unlockedIds)
            {
                if (!string.IsNullOrEmpty(id)) _unlocked.Add(id);
            }
        }
    }
}
