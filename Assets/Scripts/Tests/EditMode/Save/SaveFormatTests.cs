using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests.EditMode.Save
{
    /// <summary>
    /// Pins the on-disk save format (SAUVEGARDE.md): the exact set of keys, their values, and the
    /// fact that a null still occupies its key rather than vanishing.
    ///
    /// Written for a cleanup that had to touch SaveData without moving the format by one iota, and
    /// kept because that is a permanent hazard: nothing else in the project would notice a key
    /// silently disappearing until a player's save failed to load. SaveService is deliberately not
    /// involved - it writes to the real save path, which a test must never touch - so this asserts
    /// on the one call it makes.
    ///
    /// <b>The order of the keys is deliberately not asserted.</b> JSON has no ordering, Json.NET
    /// reads a document whatever order it arrives in, and the set plus the values already catches
    /// every field added, removed or renamed. Pinning the order would add no protection and would
    /// make the test fail on a reordering done purely for readability - a failure with no real
    /// defect behind it, which is how a test gets weakened wholesale instead of understood.
    /// </summary>
    public class SaveFormatTests
    {
        static SaveData NewPopulatedSave() => new SaveData
        {
            Version = SaveData.CurrentVersion,
            SavedAtUtc = "2026-09-06T00:00:00.0000000Z",
            TerrainSeed = 1234,
            TerrainSize = 256,
            TerrainScale = 0.25f,
            TerrainProportion = 0.5f,
            Discovered = "0:120,1:16,0:120",
            DecorRemoved = "4096,4097,131072",
            WrecksDiscovered = "0,3,7",
            PowerPriority = new List<string> { "datacenter", "factory", "extractor" },
            ExplorerRobots = new JObject { ["robots"] = new JArray { new JObject { ["x"] = 40f, ["state"] = 1 } } },
            BuildingComputeReserve = 12.5f,
            ResearchComputeReserve = 8.25f,
            ResearchActiveId = "automation",
            ResearchProgress = 0.75f,
            ResearchQueue = new List<string> { "a", "b" },
            ResearchUnlocked = new List<string> { "c" },
            ConstructionSites = new JObject { ["nextId"] = 7 },
            CoreDirectives = new JObject { ["index"] = 1 },
            CoreDefinitionId = "core",
            CoreCellX = 3,
            CoreCellY = -4,
            CoreState = new JObject { ["cuTimer"] = 1.5f },
            BuildingCap = 42,
            PlayTimeSeconds = 372.5f,
            Deposits = new List<DepositSaveData>
            {
                new DepositSaveData { DefinitionId = "iron", OriginX = 1, OriginY = 2 }
            },
            Buildings = new List<BuildingSaveData>
            {
                new BuildingSaveData
                {
                    DefinitionId = "foundry", CellX = 5, CellY = 6, FacingRotation = 2, InputSide = 3,
                    State = new JObject { ["recipe"] = "Iron_Ingot" }
                }
            }
        };

        /// <summary>The one call SaveService.Save makes, so this test and the real writer cannot drift apart.</summary>
        static string Serialize(SaveData data) => JsonConvert.SerializeObject(data, Formatting.Indented);

        static readonly string[] ExpectedRootKeys =
        {
            "Version", "SavedAtUtc",
            "TerrainSeed", "TerrainSize", "TerrainScale", "TerrainProportion", "Discovered", "DecorRemoved",
            // Which wrecks have been found. Additive, with its own fallback - an absent key is a
            // world nobody has found anything in - so CurrentVersion is deliberately not bumped:
            // bumping it would refuse every existing save to add a field that reads fine as null.
            // Same call as DecorRemoved above.
            "WrecksDiscovered",
            "ExplorerRobots",
            "BuildingComputeReserve", "ResearchComputeReserve",
            "ResearchActiveId", "ResearchProgress", "ResearchQueue", "ResearchUnlocked",
            "ConstructionSites", "CoreDirectives",
            "CoreDefinitionId", "CoreCellX", "CoreCellY", "CoreState",
            "BuildingCap", "PlayTimeSeconds",

            // The power priority order (ENERGIE.md). Absent restores as the building
            // catalogue's own order, which is the default arbitration anyway - so CurrentVersion is
            // deliberately not bumped, for the same reason as WrecksDiscovered above.
            "PowerPriority",

            "Deposits", "Buildings"
        };

        [Test]
        public void TheSaveFile_HasExactlyTheseKeys()
        {
            var root = JObject.Parse(Serialize(NewPopulatedSave()));

            var actual = new List<string>();
            foreach (JProperty property in root.Properties()) actual.Add(property.Name);

            CollectionAssert.AreEquivalent(ExpectedRootKeys, actual,
                "The save file's shape changed: a key was added, removed or renamed. That is a format "
                + "change, and SaveService refuses any save whose Version does not match exactly - so "
                + "either bump SaveData.CurrentVersion deliberately, or put the key back.");
        }

        [Test]
        public void TheNestedRecords_HaveExactlyTheirOwnKeys()
        {
            var root = JObject.Parse(Serialize(NewPopulatedSave()));

            var deposit = (JObject)root["Deposits"][0];
            var depositKeys = new List<string>();
            foreach (JProperty property in deposit.Properties()) depositKeys.Add(property.Name);
            CollectionAssert.AreEquivalent(
                new[] { "DefinitionId", "OriginX", "OriginY" }, depositKeys);

            var building = (JObject)root["Buildings"][0];
            var buildingKeys = new List<string>();
            foreach (JProperty property in building.Properties()) buildingKeys.Add(property.Name);
            CollectionAssert.AreEquivalent(
                new[] { "DefinitionId", "CellX", "CellY", "FacingRotation", "InputSide", "State" }, buildingKeys);

            Assert.AreEqual("Iron_Ingot", building["State"]["recipe"].Value<string>(),
                "A per-building blob is stored verbatim and never interpreted by the save layer.");
        }

        /// <summary>
        /// BuildingCap is nullable precisely so an absent value is distinguishable from 0. If the
        /// writer ever started omitting nulls, an old save and a save with the cap explicitly unset
        /// would become indistinguishable, and the fallback to DefaultBuildingCap would silently
        /// become a fallback to nothing.
        /// </summary>
        [Test]
        public void ANullField_KeepsItsKey_RatherThanDisappearing()
        {
            SaveData data = NewPopulatedSave();
            data.BuildingCap = null;
            data.ConstructionSites = null;

            var root = JObject.Parse(Serialize(data));

            Assert.IsTrue(root.ContainsKey("BuildingCap"), "The key must still be written.");
            Assert.AreEqual(JTokenType.Null, root["BuildingCap"].Type);
            Assert.IsTrue(root.ContainsKey("ConstructionSites"));
            Assert.AreEqual(JTokenType.Null, root["ConstructionSites"].Type);

            var actual = new List<string>();
            foreach (JProperty property in root.Properties()) actual.Add(property.Name);
            CollectionAssert.AreEquivalent(ExpectedRootKeys, actual, "Nulls do not change the shape either.");
        }

        /// <summary>
        /// Every field of <see cref="SaveData"/>, compared across a round trip - <b>enumerated by
        /// reflection, so a field added later is covered without anybody remembering to add a line
        /// here</b>.
        ///
        /// The hand-written version of this test carried the warning about fields that sit in the
        /// fixture without ever being compared, and still failed to heed it: three of them
        /// (WrecksDiscovered, PowerPriority, ExplorerRobots) were in the type and in nobody's
        /// assertion. A test that stays green when you break what it tests is not a test
        /// (DEVELOPMENT_RULES §7), and the only durable fix is to stop the list being written by
        /// hand - the same move as making a limit structurally unreachable rather than watching for
        /// it.
        /// </summary>
        [Test]
        public void EveryField_SurvivesARoundTrip()
        {
            SaveData original = NewPopulatedSave();
            SaveData restored = JsonConvert.DeserializeObject<SaveData>(Serialize(original));

            foreach (FieldInfo field in SavedFields(typeof(SaveData)))
            {
                Assert.AreEqual(Describe(field.GetValue(original)), Describe(field.GetValue(restored)),
                    $"SaveData.{field.Name} does not survive the round trip: it is written, read back, and comes out different.");
            }
        }

        /// <summary>
        /// The other half, and the one the hand-written test was missing: a field the fixture never
        /// sets round-trips null to null and proves nothing at all. Every field must differ from a
        /// default-constructed SaveData, so adding one to the type fails here until the fixture
        /// gives it a value worth comparing.
        /// </summary>
        [Test]
        public void TheFixtureSetsEveryField_SoTheRoundTripProvesSomething()
        {
            SaveData populated = NewPopulatedSave();
            var untouched = new SaveData();

            foreach (FieldInfo field in SavedFields(typeof(SaveData)))
            {
                // Version is the one field whose populated value is its default, and must stay so:
                // a save that is not at CurrentVersion is one SaveService refuses to load.
                if (field.Name == nameof(SaveData.Version)) continue;

                Assert.AreNotEqual(Describe(field.GetValue(untouched)), Describe(field.GetValue(populated)),
                    $"NewPopulatedSave leaves SaveData.{field.Name} at its default, so the round trip above "
                    + "compares a default with a default and would not notice the field being dropped.");
            }
        }

        static FieldInfo[] SavedFields(Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>
        /// A value written out far enough to be compared as text, nested records included: those are
        /// plain field bags with no Equals, so comparing them directly would compare references -
        /// passing on two different objects, or failing on two identical ones.
        /// </summary>
        static string Describe(object value)
        {
            if (value == null) return "<null>";
            if (value is string text) return text;
            if (value is JToken token) return token.ToString(Formatting.None);

            if (value is IEnumerable items)
            {
                var elements = new List<string>();
                foreach (object element in items) elements.Add(Describe(element));
                return "[" + string.Join(", ", elements) + "]";
            }

            FieldInfo[] fields = SavedFields(value.GetType());
            if (fields.Length == 0) return value.ToString();

            var written = new List<string>();
            foreach (FieldInfo field in fields) written.Add(field.Name + "=" + Describe(field.GetValue(value)));
            return value.GetType().Name + "{" + string.Join(", ", written) + "}";
        }

        /// <summary>An absent optional key restores to null, not to 0 - the distinction the nullable exists for.</summary>
        [Test]
        public void AnOlderSaveMissingOptionalKeys_RestoresThemAsAbsent()
        {
            const string json = @"{ ""Version"": 3, ""CoreDefinitionId"": ""core"" }";

            SaveData restored = JsonConvert.DeserializeObject<SaveData>(json);

            Assert.AreEqual(3, restored.Version);
            Assert.IsNull(restored.BuildingCap, "Absent means absent, never 0.");
            Assert.IsNull(restored.PlayTimeSeconds, "A save from before the run clock is not a run that lasted zero seconds.");
            Assert.IsNull(restored.ConstructionSites, "A save from before the robots restores without one.");
            Assert.IsNull(restored.DecorRemoved,
                "A save from before the decor recorded no clearing, which DecorRuntime.RestoreState reads as a world nobody has cleared anything in.");
        }
    }
}
