using Game.Construction;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Selection;
using Game.Gameplay.Transport;
using Game.Gameplay.WorldGeneration;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Minimal bootstrap wiring: constructs GridRuntime + TerrainRuntime + ConstructionService
    /// and exposes them via a plain scene reference (not a singleton, not the full staged
    /// pipeline from PROJECT_ARCHITECTURE.md - collapsed here until enough systems exist to
    /// justify stages).
    /// </summary>
    public sealed class GameRuntime : MonoBehaviour
    {
        [SerializeField] float cellSize = 1f;
        [SerializeField] TerrainGenerationSettings terrainSettings;
        [SerializeField] TerrainView terrainView;
        [SerializeField] GridLineView gridLineView;

        [Header("Item/Recipe registries")]
        [SerializeField] ItemDatabase itemDatabase;
        [SerializeField] RecipeDatabase recipeDatabase;

        [Header("World generation (Core + ore deposits, spawned once at game start)")]
        [SerializeField] WorldGenerationSettings worldGenerationSettings;
        [SerializeField] ActionRadiusView actionRadiusView;
        [SerializeField] FogOfWarView fogOfWarView;
        [SerializeField] ItemVisualSync itemVisuals;

        public GridRuntime Grid { get; private set; }
        public TerrainRuntime Terrain { get; private set; }
        public ConstructionService Construction { get; private set; }
        public WorldGenerator World { get; private set; }
        public TransportSystem Transport { get; private set; }
        public SelectionRuntime Selection { get; private set; }
        public ItemVisualSync ItemVisuals => itemVisuals;
        public ItemDatabase Items => itemDatabase;
        public RecipeDatabase Recipes => recipeDatabase;
        public PowerSystem Power { get; private set; }
        public ComputeSystem Compute { get; private set; }
        public ResearchSystem Research { get; private set; }

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

        void Awake()
        {
            Grid = new GridRuntime(cellSize);
            Terrain = new TerrainRuntime(terrainSettings.Size, terrainSettings.Seed, terrainSettings.TerrainScale, terrainSettings.Proportion);
            Power = new PowerSystem();
            Compute = new ComputeSystem();
            Research = new ResearchSystem();
            Transport = new TransportSystem(Grid);

            // World generation (Core + deposits) must exist before ConstructionService, which
            // needs the Core instance to check/deduct construction costs and its action radius.
            if (worldGenerationSettings != null)
            {
                World = new WorldGenerator();
                World.Generate(Grid, Terrain.Size, worldGenerationSettings, Compute, Power);
            }

            Construction = new ConstructionService(Grid, itemDatabase, recipeDatabase, Compute, Power, Research, Transport, World?.Core);
            Selection = new SelectionRuntime();
            Selection.GlobalPanelChanged += name =>
            {
                if (name == null) LastMenuCloseFrame = Time.frameCount;
            };
            Selection.SelectionChanged += building =>
            {
                if (building == null) LastMenuCloseFrame = Time.frameCount;
            };
        }

        void Update()
        {
            // Settle last frame's Power/Compute reports before this frame's buildings report new
            // ones - the one-frame lag is intentional (CONTRACTS.md §9/§10's report-then-settle
            // contract), not an ordering bug.
            Power.Settle();
            Compute.Settle();
            Compute.GrowReserve(Time.deltaTime);

            Transport.Tick(Time.deltaTime);
            Research.Tick(Time.deltaTime);
        }

        void Start()
        {
            // Cross-object wiring in Start(), not Awake(): see ConstructionInputAdapter for why.
            if (terrainView != null)
            {
                terrainView.Initialize(Terrain, Grid);
            }

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
                var contentSpawner = new WorldContentSpawner(Grid, new ProceduralSpriteFactory());
                contentSpawner.SpawnCore(World.Core);
                Transport.Register(World.Core);
                foreach (var deposit in World.OreDeposits)
                {
                    contentSpawner.SpawnOreDeposit(deposit);
                }

                Vector3 coreCenter = Grid.FootprintCenterToWorld(World.CoreOrigin, worldGenerationSettings.CoreDefinition.FootprintSize);

                if (actionRadiusView != null)
                {
                    actionRadiusView.Initialize(coreCenter, World.ActionRadiusCells * Grid.CellSize);
                }

                if (fogOfWarView != null)
                {
                    fogOfWarView.Initialize(coreCenter, World.ActionRadiusCells * Grid.CellSize);
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
        }
    }
}
