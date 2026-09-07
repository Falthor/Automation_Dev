using System.Collections.Generic;
using Game.Data;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests.EditMode.Data
{
    /// <summary>
    /// Transport pieces are free to place.
    ///
    /// <b>The reason is the one already written on the building cap</b>, and it is the same reason:
    /// the belts between machines are not the thing being budgeted. Counting them against the cap
    /// would make connecting a factory the expensive part of building one, and charging for them does
    /// exactly the same, in materials instead of slots.
    ///
    /// This existed as an inconsistency for a long time - conveyors were free while a splitter and a
    /// crossroad each cost an iron plate, which the player meets at the moment of placing one and
    /// nowhere else. Nothing was watching, which is why it survived; the assets are read here by
    /// <b>type</b> rather than by path, so a new splitter or conveyor variant is covered the day it is
    /// added rather than quietly reintroducing the charge.
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

        [Test]
        public void EveryShippedSplitterIsFree()
        {
            var found = 0;
            foreach (SplitterDefinition definition in ShippedAssetsOfType<SplitterDefinition>())
            {
                AssertFree(definition);
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
