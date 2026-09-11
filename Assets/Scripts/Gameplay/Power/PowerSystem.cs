using System.Collections.Generic;

namespace Game.Gameplay.Power
{
    /// <summary>
    /// Global power supply/demand (CONTRACTS.md §9), and the allocation of it.
    ///
    /// <b>Report-then-settle, one frame of lag by design</b>: buildings draw during their own tick
    /// this frame; <see cref="Settle"/> (called once per GameRuntime.Update(), before that tick)
    /// turns last frame's reports into this frame's budgets.
    ///
    /// <b>It used to be one boolean for the whole base</b> - <c>SettledDemand &lt;= SettledSupply</c>
    /// - so a shortage stopped every powered building at once. That is a dead end rather than a
    /// setback: the Data Center stops, CU production stops, and without CU nothing can be built or
    /// burned to get out of it. The current is allocated by building type now, in the order the
    /// player arranges (<see cref="PowerPriorityOrder"/>), so a shortage is something to arbitrate
    /// instead of something to be stuck in.
    ///
    /// <b>Partial service is per instance, not per building's speed.</b> A group allocated 1 kW of
    /// the 2 kW it asked for runs one of its two extractors, rather than running both at half
    /// speed: within a group, instances draw until the group's budget is spent, in the order they
    /// tick (registration order) - the same "whoever asks first" the one-shot CU spend already uses.
    /// A machine is either running or it is not, which is the only thing the rest of the simulation
    /// knows how to represent.
    ///
    /// <b>Nothing is served off the top.</b> The gas plant used to declare 2 kW of
    /// self-consumption, reported unconditionally and never gated - a load nobody could switch off
    /// and that this system had no way to arbitrate. It was removed rather than modelled as
    /// overhead: a plant draws nothing from the network it feeds, and every kilowatt here now
    /// belongs to a group the player can order.
    /// </summary>
    public sealed class PowerSystem
    {
        float _pendingSupply;

        readonly Dictionary<string, float> _pendingDemand = new Dictionary<string, float>();

        /// <summary>Last frame's demand per group - what the budgets below were computed from, and what the panel reads.</summary>
        readonly Dictionary<string, float> _settledDemand = new Dictionary<string, float>();

        /// <summary>What each group was allocated this frame, before its instances started drawing on it.</summary>
        readonly Dictionary<string, float> _allocated = new Dictionary<string, float>();

        /// <summary>What is left of each group's allocation right now, as its instances draw.</summary>
        readonly Dictionary<string, float> _remaining = new Dictionary<string, float>();

        /// <summary>Groups seen since the last settle, in the order they were first heard from - the tie-break for anything the priority order does not mention.</summary>
        readonly List<string> _heardOrder = new List<string>();

        readonly List<string> _settleScratch = new List<string>();

        /// <summary>Draws attempted per group during the frame being built, and the same for the last complete one.</summary>
        readonly Dictionary<string, int> _askingNow = new Dictionary<string, int>();
        readonly Dictionary<string, int> _asked = new Dictionary<string, int>();

        /// <summary>Draws that succeeded - "1 sur 2" measured rather than divided. A group's allocation can exceed what its instances can use: a single Data Center asking for 5 kW and allocated 3 takes none of it, because an instance draws its demand or nothing.</summary>
        readonly Dictionary<string, int> _servingNow = new Dictionary<string, int>();
        readonly Dictionary<string, int> _served = new Dictionary<string, int>();

        public float SettledDemand { get; private set; }
        public float SettledSupply { get; private set; }

        /// <summary>Where the player's arbitration comes from. Null is legitimate - a scene with no priority screen serves groups in the order they were first heard from.</summary>
        public PowerPriorityOrder Priority { get; set; }

        public void ReportSupply(float kilowatts) => _pendingSupply += kilowatts;

        /// <summary>
        /// Reports a draw and answers whether it is served.
        ///
        /// Both at once on purpose: a building that asks is a building that counts towards next
        /// frame's demand whether or not it runs this one, and separating the two invites a caller
        /// to do one without the other. The demand recorded here is what the next
        /// <see cref="Settle"/> allocates from.
        /// </summary>
        public bool TryDraw(string groupId, float kilowatts)
        {
            // An unnamed group is a group, not an exemption. This used to return true for a null or
            // empty id, which meant a definition shipped without one would be powered
            // unconditionally and nobody would ever find out - where the same defect read as "this
            // never runs" is one somebody notices in a minute.
            if (groupId == null) groupId = string.Empty;

            Accumulate(_pendingDemand, groupId, kilowatts);
            Count(_askingNow, groupId);
            if (!_heardOrder.Contains(groupId)) _heardOrder.Add(groupId);

            if (!_remaining.TryGetValue(groupId, out float left)) return false;
            if (left < kilowatts) return false;

            _remaining[groupId] = left - kilowatts;
            Count(_servingNow, groupId);
            return true;
        }

        /// <summary>Whether supply covers everything asked for - the global answer the Power panel's header and its graph still want.</summary>
        public bool IsPowered() => SettledDemand <= SettledSupply;

        /// <summary>What that group asked for last frame, 0 for one that asked for nothing. The panel's own figure, and the only truthful one for a group whose demand is dynamic - the Data Center's is its installed components', not a number on its definition.</summary>
        public float DemandOf(string groupId)
            => groupId != null && _settledDemand.TryGetValue(groupId, out float kw) ? kw : 0f;

        /// <summary>What that group was allocated this frame: all of its demand, part of it, or none.</summary>
        public float AllocatedTo(string groupId)
            => groupId != null && _allocated.TryGetValue(groupId, out float kw) ? kw : 0f;

        /// <summary>How many instances of that group asked for power on the last complete frame.</summary>
        public int AskingInstancesOf(string groupId)
            => groupId != null && _asked.TryGetValue(groupId, out int count) ? count : 0;

        /// <summary>
        /// How many of them were served - the "1 sur 2" the screen shows, counted rather than
        /// derived from the kilowatts.
        ///
        /// Dividing the allocation by a per-instance figure would be wrong in exactly the case that
        /// matters: a group's instances need not draw the same amount (the Data Center's demand is
        /// its installed components'), and an instance takes its whole demand or nothing.
        /// </summary>
        public int ServedInstancesOf(string groupId)
            => groupId != null && _served.TryGetValue(groupId, out int count) ? count : 0;

        /// <summary>Moves this frame's reports into the settled totals, then allocates supply along the priority order.</summary>
        public void Settle()
        {
            SettledSupply = _pendingSupply;

            _settledDemand.Clear();
            float total = 0f;
            foreach (var pair in _pendingDemand)
            {
                _settledDemand[pair.Key] = pair.Value;
                total += pair.Value;
            }
            SettledDemand = total;

            _pendingDemand.Clear();
            _pendingSupply = 0f;

            Swap(_askingNow, _asked);
            Swap(_servingNow, _served);

            Allocate();
        }

        /// <summary>
        /// Hands out supply group by group, most-served first. A group gets all of its demand or
        /// what remains of supply, whichever is smaller -
        /// so the shortage falls on exactly one group, the one the running total crosses, and
        /// everything below it gets nothing.
        /// </summary>
        void Allocate()
        {
            _allocated.Clear();
            _remaining.Clear();

            float available = SettledSupply;

            _settleScratch.Clear();
            foreach (string id in _heardOrder)
            {
                if (_settledDemand.ContainsKey(id)) _settleScratch.Add(id);
            }

            // Stable: equal ranks keep the order they were first heard in, so a group the player
            // has not arbitrated does not swap places with another from frame to frame.
            if (Priority != null) SortByRank(_settleScratch);

            for (int i = 0; i < _settleScratch.Count; i++)
            {
                string id = _settleScratch[i];
                float wanted = _settledDemand[id];
                float given = wanted <= available ? wanted : available;

                _allocated[id] = given;
                _remaining[id] = given;
                available -= given;
            }

            _heardOrder.Clear();
        }

        /// <summary>Insertion sort on the rank: the list is one entry per building type that drew power, so it is short, and an insertion sort is stable without a comparer allocation.</summary>
        void SortByRank(List<string> ids)
        {
            for (int i = 1; i < ids.Count; i++)
            {
                string id = ids[i];
                int rank = Priority.RankOf(id);
                int j = i - 1;

                while (j >= 0 && Priority.RankOf(ids[j]) > rank)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }

                ids[j + 1] = id;
            }
        }

        static void Accumulate(Dictionary<string, float> into, string key, float value)
        {
            into[key] = into.TryGetValue(key, out float existing) ? existing + value : value;
        }

        static void Count(Dictionary<string, int> into, string key)
        {
            into[key] = into.TryGetValue(key, out int existing) ? existing + 1 : 1;
        }

        /// <summary>Moves the frame being built into the last-complete one and empties it. Copied rather than swapped by reference, so both dictionaries stay the fields they were declared as - the panel holds no reference to either.</summary>
        static void Swap(Dictionary<string, int> from, Dictionary<string, int> to)
        {
            to.Clear();
            foreach (var pair in from) to[pair.Key] = pair.Value;
            from.Clear();
        }
    }
}
