using System.Collections.Generic;
using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Notifications;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Sites;
using Game.Gameplay.Transport;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Data
{
    /// <summary>
    /// The early-game construction economy as it actually ships: starting stock, what each
    /// bootstrap building costs, and that the reference bootstrap (early-game-economy-rebalance)
    /// leaves only a small margin rather than funding real expansion. Reads the shipped assets by
    /// path, like <see cref="TransportPieceCostTests"/> and <see cref="WorldGenerationSettingsTests"/>,
    /// so a figure changed in the Inspector is what fails here, never a copy of it.
    /// </summary>
    public class EarlyGameEconomyTests
    {
        const string IronPlate = "Iron_Plate";
        const string CopperPlate = "copper_plate";
        const string IronRod = "iron_rod";
        const string CopperWire = "copper_wire";

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"{path} is missing.");
            return asset;
        }

        static Dictionary<string, int> CostById(BuildingDefinition definition)
        {
            var byId = new Dictionary<string, int>();
            foreach (RecipeIngredient ingredient in definition.Cost)
            {
                Assert.IsNotNull(ingredient.Item, definition.name + ": an unassigned cost item");
                byId[ingredient.Item.Id] = ingredient.Amount;
            }
            return byId;
        }

        [Test]
        public void ExtractorCosts_EightPlatesAndFourRods()
        {
            Dictionary<string, int> cost = CostById(Load<ExtractorDefinition>("Assets/Data/Buildings/ExtractorDefinition.asset"));
            Assert.AreEqual(2, cost.Count);
            Assert.AreEqual(8, cost[IronPlate]);
            Assert.AreEqual(4, cost[IronRod]);
        }

        [Test]
        public void FoundryCosts_SixPlatesAndFourRods()
        {
            Dictionary<string, int> cost = CostById(Load<FoundryDefinition>("Assets/Data/Buildings/FoundryDefinition.asset"));
            Assert.AreEqual(2, cost.Count);
            Assert.AreEqual(6, cost[IronPlate]);
            Assert.AreEqual(4, cost[IronRod]);
        }

        [Test]
        public void ConstructorCosts_SixPlatesFourCopperPlatesFourWires()
        {
            Dictionary<string, int> cost = CostById(Load<ConstructorDefinition>("Assets/Data/Buildings/ConstructorDefinition.asset"));
            Assert.AreEqual(3, cost.Count);
            Assert.AreEqual(6, cost[IronPlate]);
            Assert.AreEqual(4, cost[CopperPlate]);
            Assert.AreEqual(4, cost[CopperWire]);
        }

        [Test]
        public void StorageCosts_FourPlates()
        {
            Dictionary<string, int> cost = CostById(Load<StorageDefinition>("Assets/Data/Buildings/StorageDefinition.asset"));
            Assert.AreEqual(1, cost.Count);
            Assert.AreEqual(4, cost[IronPlate]);
        }

        [Test]
        public void StartingStock_Matches120PlatesTwentyFourCopperTwentyFourRodsSixteenWires()
        {
            WorldGenerationSettings settings = Load<WorldGenerationSettings>("Assets/Data/World/WorldGenerationSettings.asset");
            var byId = new Dictionary<string, int>();
            foreach (RecipeIngredient ingredient in settings.StartingStock)
            {
                Assert.IsNotNull(ingredient.Item, "an unassigned starting-stock item");
                byId[ingredient.Item.Id] = ingredient.Amount;
            }

            Assert.AreEqual(4, byId.Count, "no advanced component should be seeded at game start");
            Assert.AreEqual(120, byId[IronPlate]);
            Assert.AreEqual(24, byId[CopperPlate]);
            Assert.AreEqual(24, byId[IronRod]);
            Assert.AreEqual(16, byId[CopperWire]);
        }

        static void Accumulate(Dictionary<string, int> bill, BuildingDefinition definition, int count)
        {
            foreach (RecipeIngredient ingredient in definition.Cost)
            {
                bill.TryGetValue(ingredient.Item.Id, out int existing);
                bill[ingredient.Item.Id] = existing + ingredient.Amount * count;
            }
        }

        /// <summary>
        /// The reference bootstrap (2 Extracteurs, 2 Fonderies, 2 Constructeurs, 2 Stockages, 40
        /// Convoyeurs) costs and leaves exactly what the chantier's directive promised, computed from
        /// the real assets rather than pinned by hand - so a later cost tweak that breaks the
        /// promised margin fails here instead of only surfacing in play.
        /// </summary>
        [Test]
        public void ReferenceBootstrap_Costs88PlatesEightCopperSixteenRodsEightWires_AndLeavesAMargin()
        {
            var extractor = Load<ExtractorDefinition>("Assets/Data/Buildings/ExtractorDefinition.asset");
            var foundry = Load<FoundryDefinition>("Assets/Data/Buildings/FoundryDefinition.asset");
            var constructor = Load<ConstructorDefinition>("Assets/Data/Buildings/ConstructorDefinition.asset");
            var storage = Load<StorageDefinition>("Assets/Data/Buildings/StorageDefinition.asset");
            var conveyor = Load<ConveyorDefinition>("Assets/Data/Buildings/ConveyorStraight.asset");
            var settings = Load<WorldGenerationSettings>("Assets/Data/World/WorldGenerationSettings.asset");

            var bill = new Dictionary<string, int>();
            Accumulate(bill, extractor, 2);
            Accumulate(bill, foundry, 2);
            Accumulate(bill, constructor, 2);
            Accumulate(bill, storage, 2);
            Accumulate(bill, conveyor, 40);

            Assert.AreEqual(88, bill[IronPlate]);
            Assert.AreEqual(8, bill[CopperPlate]);
            Assert.AreEqual(16, bill[IronRod]);
            Assert.AreEqual(8, bill[CopperWire]);

            var stock = new Dictionary<string, int>();
            foreach (RecipeIngredient ingredient in settings.StartingStock) stock[ingredient.Item.Id] = ingredient.Amount;

            foreach (var line in bill)
            {
                Assert.IsTrue(stock.TryGetValue(line.Key, out int available) && available >= line.Value,
                    $"the reference bootstrap needs {line.Value} {line.Key} but starting stock has less");
            }

            Assert.AreEqual(32, stock[IronPlate] - bill[IronPlate]);
            Assert.AreEqual(16, stock[CopperPlate] - bill[CopperPlate]);
            Assert.AreEqual(8, stock[IronRod] - bill[IronRod]);
            Assert.AreEqual(8, stock[CopperWire] - bill[CopperWire]);
        }

        /// <summary>
        /// Directive 1 (currently 50 Tiges + 50 Fils, untouched by this chantier) must stay out of
        /// reach of the starting stock alone once the reference bootstrap is funded - raising the
        /// stock only as far as the bootstrap needs it is the whole point. Reads
        /// FirstDirective.asset directly rather than pinning its numbers, since this chantier is
        /// explicitly forbidden from touching them.
        /// </summary>
        [Test]
        public void Directive1_StaysUnreachable_AfterTheReferenceBootstrap()
        {
            var extractor = Load<ExtractorDefinition>("Assets/Data/Buildings/ExtractorDefinition.asset");
            var foundry = Load<FoundryDefinition>("Assets/Data/Buildings/FoundryDefinition.asset");
            var constructor = Load<ConstructorDefinition>("Assets/Data/Buildings/ConstructorDefinition.asset");
            var storage = Load<StorageDefinition>("Assets/Data/Buildings/StorageDefinition.asset");
            var conveyor = Load<ConveyorDefinition>("Assets/Data/Buildings/ConveyorStraight.asset");
            var settings = Load<WorldGenerationSettings>("Assets/Data/World/WorldGenerationSettings.asset");
            var directive = Load<CoreDirectiveDefinition>("Assets/Data/Core/FirstDirective.asset");

            var stock = new Dictionary<string, int>();
            foreach (RecipeIngredient ingredient in settings.StartingStock) stock[ingredient.Item.Id] = ingredient.Amount;

            var bill = new Dictionary<string, int>();
            Accumulate(bill, extractor, 2);
            Accumulate(bill, foundry, 2);
            Accumulate(bill, constructor, 2);
            Accumulate(bill, storage, 2);
            Accumulate(bill, conveyor, 40);
            foreach (var line in bill) stock[line.Key] -= line.Value;

            Assert.Greater(directive.Requirements.Length, 0, "precondition: Directive 1 still asks for something");
            foreach (RecipeIngredient requirement in directive.Requirements)
            {
                stock.TryGetValue(requirement.Item.Id, out int remaining);
                Assert.Less(remaining, requirement.Amount,
                    $"Directive 1 must still need {requirement.Item.Id} to be produced, not merely left over in "
                    + $"the chest after the bootstrap ({remaining} remain against {requirement.Amount} needed).");
            }
        }

        /// <summary>
        /// The real entry point, not just the definition: the shipped ConveyorStraight, driven
        /// through the real ConstructionService, is refused without a Plaque de fer to spend and
        /// consumes exactly one once funded - proving the per-segment cost is actually wired, not
        /// only declared on the asset (DEVELOPMENT_RULES.md: a tested predicate is not an applied
        /// predicate).
        /// </summary>
        [Test]
        public void TheShippedConveyor_IsRefusedWithoutAPlate_AndConsumesOneWhenFunded()
        {
            var conveyor = Load<ConveyorDefinition>("Assets/Data/Buildings/ConveyorStraight.asset");

            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);
            var sites = new ConstructionSiteSystem(transport, grid, new NotificationSystem(), Vector2.zero);
            var service = new ConstructionService(grid, null, null, new ComputeSystem(), new ComputeSystem(),
                new PowerSystem(), new ResearchSystem(new ComputeSystem(), new ComputeSystem()), transport, null, sites);

            service.SelectBuilding(conveyor);
            Assert.IsFalse(service.CanAfford(conveyor), "Precondition: nothing has been seeded yet.");
            Assert.AreEqual(PlacementRefusalReason.CannotAfford, service.GetPlacementRefusalReason(new GridCoord(0, 0)));
            Assert.IsFalse(service.TryPlace(new GridCoord(0, 0), Direction.North, out ConstructionSiteRuntime refused),
                "No standard placement path may create a segment for free.");
            Assert.IsNull(refused);

            StorageDefinition chestDefinition = TestDataFactory.NewStorage("chest");
            var chest = new StorageRuntime(chestDefinition, new GridCoord(5, 5), Direction.North);
            grid.SetOccupantFootprint(chest.Cell, chestDefinition.FootprintSize, chest);
            transport.Register(chest);
            chest.SeedInitialContents(IronPlate, 1);

            Assert.IsTrue(service.CanAfford(conveyor));
            Assert.IsTrue(service.TryPlace(new GridCoord(0, 0), Direction.North, out ConstructionSiteRuntime site));
            Assert.IsNotNull(site);
            Assert.AreEqual(0, service.GetAvailableAmount(IronPlate), "The one plate is now reserved by the site.");
            Assert.IsFalse(service.CanAfford(conveyor), "Nothing left to fund a second segment.");
        }
    }
}
