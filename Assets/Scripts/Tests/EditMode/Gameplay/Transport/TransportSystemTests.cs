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
        ///
        /// The boxes are the probe again. They were belts for a morning, while a chest was not
        /// allowed to take from a machine at all - which made a box answer "nothing arrived" for a
        /// reason that had nothing to do with the arrow.
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

        /// <summary>
        /// A chest empties onto a belt leading away from it. It used to expose no pullable item at
        /// all, so a box that filled up could only be emptied by hand, one slot at a time.
        /// </summary>
        [Test]
        public void ABeltLeadingAwayFromAChest_EmptiesIt()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            StorageRuntime chest = AddBox(grid, transport, new GridCoord(0, 0));
            chest.SeedInitialContents("copper_Ingot", 20);

            // Behind this belt is the chest, so its straight-through pull reaches it.
            ConveyorRuntime belt = AddBelt(grid, transport, new GridCoord(1, 0), Direction.East);

            for (int i = 0; i < 60; i++) transport.Tick(0.1f);

            Assert.Greater(belt.Items.Count, 0, "The belt is carrying what the chest gave it.");
            Assert.Less(chest.GetInputAmount("copper_Ingot"), 20, "And the chest is lighter by the same.");
        }

        /// <summary>
        /// A line running past a chest does not siphon it. Only a belt <b>leading away</b> from a
        /// chest - its back edge against it - may take from one.
        ///
        /// The distinction is the whole of what a chest's hand-out means, and losing it was visible
        /// from orbit: a chest has no output side to declare, so it hands out on all four
        /// (<c>StorageRuntime.FeedsCell</c>), and the side merge asks exactly that question. Every
        /// buffer in a base bled into whatever line passed it, the chests that used to be where a
        /// line ended started feeding it back, and a loaded save came up with every belt packed at
        /// three items a cell.
        /// </summary>
        [Test]
        public void ABeltRunningPastAChest_DoesNotSiphonIt()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            StorageRuntime chest = AddBox(grid, transport, new GridCoord(0, 0));
            chest.SeedInitialContents("copper_Ingot", 20);

            // Running north past the chest's east flank: its back edge is south, on open ground, so
            // the chest is beside it and never behind it.
            ConveyorRuntime passing = AddBelt(grid, transport, new GridCoord(1, 0), Direction.North);

            for (int i = 0; i < 200; i++) transport.Tick(0.1f);

            Assert.AreEqual(0, passing.Items.Count, "A line passing a chest is not a line leaving it.");
            Assert.AreEqual(20, chest.GetInputAmount("copper_Ingot"), "And the chest keeps everything.");
        }

        /// <summary>
        /// Several belts leaving one chest share it in turn, rather than the first-registered one
        /// taking every item the rate gate lets out.
        ///
        /// The share is exact, not approximate: one item per <c>RawOutputPullIntervalSeconds</c>
        /// leaves the chest, and the belt that took the last one stands aside on the next.
        /// </summary>
        [Test]
        public void TwoBeltsLeavingOneChest_TakeTurns()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            StorageRuntime chest = AddBox(grid, transport, new GridCoord(0, 0));
            chest.SeedInitialContents("copper_Ingot", 20);

            ConveyorRuntime east = AddBelt(grid, transport, new GridCoord(1, 0), Direction.East);
            ConveyorRuntime west = AddBelt(grid, transport, new GridCoord(-1, 0), Direction.West);

            // Stop as soon as four items are out, which is before either belt can fill up (three
            // per cell) and therefore before a full belt could distort the share.
            for (int i = 0; i < 200 && east.Items.Count + west.Items.Count < 4; i++)
            {
                transport.Tick(0.1f);
            }

            Assert.AreEqual(4, east.Items.Count + west.Items.Count, "Four items left the chest.");
            Assert.AreEqual(2, east.Items.Count, "Two each, not four and none.");
            Assert.AreEqual(2, west.Items.Count);
        }

        /// <summary>
        /// A chest gives to the belt network and to nothing else: a machine standing against one may
        /// not reach into it. The mirror of what a chest accepts, and what keeps a box a buffer on a
        /// line rather than a feeder with no belt to see and no rate to read.
        /// </summary>
        [Test]
        public void AMachineTouchingAChest_MayNotTakeFromIt()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            StorageRuntime chest = AddBox(grid, transport, new GridCoord(0, 0));
            chest.SeedInitialContents("copper_Ingot", 20);

            ItemDefinition ingot = TestDataFactory.NewItem("copper_Ingot");
            RecipeDatabase recipes = TestDataFactory.NewRecipeDatabase(
                TestDataFactory.NewRecipe("copper_wire", 100f, 0f, 1, (ingot, 1000)));
            FactoryDefinition definition = TestDataFactory.NewFactory(0f, new[] { "copper_wire" }, new[] { "copper_Ingot" });

            // Facing East, so its input sides are the other three - the chest is on its West side.
            var factory = new FactoryRuntime(definition, new GridCoord(1, 0), Direction.East, recipes,
                new ComputeSystem(), new PowerSystem(), new ResearchSystem(new ComputeSystem()));
            factory.SetSelectedRecipe("copper_wire");
            grid.SetOccupantFootprint(factory.Cell, definition.FootprintSize, factory);
            transport.Register(factory);

            for (int i = 0; i < 200; i++) transport.Tick(0.1f);

            Assert.AreEqual(0, factory.GetInputAmount("copper_Ingot"), "Put a belt between them.");
            Assert.AreEqual(20, chest.GetInputAmount("copper_Ingot"), "And the chest is untouched.");
        }

        /// <summary>
        /// The Core chest is the one container a conveyor may not connect to <b>in either
        /// direction</b>: it refuses a belt's delivery (RejectsConveyorInput) and hands nothing to
        /// one either. It is the construction reserve, and a belt draining it would empty the
        /// player's stock of plates into a line.
        /// </summary>
        [Test]
        public void TheCoreChest_HandsOutNothingToABelt()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            StorageDefinition definition = TestDataFactory.NewStorage("core_storage", 6, 200, rejectsConveyorInput: true);
            var coreChest = new StorageRuntime(definition, new GridCoord(0, 0), Direction.North);
            grid.SetOccupantFootprint(coreChest.Cell, definition.FootprintSize, coreChest);
            transport.Register(coreChest);
            coreChest.SeedInitialContents("copper_Ingot", 20);

            ConveyorRuntime belt = AddBelt(grid, transport, new GridCoord(1, 0), Direction.East);

            for (int i = 0; i < 60; i++) transport.Tick(0.1f);

            Assert.IsNull(coreChest.PeekPullableItem(), "It exposes nothing to pull.");
            Assert.AreEqual(0, belt.Items.Count);
            Assert.AreEqual(20, coreChest.GetInputAmount("copper_Ingot"));
        }

        /// <summary>
        /// <b>No intake may put more on a cell than the cell holds</b>, however long the line stays
        /// jammed. Driven through the side merge, which is the path that stopped asking.
        ///
        /// What it cost: a belt merged into by another belt took an item every tick, so a jam grew a
        /// column of up to twenty-nine items on one cell. AdvanceItem spaces each item a third of a
        /// cell behind the one ahead, and nothing said where to stop - so the twenty-ninth sat at
        /// progress -8.33 and the view drew it eight cells behind the belt, on open ground and behind
        /// buildings. The player who reported it was looking at iron ore standing in a field.
        ///
        /// Both halves are asserted: the count, and that every item is somewhere on its own cell.
        /// </summary>
        [Test]
        public void ASideMerge_NeverPutsMoreOnACellThanItHolds()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            // A belt running north, jammed: nothing ahead of it takes anything.
            ConveyorRuntime jammed = AddBelt(grid, transport, new GridCoord(0, 0), Direction.North);

            // A second belt on its east flank, pointing west into it - a side merge, and a belt
            // source, so the entry rate does not gate it either.
            ConveyorRuntime feeder = AddBelt(grid, transport, new GridCoord(1, 0), Direction.West);

            for (int i = 0; i < 600; i++)
            {
                if (feeder.HasRoomForNewItem) feeder.ReceiveItem("iron_ore");
                transport.Tick(0.1f);
            }

            Assert.LessOrEqual(jammed.Items.Count, ConveyorRuntime.MaxItemsPerCell,
                $"A cell holds {ConveyorRuntime.MaxItemsPerCell}, and it was handed {jammed.Items.Count}.");

            foreach (ConveyorItemSlot slot in jammed.Items)
            {
                Assert.GreaterOrEqual(slot.Progress, 0f, "An item behind the back edge is drawn off the belt entirely.");
                Assert.LessOrEqual(slot.Progress, 1f);
            }
        }

        /// <summary>
        /// A save written while a belt was overfilled comes back holding what a belt can hold. The
        /// extras are lost rather than carried: they sit at positions the simulation has no rule for
        /// - off the belt, behind buildings - and reading them back would keep drawing them there for
        /// the rest of the run.
        /// </summary>
        [Test]
        public void RestoringABeltThatWasOverfilled_KeepsOnlyWhatFits()
        {
            var belt = new ConveyorRuntime(TestDataFactory.NewConveyor(), new GridCoord(0, 0), Direction.North);

            var items = new Newtonsoft.Json.Linq.JArray();
            float progress = 1f;
            for (int i = 0; i < 29; i++)
            {
                items.Add(new Newtonsoft.Json.Linq.JObject
                {
                    ["itemId"] = "iron_ore",
                    ["progress"] = progress
                });
                progress -= 1f / 3f;   // exactly what the mangled save carried, down to -8.33
            }

            belt.RestoreState(new Newtonsoft.Json.Linq.JObject
            {
                ["shape"] = (int)ConveyorShapeKind.Straight,
                ["rotation"] = (int)Direction.North,
                ["mirrored"] = false,
                ["items"] = items
            });

            Assert.AreEqual(ConveyorRuntime.MaxItemsPerCell, belt.Items.Count);
            foreach (ConveyorItemSlot slot in belt.Items)
            {
                Assert.GreaterOrEqual(slot.Progress, 0f);
                Assert.LessOrEqual(slot.Progress, 1f);
            }
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

        /// <summary>
        /// A Splitter's entry arm pays the entry rate, like every other way into the belt network.
        ///
        /// It was the one path that did not. A source that is not itself a belt - a chest, a
        /// machine's raw output - was drained one item per <b>tick</b> through a splitter's arm
        /// rather than one per <c>RawOutputPullIntervalSeconds</c>: at sixty frames a second that is
        /// sixty items a second out of a container rated for one, and everything downstream of it
        /// packed solid.
        ///
        /// Measured over one second with somewhere for the items to go, so that what bounds the
        /// count is the rate and not the splitter's single slot: ungated it drains four (three on
        /// the belt, one held), gated it takes one.
        /// </summary>
        [Test]
        public void ASplittersEntryArm_PaysTheEntryRate()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            var splitterDefinition = ScriptableObject.CreateInstance<SplitterDefinition>();
            var origin = new GridCoord(5, 5);
            var splitter = new SplitterRuntime(splitterDefinition, origin, Direction.South); // entry side South
            grid.SetOccupantFootprint(origin, splitterDefinition.FootprintCells, splitter);
            transport.Register(splitter);

            // The chest against the entry arm, and a belt on one exit so the splitter keeps asking.
            StorageRuntime chest = AddBox(grid, transport, CrossFootprint_NeighborCellForTest(origin, Direction.South));
            chest.SeedInitialContents("copper_Ingot", 20);
            AddBelt(grid, transport, CrossFootprint_NeighborCellForTest(origin, Direction.North), Direction.North);

            for (int i = 0; i < 10; i++) transport.Tick(0.1f);

            int taken = 20 - chest.GetInputAmount("copper_Ingot");
            Assert.LessOrEqual(taken, 2, $"One second, one item (two at a boundary) - {taken} left the chest.");
            Assert.GreaterOrEqual(taken, 1, "And the arm does take: this is a rate, not a refusal.");
        }

        /// <summary>
        /// The cell against a given side of a splitter or a crossroad.
        ///
        /// <b>This used to be a four-case copy of CrossFootprint's "+" offsets</b>, and it is what
        /// DEVELOPMENT_RULES §7 warns about: the copy stopped following its model the moment the
        /// piece became a single cell, and these tests then built layouts whose neighbours stood two
        /// and three cells clear of the splitter - failing on geometry that had nothing to do with
        /// what they assert. Adjacency is a fact about the grid rather than about either piece's
        /// shape, so there is nothing left here to fall out of step.
        /// </summary>
        static GridCoord CrossFootprint_NeighborCellForTest(GridCoord origin, Direction direction)
            => origin + direction.ToOffset();
    }
}
