using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Renders the nano conversion of the ground under construction sites: a signed distance to the
    /// conversion front, uploaded to an R8 texture and drawn by a quad sitting above the terrain and
    /// below the concrete slab.
    ///
    /// <b>The field is not bounded by the footprint.</b> A threshold that stops at the footprint's
    /// edge makes the rectangle itself the outer boundary, so the finished shape is a square however
    /// the front travels inside it. The threshold therefore keeps rising through the ring of cells
    /// around the building, and it is the threshold plus the noise - not the edge of a rectangle -
    /// that decides where the conversion stops. The patch spills a little onto the neighbouring
    /// cells by design, which also narrows the gap with a building whose art already overhangs.
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
        /// <summary>
        /// Ground progress at which the front has just finished crossing the footprint - every one
        /// of its points, corners included - and starts spilling into the ring around it. A constant
        /// rather than a setting: it is a proportion of the animation, so it needs no retuning per
        /// building, and the two knobs that do change the look - groundOverflowCells and
        /// groundNoiseWeight - are enough to shape the halo.
        /// </summary>
        const float FootprintShare = 0.8f;

        /// <summary>
        /// Ceiling on the noise amplitude, so that a footprint point sitting at FootprintShare can
        /// never be pushed past 1 by the noise. Without it the ground's phase could end with unlit
        /// specks left in the corners of a large footprint - the field has to reach 1 <b>everywhere</b>
        /// on the footprint by the end of its own phase, and that has to hold by construction rather
        /// than by arithmetic luck at the current settings.
        /// </summary>
        const float MaxNoiseWeight = 2f * (1f - FootprintShare);

        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] NanoConstructionSettings settings;

        readonly Dictionary<int, Zone> _zones = new Dictionary<int, Zone>();
        readonly Dictionary<BuildingRuntime, Patch> _patches = new Dictionary<BuildingRuntime, Patch>();
        readonly List<BuildingRuntime> _expiredScratch = new List<BuildingRuntime>();
        readonly List<ZoneDescriptor> _zoneScratch = new List<ZoneDescriptor>();
        readonly List<ConstructionSiteVisualSync.DrawnSegment> _segmentScratch = new List<ConstructionSiteVisualSync.DrawnSegment>();
        readonly List<int> _closedScratch = new List<int>();

        GridRuntime _grid;

        /// <summary>Set when a zone is created or resized, so the patches are written into its blank field even on a tick where nothing else moved.</summary>
        bool _fieldStale;

        /// <summary>Number of zones currently holding a texture. Exposed for tests.</summary>
        public int ZoneCount => _zones.Count;

        /// <summary>How many texture uploads have happened since this component was created. Exposed for tests: the field must only reach the GPU when it actually changed.</summary>
        public int UploadCount { get; private set; }

        /// <summary>
        /// Identifies a zone and the square of cells its texture has to cover. Sized on the largest
        /// radius the zone can ever reach rather than its current one, so research extending the
        /// radius never reallocates anything - the extra cells simply stay unconverted and are
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

        /// <summary>
        /// One converting patch. A plain snapshot rather than a reference to the site, because a
        /// patch outlives the segment that spawned it: when the segment stops being drawn the patch
        /// keeps living with a falling progress, which is what makes the front <b>retreat</b> the way
        /// it advanced instead of the whole plateau dimming at once.
        /// </summary>
        sealed class Patch
        {
            public GridCoord Origin;
            public Vector2Int Size;
            public float Progress;
            public float Flash;
            public bool Live;
        }

        sealed class Zone
        {
            public GridCoord OriginCell;
            public int SideCells;
            public int TexelsPerCell;
            public int TexelSide;

            /// <summary>Encoded signed distance to the front, one byte per texel. 0 is "far outside", 128 is the front itself.</summary>
            public byte[] Field;

            /// <summary>World position of the zone's bottom-left corner - the noise is sampled in world space, so every patch needs it.</summary>
            public Vector2 MinWorld;

            /// <summary>Texel rectangles written last rebuild, and therefore the only ones that have to be cleared at the next one.</summary>
            public readonly List<RectInt> Written = new List<RectInt>();

            public Texture2D Texture;
            public Material Material;
            public SpriteRenderer Quad;

            /// <summary>Set whenever a byte in Field actually changed; only a dirty zone is re-uploaded.</summary>
            public bool Dirty;

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
        /// Advances every patch and rewrites the zones that changed. Public and frame-free so the
        /// whole thing is testable without a frame loop, exactly like BuildDissolveView.Tick.
        /// </summary>
        public void Tick(float deltaTime, IReadOnlyList<ZoneDescriptor> zones, IReadOnlyList<ConstructionSiteVisualSync.DrawnSegment> segments)
        {
            if (_grid == null || settings == null) return;

            CloseZonesNotIn(zones);
            for (int i = 0; i < zones.Count; i++) EnsureZone(zones[i]);

            bool changed = UpdatePatches(deltaTime, segments);

            // A rebuild is the only thing that touches texels, so it is skipped outright when no
            // patch appeared, moved or advanced - which is every frame of a base that is not
            // building anything.
            if (changed || _fieldStale) Rebuild();
            _fieldStale = false;

            UpdateFlashes();

            foreach (var kvp in _zones) Upload(kvp.Value);
        }

        // --- Patches ---

        /// <summary>
        /// Brings the patch set in line with what is drawn, and fades out the ones that are not.
        /// Returns whether anything the field depends on moved.
        /// </summary>
        bool UpdatePatches(float deltaTime, IReadOnlyList<ConstructionSiteVisualSync.DrawnSegment> segments)
        {
            bool changed = false;

            foreach (var kvp in _patches) kvp.Value.Live = false;

            for (int i = 0; i < segments.Count; i++)
            {
                BuildingRuntime segment = segments[i].Segment;
                if (segment == null) continue;

                if (!_patches.TryGetValue(segment, out Patch patch))
                {
                    patch = new Patch();
                    _patches[segment] = patch;
                    changed = true;
                }

                // The ground runs ahead of the building, and finishes well before it. It has to:
                // the sprite covers its own footprint, so a ground on the same clock is hidden for
                // the whole build and only ever shows as a thin halo in the last instants.
                float progress = settings.GroundProgressFor(segments[i].DisplayedProgress);
                if (patch.Progress != progress) changed = true;

                // The logical footprint, never ArtWorldSize or the sprite's AABB: this field says
                // which cells are converted, not how far the drawing reaches. Same rule as the
                // concrete slab - see BuildingSpawner.ArtWorldSize.
                patch.Origin = segment.Cell;
                patch.Size = segment.Definition.FootprintSize;
                patch.Progress = progress;
                patch.Flash = segments[i].FlashBoost;
                patch.Live = true;
            }

            float fade = settings.CoverageFadeSeconds > 0f ? deltaTime / settings.CoverageFadeSeconds : 1f;
            _expiredScratch.Clear();

            foreach (var kvp in _patches)
            {
                Patch patch = kvp.Value;
                if (patch.Live) continue;

                // Fading is a falling progress, not a dimming field. Subtracting from the stored
                // values instead would take the whole converted plateau down through the rim band
                // together and flash the entire patch on its way out; a falling progress walks the
                // front back to the centre, rim included, exactly the way it came.
                patch.Flash = 0f;
                float next = Mathf.Max(0f, patch.Progress - fade);
                if (next != patch.Progress) changed = true;
                patch.Progress = next;

                if (next <= 0f) _expiredScratch.Add(kvp.Key);
            }

            for (int i = 0; i < _expiredScratch.Count; i++) _patches.Remove(_expiredScratch[i]);
            if (_expiredScratch.Count > 0) changed = true;

            return changed;
        }

        void Rebuild()
        {
            foreach (var kvp in _zones) ClearWritten(kvp.Value);

            foreach (var kvp in _patches)
            {
                Patch patch = kvp.Value;
                if (patch.Progress <= 0f) continue;

                Zone zone = ZoneContaining(patch.Origin);
                if (zone == null) continue;

                WritePatch(zone, patch);
            }
        }

        /// <summary>
        /// Only the texels written last time can hold a stale value, so the field is cleared through
        /// that list rather than wholesale - a zone is 260x260 texels and almost always empty.
        /// </summary>
        static void ClearWritten(Zone zone)
        {
            for (int r = 0; r < zone.Written.Count; r++)
            {
                RectInt rect = zone.Written[r];
                for (int y = rect.yMin; y < rect.yMax; y++)
                {
                    int row = y * zone.TexelSide;
                    for (int x = rect.xMin; x < rect.xMax; x++)
                    {
                        if (zone.Field[row + x] == 0) continue;
                        zone.Field[row + x] = 0;
                        zone.Dirty = true;
                    }
                }
            }

            zone.Written.Clear();
        }

        /// <summary>
        /// One flash per zone, not per site: the layer is one texture and one material, so
        /// concurrent sites in the same zone share the brightest of their flashes. Visible only when
        /// two sites in one zone take deliveries at once, which reads as a single pulse rather than
        /// a wrong one. Kept out of the rebuild because a flash never changes a single texel.
        /// </summary>
        void UpdateFlashes()
        {
            foreach (var kvp in _zones) kvp.Value.FlashBoost = 0f;

            foreach (var kvp in _patches)
            {
                Patch patch = kvp.Value;
                if (patch.Flash <= 0f) continue;

                Zone zone = ZoneContaining(patch.Origin);
                if (zone == null) continue;

                zone.FlashBoost = Mathf.Max(zone.FlashBoost, patch.Flash);
            }
        }

        // --- The field ---

        void WritePatch(Zone zone, Patch patch)
        {
            int texels = zone.TexelsPerCell;
            float halfX = Mathf.Max(patch.Size.x, 1) * 0.5f;
            float halfY = Mathf.Max(patch.Size.y, 1) * 0.5f;
            float inner = Mathf.Min(halfX, halfY);
            float round = inner * 0.5f;
            float overflow = Mathf.Max(settings.GroundOverflowCells, 0.01f);
            float noiseWeight = Mathf.Min(settings.GroundNoiseWeight, MaxNoiseWeight);
            float noiseScale = settings.GroundNoiseScale;
            float cellSize = _grid.CellSize;

            // Distance to the furthest point of the footprint itself. The threshold is normalised on
            // it rather than on the outline, so FootprintShare lands exactly on the corners and the
            // whole footprint is converted by then whatever its size. Normalising on the outline
            // instead leaves the corner region past FootprintShare, and past 1 outright on a large
            // enough footprint - the corners would then never convert at all.
            float corner = RoundedBoxDistance(halfX, halfY, halfX, halfY, round);

            // Far enough that the outermost written texel is unconverted even at full progress and
            // with the noise pushing the boundary outwards, plus one cell for the bilinear falloff
            // to reach zero. Anything short of that would put a straight edge back in the picture.
            int pad = Mathf.CeilToInt(overflow * (1f + 2.5f * noiseWeight)) + 1;

            int cellMinX = patch.Origin.X - zone.OriginCell.X - pad;
            int cellMinY = patch.Origin.Y - zone.OriginCell.Y - pad;

            int texMinX = Mathf.Clamp(cellMinX * texels, 0, zone.TexelSide);
            int texMinY = Mathf.Clamp(cellMinY * texels, 0, zone.TexelSide);
            int texMaxX = Mathf.Clamp((cellMinX + patch.Size.x + 2 * pad) * texels, 0, zone.TexelSide);
            int texMaxY = Mathf.Clamp((cellMinY + patch.Size.y + 2 * pad) * texels, 0, zone.TexelSide);
            if (texMaxX <= texMinX || texMaxY <= texMinY) return;

            zone.Written.Add(new RectInt(texMinX, texMinY, texMaxX - texMinX, texMaxY - texMinY));

            // Centre of the footprint, in cells from the zone's bottom-left corner.
            float centerX = patch.Origin.X - zone.OriginCell.X + halfX;
            float centerY = patch.Origin.Y - zone.OriginCell.Y + halfY;

            for (int ty = texMinY; ty < texMaxY; ty++)
            {
                float py = (ty + 0.5f) / texels;
                float worldY = (zone.MinWorld.y + py * cellSize) * noiseScale;
                int row = ty * zone.TexelSide;

                for (int tx = texMinX; tx < texMaxX; tx++)
                {
                    float px = (tx + 0.5f) / texels;

                    float threshold = Threshold(px - centerX, py - centerY, halfX, halfY, round, inner, corner, overflow);

                    if (noiseWeight > 0f)
                    {
                        float worldX = (zone.MinWorld.x + px * cellSize) * noiseScale;
                        threshold += (ValueNoise(worldX, worldY) - 0.5f) * noiseWeight;
                    }

                    float distance = patch.Progress - Mathf.Max(threshold, 0f);
                    if (distance <= -1f) continue;

                    byte encoded = Encode(distance);
                    int index = row + tx;
                    if (encoded <= zone.Field[index]) continue;

                    zone.Field[index] = encoded;
                    zone.Dirty = true;
                }
            }
        }

        /// <summary>
        /// The static threshold a point has to be reached for: 0 at the footprint's centre,
        /// <see cref="FootprintShare"/> at its corners - the last of its own points the front
        /// reaches - and 1 a full groundOverflowCells beyond them.
        ///
        /// The shape is the exact distance to a rounded rectangle, so the front is round inside the
        /// footprint (a plain box distance would grow a square) and keeps rising smoothly outside it
        /// (a threshold clamped at the outline would make the rectangle the final shape, whatever
        /// happens inside).
        /// </summary>
        static float Threshold(float dx, float dy, float halfX, float halfY, float round, float inner, float corner, float overflow)
        {
            float sdf = RoundedBoxDistance(dx, dy, halfX, halfY, round);

            return sdf <= corner
                ? FootprintShare * (sdf + inner) / (inner + corner)
                : FootprintShare + (1f - FootprintShare) * ((sdf - corner) / overflow);
        }

        /// <summary>Signed distance in cells to a rectangle with rounded corners: negative inside, reaching -min(halfX, halfY) at the centre.</summary>
        static float RoundedBoxDistance(float dx, float dy, float halfX, float halfY, float round)
        {
            float qx = Mathf.Abs(dx) - (halfX - round);
            float qy = Mathf.Abs(dy) - (halfY - round);

            float outsideX = Mathf.Max(qx, 0f);
            float outsideY = Mathf.Max(qy, 0f);
            float outside = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);

            return outside + inside - round;
        }

        /// <summary>
        /// Dave Hoskins' "hash without sine", the same pair Custom/BuildDissolve and
        /// Custom/ShadedGroundTiled already use, so the ground's grain belongs to the same family as
        /// the building's. Sampled in <b>world</b> space for the same reason as the dissolve's: two
        /// neighbouring sites share one continuous field instead of restarting the same pattern.
        /// </summary>
        static float Hash21(float x, float y)
        {
            float px = Frac(x * 0.1031f);
            float py = Frac(y * 0.1031f);
            float pz = px;

            float d = px * (py + 33.33f) + py * (pz + 33.33f) + pz * (px + 33.33f);
            px += d;
            py += d;
            pz += d;

            return Frac((px + py) * pz);
        }

        /// <summary>
        /// One octave, unlike the dissolve's three: the field is quantised to groundTexelsPerCell
        /// texels per cell, so octaves finer than that alias instead of adding detail.
        /// </summary>
        static float ValueNoise(float x, float y)
        {
            float ix = Mathf.Floor(x);
            float iy = Mathf.Floor(y);
            float fx = x - ix;
            float fy = y - iy;

            float a = Hash21(ix, iy);
            float b = Hash21(ix + 1f, iy);
            float c = Hash21(ix, iy + 1f);
            float d = Hash21(ix + 1f, iy + 1f);

            float ux = fx * fx * (3f - 2f * fx);
            float uy = fy * fy * (3f - 2f * fy);

            return Mathf.Lerp(a, b, ux) + (c - a) * uy * (1f - ux) + (d - b) * ux * uy;
        }

        static float Frac(float value) => value - Mathf.Floor(value);

        /// <summary>Signed distance to the front, [-1, 1], packed into a byte. 128 is the front, so the shader can clip on it without knowing anything about thresholds.</summary>
        static byte Encode(float distance) => (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f + 0.5f * distance) * 255f);

        static float Decode(byte value) => value / 255f * 2f - 1f;

        // --- Zones ---

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
            int texels = Mathf.Max(1, settings.GroundTexelsPerCell);

            if (_zones.TryGetValue(descriptor.Id, out Zone existing)
                && existing.SideCells == descriptor.SideCells
                && existing.TexelsPerCell == texels
                && existing.OriginCell.Equals(descriptor.OriginCell)
                && existing.Texture != null)
            {
                return existing;
            }

            if (existing != null) Release(existing);

            int side = descriptor.SideCells;
            int texelSide = side * texels;

            var zone = new Zone
            {
                OriginCell = descriptor.OriginCell,
                SideCells = side,
                TexelsPerCell = texels,
                TexelSide = texelSide,
                Field = new byte[texelSide * texelSide],
                Dirty = true
            };

            zone.Texture = new Texture2D(texelSide, texelSide, TextureFormat.R8, mipChain: false)
            {
                name = "GroundCoverage " + descriptor.Id,
                filterMode = FilterMode.Bilinear,   // the quantised field only reads as continuous because of this
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
            _fieldStale = true;
            return zone;
        }

        /// <summary>
        /// The quad spans the zone's cells exactly, so a texel centre lands where the field says it
        /// does: texel i sits at UV (i + 0.5) / texelSide, which maps back to cell-space
        /// (i + 0.5) / texelsPerCell - the coordinate WritePatch computes its threshold at. Any
        /// other framing would offset the whole field.
        /// </summary>
        void PlaceQuad(Zone zone)
        {
            float cellSize = _grid.CellSize;
            Vector3 originCenter = _grid.CellCenterToWorld(zone.OriginCell);
            var min = new Vector2(originCenter.x - cellSize * 0.5f, originCenter.y - cellSize * 0.5f);
            float extent = zone.SideCells * cellSize;

            zone.MinWorld = min;
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

            zone.Texture.SetPixelData(zone.Field, 0);
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

        // --- Test seams ---

        /// <summary>True when this zone's field changed since its last upload. Exposed for tests.</summary>
        public bool IsDirty(int zoneId) => _zones.TryGetValue(zoneId, out Zone zone) && zone.Dirty;

        /// <summary>
        /// Signed distance to the conversion front at a cell's centre, positive once converted.
        /// Averaged over the texels straddling that centre - an even groundTexelsPerCell has no
        /// single central texel, and picking one of the two would read a point off to one side and
        /// make the field look asymmetric when it is not. Returns -1 outside every zone. Exposed for
        /// tests.
        /// </summary>
        public float FrontDistanceAt(GridCoord cell)
        {
            Zone zone = ZoneContaining(cell);
            if (zone == null) return -1f;

            int texels = zone.TexelsPerCell;
            int low = (texels - 1) / 2;
            int high = texels / 2;

            int baseX = (cell.X - zone.OriginCell.X) * texels;
            int baseY = (cell.Y - zone.OriginCell.Y) * texels;

            float total = 0f;
            int count = 0;

            for (int y = baseY + low; y <= baseY + high; y++)
            {
                if (y < 0 || y >= zone.TexelSide) continue;
                for (int x = baseX + low; x <= baseX + high; x++)
                {
                    if (x < 0 || x >= zone.TexelSide) continue;
                    total += Decode(zone.Field[y * zone.TexelSide + x]);
                    count++;
                }
            }

            return count == 0 ? -1f : total / count;
        }

        /// <summary>Whether the front has passed a cell's centre. Exposed for tests.</summary>
        public bool IsConvertedAt(GridCoord cell) => FrontDistanceAt(cell) > 0f;

        /// <summary>
        /// Signed distance to the front at an arbitrary world point, from the single texel that
        /// holds it - no interpolation, unlike the shader. Exposed for tests: it is the only way to
        /// observe that the field really is finer than one value per cell.
        /// </summary>
        public float FrontDistanceAtWorld(Vector2 world)
        {
            foreach (var kvp in _zones)
            {
                Zone zone = kvp.Value;
                float localX = (world.x - zone.MinWorld.x) / _grid.CellSize;
                float localY = (world.y - zone.MinWorld.y) / _grid.CellSize;

                int tx = Mathf.FloorToInt(localX * zone.TexelsPerCell);
                int ty = Mathf.FloorToInt(localY * zone.TexelsPerCell);
                if (tx < 0 || ty < 0 || tx >= zone.TexelSide || ty >= zone.TexelSide) continue;

                return Decode(zone.Field[ty * zone.TexelSide + tx]);
            }

            return -1f;
        }

        /// <summary>Side of a zone's texture, in texels. Exposed for tests.</summary>
        public int TexelSideOf(int zoneId) => _zones.TryGetValue(zoneId, out Zone zone) ? zone.TexelSide : 0;

        /// <summary>
        /// Smallest front distance over every texel of a footprint - not just its cell centres, which
        /// is where the field is at its most converted. This is the only way to state the guarantee
        /// the ground's phase owes: at its end the field is past the front <b>everywhere</b> on the
        /// footprint, corners included. Exposed for tests.
        /// </summary>
        public float MinFrontDistanceOver(GridCoord origin, Vector2Int size)
        {
            Zone zone = ZoneContaining(origin);
            if (zone == null) return -1f;

            int texels = zone.TexelsPerCell;
            int baseX = (origin.X - zone.OriginCell.X) * texels;
            int baseY = (origin.Y - zone.OriginCell.Y) * texels;

            float min = float.MaxValue;

            for (int y = baseY; y < baseY + size.y * texels; y++)
            {
                if (y < 0 || y >= zone.TexelSide) continue;
                for (int x = baseX; x < baseX + size.x * texels; x++)
                {
                    if (x < 0 || x >= zone.TexelSide) continue;
                    min = Mathf.Min(min, Decode(zone.Field[y * zone.TexelSide + x]));
                }
            }

            return min == float.MaxValue ? -1f : min;
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
            zone.Written.Clear();
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
