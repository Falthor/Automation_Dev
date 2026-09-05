using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Renders the nano conversion of the ground under construction sites: one float per cell
    /// between 0 and 1, uploaded to an R8 texture and drawn by a quad sitting above the terrain and
    /// below the concrete slab.
    ///
    /// <b>One texture per zone, never one for the map.</b> A builder robot only works inside its own
    /// Core's (later, its own AI Agent's) zone, so every site belongs to exactly one zone - that
    /// guarantee is what makes the partition correct, and it is why this is not an arbitrary square
    /// tiling. A single map-wide texture would work today with one Core and two robots, and become
    /// untenable at the first Agent: it would grow with the map, sit almost entirely empty, and be
    /// re-uploaded whenever any one site moved anywhere.
    ///
    /// The per-zone quad is also what keeps the shader trivial - it has no zone to resolve and no
    /// indirection to do, it samples the texture attached to it.
    ///
    /// Read-only over gameplay, like the rest of the nano layer: it reads site footprints and the
    /// displayed progress the dissolve is already showing, and writes nothing back. In particular it
    /// takes <b>displayed</b> progress, never the raw advancement - the ground would otherwise jump
    /// in steps while the building glides.
    /// </summary>
    public sealed class GroundCoverageRenderer : MonoBehaviour
    {
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] NanoConstructionSettings settings;

        readonly Dictionary<int, Zone> _zones = new Dictionary<int, Zone>();
        readonly List<ZoneDescriptor> _zoneScratch = new List<ZoneDescriptor>();
        readonly List<ConstructionSiteVisualSync.DrawnSegment> _segmentScratch = new List<ConstructionSiteVisualSync.DrawnSegment>();
        readonly List<int> _closedScratch = new List<int>();

        GridRuntime _grid;

        /// <summary>Number of zones currently holding a texture. Exposed for tests.</summary>
        public int ZoneCount => _zones.Count;

        /// <summary>How many texture uploads have happened since this component was created. Exposed for tests: the field must only reach the GPU when it actually changed.</summary>
        public int UploadCount { get; private set; }

        /// <summary>
        /// Identifies a zone and the square of cells its texture has to cover. Sized on the largest
        /// radius the zone can ever reach rather than its current one, so research extending the
        /// radius never reallocates anything - the extra cells simply stay at zero coverage and are
        /// clipped away.
        /// </summary>
        public readonly struct ZoneDescriptor
        {
            public readonly int Id;
            public readonly GridCoord CenterCell;
            public readonly int MaxRadiusCells;

            public ZoneDescriptor(int id, GridCoord centerCell, int maxRadiusCells)
            {
                Id = id;
                CenterCell = centerCell;
                MaxRadiusCells = Mathf.Max(1, maxRadiusCells);
            }

            public int SideCells => MaxRadiusCells * 2 + 1;
            public GridCoord OriginCell => new GridCoord(CenterCell.X - MaxRadiusCells, CenterCell.Y - MaxRadiusCells);
        }

        sealed class Zone
        {
            public GridCoord OriginCell;
            public int SideCells;
            public float[] Field;
            public byte[] Pixels;
            public Texture2D Texture;
            public Material Material;
            public SpriteRenderer Quad;

            /// <summary>Set whenever a value in Field actually changed this tick; only a dirty zone is re-uploaded.</summary>
            public bool Dirty;

            /// <summary>Lets a fully faded zone skip its decay pass entirely rather than walking every cell to subtract zero.</summary>
            public bool AnyCoverage;

            public float FlashBoost;
        }

        /// <summary>Binds the grid directly instead of through the scene's GameRuntime, for EditMode tests.</summary>
        public void Initialize(GridRuntime grid, NanoConstructionSettings nanoSettings)
        {
            _grid = grid;
            settings = nanoSettings;
        }

        void LateUpdate()
        {
            if (gameRuntime == null) return;
            if (_grid == null) _grid = gameRuntime.Grid;
            if (_grid == null || gameRuntime.ConstructionSiteVisuals == null) return;

            CollectZones(_zoneScratch);
            gameRuntime.ConstructionSiteVisuals.CollectDrawnSegments(_segmentScratch);

            Tick(Time.deltaTime, _zoneScratch, _segmentScratch);
        }

        /// <summary>
        /// Today there is exactly one zone, the Core's. AI Agents will each add one; the rest of
        /// this component already treats zones as a set, so that is the only place to extend.
        /// </summary>
        void CollectZones(List<ZoneDescriptor> into)
        {
            into.Clear();

            CoreRuntime core = gameRuntime.World?.Core;
            if (core == null) return;

            Vector2Int footprint = core.Definition.FootprintSize;
            var center = new GridCoord(core.Cell.X + footprint.x / 2, core.Cell.Y + footprint.y / 2);

            // The radius is extendable by research, so the texture is sized on the ceiling rather
            // than on the current value - see ZoneDescriptor.
            into.Add(new ZoneDescriptor(core.Cell.GetHashCode(), center, CoreRuntime.ExtendedActionRadiusCells));
        }

        /// <summary>
        /// Advances every zone's field and uploads the ones that changed. Public and frame-free so
        /// the whole thing is testable without a frame loop, exactly like BuildDissolveView.Tick.
        /// </summary>
        public void Tick(float deltaTime, IReadOnlyList<ZoneDescriptor> zones, IReadOnlyList<ConstructionSiteVisualSync.DrawnSegment> segments)
        {
            if (_grid == null || settings == null) return;

            CloseZonesNotIn(zones);

            for (int i = 0; i < zones.Count; i++)
            {
                Zone zone = EnsureZone(zones[i]);
                Decay(zone, deltaTime);
                zone.FlashBoost = 0f;
            }

            WriteSegments(segments);

            foreach (var kvp in _zones)
            {
                Upload(kvp.Value);
            }
        }

        /// <summary>A zone that has fallen releases its texture, material and quad - nothing is pooled for a zone that no longer exists.</summary>
        void CloseZonesNotIn(IReadOnlyList<ZoneDescriptor> zones)
        {
            _closedScratch.Clear();

            foreach (var kvp in _zones)
            {
                bool stillOpen = false;
                for (int i = 0; i < zones.Count; i++)
                {
                    if (zones[i].Id != kvp.Key) continue;
                    stillOpen = true;
                    break;
                }
                if (!stillOpen) _closedScratch.Add(kvp.Key);
            }

            foreach (int id in _closedScratch)
            {
                Release(_zones[id]);
                _zones.Remove(id);
            }
        }

        Zone EnsureZone(ZoneDescriptor descriptor)
        {
            if (_zones.TryGetValue(descriptor.Id, out Zone existing)
                && existing.SideCells == descriptor.SideCells
                && existing.OriginCell.Equals(descriptor.OriginCell)
                && existing.Texture != null)
            {
                return existing;
            }

            if (existing != null) Release(existing);

            int side = descriptor.SideCells;
            var zone = new Zone
            {
                OriginCell = descriptor.OriginCell,
                SideCells = side,
                Field = new float[side * side],
                Pixels = new byte[side * side],
                Dirty = true
            };

            zone.Texture = new Texture2D(side, side, TextureFormat.R8, mipChain: false)
            {
                name = "GroundCoverage " + descriptor.Id,
                filterMode = FilterMode.Bilinear,   // the per-cell field only reads as continuous because of this
                wrapMode = TextureWrapMode.Clamp
            };

            zone.Material = new Material(settings.CoverageShader) { name = "GroundCoverage (Instance)" };

            var go = new GameObject("GroundCoverageZone " + descriptor.Id);
            go.transform.SetParent(transform, false);
            zone.Quad = go.AddComponent<SpriteRenderer>();
            zone.Quad.sharedMaterial = zone.Material;
            zone.Quad.sortingOrder = settings.GroundCoverageSortingOrder;

            PlaceQuad(zone);

            _zones[descriptor.Id] = zone;
            return zone;
        }

        /// <summary>
        /// The quad spans the zone's cells exactly, so a texel centre lands on a cell centre: texel
        /// i sits at UV (i + 0.5) / side, which maps back to the centre of cell i. Any other framing
        /// would offset the whole field by half a cell.
        /// </summary>
        void PlaceQuad(Zone zone)
        {
            float cellSize = _grid.CellSize;
            Vector3 originCenter = _grid.CellCenterToWorld(zone.OriginCell);
            var min = new Vector2(originCenter.x - cellSize * 0.5f, originCenter.y - cellSize * 0.5f);
            float extent = zone.SideCells * cellSize;

            zone.Quad.transform.position = new Vector3(min.x + extent * 0.5f, min.y + extent * 0.5f, 0f);
            BuildingSpawner.SetSpriteToWorldSize(zone.Quad, UnitSprite(), new Vector2(extent, extent));

            zone.Material.SetVector("_ZoneBounds", new Vector4(min.x, min.y, extent, extent));
        }

        static Sprite _unitSprite;

        /// <summary>A blank carrier: none of its own pixels are ever shown, the material supplies everything. Same trick the concrete slab uses.</summary>
        static Sprite UnitSprite()
        {
            if (_unitSprite != null) return _unitSprite;

            var texture = new Texture2D(1, 1) { name = "GroundCoverageUnit" };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            _unitSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            _unitSprite.name = "GroundCoverageUnit";
            return _unitSprite;
        }

        void Decay(Zone zone, float deltaTime)
        {
            if (!zone.AnyCoverage) return;

            float fade = settings.CoverageFadeSeconds > 0f ? deltaTime / settings.CoverageFadeSeconds : 1f;
            bool any = false;

            for (int i = 0; i < zone.Field.Length; i++)
            {
                float previous = zone.Field[i];
                if (previous <= 0f) continue;

                float next = Mathf.Max(0f, previous - fade);
                if (next != previous) zone.Dirty = true;

                zone.Field[i] = next;
                if (next > 0f) any = true;
            }

            zone.AnyCoverage = any;
        }

        void WriteSegments(IReadOnlyList<ConstructionSiteVisualSync.DrawnSegment> segments)
        {
            for (int s = 0; s < segments.Count; s++)
            {
                BuildingRuntime segment = segments[s].Segment;
                if (segment == null) continue;

                float value = Mathf.Clamp01(segments[s].DisplayedProgress);
                if (value <= 0f) continue;

                BuildingDefinition definition = segment.Definition;
                Zone zone = ZoneContaining(segment.Cell);
                if (zone == null) continue;

                // One flash per zone, not per site: the layer is one texture and one material, so
                // concurrent sites in the same zone share the brightest of their flashes. Visible
                // only when two sites in one zone take deliveries at once, which reads as a single
                // pulse rather than a wrong one.
                zone.FlashBoost = Mathf.Max(zone.FlashBoost, segments[s].FlashBoost);

                // The logical footprint, never ArtWorldSize or the sprite's AABB: this field says
                // which cells are converted, not how far the drawing reaches. Same rule as the
                // concrete slab - see BuildingSpawner.ArtWorldSize.
                foreach (Vector2Int offset in definition.FootprintCells)
                {
                    Write(zone, segment.Cell.X + offset.x, segment.Cell.Y + offset.y, value);
                }
            }
        }

        void Write(Zone zone, int cellX, int cellY, float value)
        {
            int x = cellX - zone.OriginCell.X;
            int y = cellY - zone.OriginCell.Y;
            if (x < 0 || y < 0 || x >= zone.SideCells || y >= zone.SideCells) return;

            int index = y * zone.SideCells + x;
            if (value <= zone.Field[index]) return;

            zone.Field[index] = value;
            zone.Dirty = true;
            zone.AnyCoverage = true;
        }

        Zone ZoneContaining(GridCoord cell)
        {
            foreach (var kvp in _zones)
            {
                Zone zone = kvp.Value;
                int x = cell.X - zone.OriginCell.X;
                int y = cell.Y - zone.OriginCell.Y;
                if (x >= 0 && y >= 0 && x < zone.SideCells && y < zone.SideCells) return zone;
            }
            return null;
        }

        void Upload(Zone zone)
        {
            PushMaterialSettings(zone);

            if (!zone.Dirty) return;

            for (int i = 0; i < zone.Field.Length; i++)
            {
                zone.Pixels[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(zone.Field[i]) * 255f);
            }

            zone.Texture.SetPixelData(zone.Pixels, 0);
            zone.Texture.Apply(updateMipmaps: false);
            zone.Material.SetTexture("_CoverageTex", zone.Texture);

            zone.Dirty = false;
            UploadCount++;
        }

        /// <summary>Re-read every tick so the settings asset can be tuned live during play, exactly like the dissolve.</summary>
        void PushMaterialSettings(Zone zone)
        {
            zone.Material.SetColor("_Tint", settings.GroundRimColor);
            zone.Material.SetFloat("_Intensity", settings.GroundIntensity);
            zone.Material.SetColor("_RimColor", settings.GroundRimColor);
            zone.Material.SetFloat("_RimIntensity", settings.GroundRimIntensity);
            zone.Material.SetFloat("_RimWidth", settings.GroundRimWidth);
            zone.Material.SetFloat("_RimBoost", zone.FlashBoost);
            zone.Quad.sortingOrder = settings.GroundCoverageSortingOrder;
        }

        /// <summary>True when this zone's field changed since its last upload. Exposed for tests.</summary>
        public bool IsDirty(int zoneId) => _zones.TryGetValue(zoneId, out Zone zone) && zone.Dirty;

        /// <summary>Coverage currently stored for a cell, 0 outside every zone. Exposed for tests.</summary>
        public float CoverageAt(GridCoord cell)
        {
            Zone zone = ZoneContaining(cell);
            if (zone == null) return 0f;

            int x = cell.X - zone.OriginCell.X;
            int y = cell.Y - zone.OriginCell.Y;
            return zone.Field[y * zone.SideCells + x];
        }

        void OnDestroy()
        {
            foreach (var kvp in _zones) Release(kvp.Value);
            _zones.Clear();
        }

        void Release(Zone zone)
        {
            DestroyOwned(zone.Texture);
            DestroyOwned(zone.Material);
            if (zone.Quad != null) DestroyOwned(zone.Quad.gameObject);

            zone.Texture = null;
            zone.Material = null;
            zone.Quad = null;
        }

        /// <summary>Destroy takes effect next frame in play mode and is an error outside it, so the two cases are split - same as BuildDissolveView.</summary>
        static void DestroyOwned(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
