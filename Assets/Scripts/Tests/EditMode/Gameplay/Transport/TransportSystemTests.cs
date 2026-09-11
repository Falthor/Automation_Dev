using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Sites;
using Game.Gameplay.Transport;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Transport
{
    public class TransportSystemTests
    {
        /// <summary>
        /// A building with an output arrow hands out on that cell and on no other, whether the
        /// receiver is pushed to or pulls for itself.
        ///
        /// Reported as a box beside a Factory's arrow filling up with wire. Two separate paths were
        /// feeding it: the push walked the whole output edge (three cells for a 3-wide building,
        /// one arrow), and the Storage's own pull reached into any neighbour it merely touched -
        /// so a box parked against the back of the Factory was served too. Both now ask the source
        /// where it actually hands out, which is the same promise the entry arrows already make.
        /// </summary>
        [Test]
        public void ABuildingWithAnOutputArrow_FeedsOnlyTheCellThatArrowMarks()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            ItemDefinition ingot = TestDataFactory.NewItem("copper_Ingot");
            RecipeDatabase recipes = TestDataFactory.NewRecipeDatabase(
                TestDataFactory.NewRecipe("copper_wire", 3f, 0f, 2, (ingot, 1)));

            // 3x3 facing East: its output edge spans (3,0) (3,1) (3,2), the arrow sits on (3,1).
            FactoryDefinition definition = TestDataFactory.NewFactory(0f, new[] { "copper_wire" }, new[] { "copper_Ingot" });
            var so = new UnityEditor.SerializedObject(definition);
            so.FindProperty("footprintSize").vector2IntValue = new Vector2Int(3, 3);
            so.ApplyModifiedPropertiesWithoutUndo();

            var factory = new FactoryRuntime(definition, new GridCoord(0, 0), Direction.East, recipes,
                new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            grid.SetOccupantFootprint(factory.Cell, definition.FootprintSize, factory);
            transport.Register(factory);

            Assert.AreEqual(new GridCoord(3, 1), factory.GetOutputCell(), "Precondition: the arrow is on the middle cell of the edge.");
            CollectionAssert.AreEqual(new[] { new GridCoord(3, 1) }, factory.GetOutputCells(), "And that is the only cell it hands out on.");

            StorageRuntime beside = AddBox(grid, transport, new GridCoord(3, 0));   // on the output edge, beside the arrow
            StorageRuntime inFront = AddBox(grid, transport, new GridCoord(3, 1));  // on the arrow
            StorageRuntime behind = AddBox(grid, transport, new GridCoord(-1, 1));  // touching a side with no output at all

            for (int i = 0; i < 200; i++)
            {
                factory.AddOutput("copper_wire", 1);
                transport.Tick(0.1f);
            }

            Assert.Greater(inFront.GetInputAmount("copper_wire"), 0, "The box the arrow points at is fed.");
            Assert.AreEqual(0, beside.GetInputAmount("copper_wire"), "The box beside it is not - the edge is wide, the outlet is not.");
            Assert.AreEqual(0, behind.GetInputAmount("copper_wire"), "And neither is one against a side that shows nothing.");
        }

        /// <summary>
        /// The rule is about a <b>declared</b> output side, not about being multi-cell. A Storage
        /// declares none - it is meant to be taken from wherever it is touched - and keeps behaving
        /// that way, or half the belt layouts in the game would quietly stop working.
        /// </summary>
        [Test]
        public void ABuildingWithNoOutputArrow_StillHandsOutWhereverItIsTouched()
        {
            StorageDefinition definition = TestDataFactory.NewStorage("box", 4, 100);
            var box = new StorageRuntime(definition, new GridCoord(10, 10), Direction.North);

            Assert.IsFalse(definition.HasOutputArrow, "Precondition.");
            foreach (var (cell, _) in box.GetEdgeCells()) Assert.IsTrue(box.HandsOutTo(cell), $"cell {cell}");
        }

        /// <summary>
        /// A chest takes only from a belt piece whose exit lands on it - not from a line running
        /// past its flank.
        ///
        /// Reported as a chest filling up from a line that never pointed at it. A belt hands out on
        /// every side it is touched on, deliberately (a machine may tap a passing line, and
        /// <c>Tick_TwoConsumersSharingOneInputCell_AlternateWhichOneIsFed</c> pins that), so the
        /// direction is a chest's own rule to ask - and it was asking about the belt's <i>shape</i>
        /// instead, which is a different question that happened to hide half the cases.
        /// </summary>
        [Test]
        public void ABeltRunningPastAChest_IsNotTakenFrom()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            // A line running north through (0,0) and (0,1): neither belt points at the chest.
            ConveyorRuntime lower = AddBelt(grid, transport, new GridCoord(0, 0), Direction.North);
            AddBelt(grid, transport, new GridCoord(0, 1), Direction.North);

            StorageRuntime beside = AddBox(grid, transport, new GridCoord(1, 0));

            Assert.IsFalse(lower.FeedsCell(beside.Cell), "The belt's exit is north, the chest is east.");
            Assert.IsTrue(lower.HandsOutTo(beside.Cell), "And it does offer its flank - to a machine.");

            for (int i = 0; i < 200; i++)
            {
                if (lower.HasRoomForNewItem) lower.ReceiveItem("copper_Ingot");
                transport.Tick(0.1f);
            }

            Assert.AreEqual(0, beside.GetInputAmount("copper_Ingot"), "A line is not a source on its flank.");
        }

        /// <summary>
        /// The other half of the same rule: a belt aimed at the chest feeds it, whatever shape that
        /// belt is. A corner ending on a chest is an ordinary layout and was refused for a while, by
        /// a straight-only restriction standing in for a direction check that was not enforcing one.
        /// </summary>
        [Test]
        public void ACornerAimedAtAChest_FeedsIt()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            // Entering from the south, leaving east onto the chest at (1,0).
            var corner = new ConveyorRuntime(TestDataFactory.NewConveyor(), new GridCoord(0, 0), Direction.North);
            corner.ConfigureAsCorner(Direction.South, Direction.East);
            grid.SetOccupant(corner.Cell, corner);
            transport.Register(corner);

            StorageRuntime chest = AddBox(grid, transport, new GridCoord(1, 0));

            Assert.AreEqual(ConveyorShapeKind.Corner, corner.Orientation.Shape, "Precondition.");
            Assert.IsTrue(corner.FeedsCell(chest.Cell), "Precondition: the corner does end on the chest.");

            for (int i = 0; i < 200; i++)
            {
                if (corner.HasRoomForNewItem) corner.ReceiveItem("copper_Ingot");
                transport.Tick(0.1f);
            }

            Assert.Greater(chest.GetInputAmount("copper_Ingot"), 0);
        }

        /// <summary>
        /// A machine standing alongside a chest never feeds it, whichever way its arrow points. A
        /// chest is fed by a line - that is what keeps it a reserve rather than the place a
        /// production building quietly empties itself into.
        /// </summary>
        [Test]
        public void AProductionBuildingFacingAChest_DoesNotFeedIt()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            ItemDefinition ingot = TestDataFactory.NewItem("copper_Ingot");
            RecipeDatabase recipes = TestDataFactory.NewRecipeDatabase(
                TestDataFactory.NewRecipe("copper_wire", 3f, 0f, 2, (ingot, 1)));

            FactoryDefinition definition = TestDataFactory.NewFactory(0f, new[] { "copper_wire" }, new[] { "copper_Ingot" });
            var factory = new FactoryRuntime(definition, new GridCoord(0, 0), Direction.East, recipes,
                new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            grid.SetOccupantFootprint(factory.Cell, definition.FootprintSize, factory);
            transport.Register(factory);

            StorageRuntime chest = AddBox(grid, transport, factory.GetOutputCell());

            for (int i = 0; i < 200; i++)
            {
                factory.AddOutput("copper_wire", 1);
                transport.Tick(0.1f);
            }

            Assert.AreEqual(0, chest.GetInputAmount("copper_wire"), "Put a belt between them.");
        }

        /// <summary>
        /// A building still being built holds its ground and nothing else: a belt aimed straight at
        /// it delivers nothing, and its own pull never runs. The flag is <c>IsUnderConstruction</c>
        /// and transport honours it at one chokepoint (<c>ActiveBuildingAt</c>); this pins the
        /// behaviour from the outside, which is where it was doubted.
        /// </summary>
        [Test]
        public void ABeltAimedAtABuildingStillUnderConstruction_DeliversNothing()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            ConveyorRuntime belt = AddBelt(grid, transport, new GridCoord(0, 0), Direction.East);

            StorageDefinition definition = TestDataFactory.NewStorage("box", 4, 100);
            var pending = new StorageRuntime(definition, new GridCoord(1, 0), Direction.North);
            grid.SetOccupantFootprint(pending.Cell, definition.FootprintSize, pending);

            // Through a real site rather than by setting the flag: the flag's setter is internal to
            // Game.Gameplay, and opening it for a test would let one be built that no chantier can
            // produce.
            var site = new ConstructionSiteRuntime(1, pending);
            Assert.IsTrue(pending.IsUnderConstruction, "Precondition: placing a segment flags it.");
            Assert.IsNotNull(site);

            Assert.IsTrue(belt.FeedsCell(pending.Cell), "Precondition: the belt does point at it.");

            for (int i = 0; i < 200; i++)
            {
                if (belt.HasRoomForNewItem) belt.ReceiveItem("copper_Ingot");
                transport.Tick(0.1f);
            }

            Assert.AreEqual(0, pending.GetInputAmount("copper_Ingot"), "A chantier takes nothing.");
        }

        static ConveyorRuntime AddBelt(GridRuntime grid, TransportSystem transport, GridCoord cell, Direction exit)
        {
            var belt = new ConveyorRuntime(TestDataFactory.NewConveyor(), cell, exit);
            belt.ConfigureAsStraight(exit);
            grid.SetOccupant(cell, belt);
            transport.Register(belt);
            return belt;
        }

        static StorageRuntime AddBox(GridRuntime grid, TransportSystem transport, GridCoord cell)
        {
            StorageDefinition definition = TestDataFactory.NewStorage("box", 4, 100);
            var box = new StorageRuntime(definition, cell, Direction.North);
            grid.SetOccupantFootprint(cell, definition.FootprintSize, box);
            transport.Register(box);
            return box;
        }

        [Test]
        public void Tick_TwoConsumersSharingOneInputCell_AlternateWhichOneIsFed()
        {
            // Reproduces the reported bug: two Factories both facing the same conveyor cell -
            // one to its north (entry south), one to its west (entry east) - used to always feed
            // the same, earlier-registered Factory every tick. The recipe's ingredient amount is
            // set absurdly high so neither Factory ever actually starts crafting (which would
            // consume the delivered item and confuse the win-count below) - this test only cares
            // about which Factory's input pool grows on a given tick, not production itself.
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            var ironOre = TestDataFactory.NewItem("iron_ore", ItemType.Ore);
            var recipe = TestDataFactory.NewRecipe("iron_ore_sink", 100f, 0f, 1, (ironOre, 1000));
            var recipeDatabase = TestDataFactory.NewRecipeDatabase(recipe);

            var definitionA = TestDataFactory.NewFactory(0f, new[] { "iron_ore_sink" }, System.Array.Empty<string>());
            var definitionB = TestDataFactory.NewFactory(0f, new[] { "iron_ore_sink" }, System.Array.Empty<string>());

            var conveyorCell = new GridCoord(5, 5);
            // North of the conveyor, entry pointing South at it.
            var factoryA = new FactoryRuntime(definitionA, new GridCoord(5, 6), Direction.North, recipeDatabase, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            // West of the conveyor, entry pointing East at it.
            var factoryB = new FactoryRuntime(definitionB, new GridCoord(4, 5), Direction.West, recipeDatabase, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            factoryA.SetSelectedRecipe("iron_ore_sink");
            factoryB.SetSelectedRecipe("iron_ore_sink");
            transport.Register(factoryA);
            transport.Register(factoryB);

            var conveyorDefinition = ScriptableObject.CreateInstance<ConveyorDefinition>();
            var conveyor = new ConveyorRuntime(conveyorDefinition, conveyorCell, Direction.North);
            grid.SetOccupant(conveyorCell, conveyor);

            int aWins = 0;
            int bWins = 0;
            for (int i = 0; i < 6; i++)
            {
                conveyor.ReceiveItem("iron_ore");
                conveyor.AdvanceItem(10f, 100f); // force the item's progress to 1 (pullable) this tick

                int beforeA = factoryA.GetInputAmount("iron_ore");
                int beforeB = factoryB.GetInputAmount("iron_ore");

                transport.Tick(0.016f);

                if (factoryA.GetInputAmount("iron_ore") > beforeA) aWins++;
                else if (factoryB.GetInputAmount("iron_ore") > beforeB) bWins++;
            }

            Assert.AreEqual(3, aWins, "Factory A should win exactly half of the contested pulls.");
            Assert.AreEqual(3, bWins, "Factory B should win exactly half of the contested pulls.");
        }

        [Test]
        public void TickSplitters_DeliversDirectlyIntoAFactory_WithNoConveyorBetween()
        {
            // Reproduces the reported bug: a splitter's exit wired straight into a production
            // building (no belt in between) never delivered - TryDeliverFromSplitter only
            // considered a direction "connected" when the neighbor was a Conveyor/Splitter/
            // Crossroad/Storage, silently excluding every production building.
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            var ironOre = TestDataFactory.NewItem("iron_ore", ItemType.Ore);
            var recipe = TestDataFactory.NewRecipe("iron_ore_sink", 100f, 0f, 1, (ironOre, 1000));
            var recipeDatabase = TestDataFactory.NewRecipeDatabase(recipe);
            var factoryDefinition = TestDataFactory.NewFactory(0f, new[] { "iron_ore_sink" }, System.Array.Empty<string>());

            var splitterDefinition = ScriptableObject.CreateInstance<SplitterDefinition>();
            var splitterOrigin = new GridCoord(5, 5);
            var splitter = new SplitterRuntime(splitterDefinition, splitterOrigin, Direction.North); // entry side North
            transport.Register(splitter);

            // East arm's neighbor cell - one of the splitter's non-entry candidate exits.
            GridCoord factoryCell = CrossFootprint_NeighborCellForTest(splitterOrigin, Direction.East);
            var factory = new FactoryRuntime(factoryDefinition, factoryCell, Direction.North, recipeDatabase, new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            factory.SetSelectedRecipe("iron_ore_sink");
            grid.SetOccupant(factoryCell, factory);
            transport.Register(factory);

            splitter.AddInput("iron_ore", 1, Direction.North);
            Assert.IsTrue(splitter.HasItem);

            transport.Tick(0.016f);

            Assert.IsFalse(splitter.HasItem, "The splitter should have handed its item to the factory.");
            Assert.AreEqual(1, factory.GetInputAmount("iron_ore"));
        }

        static GridCoord CrossFootprint_NeighborCellForTest(GridCoord origin, Direction direction)
        {
            // Mirrors the internal (non-public) CrossFootprint math used by SplitterRuntime/CrossroadRuntime.
            switch (direction)
            {
                case Direction.North: return new GridCoord(origin.X + 1, origin.Y + 3);
                case Direction.South: return new GridCoord(origin.X + 1, origin.Y - 1);
                case Direction.East: return new GridCoord(origin.X + 3, origin.Y + 1);
                default: return new GridCoord(origin.X - 1, origin.Y + 1); // West
            }
        }
    }
}
