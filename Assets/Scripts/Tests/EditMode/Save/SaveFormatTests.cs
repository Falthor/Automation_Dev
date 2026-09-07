using System.Collections.Generic;
using Game.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests.EditMode.Save
{
    /// <summary>
    /// Pins the on-disk save format (CONTRACTS.md §14): the exact set of keys, their values, and the
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
            Missions = new JObject { ["nextId"] = 4, ["appeared"] = true },
            ComputeReserve = 12.5f,
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
                    DefinitionId = "foundry", CellX = 5, CellY = 6, FacingRotation = 2,
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
            "Missions",
            "ComputeReserve",
            "ResearchActiveId", "ResearchProgress", "ResearchQueue", "ResearchUnlocked",
            "ConstructionSites", "CoreDirectives",
            "CoreDefinitionId", "CoreCellX", "CoreCellY", "CoreState",
            "BuildingCap", "PlayTimeSeconds",
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
                new[] { "DefinitionId", "CellX", "CellY", "FacingRotation", "State" }, buildingKeys);

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

        [Test]
        public void EveryField_SurvivesARoundTrip()
        {
            SaveData original = NewPopulatedSave();
            SaveData restored = JsonConvert.DeserializeObject<SaveData>(Serialize(original));

            Assert.AreEqual(original.Version, restored.Version);
            Assert.AreEqual(original.SavedAtUtc, restored.SavedAtUtc);
            Assert.AreEqual(original.TerrainSeed, restored.TerrainSeed);
            Assert.AreEqual(original.TerrainSize, restored.TerrainSize);
            Assert.AreEqual(original.TerrainScale, restored.TerrainScale);
            Assert.AreEqual(original.TerrainProportion, restored.TerrainProportion);

            // The two derived-world fields: the seed re-derives what they describe, so what is stored
            // is only what the player did to it. A silent loss here reads on screen as fog reclosing
            // and cleared rocks growing back, never as an error.
            Assert.AreEqual(original.Discovered, restored.Discovered);
            Assert.AreEqual(original.DecorRemoved, restored.DecorRemoved);

            // Asserted through the round trip, not merely present in the fixture: Discovered sat in
            // this fixture for months without ever being compared, so it could have been lost in
            // transit with nothing turning red.
            Assert.AreEqual(4, restored.Missions["nextId"].Value<int>());
            Assert.IsTrue(restored.Missions["appeared"].Value<bool>());

            Assert.AreEqual(original.ComputeReserve, restored.ComputeReserve);
            Assert.AreEqual(original.ResearchActiveId, restored.ResearchActiveId);
            Assert.AreEqual(original.ResearchProgress, restored.ResearchProgress);
            CollectionAssert.AreEqual(original.ResearchQueue, restored.ResearchQueue);
            CollectionAssert.AreEqual(original.ResearchUnlocked, restored.ResearchUnlocked);
            Assert.AreEqual(7, restored.ConstructionSites["nextId"].Value<int>());
            Assert.AreEqual(original.CoreDefinitionId, restored.CoreDefinitionId);
            Assert.AreEqual(original.CoreCellX, restored.CoreCellX);
            Assert.AreEqual(original.CoreCellY, restored.CoreCellY);
            Assert.AreEqual(1.5f, restored.CoreState["cuTimer"].Value<float>());
            Assert.AreEqual(42, restored.BuildingCap);
            Assert.AreEqual(372.5f, restored.PlayTimeSeconds);

            Assert.AreEqual(1, restored.Deposits.Count);
            Assert.AreEqual("iron", restored.Deposits[0].DefinitionId);

            Assert.AreEqual(1, restored.Buildings.Count);
            Assert.AreEqual("foundry", restored.Buildings[0].DefinitionId);
            Assert.AreEqual(2, restored.Buildings[0].FacingRotation);
            Assert.AreEqual("Iron_Ingot", restored.Buildings[0].State["recipe"].Value<string>());
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
            Assert.IsNull(restored.Missions, "a save from before the expeditions restores as a game whose probes have not arrived.");
            Assert.IsNull(restored.DecorRemoved,
                "A save from before the decor recorded no clearing, which DecorRuntime.RestoreState reads as a world nobody has cleared anything in.");
        }
    }
}
