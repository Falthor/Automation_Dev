using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Sites;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws every construction site segment through its three visual states, and owns the handover
    /// to the real building view:
    ///
    /// <list type="number">
    /// <item><b>Pending</b> - nothing delivered for this segment yet: a blue silhouette of the
    /// building it will become, at full siteTint alpha, with its sprite entirely clipped away.</item>
    /// <item><b>Assembling</b> - material is arriving: the silhouette drops to
    /// NanoConstructionSettings.SitePlaceholderAlpha and the real sprite materialises over it under
    /// BuildDissolveView.</item>
    /// <item><b>Complete</b> - the dissolve reaches 1: both objects are destroyed and
    /// BuildingSpawner.SpawnView draws the real thing, in the same call so nothing flickers.</item>
    /// </list>
    ///
    /// The assembling set deliberately <b>outlives the site</b>. A segment materialises the instant
    /// its last item lands, which is long before it has finished assembling on screen; from that
    /// moment it is a real registered building and has left ConstructionSiteSystem's pending range.
    /// Detached entries are therefore kept here, driven at target 1, and only released when the
    /// dissolve completes. Their liveness is the grid instead of the site: an entry whose cell no
    /// longer holds it was demolished or overtaken mid-assembly, and is dropped without a handover.
    ///
    /// Purely a view over runtime state, on the same pooled-view/LateUpdate model as ItemVisualSync
    /// - it owns no gameplay state and decides nothing. It does not own a BuildingSpawner either:
    /// ConstructionInputAdapter hands it the one spawner of the scene through SetViewSpawner, since
    /// a second spawner would keep its own per-cell view dictionary and demolition would stop
    /// finding views created by the other.
    /// </summary>
    public sealed class ConstructionSiteVisualSync : MonoBehaviour
    {
        /// <summary>Fallback silhouette order used only when no NanoConstructionSettings is assigned; otherwise settings.SiteSilhouetteSortingOrder, which sits under the drop shadow and the sprite.</summary>
        const int FallbackSilhouetteSortingOrder = 10;

        /// <summary>Matches BuildingSpawner.StandardSortingOrder: the assembling sprite stands exactly where the real one will, so the handover changes nothing on screen.</summary>
        const int AssemblySortingOrder = 10;

        [SerializeField] GameRuntime gameRuntime;

        /// <summary>Multiplied over the building's own art, so what shows is a blue-shadowed silhouette of the real thing rather than a flat rectangle.</summary>
        [SerializeField] Color siteTint = new Color(0.35f, 0.6f, 1f, 0.6f);

        /// <summary>Appearance of the dissolve, shared with BuildDissolveView. Null disables the assembling state entirely - segments then jump from silhouette to real view, the behaviour that predates the nano materialisation.</summary>
        [SerializeField] NanoConstructionSettings settings;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();
        readonly Dictionary<BuildingRuntime, SegmentView> _views = new Dictionary<BuildingRuntime, SegmentView>();
        readonly HashSet<BuildingRuntime> _liveKeys = new HashSet<BuildingRuntime>();
        readonly List<BuildingRuntime> _scratch = new List<BuildingRuntime>();

        System.Action<BuildingRuntime> _spawnRealView;
        ConstructionSiteSystem _sites;
        GridRuntime _grid;
        bool _subscribed;

        /// <summary>
        /// True when this component takes responsibility for a segment's view after materialisation.
        /// ConstructionInputAdapter reads it to decide whether to spawn the real view immediately
        /// (no dissolve configured) or to let the assembly finish first.
        /// </summary>
        public bool AssemblesMaterializedSegments => settings != null && settings.DissolveShader != null;

        /// <summary>Number of segments currently assembling, materialized ones included. Exposed for tests.</summary>
        public int AssemblingCount
        {
            get
            {
                int count = 0;
                foreach (var kvp in _views)
                {
                    if (kvp.Value.IsAssembling) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// True while this segment still has assembling objects of its own - pending or detached.
        /// Callers that would otherwise spawn or refresh a real view must stand down: this component
        /// re-reads the segment every frame and will spawn it once, at the handover.
        /// </summary>
        public bool Draws(BuildingRuntime segment) => segment != null && _views.ContainsKey(segment);

        /// <summary>
        /// The scene's single BuildingSpawner.SpawnView, handed over by ConstructionInputAdapter.
        /// Called once per segment, at the instant its dissolve finishes.
        /// </summary>
        public void SetViewSpawner(System.Action<BuildingRuntime> spawnRealView) => _spawnRealView = spawnRealView;

        /// <summary>
        /// The blue silhouette drawn for this segment, null when none. A view accessor, also what
        /// the EditMode tests read to assert the three states apart.
        /// </summary>
        public SpriteRenderer SilhouetteOf(BuildingRuntime segment)
            => segment != null && _views.TryGetValue(segment, out SegmentView view) ? view.Silhouette : null;

        /// <summary>The dissolve assembling over the silhouette, null while none exists (no settings) or once it has completed.</summary>
        public BuildDissolveView DissolveOf(BuildingRuntime segment)
            => segment != null && _views.TryGetValue(segment, out SegmentView view) ? view.Dissolve : null;

        /// <summary>
        /// Every segment this component is currently drawing, with the progress actually shown for
        /// it - the smoothed value, never the site's raw advancement, so a layer reading this stays
        /// in step with the sprite instead of jumping at each delivery.
        ///
        /// This is the only place a materialized-but-still-assembling segment can be enumerated:
        /// those have left ConstructionSiteSystem.Sites while still being drawn here. Fills a
        /// caller-owned list, so a per-frame reader allocates nothing.
        /// </summary>
        public void CollectDrawnSegments(List<DrawnSegment> into)
        {
            into.Clear();

            foreach (var kvp in _views)
            {
                SegmentView view = kvp.Value;

                // A null Dissolve beside a live AssemblyRenderer means the effect completed and
                // removed itself, so the segment is fully formed; with no AssemblyRenderer at all
                // there is no dissolve configured and nothing has been assembled.
                float displayed;
                float flash = 0f;
                if (view.Dissolve != null)
                {
                    displayed = view.Dissolve.DisplayedProgress;
                    flash = view.Dissolve.CurrentFlashBoost();
                }
                else displayed = view.AssemblyRenderer != null ? 1f : 0f;

                into.Add(new DrawnSegment(kvp.Key, displayed, flash));
            }
        }

        /// <summary>A segment being drawn, paired with the progress shown for it and its current delivery flash.</summary>
        public readonly struct DrawnSegment
        {
            public readonly BuildingRuntime Segment;
            public readonly float DisplayedProgress;

            /// <summary>The dissolve's current flash, so the ground layer can pulse on the same delivery rather than computing its own.</summary>
            public readonly float FlashBoost;

            public DrawnSegment(BuildingRuntime segment, float displayedProgress, float flashBoost = 0f)
            {
                Segment = segment;
                DisplayedProgress = displayedProgress;
                FlashBoost = flashBoost;
            }
        }

        /// <summary>
        /// Binds the two runtime systems directly instead of through the scene's GameRuntime, for
        /// EditMode tests - which have no GameRuntime to build and no frame loop to run LateUpdate.
        /// </summary>
        public void Initialize(ConstructionSiteSystem sites, GridRuntime grid, System.Action<BuildingRuntime> spawnRealView = null)
        {
            _sites = sites;
            _grid = grid;
            if (spawnRealView != null) _spawnRealView = spawnRealView;
        }

        void LateUpdate() => Tick();

        /// <summary>Brings every site view in step with runtime state. Public and frame-free so the state machine is testable without a frame loop, exactly like BuildDissolveView.Tick.</summary>
        public void Tick()
        {
            if (!Resolve()) return;

            Subscribe();
            SyncPendingSegments();
            SyncDetachedSegments();
            RemoveStaleViews();
        }

        bool Resolve()
        {
            if (_sites == null && gameRuntime != null) _sites = gameRuntime.ConstructionSites;
            if (_grid == null && gameRuntime != null) _grid = gameRuntime.Grid;
            return _sites != null && _grid != null;
        }

        void Subscribe()
        {
            if (_subscribed) return;
            _sites.SegmentMaterialized += OnSegmentMaterialized;
            _subscribed = true;
        }

        void OnDestroy()
        {
            if (!_subscribed || _sites == null) return;
            _sites.SegmentMaterialized -= OnSegmentMaterialized;
        }

        /// <summary>
        /// A segment just received its full cost. It is already a real building elsewhere, but on
        /// screen it is only as far along as its dissolve says, so its view detaches from the site
        /// and keeps assembling at target 1 rather than being destroyed with the pending range.
        /// </summary>
        void OnSegmentMaterialized(BuildingRuntime segment)
        {
            if (!_views.TryGetValue(segment, out SegmentView view)) return;

            view.Detached = true;
            if (view.Dissolve != null) view.Dissolve.TargetProgress = 1f;
        }

        void SyncPendingSegments()
        {
            _liveKeys.Clear();

            foreach (ConstructionSiteRuntime site in _sites.Sites)
            {
                for (int i = site.MaterializedCount; i < site.Segments.Count; i++)
                {
                    BuildingRuntime segment = site.Segments[i];
                    _liveKeys.Add(segment);

                    SegmentView view = EnsureView(segment);
                    if (view.Dissolve != null) view.Dissolve.TargetProgress = site.SegmentProgress(i);
                    SyncAppearance(view, segment);
                }
            }
        }

        /// <summary>
        /// Segments that materialised while still assembling. They are no longer in any site's
        /// pending range, so liveness comes from the grid: an entry whose own cell no longer holds
        /// it was demolished or overtaken, and goes away without ever becoming a real view.
        /// </summary>
        void SyncDetachedSegments()
        {
            _scratch.Clear();

            foreach (var kvp in _views)
            {
                if (!kvp.Value.Detached) continue;
                _scratch.Add(kvp.Key);
            }

            foreach (BuildingRuntime segment in _scratch)
            {
                SegmentView view = _views[segment];

                if (!ReferenceEquals(_grid.GetOccupant(segment.Cell), segment))
                {
                    Discard(segment, view);
                    continue;
                }

                // Null means BuildDissolveView already completed and destroyed itself; both that
                // and IsComplete mean the sprite is whole and the real view can take over.
                if (view.Dissolve == null || view.Dissolve.IsComplete)
                {
                    HandOver(segment, view);
                    continue;
                }

                SyncAppearance(view, segment);
            }
        }

        /// <summary>
        /// The single-frame switch: the real view is spawned and the assembling objects are
        /// destroyed in the same call, so there is never a frame with both or with neither. The
        /// dissolve sprite already carries BuildingSpawner's own position, size and rotation, so
        /// nothing moves or resizes at the swap.
        /// </summary>
        void HandOver(BuildingRuntime segment, SegmentView view)
        {
            _spawnRealView?.Invoke(segment);
            Discard(segment, view);
        }

        void Discard(BuildingRuntime segment, SegmentView view)
        {
            view.Destroy();
            _views.Remove(segment);
        }

        SegmentView EnsureView(BuildingRuntime segment)
        {
            if (_views.TryGetValue(segment, out SegmentView existing) && existing.Silhouette != null) return existing;

            var view = new SegmentView { Silhouette = NewRenderer($"ConstructionSite {segment.Cell}", SilhouetteSortingOrder) };

            if (AssemblesMaterializedSegments)
            {
                view.AssemblyRenderer = NewRenderer($"ConstructionAssembly {segment.Cell}", AssemblySortingOrder);
                view.Dissolve = view.AssemblyRenderer.gameObject.AddComponent<BuildDissolveView>();
                view.Dissolve.Settings = settings;

                // The logical footprint, not the sprite's AABB: assembly speed is per cell covered,
                // and a sprite that deliberately overflows its footprint must not slow it down.
                Vector2Int footprint = segment.Definition.FootprintSize;
                view.Dissolve.FootprintCells = footprint.x * footprint.y;
            }

            _views[segment] = view;
            return view;
        }

        int SilhouetteSortingOrder => settings != null ? settings.SiteSilhouetteSortingOrder : FallbackSilhouetteSortingOrder;

        SpriteRenderer NewRenderer(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        void SyncAppearance(SegmentView view, BuildingRuntime segment)
        {
            BuildingDefinition definition = segment.Definition;
            Sprite sprite = ResolveSprite(segment, definition);
            Vector3 position = segment is ConveyorRuntime
                ? _grid.CellCenterToWorld(segment.Cell)
                : _grid.FootprintCenterToWorld(segment.Cell, definition.FootprintSize);

            SpriteRenderer silhouette = view.Silhouette;
            silhouette.color = SilhouetteColor(view);
            silhouette.sortingOrder = SilhouetteSortingOrder;
            silhouette.transform.position = position;
            ApplySizing(silhouette, sprite, segment, definition);
            ApplyRotation(silhouette.transform, segment, definition);

            if (view.AssemblyRenderer == null) return;

            SpriteRenderer assembly = view.AssemblyRenderer;
            assembly.transform.position = position;
            ApplySizing(assembly, sprite, segment, definition);
            ApplyRotation(assembly.transform, segment, definition);
        }

        /// <summary>
        /// Exactly the size the real view will be, and the same for both renderers - so the
        /// silhouette sits precisely under the sprite assembling over it, and nothing changes
        /// dimension at the handover.
        ///
        /// Always a uniform fit, preserving the art's aspect ratio, which is what every other art
        /// path does - RenderOverscan included, without which the Foundry's silhouette came out 9%
        /// too small. A per-axis stretch is a no-op on square art and squashes a sprite deliberately
        /// drawn taller than its footprint into a square.
        ///
        /// The one thing a conveyor changes is the overscan, not the fit: a belt wearing the
        /// procedural placeholder already fills its cell exactly.
        /// </summary>
        void ApplySizing(SpriteRenderer renderer, Sprite sprite, BuildingRuntime segment, BuildingDefinition definition)
        {
            bool overscanned = !(segment is ConveyorRuntime) || UsesOwnConveyorArt(segment, definition);

            BuildingSpawner.FitSpriteUniform(renderer, sprite,
                BuildingSpawner.ArtWorldSize(definition, _grid.CellSize, overscanned));
        }

        /// <summary>
        /// Whether this segment will be drawn with its own belt art, asked of BuildingSpawner so the
        /// silhouette, the ghost and ConveyorView cannot disagree. Narrows the runtime to a conveyor
        /// first, since only a belt has a shape to compare.
        /// </summary>
        static bool UsesOwnConveyorArt(BuildingRuntime segment, BuildingDefinition definition)
            => segment is ConveyorRuntime conveyor
               && definition is ConveyorDefinition conveyorDefinition
               && BuildingSpawner.UsesOwnConveyorArt(conveyorDefinition, conveyor.Orientation.Shape);

        /// <summary>
        /// Full tint while nothing has arrived, SitePlaceholderAlpha as soon as the sprite starts
        /// forming over it. Constant during assembly rather than fading with progress: a half-erased
        /// outline under a half-formed building reads as mush, and the clean cut at completion is
        /// what makes it legible.
        /// </summary>
        Color SilhouetteColor(SegmentView view)
        {
            if (!view.IsAssembling || settings == null) return siteTint;

            Color tint = siteTint;
            tint.a = settings.SitePlaceholderAlpha;
            return tint;
        }

        /// <summary>
        /// Same resolution the real views use: a conveyor shows its own art only while its
        /// current shape still matches the definition it was placed from (ConveyorView's rule),
        /// otherwise the procedural shape sprite - which always matches the actual straight/corner
        /// shape, so a drag-turned segment still reads as a corner while pending. Any other
        /// building shows its art, or its procedural placeholder colour when it has none
        /// (BuildingGhostView's own fallback).
        /// </summary>
        Sprite ResolveSprite(BuildingRuntime segment, BuildingDefinition definition)
        {
            if (segment is ConveyorRuntime conveyor && definition is ConveyorDefinition conveyorDefinition)
            {
                return UsesOwnConveyorArt(segment, definition)
                    ? conveyorDefinition.OverrideSprite
                    : _spriteFactory.CreateShapeSprite(conveyor.Orientation.Shape, definition.PlaceholderColor);
            }

            return definition.Sprite != null
                ? definition.Sprite
                : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
        }

        /// <summary>
        /// A conveyor ghost turns with its own orientation (mirroring included, exactly as
        /// ConveyorView does - a corner's chirality is otherwise wrong half the time); a
        /// Splitter/Crossroad turns with its facing, measured against its art's native side; every
        /// other building's view never rotates, so its ghost must not either.
        /// </summary>
        void ApplyRotation(Transform target, BuildingRuntime segment, BuildingDefinition definition)
        {
            if (segment is ConveyorRuntime conveyor)
            {
                ConveyorOrientation orientation = conveyor.Orientation;
                Direction artNativeDirection = definition is ConveyorDefinition conveyorDefinition
                    && conveyorDefinition.OverrideSprite != null
                    && orientation.Shape == conveyorDefinition.DefaultShape
                        ? conveyorDefinition.ArtNativeDirection
                        : Direction.North;

                Direction effectiveNative = orientation.Mirrored ? artNativeDirection.Opposite() : artNativeDirection;
                int conveyorDegrees = orientation.Rotation.ToRotationDegrees() - effectiveNative.ToRotationDegrees();
                target.rotation = Quaternion.Euler(0f, 0f, -conveyorDegrees);

                Vector3 scale = target.localScale;
                scale.x = orientation.Mirrored ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
                target.localScale = scale;
                return;
            }

            Direction artNative;
            if (definition is SplitterDefinition splitter) artNative = splitter.ArtNativeEntrySide;
            else if (definition is CrossroadDefinition) artNative = Direction.North;
            else
            {
                target.rotation = Quaternion.identity;
                return;
            }

            int degrees = segment.FacingRotation.ToRotationDegrees() - artNative.ToRotationDegrees();
            target.rotation = Quaternion.Euler(0f, 0f, -degrees);
        }

        /// <summary>
        /// Drops views whose segment left the pending range without materialising - a cancelled or
        /// overtaken site. Detached entries are exempt by construction: they are handled by
        /// SyncDetachedSegments against the grid, and would otherwise be destroyed by this exact
        /// pass on the frame they materialise.
        /// </summary>
        void RemoveStaleViews()
        {
            _scratch.Clear();

            foreach (var kvp in _views)
            {
                if (kvp.Value.Detached || _liveKeys.Contains(kvp.Key)) continue;
                _scratch.Add(kvp.Key);
            }

            foreach (BuildingRuntime key in _scratch)
            {
                Discard(key, _views[key]);
            }
        }

        /// <summary>The pair of objects standing in for one segment: the blue silhouette, and the sprite assembling over it.</summary>
        sealed class SegmentView
        {
            public SpriteRenderer Silhouette;
            public SpriteRenderer AssemblyRenderer;
            public BuildDissolveView Dissolve;

            /// <summary>Set once the segment materialised: the view no longer belongs to a site and finishes assembling on its own.</summary>
            public bool Detached;

            /// <summary>
            /// A null Dissolve next to a live AssemblyRenderer means the effect already completed
            /// and removed itself - the sprite is whole, so the silhouette must stay faded rather
            /// than snapping back to full opacity under a finished building.
            /// </summary>
            public bool IsAssembling => Detached
                || (AssemblyRenderer != null && (Dissolve == null || Dissolve.DisplayedProgress > 0f));

            public void Destroy()
            {
                DestroyObject(Silhouette);
                DestroyObject(AssemblyRenderer);
                Silhouette = null;
                AssemblyRenderer = null;
                Dissolve = null;
            }

            static void DestroyObject(SpriteRenderer renderer)
            {
                if (renderer == null) return;
                if (Application.isPlaying) Object.Destroy(renderer.gameObject);
                else Object.DestroyImmediate(renderer.gameObject);
            }
        }
    }
}
