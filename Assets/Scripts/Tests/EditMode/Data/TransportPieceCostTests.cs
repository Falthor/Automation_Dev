using System.Collections.Generic;
using Game.Data;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests.EditMode.Data
{
    /// <summary>
    /// Conveyors and crossroads are free to place. The splitter is not, by decision.
    ///
    /// <b>The rule and its one exception.</b> The reason conveyors are free is the one already
    /// written on the building cap: the belts between machines are not the thing being budgeted, and
    /// charging for them would make connecting a factory the expensive part of building one. The
    /// splitter is charged anyway - four iron plates and a gear - because it is not a length of belt
    /// but a routing decision the player makes deliberately and rarely. That is a design call, not a
    /// derivation, so it is pinned here as a number rather than argued from the rule.
    ///
    /// The costs used to be an unwatched inconsistency - a splitter and a crossroad each cost one
    /// iron plate while conveyors were free, which the player met at the moment of placing one and
    /// nowhere else. The assets are read here by <b>type</b> rather than by path, so a new conveyor
    /// or crossroad variant is covered the day it is added rather than quietly reintroducing a
    /// charge, and a splitter variant is held to the splitter's own figure.
    ///
    /// Storage boxes are exempt from the cap too and are deliberately <b>not</b> in this rule: a box
    /// is a thing you build, not a wire between two things.
    /// </summary>
    public class TransportPieceCostTests
    {
        static IEnumerable<T> ShippedAssetsOfType<T>() where T : BuildingDefinition
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets/Data" }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null) yield return definition;
            }
        }

        static void AssertFree(BuildingDefinition definition)
        {
            Assert.AreEqual(0, definition.Cost.Length,
                $"{definition.name} charges for a transport piece. Conveyors are free, so a splitter or a "
                + "crossroad that is not reads as an inconsistency at the moment of placing one - which is "
                + "the only moment the player ever sees it.");
        }

        [Test]
        public void EveryShippedConveyorIsFree()
        {
            var found = 0;
            foreach (ConveyorDefinition definition in ShippedAssetsOfType<ConveyorDefinition>())
            {
                AssertFree(definition);
                found++;
            }

            Assert.Greater(found, 0, "no conveyor definition was found - this test would pass on an empty project");
        }

        /// <summary>
        /// The splitter's exception, held to its exact figure. Written as the numbers rather than
        /// "costs something" so that losing one of the two ingredients - the likelier accident than
        /// the cost vanishing altogether - is caught too.
        /// </summary>
        [Test]
        public void EveryShippedSplitterCostsFourPlatesAndAGear()
        {
            var found = 0;
            foreach (SplitterDefinition definition in ShippedAssetsOfType<SplitterDefinition>())
            {
                Assert.AreEqual(2, definition.Cost.Length, definition.name + ": two ingredients");

                var byId = new Dictionary<string, int>();
                foreach (RecipeIngredient ingredient in definition.Cost)
                {
                    Assert.IsNotNull(ingredient.Item, definition.name + ": an unassigned cost item");
                    byId[ingredient.Item.Id] = ingredient.Amount;
                }

                Assert.AreEqual(4, byId["Iron_Plate"], definition.name + ": iron plates");
                Assert.AreEqual(1, byId["Gear"], definition.name + ": gears");
                found++;
            }

            Assert.Greater(found, 0, "no splitter definition was found");
        }

        [Test]
        public void EveryShippedCrossroadIsFree()
        {
            var found = 0;
            foreach (CrossroadDefinition definition in ShippedAssetsOfType<CrossroadDefinition>())
            {
                AssertFree(definition);
                found++;
            }

            Assert.Greater(found, 0, "no crossroad definition was found");
        }

        /// <summary>
        /// The boundary of the rule, stated so nobody widens it by accident. A Storage box shares the
        /// cap exemption but not the reason: it is a thing you build, not a wire between two things,
        /// and it is meant to cost something.
        /// </summary>
        [Test]
        public void AStorageBoxStillCostsSomething()
        {
            var storage = AssetDatabase.LoadAssetAtPath<StorageDefinition>("Assets/Data/Buildings/StorageDefinition.asset");
            Assert.IsNotNull(storage);

            Assert.Greater(storage.Cost.Length, 0,
                "being exempt from the building cap is not the same as being free - a box is built, not routed.");
        }
    }
}
