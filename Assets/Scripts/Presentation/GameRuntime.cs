using System.Collections.Generic;
using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Directives;
using Game.Gameplay.Expeditions;
using Game.Gameplay.Missions;
using Game.Gameplay.Notifications;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Session;
using Game.Gameplay.Sectors;
using Game.Gameplay.Selection;
using Game.Gameplay.Sites;
using Game.Gameplay.Transport;
using Game.Gameplay.WorldGeneration;
using Game.Grid;
using Game.Save;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Minimal bootstrap wiring: constructs GridRuntime + TerrainRuntime + ConstructionService
    /// and exposes them via a plain scene reference (not a singleton, not the full staged
    /// pipeline from PROJECT_ARCHITECTURE.md - collapsed here until enough systems exist to
    /// justify stages).
    ///
    /// Awake() branches on PendingGameStart.LoadedSave (Game.Save): null means a fresh game
    /// (world generation, exactly as before), a non-null SaveData means every system is restored
    /// from it instead (CONTRACTS.md §14) - MainMenu.unity is the only place that sets this,
    /// via New Game/Load before loading Bootstrap.unity.
    /// </summary>
    public sealed class GameRuntime : MonoBehaviour
    {
        [SerializeField] float cellSize = 1f;
        [SerializeField] TerrainGenerationSettings terrainSettings;
        [SerializeField] TerrainView terrainView;
        [SerializeField] GridLineView gridLineView;

        /// <summary>
        /// Tileable diffuse/normal pair for the concrete pad shown under every placed building
        /// (BuildingSpawner) and the Core (WorldContentSpawner), sampled by Custom/BuildingGroundSlab.
        /// Optional; either being null disables the slab entirely rather than falling back to a
        /// placeholder.
        /// </summary>
        [Header("Ground slab")]
        [SerializeField] Texture2D groundSlabDiffuse;
        [SerializeField] Texture2D groundSlabNormal;

        /// <summary>Custom/BuildingGroundSlab. Assigned here and handed to ProceduralSpriteFactory through GroundSlabSettings, because that class has no inspector of its own. An asset reference cannot be stripped from a build the way a Shader.Find by name can - see docs/BUILD.md.</summary>
        [SerializeField] Shader groundSlabShader;

        /// <summary>1 = texture's own colors unchanged; lower values darken it (simple RGB multiply, applied in Custom/BuildingGroundSlab).</summary>
        [SerializeField, Range(0f, 1f)] float groundSlabDarken = 1f;

        /// <summary>How far inward from the footprint edge the ground's real Mars/Gravel04 texture starts showing through the still-mostly-opaque slab ("taille de la zone de transition").</summary>
        [SerializeField, Min(0f)] float groundSlabSandBandWidth = 1f;

        /// <summary>World-unit width of the final alpha fade past the sand band, before the slab's own geometry ends ("vitesse de transparence" - a narrower value fades faster/sooner).</summary>
        [SerializeField, Min(0f)] float groundSlabEdgeSoftness = 0.6f;

        /// <summary>World-unit patch size of the noise that makes the sand-encroachment boundary jagged rather than a perfect rounded rectangle ("forme" - larger values = bigger, blobbier patches).</summary>
        [SerializeField, Min(0.1f)] float groundSlabSandNoiseScale = 1.2f;

        /// <summary>How far (world units) the noise can perturb the sand-encroachment boundary in or out ("forme" - 0 = a perfectly smooth ring, larger = more jagged/irregular).</summary>
        [SerializeField, Min(0f)] float groundSlabSandNoiseAmplitude = 0.5f;

        /// <summary>
        /// Single global source for every drop shadow: sun offset, opacity, depth. Optional - null
        /// means nothing casts a shadow, rather than a hardcoded fallback look, matching the slab
        /// above. Only the Core reads it for now; the placed-building spawner does not, so shadows
        /// stay a deliberate, one-building experiment until the look is judged.
        /// </summary>
        [Header("Shadows")]
        [SerializeField] BuildingShadowSettings buildingShadowSettings;

        [Header("Item/Recipe registries")]
        [SerializeField] ItemDatabase itemDatabase;
        [SerializeField] RecipeDatabase recipeDatabase;

        /// <summary>
        /// How the map is divided into chunks and sectors, and how dangerous each distance from the
        /// Core reads. The only place those numbers exist - see SectorSettings.
        /// </summary>
        [SerializeField] SectorSettings sectorSettings;

        /// <summary>How the expedition system is tuned. Optional: null means a world without expeditions.</summary>
        [SerializeField] MissionSettings missionSettings;

        /// <summary>
        /// How the ground around the Core is cut into expedition zones, and what one holds. Optional:
        /// null means a world where a mission may be aimed anywhere its band allows, which is what the
        /// game did before the zones existed.
        /// </summary>
        [SerializeField] ExpeditionZoneSettings expeditionZoneSettings;

        /// <summary>What grows on the ground and how thickly. Optional: null means a world with no decor, which is a plain world rather than a broken one.</summary>
        [SerializeField] DecorSettings decorSettings;

        /// <summary>Draws the decor of the chunks around the camera. Optional, like the settings above.</summary>
        [SerializeField] DecorVisualSync decorVisuals;

        [Header("World generation (Core + ore deposits, spawned once at game start)")]
        [SerializeField] WorldGenerationSettings worldGenerationSettings;
        [SerializeField] ActionRadiusView actionRadiusView;
        [SerializeField] FogOfWarView fogOfWarView;

        [SerializeField] ItemVisualSync itemVisuals;

        /// <summary>
        /// Optional. When present it draws construction sites and owns the dissolve assembly, so
        /// ConstructionInputAdapter hands it its BuildingSpawner instead of spawning a materialized
        /// segment's view itself. Null means segments appear the instant they materialize.
        /// </summary>
        [SerializeField] ConstructionSiteVisualSync constructionSiteVisuals;

        [Header("Save/Load id -> asset resolution (CONTRACTS.md §14)")]
        [SerializeField] BuildingDefinition[] buildingCatalog = System.Array.Empty<BuildingDefinition>();

        [Header("Research (CONTRACTS.md §11)")]
        [SerializeField] ResearchDatabase researchDatabase;

        [Header("Core directives - what the Core asks the player for, in order")]
        [SerializeField] CoreDirectiveDatabase coreDirectiveDatabase;

        [Header("Débogage")]

        /// <summary>
        /// Hands over everything the introduction normally hands over slowly: the explorer robots,
        /// and with them the map and the Research menu. On while the expedition brick is being built
        /// - reaching the map today means draining 70 000 CU down to 25 000 first, which is minutes
        /// of waiting before any test of the map can even begin.
        ///
        /// One switch rather than one per unlock, because they are one thing: the state a run is in
        /// once its opening is over. Uncheck it to play the opening as a player meets it.
        ///
        /// It only ever opens. A run that has already earned these keeps them either way, and no
        /// directive is marked done - the Core still asks for its first delivery.
        /// </summary>
        [SerializeField] bool startWithEverythingUnlocked = true;

        public GridRuntime Grid { get; private set; }
        public TerrainRuntime Terrain { get; private set; }

        /// <summary>What the player has discovered, one state per cell. Written by the Core's radius (RevealDiscoveredByCore) and later by missions; read by the fog renderer, which must never recompute a distance to the Core instead.</summary>
        public DiscoveryRuntime Discovery { get; private set; }

        /// <summary>What grows on the ground, derived per chunk, minus what the player has cleared. Null when no decor settings are configured.</summary>
        public DecorRuntime Decor { get; private set; }

        /// <summary>The map cut into sectors - pure geometry, the unit a mission is aimed at.</summary>
        public SectorGrid Sectors { get; private set; }

        /// <summary>A sector's name, risk and contents, derived from the world seed on demand. Nothing is materialised for a sector nobody has reached.</summary>
        public SectorCatalog SectorCatalog { get; private set; }

        /// <summary>Which sectors are currently within mission reach. Reads the Core's radius at call time, so extending it moves the ring on its own.</summary>
        public SectorMissionRange MissionRange { get; private set; }

        /// <summary>
        /// The six expedition zones, their derived content, and which one the player is working. Null
        /// when no zone settings are configured, which is a world where a mission may go anywhere its
        /// band allows.
        /// </summary>
        public ExpeditionZoneSystem ExpeditionZones { get; private set; }

        /// <summary>The expedition process: probes, missions in flight, reports. Null when no mission settings are configured, which is a world without expeditions rather than a broken one.</summary>
        public MissionSystem Missions { get; private set; }

        /// <summary>One texel per sector, for the whole map - what the zoomed-out map draws. Rebuilds only the chunks discovery actually moved, so a still frame costs one comparison.</summary>
        public SectorMapImage SectorMap { get; private set; }

        /// <summary>Where the explorer robots stand between missions. A view, driven from the tick below; the fleet's real state is charges in MissionSystem.</summary>
        ExplorerRobotParkView _explorerPark;

        /// <summary>Whether a parked explorer robot stands on this cell. What lets a click on the fleet open the map instead of falling through to empty ground.</summary>
        public bool ExplorerRobotStandsOn(GridCoord cell) => _explorerPark != null && _explorerPark.StandsOn(cell);

        /// <summary>
        /// The scene's one depth ladder - every sorted-band rank comes from it. There must be
        /// exactly one: a rank is only meaningful against the window it was measured in, so ranks
        /// from two ladders are not comparable.
        /// </summary>
        public DepthSortLadder DepthSort { get; private set; }

        /// <summary>
        /// Redraws one building's view, given the cell its current view is filed under. Installed by
        /// ConstructionInputAdapter, which owns the scene's only BuildingSpawner; null in a scene
        /// without one (a test), where rotating still changes the runtime and simply draws nothing.
        /// </summary>
        public System.Action<BuildingRuntime, GridCoord> BuildingViewRebuilder { get; set; }

        public ConstructionService Construction { get; private set; }
        public WorldGenerator World { get; private set; }
        public TransportSystem Transport { get; private set; }
        public SelectionRuntime Selection { get; private set; }
        public ItemVisualSync ItemVisuals => itemVisuals;
        public ConstructionSiteVisualSync ConstructionSiteVisuals => constructionSiteVisuals;
        public ItemDatabase Items => itemDatabase;

        /// <summary>
        /// Built once in Start() (after TerrainView.Initialize has populated its ground material)
        /// from this component's own slab options plus TerrainView.GroundMaterial's live biome
        /// values - see BuildingGroundSlab.shader for why the two must match formula-for-formula.
        /// Null until Start() runs; ConstructionInputAdapter reads this lazily from Update() (not
        /// its own Start()) specifically to avoid depending on Start() order between components.
        /// </summary>
        public GroundSlabSettings GroundSlabSettings { get; private set; }

        /// <summary>
        /// The one shadow configuration every caster reads, so it is assigned in a single place in
        /// the scene. Null means nothing casts a shadow. See BuildingShadowSettings.
        /// </summary>
        public BuildingShadowSettings ShadowSettings => buildingShadowSettings;

        /// <summary>
        /// Built once in Start() alongside GroundSlabSettings; keeps every placed slab's edge
        /// mask (Custom/BuildingGroundSlab._EdgeMask) in sync as buildings are placed/demolished
        /// next to each other or the Core. See GroundSlabNeighborLinker.
        /// </summary>
        public GroundSlabNeighborLinker GroundSlabNeighborLinker { get; private set; }

        public RecipeDatabase Recipes => recipeDatabase;
        public ResearchDatabase Researches => researchDatabase;
        public PowerSystem Power { get; private set; }
        public ComputeSystem Compute { get; private set; }
        public ResearchSystem Research { get; private set; }

        /// <summary>What the Core is currently asking for. Built after ConstructionSiteSystem, which carries the deliveries it starts.</summary>
        public CoreDirectiveSystem CoreDirectives { get; private set; }

        /// <summary>
        /// GlobalStock keeps its name but its contract is inverted since TASK_05_ROBOT_CONSTRUCTEUR.md:
        /// it holds nothing at all any more. It is a read-only aggregated view over the Core chest,
        /// every placed Storage and every production building's output, minus everything already
        /// reserved by a construction site - i.e. exactly what a builder robot could still be sent
        /// to fetch (CONTRACTS.md §15). Recomputed on every read; never serialized.
        /// </summary>
        public IReadOnlyDictionary<string, int> GlobalStock =>
            ConstructionSites != null ? ConstructionSites.GetAvailableAggregate() : new Dictionary<string, int>();

        /// <summary>
        /// What a Core directive counts: <see cref="GlobalStock"/> minus the Core's own reserve.
        ///
        /// A directive asks for material to be brought to the Core, and what already sits in the
        /// hatch beneath it was never brought anywhere - counting it let the opening directive be
        /// satisfied by the starting stock alone. The reserve still funds construction, which is
        /// what GlobalStock is for.
        ///
        /// Here rather than in a panel because two places display it - the Top Bar's chips and the
        /// Core panel's figures - and a rule split across its readers is a rule that will disagree
        /// with itself.
        ///
        /// <b>Display only.</b> Whether a directive may be validated is not asked of this: it is asked
        /// of <c>CoreDirectiveSystem.CanValidate()</c>, which reads the same view for itself. While the
        /// decision took a dictionary from its caller, a caller could hand in the wrong one - and the
        /// directive tests did, accepting on stock the haul was then forbidden to claim.
        /// </summary>
        public IReadOnlyDictionary<string, int> DirectiveStock =>
            ConstructionSites != null ? ConstructionSites.GetAvailableForCoreHaul() : new Dictionary<string, int>();

        /// <summary>
        /// True while an open panel is navigating with the keyboard itself - the zoomed-out map is
        /// the only one today, where ZQSD moves the map. The world camera stands down for as long as
        /// it is set, so the two never move at once. Set by the panel that takes the keys, cleared
        /// when it closes.
        /// </summary>
        public bool KeyboardOwnedByPanel { get; set; }

        /// <summary>Construction sites + the two builder robots (TASK_05_ROBOT_CONSTRUCTEUR.md), ticked from this object's central Update() like every other simulation system.</summary>
        public ConstructionSiteSystem ConstructionSites { get; private set; }

        /// <summary>Generic notification banner feed (TASK_05_ROBOT_CONSTRUCTEUR.md §6) - a robot unable to unload is its first user, not its only intended one.</summary>
        public NotificationSystem Notifications { get; private set; }

        /// <summary>How long this run has been played, in simulated seconds - stops with the pause and survives a save/load. Read by the Top Bar; see PlayClock for why it never has to check whether the game is paused.</summary>
        public PlayClock Clock { get; private set; }

        /// <summary>
        /// True while a UI panel (Building menu, Storage panel, ...) is open and should own
        /// mouse input exclusively. World input adapters (construction, storage selection) must
        /// skip their own click handling while this is set, otherwise a click that selects a
        /// menu item or closes a panel also leaks through as a world click on the same frame.
        /// Derived from Selection (both the named global panel and the currently inspected
        /// building) - there is exactly one source of truth for "is a panel open" (CONTRACTS.md
        /// §7), panels no longer track this themselves.
        /// </summary>
        public bool IsUIBlockingInput => Selection.ActiveGlobalPanel != null
            || Selection.SelectedBuilding != null
            || Selection.SelectedSite != null;

        /// <summary>
        /// The frame a UI panel last closed. World input adapters also skip their click handling
        /// during this exact frame, covering the case where the panel's close callback runs
        /// after this frame's Update() already saw IsUIBlockingInput as false.
        /// </summary>
        public int LastMenuCloseFrame { get; private set; } = -1;

        /// <summary>Buildings created by the load-game restore path in Awake(), spawned into views once Start() runs (view construction needs cross-object wiring, same reason as terrainView/gridLineView below).</summary>
        readonly List<BuildingRuntime> _restoredBuildings = new List<BuildingRuntime>();

        void Awake()
        {
            Grid = new GridRuntime(cellSize);
            Power = new PowerSystem();
            Compute = new ComputeSystem();
            Research = new ResearchSystem(Compute);
            Transport = new TransportSystem(Grid);
            Notifications = new NotificationSystem();
            Clock = new PlayClock();

            // Sized from the zoom-out cap, because that is exactly what bounds how much world can be
            // on screen at once - and therefore how many draw orders the sorted band needs. Found
            // once here rather than wired in the scene: there is one zoom controller, and a missing
            // one only means the ladder assumes a zero-height view, which still ranks correctly.
            var zoom = FindAnyObjectByType<CameraZoomController>();
            _maxOrthographicSize = zoom != null ? zoom.MaxOrthographicSize : 0f;
            DepthSort = new DepthSortLadder(_maxOrthographicSize);
            _depthSortCamera = Camera.main;

            SaveData loadedSave = PendingGameStart.LoadedSave;
            PendingGameStart.RequestNewGame(); // consume immediately - never read a second time this session

            if (loadedSave != null)
            {
                Terrain = new TerrainRuntime(loadedSave.TerrainSize, loadedSave.TerrainSeed, loadedSave.TerrainScale, loadedSave.TerrainProportion);
                Discovery = new DiscoveryRuntime(Terrain.Size, sectorSettings.ChunkSizeCells);
                Discovery.RestoreState(loadedSave.Discovered);
                _pendingDecorRemoved = loadedSave.DecorRemoved;
                Compute.RestoreReserve(loadedSave.ComputeReserve);

                var restoredQueue = new List<ResearchDefinition>();
                foreach (string queuedId in loadedSave.ResearchQueue)
                {
                    ResearchDefinition queuedResearch = FindResearchDefinition(queuedId);
                    if (queuedResearch != null) restoredQueue.Add(queuedResearch);
                }
                Research.RestoreState(FindResearchDefinition(loadedSave.ResearchActiveId), loadedSave.ResearchProgress, restoredQueue, loadedSave.ResearchUnlocked);

                RestoreWorldAndBuildings(loadedSave);
            }
            else
            {
                // <b>A new run draws its own seed.</b> The settings asset carries 0, so every new game
                // was the same world down to the last site on the map - three runs in a row put the far
                // explorations on the same cells. The draw happens here, once, and goes straight into
                // the save (SaveData.TerrainSeed); everything downstream is derived from it through
                // DeterministicHash, which is what keeps a reloaded run identical to itself.
                int runSeed = terrainSettings.RandomiseSeedEachRun
                    ? Random.Range(int.MinValue, int.MaxValue)
                    : terrainSettings.Seed;

                Terrain = new TerrainRuntime(terrainSettings.Size, runSeed, terrainSettings.TerrainScale, terrainSettings.Proportion);
                Discovery = new DiscoveryRuntime(Terrain.Size, sectorSettings.ChunkSizeCells);

                // The player's starting resources live in the Core chest fixture placed by
                // WorldGenerator.Generate (WorldGenerationSettings.CoreStorageDefinition), one cell
                // south of the Core and seeded from StartingStock - never in a building-less pool.

                // World generation (Core + deposits) must exist before ConstructionSiteSystem and
                // ConstructionService: the former parks its robots next to the Core, the latter
                // needs the Core instance for the action radius.
                if (worldGenerationSettings != null)
                {
                    World = new WorldGenerator();
                    World.Generate(Grid, Terrain.Size, worldGenerationSettings, Compute, Power, Research);
                }

                ConstructionSites = new ConstructionSiteSystem(Transport, Grid, Notifications, RobotParkOrigin());
                CoreDirectives = new CoreDirectiveSystem(coreDirectiveDatabase, ConstructionSites, Research);
                Construction = new ConstructionService(Grid, itemDatabase, recipeDatabase, Compute, Power, Research, Transport, World?.Core, ConstructionSites);
            }

            // After both branches: the Core exists whether it was generated or restored, and its
            // radius has a disc to write before the first frame is drawn.
            RevealDiscoveredByCore();

            // Built here rather than in either branch because both need it and neither owns it. All
            // three are stateless views over the map: SectorGrid is arithmetic, SectorCatalog is a
            // pure function of Terrain.Seed, and SectorMissionRange reads the radius it is handed.
            // Nothing here is restored from the save, and nothing here needs to be - the seed is,
            // and everything else follows from it.
            Sectors = new SectorGrid(Terrain.Size, sectorSettings.SectorSizeCells);
            SectorCatalog = new SectorCatalog(Sectors, Terrain.Seed, World?.CoreCenterCells ?? Vector2.zero,
                sectorSettings.LowRiskWithinCells, sectorSettings.ModerateRiskWithinCells, sectorSettings.HighRiskWithinCells,
                sectorSettings.PreferredRegionSizeCells);

            // The maximum radius comes from the Core, which owns it, so the exploration threshold
            // follows it on its own rather than being a second figure to keep in step.
            MissionRange = new SectorMissionRange(CoreRuntime.ExtendedActionRadiusCells, sectorSettings.TerritorySpacingCells);
            SectorMap = new SectorMapImage(Sectors, Discovery);
            _explorerPark = new ExplorerRobotParkView(Grid, missionSettings, DepthSort);

            // Before the missions, which are gated on it. The inner edge is read off the Core once, here,
            // and then travels in the save: the zones are cut against the ground the Core already owned
            // when they were laid out, and re-cutting them later would move a run's map underneath it.
            if (expeditionZoneSettings != null)
            {
                ExpeditionZones = new ExpeditionZoneSystem(expeditionZoneSettings, Sectors, MissionRange,
                    World?.CoreCenterCells ?? Vector2.zero, World?.ActionRadiusCells ?? 0, Terrain.Seed);
                // The release condition reads cartography, so the system needs the discovery it measures
                // against - handed over once here rather than threaded through the launch path.
                ExpeditionZones.UseDiscovery(Discovery);
                ExpeditionZones.RestoreState(loadedSave?.ExpeditionZones);
            }

            if (missionSettings != null)
            {
                Missions = new MissionSystem(missionSettings, Sectors, Discovery, SectorCatalog, Compute,
                    MissionRange, ExpeditionZones, Terrain.Seed);

                // Set after construction because it needs the ore definitions world generation owns.
                // Placed content wins over the derivation, and World has already run - which is the
                // ordering constraint that keeps the starting area from being overwritten.
                if (worldGenerationSettings != null)
                {
                    Missions.Materialisation = new SectorMaterialisation(Sectors, Grid, SectorCatalog, new[]
                    {
                        worldGenerationSettings.IronOreDefinition,
                        worldGenerationSettings.CopperOreDefinition,
                        worldGenerationSettings.CoalOreDefinition
                    });
                }
                Missions.RestoreState(loadedSave?.Missions);
            }

            // Last, so it applies to the restored state rather than being overwritten by it: a save
            // written before the switch was on still opens, and a save written after loses nothing.
            if (startWithEverythingUnlocked)
            {
                Missions?.MakeRobotsAppear();
                if (CoreDirectives != null) CoreDirectives.ResearchMenuForcedOpen = true;
            }

            Selection = new SelectionRuntime();
            Selection.GlobalPanelChanged += name =>
            {
                if (name == null) LastMenuCloseFrame = Time.frameCount;
            };
            Selection.SelectionChanged += building =>
            {
                if (building == null) LastMenuCloseFrame = Time.frameCount;
            };

            // New Game "generates a save" (per the main-menu contract) - the initial state is
            // written immediately so a Load right after New Game (without ever quitting) still
            // finds a file. A loaded game's file already exists and is left untouched here;
            // OnApplicationQuit() is what keeps it in sync with actual progress.
            if (loadedSave == null)
            {
                SaveCurrentGame();
            }
        }

        /// <summary>
        /// Reconstructs Core, every deposit and every placed building from a save (CONTRACTS.md
        /// §14), in the same dependency order World generation followed: Core, then deposits
        /// (an Extractor resolves its deposit from whatever already occupies its cell), then
        /// every other building. Views are spawned later, in Start().
        /// </summary>
        void RestoreWorldAndBuildings(SaveData save)
        {
            if (worldGenerationSettings != null && FindBuildingDefinition(save.CoreDefinitionId) is CoreDefinition coreDefinition)
            {
                var coreCell = new GridCoord(save.CoreCellX, save.CoreCellY);
                var core = new CoreRuntime(coreDefinition, coreCell, Direction.North, Compute, Power, Research);
                core.RestoreState(save.CoreState ?? new JObject());
                Grid.SetOccupantFootprint(coreCell, coreDefinition.FootprintSize, core);

                var deposits = new List<DepositRuntime>();
                foreach (DepositSaveData depositSave in save.Deposits)
                {
                    if (!(FindBuildingDefinition(depositSave.DefinitionId) is OreDepositDefinition oreDefinition)) continue;

                    var origin = new GridCoord(depositSave.OriginX, depositSave.OriginY);
                    DepositRuntime deposit = Grid.PlaceDeposit(origin, oreDefinition);
                    deposits.Add(deposit);
                }

                World = new WorldGenerator();
                World.RestoreState(core, coreCell, deposits);
            }

            ConstructionSites = new ConstructionSiteSystem(Transport, Grid, Notifications, RobotParkOrigin());
            CoreDirectives = new CoreDirectiveSystem(coreDirectiveDatabase, ConstructionSites, Research);
            CoreDirectives.RestoreState(save.CoreDirectives);
            Construction = new ConstructionService(Grid, itemDatabase, recipeDatabase, Compute, Power, Research, Transport, World?.Core, ConstructionSites);
            Construction.RestoreBuildingCap(save.BuildingCap);
            Clock.Restore(save.PlayTimeSeconds);

            foreach (BuildingSaveData buildingSave in save.Buildings)
            {
                BuildingDefinition definition = FindBuildingDefinition(buildingSave.DefinitionId);
                if (definition == null) continue;

                var cell = new GridCoord(buildingSave.CellX, buildingSave.CellY);
                var rotation = (Direction)buildingSave.FacingRotation;
                BuildingRuntime runtime = Construction.CreateForRestore(definition, cell, rotation);
                if (runtime == null) continue;

                runtime.RestoreState(buildingSave.State ?? new JObject());
                Transport.Register(runtime);
                _restoredBuildings.Add(runtime);
            }

            // Sites/robots restore last: their segments are rebuilt with the same factory the
            // buildings above used (no cost, no placement check), and their reservations/containers
            // are re-resolved by cell, so every real building must already sit in Game.Grid first.
            ConstructionSites.RestoreState(save.ConstructionSites, Construction.CreateForRestore, FindBuildingDefinition);
        }

        /// <summary>Where the two builder robots park when idle (TASK_05_ROBOT_CONSTRUCTEUR.md §2) - just south of the Core, next to the Core chest. Grid-space, like BuilderRobotRuntime.Position.</summary>
        Vector2 RobotParkOrigin()
        {
            if (World?.Core == null) return Vector2.zero;
            Vector2Int footprint = World.Core.Definition.FootprintSize;
            return new Vector2(World.Core.Cell.X + footprint.x / 2f, World.Core.Cell.Y - 2f);
        }

        BuildingDefinition FindBuildingDefinition(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (BuildingDefinition definition in buildingCatalog)
            {
                if (definition != null && definition.Id == id) return definition;
            }
            return null;
        }

        ResearchDefinition FindResearchDefinition(string id) => researchDatabase != null ? researchDatabase.Get(id) : null;

        List<string> BuildResearchQueueIds()
        {
            var ids = new List<string>();
            foreach (ResearchDefinition queued in Research.GetQueue())
            {
                ids.Add(queued.Id);
            }
            return ids;
        }

        /// <summary>Captures every system's current state into a SaveData and writes it to the single save file (CONTRACTS.md §14). Called by New Game (initial state) and OnApplicationQuit (current progress).</summary>
        void SaveCurrentGame()
        {
            var data = new SaveData
            {
                // From the RUNNING world, never from the settings asset. Those agree on a fresh
                // game, but a loaded one runs on the values its save carried - and writing the
                // asset's back would silently re-stamp the save with whatever the asset says today.
                //
                // That was a harmless slip while terrain was a stored array reloaded from the save.
                // It is not one now that terrain is re-derived from these four numbers: editing the
                // asset between two sessions would regenerate a different world underneath the
                // buildings the player had already placed.
                TerrainSeed = Terrain.Seed,
                TerrainSize = Terrain.Size,
                TerrainScale = Terrain.TerrainScale,
                TerrainProportion = Terrain.Proportion,
                Discovered = Discovery?.CaptureState(),

                // Only what the player cleared. What grows re-derives itself from the seed, so
                // storing it would be storing what the seed already says.
                DecorRemoved = Decor?.CaptureState(),
                Missions = Missions?.CaptureState(),
                ExpeditionZones = ExpeditionZones?.CaptureState(),
                ComputeReserve = Compute.Reserve,
                ResearchActiveId = Research.ActiveResearch != null ? Research.ActiveResearch.Id : null,
                ResearchProgress = Research.AbsorbedCu,
                ResearchQueue = BuildResearchQueueIds(),
                ResearchUnlocked = new List<string>(Research.GetUnlockedIds()),
                BuildingCap = Construction.BuildingCap,
                PlayTimeSeconds = Clock.ElapsedSeconds,
                ConstructionSites = ConstructionSites?.CaptureState(),
                CoreDirectives = CoreDirectives?.CaptureState()
            };

            if (World?.Core != null)
            {
                data.CoreDefinitionId = World.Core.Definition.Id;
                data.CoreCellX = World.Core.Cell.X;
                data.CoreCellY = World.Core.Cell.Y;
                data.CoreState = World.Core.CaptureState();
            }

            if (World != null)
            {
                foreach (DepositRuntime deposit in World.OreDeposits)
                {
                    data.Deposits.Add(new DepositSaveData
                    {
                        DefinitionId = deposit.Definition.Id,
                        OriginX = deposit.Origin.X,
                        OriginY = deposit.Origin.Y,
                    });
                }
            }

            foreach (BuildingRuntime building in Transport.GetAllBuildings())
            {
                if (World != null && ReferenceEquals(building, World.Core)) continue; // Core is saved separately above

                data.Buildings.Add(new BuildingSaveData
                {
                    DefinitionId = building.Definition.Id,
                    CellX = building.Cell.X,
                    CellY = building.Cell.Y,
                    FacingRotation = (int)building.FacingRotation,
                    State = building.CaptureState()
                });
            }

            SaveService.Save(data);
        }

        /// <summary>The map image owns a Texture2D, which Unity does not collect on its own.</summary>
        void OnDestroy() => SectorMap?.Dispose();

        void OnApplicationQuit()
        {
            SaveCurrentGame();
        }

        /// <summary>The camera the depth ladder and the fog window both follow. Cached once - Camera.main is a scene search.</summary>
        Camera _depthSortCamera;

        /// <summary>The zoom-out cap. It is what bounds how much world can be on screen at once, which is what sizes both the depth ladder and the fog's window.</summary>
        float _maxOrthographicSize;

        /// <summary>
        /// The cleared-decor list from the save, held between Awake and Start.
        ///
        /// DecorRuntime cannot be built in Awake: it needs the ground material's biome parameters,
        /// and TerrainView only writes those in Start. So the save is read where saves are read, and
        /// applied where the runtime can exist.
        /// </summary>
        string _pendingDecorRemoved;

        /// <summary>The radius last written into the discovery state, so a repeat pass costs one comparison. NaN until the first pass, which no real radius equals.</summary>
        float _lastRevealedCoreRadius = float.NaN;

        /// <summary>
        /// Builds the decor runtime and hands it to its view.
        ///
        /// In Start rather than Awake, and that is not a preference: the biome classification needs
        /// the ground material's own parameters, and TerrainView writes those in its own Initialize.
        /// Reading them from the material rather than from the profile asset keeps a single source -
        /// whatever the shader was actually given is what the decor is placed against.
        ///
        /// Missing settings mean a world with no decor, not a broken one.
        /// </summary>
        void InitialiseDecor()
        {
            if (decorSettings == null || Terrain == null) return;

            Material groundMaterial = terrainView != null ? terrainView.GroundMaterial : null;
            if (groundMaterial == null) return;

            Vector4 origin = groundMaterial.GetVector("_VariationOrigin");
            var biome = new BiomeField(
                new Vector2(origin.x, origin.y),
                groundMaterial.GetFloat("_BiomeCellSize"),
                groundMaterial.GetFloat("_BiomeSeed"),
                new[]
                {
                    groundMaterial.GetFloat("_BiomeWeight0"),
                    groundMaterial.GetFloat("_BiomeWeight1"),
                    groundMaterial.GetFloat("_BiomeWeight2")
                },
                (int)groundMaterial.GetFloat("_BiomeTexCount"));

            // Game.Data cannot name Game.Grid's types, so the clumping shapes are assembled here -
            // the same boundary that makes the band weights arrive as a plain float[][].
            var clustering = new DecorClustering[decorSettings.Kinds.Length];
            for (int i = 0; i < clustering.Length; i++)
            {
                DecorSettings.Kind kind = decorSettings.Kinds[i];
                clustering[i] = new DecorClustering(kind.ClusterChance, kind.ClusterSize.x, kind.ClusterSize.y, kind.ClusterRadius);
            }

            Decor = new DecorRuntime(Terrain.Size, sectorSettings.ChunkSizeCells, Terrain.Seed, biome,
                decorSettings.BandWeightsPerKind(), decorSettings.SpotsPerChunk, decorSettings.BandEdgeExclusion,
                clustering);

            // What the player cleared, from the save. Applied before the view spawns anything, so a
            // cleared rock is never briefly visible on load.
            Decor.RestoreState(_pendingDecorRemoved);
            _pendingDecorRemoved = null;

            // Ore deposits, live rather than stored. The old whole-map scatter excluded their
            // footprints and this keeps that; putting them in the removal set instead would write
            // hundreds of cells into every save and leave them bare once the deposit is mined out.
            Decor.GroundIsTaken = cell => Grid.GetOccupant(cell) is DepositRuntime;

            if (decorVisuals != null)
            {
                decorVisuals.Initialize(Decor, Grid, DepthSort, decorSettings, _depthSortCamera, _maxOrthographicSize);
            }

            // Handed over after the runtime exists: ConstructionService is built in Awake, and the
            // decor cannot be. Every building created from here on clears its own ground.
            if (Construction != null)
            {
                Construction.Decor = Decor;
                Construction.DecorCleared = cell => decorVisuals?.ForgetCell(cell);
            }

            // And everything already standing. Buildings restored from a save were created before
            // this point, and a save predating the decor lists nothing at all - without this sweep,
            // an old base would be growing rocks inside its own factories.
            ClearDecorUnderExistingBuildings();
        }

        /// <summary>One pass over what is already on the grid, so the decor is consistent with it whatever order the two came into existence.</summary>
        void ClearDecorUnderExistingBuildings()
        {
            if (Decor == null || Construction == null) return;

            if (World?.Core != null) Construction.ClearDecorUnder(World.Core.Definition, World.Core.Cell);
            if (World?.CoreStorage != null) Construction.ClearDecorUnder(World.CoreStorage.Definition, World.CoreStorage.Cell);

            foreach (BuildingRuntime building in _restoredBuildings)
            {
                Construction.ClearDecorUnder(building.Definition, building.Cell);
            }
        }

        /// <summary>
        /// Puts every DepthSortedDecor authored into the scene on the depth ladder. Decor that rises
        /// above its base carries a marker rather than a baked rank, because a sorted-band rank is
        /// only true for the depth window it was measured in.
        ///
        /// <b>For hand-placed scene objects only.</b> The wild decor is not among them: it is derived
        /// per chunk and DecorVisualSync registers each sprite as it enters the window, which is what
        /// removed the bug this sweep was written for - a scatter that ran after Start() and left 527
        /// rocks at sortingOrder 0, sunk into the ground band.
        ///
        /// A scene search, so it belongs to startup and never to a frame. Registering the same
        /// renderer twice would rank it twice, so the ladder is asked to forget it first.
        /// </summary>
        int RegisterSceneDepthSortedDecor()
        {
            int registered = 0;

            foreach (DepthSortedDecor decor in FindObjectsByType<DepthSortedDecor>(FindObjectsInactive.Include))
            {
                decor.RegisterWith(DepthSort);
                registered++;
            }

            return registered;
        }

        /// <summary>
        /// Turns a placed building a quarter turn and redraws it - what the panels' rotate button
        /// calls. Kept here rather than in each panel so the runtime change and the view rebuild can
        /// never be done one without the other.
        /// </summary>
        public bool RotateBuilding(BuildingRuntime building)
        {
            if (building == null || Construction == null) return false;

            GridCoord cell = building.Cell;
            if (!Construction.TryRotateInPlace(cell, out BuildingRuntime rotated)) return false;

            BuildingViewRebuilder?.Invoke(rotated, cell);
            return true;
        }

        /// <summary>
        /// Starts moving a building: the ghost follows the mouse under the ordinary placement rules,
        /// and the next left click puts it down (ConstructionInputAdapter.RelocateTo). The caller
        /// closes its own panel - the gesture happens on the map, and which panel is open is the
        /// UI's business, not this one's.
        /// </summary>
        public void BeginBuildingRelocation(BuildingRuntime building)
        {
            if (building == null || Construction == null) return;

            Construction.BeginRelocation(building);
        }

        /// <summary>
        /// The Core's action radius writes into the discovery state - today the only source of
        /// revelation there is, with missions to come.
        ///
        /// It <b>writes</b>, it does not define: nothing ever reads the radius back to decide what is
        /// visible. A cell the radius once covered stays discovered whatever the radius does
        /// afterwards, which is the whole difference between this and the disc the fog used to be.
        ///
        /// Called from the tick and idempotent: the disc is only walked when the radius has actually
        /// moved since the last pass, so repeating it every frame allocates nothing and walks nothing.
        /// </summary>
        void RevealDiscoveredByCore()
        {
            if (Discovery == null || World?.Core == null) return;

            float radius = World.ActionRadiusCells;
            if (radius.Equals(_lastRevealedCoreRadius)) return;

            _lastRevealedCoreRadius = radius;
            Discovery.RevealDisc(World.CoreCenterCells, radius);
        }

        void Update()
        {
            // Settle last frame's Power reports before this frame's buildings report new ones -
            // the one-frame lag is intentional (CONTRACTS.md §9's report-then-settle contract),
            // not an ordering bug. Compute has no such flow: its Tick only advances the window
            // its displayed income rate is averaged over.
            Power.Settle();
            Compute.Tick(Time.deltaTime);

            Transport.Tick(Time.deltaTime);

            // After Transport so reservations and robot pickups see this frame's settled container
            // contents (a production building's output has already been pushed/pulled by now), and
            // so a segment materialized this frame is registered before the next frame's transport
            // pass. Robots and construction sites are driven from here and only from here - never
            // from an individual Update() (PROJECT_ARCHITECTURE.md §17).
            ConstructionSites?.Tick(Time.deltaTime);
            Notifications?.Tick(Time.deltaTime);

            Research.Tick(Time.deltaTime);

            // After Research, which is what extends the radius: the widened disc is written the same
            // frame it is granted rather than one frame later.
            RevealDiscoveredByCore();

            // After the Core's disc, so a mission landing this frame reveals on top of an up-to-date
            // map rather than under it. The reserve and its cap are handed over rather than read,
            // because the probe threshold is a fraction of the cap - see MissionSettings.
            Missions?.Tick(Time.deltaTime, Compute.Reserve, ComputeSystem.ReserveCap);

            // Immediately after, so the frame the fleet exists is the frame it is on the ground.
            // Does nothing at all until then, and nothing again once it has spawned. Parked at the
            // Core's hatch, falling back to the Core itself if a game somehow has no reserve.
            _explorerPark?.Refresh(Missions, (BuildingRuntime)World?.CoreStorage ?? World?.Core);

            // Scaled deltaTime, like every system above it - which is the whole of how the run
            // clock pauses and resumes. Pause sets Time.timeScale to 0, so this is fed 0 and stops
            // where it stood, without knowing pause exists. See PlayClock.
            Clock.Advance(Time.deltaTime);

            // Not part of the simulation tick - where the player is looking is not simulation state,
            // which is why it sits after the clock and reads no deltaTime at all. On almost every
            // frame this is one subtraction and one comparison; it only re-ranks anything when the
            // camera has panned out of the ladder's slack, roughly every 98 world units.
            if (_depthSortCamera != null) DepthSort?.FollowCamera(_depthSortCamera.transform.position.y);

            // The cell grid is a construction aid, not permanent decoration: it shows only while
            // a building is armed for placement. Driven from here rather than from the
            // construction input adapter because this object already owns the view's reference
            // and lifecycle, and the adapter stops updating while a UI panel owns input - which
            // would strand the lines on screen with a tool still armed behind the panel.
            if (gridLineView != null) gridLineView.SetVisible(Construction.Selected != null);
        }

        void Start()
        {
            // Cross-object wiring in Start(), not Awake(): see ConstructionInputAdapter for why.
            if (terrainView != null)
            {
                terrainView.Initialize(Terrain, Grid);
            }

            RegisterSceneDepthSortedDecor();
            InitialiseDecor();

            GroundSlabSettings = BuildGroundSlabSettings();
            GroundSlabNeighborLinker = new GroundSlabNeighborLinker(Grid);

            if (gridLineView != null)
            {
                gridLineView.Initialize(Grid, Terrain.Size);
            }

            if (itemVisuals != null)
            {
                itemVisuals.Initialize(Grid, new ProceduralSpriteFactory(), itemDatabase);
            }

            if (World != null)
            {
                var contentSpawner = new WorldContentSpawner(Grid, new ProceduralSpriteFactory(), GroundSlabSettings, GroundSlabNeighborLinker, buildingShadowSettings, DepthSort);
                contentSpawner.SpawnCore(World.Core);
                Transport.Register(World.Core);

                // CoreStorage is only set on a fresh game (WorldGenerator.Generate) - a restored
                // game's copy comes back through the ordinary restored-building path below
                // instead, already registered and viewed like any other placed Storage box.
                if (World.CoreStorage != null)
                {
                    var coreStorageSpawner = new BuildingSpawner(Grid, new ProceduralSpriteFactory(), null, null, GroundSlabSettings, GroundSlabNeighborLinker, buildingShadowSettings, DepthSort);
                    coreStorageSpawner.SpawnView(World.CoreStorage);
                    Transport.Register(World.CoreStorage);
                }

                foreach (var deposit in World.OreDeposits)
                {
                    contentSpawner.SpawnOreDeposit(deposit);
                }

                Vector3 coreCenter = Grid.FootprintCenterToWorld(World.CoreOrigin, worldGenerationSettings.CoreDefinition.FootprintSize);

                if (actionRadiusView != null)
                {
                    actionRadiusView.Initialize(coreCenter, World.ActionRadiusCells * Grid.CellSize);

                    // Re-initializing is idempotent (just recomputes the ring's transform/material
                    // from the current radius), so refreshing on every completion rather than only
                    // extended_bandwidth keeps this generic - the view reflects whatever
                    // World.ActionRadiusCells (Core.ActionRadiusCells) is right now, live, with no
                    // reload (TASK_04_PLAFOND_RAYON.md §4.3).
                    Research.ResearchCompleted += _ => actionRadiusView.Initialize(coreCenter, World.ActionRadiusCells * Grid.CellSize);
                }

                if (fogOfWarView != null)
                {
                    // Neither the Core nor the radius: the fog draws the discovery state, and the
                    // radius only writes into it (RevealDiscoveredByCore). There is deliberately no
                    // research hook here either, unlike actionRadiusView above - extending the
                    // radius reveals cells, and revealed cells are what the fog already reads. A
                    // radius passed to this view is how it used to be a disc with no memory.
                    // The camera and the zoom-out cap, because the fog texture is now a window that
                    // follows the view rather than a copy of the map - see FogOfWarView. Same two
                    // inputs the depth ladder is built from, and for the same reason: how much world
                    // can be on screen at once is what bounds both.
                    fogOfWarView.Initialize(Discovery, Grid, _depthSortCamera, _maxOrthographicSize);
                }

                // Start the camera centered on the Core - otherwise its fixed scene position
                // has no relation to where world generation actually placed the Core (e.g.
                // after resizing the map, the Core's cell moves but the camera doesn't).
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    Vector3 camPos = mainCamera.transform.position;
                    camPos.x = coreCenter.x;
                    camPos.y = coreCenter.y;
                    mainCamera.transform.position = camPos;
                }
            }

            if (_restoredBuildings.Count > 0)
            {
                // Passed the same presentation settings as the placement path, which it was not:
                // a building coming back from a save has to look like the one that was placed, and
                // this spawner was giving it neither a concrete slab nor a shadow.
                var spawner = new BuildingSpawner(Grid, new ProceduralSpriteFactory(), null, null, GroundSlabSettings, GroundSlabNeighborLinker, buildingShadowSettings, DepthSort);
                foreach (BuildingRuntime building in _restoredBuildings)
                {
                    spawner.SpawnView(building);
                    if (itemVisuals != null) itemVisuals.Register(building);
                }
            }
        }

        /// <summary>
        /// Reads TerrainView.GroundMaterial's live _Biome* values (set by TerrainView.Initialize,
        /// called just before this) so the slab shader can recompute the exact same Mars/Gravel04
        /// base layer at a given world position - see BuildingGroundSlab.shader's frag() comment.
        /// Falls back to the shader's own Properties-block defaults (an all-white single texture,
        /// no visible sand encroachment) when there is no TerrainView, rather than erroring.
        /// </summary>
        GroundSlabSettings BuildGroundSlabSettings()
        {
            var settings = new GroundSlabSettings
            {
                SlabDiffuse = groundSlabDiffuse,
                SlabNormal = groundSlabNormal,
                SlabShader = groundSlabShader,
                SlabDarken = groundSlabDarken,
                SandBandWidth = groundSlabSandBandWidth,
                EdgeSoftness = groundSlabEdgeSoftness,
                SandNoiseScale = groundSlabSandNoiseScale,
                SandNoiseAmplitude = groundSlabSandNoiseAmplitude,
            };

            Material groundMaterial = terrainView != null ? terrainView.GroundMaterial : null;
            if (groundMaterial != null)
            {
                settings.BiomeTextures = new[] { groundMaterial.GetTexture("_BiomeTex0"), groundMaterial.GetTexture("_BiomeTex1"), groundMaterial.GetTexture("_BiomeTex2") };
                settings.BiomeWeights = new[] { groundMaterial.GetFloat("_BiomeWeight0"), groundMaterial.GetFloat("_BiomeWeight1"), groundMaterial.GetFloat("_BiomeWeight2") };
                settings.BiomeTexCount = groundMaterial.GetFloat("_BiomeTexCount");
                settings.BiomeCellSize = groundMaterial.GetFloat("_BiomeCellSize");
                settings.BiomeEdgeSoftness = groundMaterial.GetFloat("_BiomeEdgeSoftness");
                settings.BiomeSeed = groundMaterial.GetFloat("_BiomeSeed");
                Vector4 origin = groundMaterial.GetVector("_VariationOrigin");
                settings.VariationOrigin = new Vector2(origin.x, origin.y);
                Vector4 textureWorldSize = groundMaterial.GetVector("_TextureWorldSize");
                settings.TextureWorldSize = new Vector2(textureWorldSize.x, textureWorldSize.y);
            }

            return settings;
        }
    }
}
