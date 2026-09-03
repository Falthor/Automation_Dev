using System;
using System.Collections.Generic;
using Game.Data;

namespace Game.Gameplay.Research
{
    /// <summary>
    /// Global RP pool and single active research slot (CONTRACTS.md §11). Laboratories report
    /// themselves every tick while a research is active; the completion rate is therefore
    /// N/60 progress-per-second for N simultaneously active laboratories. Reuses Power's
    /// report-then-settle pattern for the active-lab count. A research may also require another
    /// one to be completed first (ResearchDefinition.RequiresResearch).
    /// </summary>
    public sealed class ResearchSystem
    {
        const float BaseRate = 1f / 60f;

        readonly System.Collections.Generic.HashSet<string> _unlocked = new System.Collections.Generic.HashSet<string>();

        int _pendingActiveLabs;
        int _settledActiveLabs;

        public float Rp { get; private set; }
        public ResearchDefinition ActiveResearch { get; private set; }
        public float Progress { get; private set; }

        public event Action<string> ResearchCompleted;

        public void AddRp(float amount) => Rp += amount;

        public bool HasActiveResearch() => ActiveResearch != null;
        public ResearchDefinition GetActiveResearch() => ActiveResearch;
        public float GetProgress() => Progress;

        /// <summary>Settled count of labs that reported last tick, same one-frame-lag convention as Power/Compute's own settled values.</summary>
        public int GetActiveLabCount() => _settledActiveLabs;

        /// <summary>Called once per tick by every Laboratory currently contributing.</summary>
        public void ReportActiveLab() => _pendingActiveLabs++;

        public bool IsUnlocked(string researchId) => researchId != null && _unlocked.Contains(researchId);

        /// <summary>Every unlocked research id, for the save/load system (CONTRACTS.md §14). No other consumer should need to enumerate this - query IsUnlocked(id) instead.</summary>
        public IEnumerable<string> GetUnlockedIds() => _unlocked;

        /// <summary>Whether this research's prerequisite (if it has one) is already completed. Always true for a research with no prerequisite.</summary>
        public bool ArePrerequisitesMet(ResearchDefinition research)
        {
            return research == null || research.RequiresResearch == null || IsUnlocked(research.RequiresResearch.Id);
        }

        /// <summary>Starts research, deducting its cost immediately. Rejects if something is already active, it's already unlocked, its prerequisite isn't completed, or RP is insufficient.</summary>
        public bool Start(ResearchDefinition research)
        {
            if (research == null || ActiveResearch != null || IsUnlocked(research.Id) || Rp < research.Cost) return false;
            if (!ArePrerequisitesMet(research)) return false;

            Rp -= research.Cost;
            ActiveResearch = research;
            Progress = 0f;
            return true;
        }

        public void Tick(float deltaTime)
        {
            _settledActiveLabs = _pendingActiveLabs;
            _pendingActiveLabs = 0;

            if (ActiveResearch == null) return;

            Progress += _settledActiveLabs * BaseRate * deltaTime;
            if (Progress < 1f) return;

            string completedId = ActiveResearch.Id;
            _unlocked.Add(completedId);
            ActiveResearch = null;
            Progress = 0f;
            ResearchCompleted?.Invoke(completedId);
        }

        /// <summary>Restores a previously-captured snapshot (CONTRACTS.md §14). Used only by the save/load system - never by gameplay code, which drives state through AddRp/Start/Tick instead.</summary>
        public void RestoreState(float rp, ResearchDefinition activeResearch, float progress, IEnumerable<string> unlockedIds)
        {
            Rp = rp;
            ActiveResearch = activeResearch;
            Progress = progress;
            _pendingActiveLabs = 0;
            _settledActiveLabs = 0;

            _unlocked.Clear();
            if (unlockedIds == null) return;
            foreach (string id in unlockedIds)
            {
                if (!string.IsNullOrEmpty(id)) _unlocked.Add(id);
            }
        }
    }
}
