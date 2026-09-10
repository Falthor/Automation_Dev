using System;
using System.Collections.Generic;
using System.Text;
using Game.Core;
using Game.Data;
using UnityEngine;

namespace Game.Gameplay.Wrecks
{
    /// <summary>
    /// The wrecks scattered through the disc around the Core: where they are, and which the player
    /// has found.
    ///
    /// <b>Derived once, at construction, and never again.</b> Eight fixed places on a fixed map -
    /// there is nothing to recompute per frame and nothing to generate lazily. Position and type come
    /// from <see cref="DeterministicHash"/> over the world seed and the pair (ring, rank), so the
    /// same seed gives the same map in every session and after every load.
    ///
    /// <b>Nothing is stored but the discovered set.</b> That is the boundary this project keeps
    /// everywhere: what comes from the seed is recomputed, what comes from the player is saved.
    ///
    /// <b>Rare places, not an income.</b> The rings (<see cref="WreckRingProfile"/>) are what make
    /// one or two turn up almost at once and the rest much later, which a uniform density cannot do.
    ///
    /// <b>Discovery costs eight distance checks and allocates nothing.</b> A robot asks on the same
    /// beat it reveals ground, and once every wreck is found the loop exits on a counter.
    /// </summary>
    public sealed class WreckField
    {
        // Distinct salts so a wreck's angle, its radius and its type are independent draws rather
        // than three views of one number. The ring is folded in with a stride, so the pair
        // (ring, rank) is what each draw is keyed on.
        const uint AngleSalt = 0x1B873593;
        const uint RadiusSalt = 0xCC9E2D51;
        const uint TypeSalt = 0x85EBCA77;
        const uint RingStride = 0x9E3779B1;

        /// <summary>How many wreck assets there are to draw from. A count rather than an enum: the field names no wreck, it says "the second one".</summary>
        public const int TypeCount = 3;

        readonly WreckSite[] _sites;

        /// <summary>Raised the moment a wreck is found. Presentation spawns its view on it, and the measurement log records it.</summary>
        public event Action<WreckSite> Discovered;

        public IReadOnlyList<WreckSite> Sites => _sites;

        public int DiscoveredCount { get; private set; }

        public WreckField(int seed, Vector2 coreCentreCells, WreckRingProfile rings)
        {
            _sites = Derive(seed, coreCentreCells, rings);
        }

        static WreckSite[] Derive(int seed, Vector2 coreCentreCells, WreckRingProfile rings)
        {
            var sites = new List<WreckSite>();

            for (int ring = 0; ring < rings.RingCount; ring++)
            {
                WreckRing band = rings.Ring(ring);
                int count = Mathf.Max(0, band.Count);
                if (count == 0) continue;

                float spacing = rings.SpacingDegrees(ring);
                float jitterBound = spacing * WreckRingProfile.AngularJitterFraction;

                for (int rank = 0; rank < count; rank++)
                {
                    uint ringOffset = (uint)ring * RingStride;

                    // The rank's share of the circle, plus a jitter that cannot reach half the gap -
                    // which is what guarantees the separation without anything checking it.
                    float jitter = (float)(DeterministicHash.Unit(seed, rank, AngleSalt + ringOffset) * 2.0 - 1.0) * jitterBound;
                    float degrees = rank * spacing + jitter;
                    float radians = degrees * Mathf.Deg2Rad;

                    float radius = Mathf.Lerp(band.InnerRadiusCells, band.OuterRadiusCells,
                        (float)DeterministicHash.Unit(seed, rank, RadiusSalt + ringOffset));

                    var centre = new Vector2(
                        coreCentreCells.x + Mathf.Cos(radians) * radius,
                        coreCentreCells.y + Mathf.Sin(radians) * radius);

                    int type = (int)(DeterministicHash.Mix(seed, rank, TypeSalt + ringOffset) % TypeCount);

                    sites.Add(new WreckSite(sites.Count, ring, rank, type, centre, radius));
                }
            }

            return sites.ToArray();
        }

        /// <summary>
        /// Finds any wreck whose middle falls within <paramref name="radiusCells"/> of
        /// <paramref name="position"/>, and answers how many were newly found.
        ///
        /// <b>The wreck's middle, against the reveal radius</b>, so a wreck is found exactly when the
        /// ground it stands on is uncovered - one rule rather than a separate proximity range that
        /// could let a robot walk over an undiscovered wreck or spot one through the fog.
        /// </summary>
        public int DiscoverWithin(Vector2 position, float radiusCells)
        {
            if (DiscoveredCount >= _sites.Length) return 0;

            float limit = radiusCells * radiusCells;
            int found = 0;

            for (int i = 0; i < _sites.Length; i++)
            {
                WreckSite site = _sites[i];
                if (site.Discovered) continue;
                if ((site.CentreCells - position).sqrMagnitude > limit) continue;

                site.Discovered = true;
                DiscoveredCount++;
                found++;
                Discovered?.Invoke(site);
            }

            return found;
        }

        /// <summary>
        /// <b>A development instrument, and it is meant to be deleted.</b> Reveals the whole field at
        /// once, so the spread of eight points over a 330-cell disc can be judged on the map without
        /// playing the three quarters of an hour the outer ring is tuned for. Behind a switch that is
        /// off by default - see WorldGenerationSettings.
        /// </summary>
        public int DiscoverEverything()
        {
            int found = 0;

            for (int i = 0; i < _sites.Length; i++)
            {
                if (_sites[i].Discovered) continue;

                _sites[i].Discovered = true;
                DiscoveredCount++;
                found++;
                Discovered?.Invoke(_sites[i]);
            }

            return found;
        }

        // ---- Persistence ----

        /// <summary>
        /// The discovered indices, comma-separated - the same shape `Discovered` and `DecorRemoved`
        /// use, and for the same reason: it is a set of things the player has done, and a string
        /// keeps the save layer free of a type from this assembly.
        /// </summary>
        public string CaptureState()
        {
            var text = new StringBuilder();

            for (int i = 0; i < _sites.Length; i++)
            {
                if (!_sites[i].Discovered) continue;

                if (text.Length > 0) text.Append(',');
                text.Append(_sites[i].Index);
            }

            return text.ToString();
        }

        /// <summary>
        /// Restores the discovered set. Tolerant by design: an absent or unreadable value means a
        /// world nobody has found anything in, which is exactly what it recorded. An index the
        /// current settings no longer produce is ignored rather than throwing - a ring count changed
        /// between sessions must cost the wrecks that no longer exist, not the save.
        /// </summary>
        public void RestoreState(string encoded)
        {
            for (int i = 0; i < _sites.Length; i++) _sites[i].Discovered = false;
            DiscoveredCount = 0;

            if (string.IsNullOrEmpty(encoded)) return;

            foreach (string part in encoded.Split(','))
            {
                if (!int.TryParse(part, out int index)) continue;
                if (index < 0 || index >= _sites.Length) continue;
                if (_sites[index].Discovered) continue;

                _sites[index].Discovered = true;
                DiscoveredCount++;
            }
        }
    }
}
