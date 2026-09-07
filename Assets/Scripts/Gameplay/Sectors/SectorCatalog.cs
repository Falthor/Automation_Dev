using Game.Core;
using Game.Grid;
using UnityEngine;

namespace Game.Gameplay.Sectors
{
    /// <summary>
    /// Where a sector's name, risk and contents come from.
    ///
    /// <b>Nothing is stored.</b> There is no list of sectors, no dictionary, no cache: every answer
    /// is a pure function of the world seed and the sector's index, computed when asked. That is the
    /// lazy generation the directive requires, and it is lazy by construction rather than by
    /// bookkeeping - 625 sectors exist, and a run only ever asks about the few dozen it can reach.
    ///
    /// <b>The seed is the terrain's</b>, which is the only one a save restores
    /// (SaveData.TerrainSeed). WorldGenerator.ResourceSeed would have been the intuitive choice and
    /// is the wrong one: it is not persisted, so a loaded game would rename every sector and move
    /// every deposit it had not yet materialised. Same seed, same sector, same everything, whatever
    /// the order of discovery and whatever happened in between - which is what a test pins.
    /// </summary>
    public sealed class SectorCatalog
    {
        // Distinct salts so the name, the risk and the contents of one sector are independent draws
        // rather than three views of the same number.
        const uint NameSalt = 0x9E3779B9;
        const uint RiskSalt = 0x85EBCA6B;
        const uint FeatureSalt = 0xC2B2AE35;
        const uint DepositSalt = 0x27D4EB2F;

        /// <summary>
        /// Steps through the name space one sector at a time. Coprime with the 768 combinations
        /// (768 = 2^8 x 3; 397 is odd and not a multiple of 3), which is what makes index -> name
        /// injective: no two sectors can be handed the same name, without any global bookkeeping to
        /// check it. Drawing names at random instead would have collided constantly - 625 draws from
        /// 768 is a near-certain repeat.
        /// </summary>
        const int NameStride = 397;

        /// <summary>The kind of place. Deliberately concrete nouns: "Zone 7" does not invite anyone anywhere.</summary>
        static readonly string[] Forms =
        {
            "Épave", "Vestiges", "Carcasse", "Cratère",
            "Ravin", "Friche", "Balise", "Relais",
            "Faille", "Plateau", "Décharge", "Sillon",
            "Amas", "Dépôt", "Brèche", "Corniche"
        };

        /// <summary>
        /// Always introduced by a preposition rather than being an adjective. That is a grammar
        /// decision, not a stylistic one: "Épave" is feminine and "Cratère" masculine, so an
        /// adjective would have to agree, and a generated name that reads "Cratère Rouillée" is
        /// worse than no name at all. A complement never agrees with anything.
        /// </summary>
        static readonly string[] Complements =
        {
            "de Fer", "de Rouille", "de Cendre", "de Suie",
            "d'Ambre", "de Basalte", "de Quartz", "de Sel",
            "du Silence", "du Crépuscule", "de l'Aube", "du Nord",
            "du Levant", "du Couchant", "des Vents", "de la Poussière",
            "des Sondes", "des Câbles", "des Turbines", "des Fondeurs",
            "des Errants", "des Naufragés", "des Éclaireurs", "des Oubliés",
            "de Verre", "de Schiste", "de Craie", "de Fonte",
            "de Plomb", "de Cuivre", "de Zinc", "de Mica",
            "de la Faille", "du Vide", "de l'Écho", "de la Brume",
            "de l'Orage", "de la Dérive", "du Gel", "de la Rouille Noire",
            "des Ombres", "des Reliques", "des Antennes", "des Carènes",
            "des Mâchoires", "des Serres", "des Racines", "des Cendres Froides"
        };

        /// <summary>768. Must stay at or above SectorGrid.Count for names to remain unique - pinned by a test, so growing the map trips it rather than silently duplicating names.</summary>
        public static int NameCombinationCount => Forms.Length * Complements.Length;

        public SectorGrid Grid { get; }
        public int Seed { get; }

        /// <summary>Where the risk gradient is measured from. The Core's own sector is the safe end.</summary>
        readonly int _coreColumn;
        readonly int _coreRow;

        public SectorCatalog(SectorGrid grid, int seed, Vector2 coreCenterCells)
        {
            Grid = grid;
            Seed = seed;

            int coreIndex = grid?.IndexAt(new GridCoord(Mathf.FloorToInt(coreCenterCells.x), Mathf.FloorToInt(coreCenterCells.y))) ?? -1;
            _coreColumn = coreIndex >= 0 ? grid.ColumnOf(coreIndex) : 0;
            _coreRow = coreIndex >= 0 ? grid.RowOf(coreIndex) : 0;
        }

        /// <summary>The sector's name. Stable for a given seed and index, and distinct from every other sector's.</summary>
        public string NameOf(int index)
        {
            if (Grid == null || !Grid.ContainsIndex(index)) return string.Empty;

            int total = NameCombinationCount;

            // The seed only rotates the sequence: two worlds name the same sector differently, and
            // within one world the mapping stays a bijection.
            int rotation = (int)(Hash(Seed, 0, NameSalt) % (uint)total);
            int combination = (int)(((long)index * NameStride + rotation) % total);

            return Forms[combination / Complements.Length] + " " + Complements[combination % Complements.Length];
        }

        /// <summary>
        /// Risk grows outward from the Core, because that is the shape of the decision the player
        /// makes - a far mission should read as a bigger bet before they have read a single number.
        /// The seed then nudges individual sectors off the gradient by one band, so two worlds are
        /// not the same map with the same answers.
        /// </summary>
        public SectorRisk RiskOf(int index)
        {
            if (Grid == null || !Grid.ContainsIndex(index)) return SectorRisk.Low;

            int ring = Mathf.Max(
                Mathf.Abs(Grid.ColumnOf(index) - _coreColumn),
                Mathf.Abs(Grid.RowOf(index) - _coreRow));

            int band = ring <= 1 ? 0 : ring <= 3 ? 1 : ring <= 6 ? 2 : 3;

            uint jitter = Hash(Seed, index, RiskSalt) % 4;
            if (jitter == 0) band++;
            else if (jitter == 1) band--;

            return (SectorRisk)Mathf.Clamp(band, (int)SectorRisk.Low, (int)SectorRisk.Critical);
        }

        /// <summary>
        /// What the sector holds. Allocates the deposit array, so call it when a sector is
        /// discovered - not every frame, and not for sectors nobody has reached.
        /// </summary>
        public SectorContents ContentsOf(int index)
        {
            if (Grid == null || !Grid.ContainsIndex(index)) return new SectorContents(SectorFeature.None, new GridCoord(0, 0), null);

            GridCoord origin = Grid.OriginOf(index);
            int half = Grid.SectorSizeCells / 2;
            var featureCell = new GridCoord(origin.X + half, origin.Y + half);

            // One in eight sectors is bare. An empty sector has to be possible, or "there is
            // something in every direction" becomes the same as "direction does not matter".
            uint featureDraw = Hash(Seed, index, FeatureSalt) % 8;
            SectorFeature feature =
                featureDraw == 0 ? SectorFeature.None :
                featureDraw <= 4 ? SectorFeature.OreCluster :
                featureDraw <= 6 ? SectorFeature.Wreck :
                                   SectorFeature.Nest;

            int depositCount = feature == SectorFeature.None ? 0 : 2 + (int)(Hash(Seed, index, DepositSalt) % 4);
            var deposits = new GridCoord[depositCount];

            for (int i = 0; i < depositCount; i++)
            {
                // Anywhere in the square, corners included - no attempt to keep them inside the
                // revealed disc. The ones that fall outside it are the point.
                uint draw = Hash(Seed, index, DepositSalt + (uint)(i + 1) * 0x9E3779B9u);
                int x = origin.X + (int)(draw % (uint)Grid.SectorSizeCells);
                int y = origin.Y + (int)(draw / 65536u % (uint)Grid.SectorSizeCells);
                deposits[i] = new GridCoord(x, y);
            }

            return new SectorContents(feature, featureCell, deposits);
        }

        /// <summary>Name, risk, centre and current discovery in one value - what a tooltip on the zoomed map needs.</summary>
        public SectorIdentity IdentityOf(int index, DiscoveryRuntime discovery)
        {
            return new SectorIdentity(
                index,
                NameOf(index),
                RiskOf(index),
                Grid?.CenterCells(index) ?? Vector2.zero,
                Grid?.DiscoveryOf(index, discovery) ?? SectorDiscovery.Unknown);
        }

        /// <summary>
        /// An integer mixer written out here rather than borrowed from string.GetHashCode or
        /// Random - both are free to change between runtimes, and this has to give the same answer
        /// in a save loaded next year as it did when the save was written.
        /// </summary>
        static uint Hash(int seed, int index, uint salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u;
                h ^= (uint)index * 2246822519u;
                h ^= salt;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
