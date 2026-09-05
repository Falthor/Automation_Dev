using System.Collections.Generic;
using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Notifications;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
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

        [Header("World generation (Core + ore deposits, spawned once at game start)")]
        [SerializeField] WorldGenerationSettings worldGenerationSettings;
        [SerializeField] ActionRadiusView actionRadiusView;
        [SerializeField] FogOfWarView fogOfWarView;

        /// <summary>
        /// Cells the fog reveal extends beyond the Core's current action radius
        /// (TASK_04_PLAFOND_RAYON.md's follow-up correction) - the invitation ore clusters
        /// WorldGenerator places just outside the constructible radius must be visible before
        /// extended_bandwidth makes them constructible, or the whole invitation never gets seen.
        /// A named, tunable value rather than a buried literal: likely to be retouched once the
        /// invitation band (WorldGenerator.InvitationMinDistanceCells/MaxDistanceCells) itself is.
        /// </summary>
        [SerializeField, Min(0)] int fogRadiusMarginCells = 10;

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

        public GridRuntime Grid { get; private set; }
        public TerrainRuntime Terrain { get; private set; }
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

        /// <summary>
        /// GlobalStock keeps its name but its contract is inverted since TASK_05_ROBOT_CONSTRUCTEUR.md:
        /// it holds nothing at all any more. It is a read-only aggregated view over the Core chest,
        /// every placed Storage and every production building's output, minus everything already
        /// reserved by a construction site - i.e. exactly what a builder robot could still be sent
        /// to fetch (CONTRACTS.md §15). Recomputed on every read; never serialized.
        /// </summary>
        public IReadOnlyDictionary<string, int> GlobalStock =>
            ConstructionSites != null ? ConstructionSites.GetAvailableAggregate() : new Dictionary<string, int>();

        /// <summary>Construction sites + the two builder robots (TASK_05_ROBOT_CONSTRUCTEUR.md), ticked from this object's central Update() like every other simulation system.</summary>
        public ConstructionSiteSystem ConstructionSites { get; private set; }

        /// <summary>Generic notification banner feed (TASK_05_ROBOT_CONSTRUCTEUR.md §6) - a robot unable to unload is its first user, not its only intended one.</summary>
        public NotificationSystem Notifications { get; private set; }

        /// <summary>
        /// True while a UI panel (Building menu, Storage panel, ...) is open and should own
        /// mouse input exclusively. World input adapters (construction, storage selection) must
        /// skip their own click handling while this is set, otherwise a click that selects a
        /// menu item or closes a panel also leaks through as a world click on the same frame.
        /// Derived from Selection (both the named global panel and the currently inspected
        /// building) - there is exactly one source of truth for "is a panel open" (CONTRACTS.md
        /// §7), panels no longer track this themselves.
        /// </summary>
        public bool IsUIBlockingInput => Selection.ActiveGlobalPanel != null || Selection.SelectedBuilding != null;

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

            SaveData loadedSave = PendingGameStart.LoadedSave;
            PendingGameStart.RequestNewGame(); // consume immediately - never read a second time this session

            if (loadedSave != null)
            {
                Terrain = new TerrainRuntime(loadedSave.TerrainSize, loadedSave.TerrainSeed, loadedSave.TerrainScale, loadedSave.TerrainProportion);
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
                Terrain = new TerrainRuntime(terrainSettings.Size, terrainSettings.Seed, terrainSettings.TerrainScale, terrainSettings.Proportion);

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
                Construction = new ConstructionService(Grid, itemDatabase, recipeDatabase, Compute, Power, Research, Transport, World?.Core, ConstructionSites);
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
                    deposit.RestoreState(depositSave.RemainingQuantity);
                    deposits.Add(deposit);
                }

                World = new WorldGenerator();
                World.RestoreState(core, coreCell, deposits);
            }

            ConstructionSites = new ConstructionSiteSystem(Transport, Grid, Notifications, RobotParkOrigin());
            Construction = new ConstructionService(Grid, itemDatabase, recipeDatabase, Compute, Power, Research, Transport, World?.Core, ConstructionSites);
            Construction.RestoreBuildingCap(save.BuildingCap);

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
                TerrainSeed = terrainSettings.Seed,
                TerrainSize = terrainSettings.Size,
                TerrainScale = terrainSettings.TerrainScale,
                TerrainProportion = terrainSettings.Proportion,
                ComputeReserve = Compute.Reserve,
                ResearchActiveId = Research.ActiveResearch != null ? Research.ActiveResearch.Id : null,
                ResearchProgress = Research.AbsorbedCu,
                ResearchQueue = BuildResearchQueueIds(),
                ResearchUnlocked = new List<string>(Research.GetUnlockedIds()),
                BuildingCap = Construction.BuildingCap,
                ConstructionSites = ConstructionSites?.CaptureState()
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
                        RemainingQuantity = deposit.RemainingQuantity
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

        void OnApplicationQuit()
        {
            SaveCurrentGame();
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
                var contentSpawner = new WorldContentSpawner(Grid, new ProceduralSpriteFactory(), GroundSlabSettings, GroundSlabNeighborLinker, buildingShadowSettings);
                contentSpawner.SpawnCore(World.Core);
                Transport.Register(World.Core);

                // CoreStorage is only set on a fresh game (WorldGenerator.Generate) - a restored
                // game's copy comes back through the ordinary restored-building path below
                // instead, already registered and viewed like any other placed Storage box.
                if (World.CoreStorage != null)
                {
                    var coreStorageSpawner = new BuildingSpawner(Grid, new ProceduralSpriteFactory(), null, null, GroundSlabSettings, GroundSlabNeighborLinker);
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
                    // fogRadiusMarginCells beyond the constructible radius, not the same value as
                    // ActionRadiusView: the invitation ore clusters WorldGenerator places just
                    // outside the constructible radius must already be visible before
                    // extended_bandwidth makes them constructible, or the player never sees the
                    // invitation at all. Refreshed on every research completion for the same
                    // reason as actionRadiusView above - this was wrongly left out of scope in
                    // TASK_04_PLAFOND_RAYON.md and is corrected here, not a new feature.
                    fogOfWarView.Initialize(coreCenter, (World.ActionRadiusCells + fogRadiusMarginCells) * Grid.CellSize);
                    Research.ResearchCompleted += _ => fogOfWarView.Initialize(coreCenter, (World.ActionRadiusCells + fogRadiusMarginCells) * Grid.CellSize);
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
                var spawner = new BuildingSpawner(Grid, new ProceduralSpriteFactory());
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
