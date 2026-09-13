using Game.Save;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests.EditMode.Save
{
    /// <summary>
    /// Pins SaveService.TryMigrateToCurrentVersion (SAUVEGARDE.md §2) on hand-built JObjects, never
    /// on a real save file - SaveService writes to the real save path, which a test must never touch
    /// (same constraint as SaveFormatTests).
    /// </summary>
    public class SaveMigrationTests
    {
        [Test]
        public void Version3_MigratesComputeReserveIntoBuildingCompute_AndStartsResearchComputeAtZero()
        {
            var raw = new JObject
            {
                ["Version"] = 3,
                ["ComputeReserve"] = 12345f,
            };

            bool migrated = SaveService.TryMigrateToCurrentVersion(raw);

            Assert.IsTrue(migrated);
            Assert.AreEqual(SaveData.CurrentVersion, raw.Value<int>("Version"));
            Assert.AreEqual(12345f, raw.Value<float>("BuildingComputeReserve"),
                "Building Compute inherits every role the single reserve had - including its value.");
            Assert.AreEqual(0f, raw.Value<float>("ResearchComputeReserve"),
                "A save from before Research Compute existed means nothing had produced any yet.");
            Assert.IsFalse(raw.ContainsKey("ComputeReserve"), "The old key must not survive alongside the new ones.");
        }

        /// <summary>The exact concern that prompted this migration to exist at all: nothing earned before the format change may be lost.</summary>
        [Test]
        public void Version3_MigrationTouchesOnlyComputeReserve_EveryOtherKeySurvivesUntouched()
        {
            var raw = new JObject
            {
                ["Version"] = 3,
                ["ComputeReserve"] = 500f,
                ["ResearchUnlocked"] = new JArray { "extractor_power", "datacenter_bay_1" },
                ["ResearchQueue"] = new JArray { "advanced_foundry" },
                ["Buildings"] = new JArray { new JObject { ["DefinitionId"] = "datacenter" } },
                ["BuildingCap"] = 75,
            };

            SaveService.TryMigrateToCurrentVersion(raw);

            CollectionAssert.AreEqual(new[] { "extractor_power", "datacenter_bay_1" }, raw["ResearchUnlocked"].ToObject<string[]>(),
                "A research unlocked before the migration must still read as unlocked after it.");
            CollectionAssert.AreEqual(new[] { "advanced_foundry" }, raw["ResearchQueue"].ToObject<string[]>());
            Assert.AreEqual("datacenter", raw["Buildings"][0].Value<string>("DefinitionId"));
            Assert.AreEqual(75, raw.Value<int>("BuildingCap"));
        }

        [Test]
        public void ASaveAlreadyAtCurrentVersion_IsLeftAlone()
        {
            var raw = new JObject
            {
                ["Version"] = SaveData.CurrentVersion,
                ["BuildingComputeReserve"] = 999f,
                ["ResearchComputeReserve"] = 111f,
            };

            bool migrated = SaveService.TryMigrateToCurrentVersion(raw);

            Assert.IsTrue(migrated);
            Assert.AreEqual(999f, raw.Value<float>("BuildingComputeReserve"));
            Assert.AreEqual(111f, raw.Value<float>("ResearchComputeReserve"));
        }

        /// <summary>Version 2 predates this task's only known migration (3 -> 4) and has none registered - refused, not guessed at.</summary>
        [Test]
        public void AVersionWithNoRegisteredMigration_FailsWithoutMutatingAnything()
        {
            var raw = new JObject { ["Version"] = 2, ["GlobalStock"] = new JObject { ["iron"] = 40 } };

            bool migrated = SaveService.TryMigrateToCurrentVersion(raw);

            Assert.IsFalse(migrated);
            Assert.AreEqual(2, raw.Value<int>("Version"), "A failed migration must not leave the blob half-bumped.");
        }

        /// <summary>A save from a newer build than this one is not a lesser format to fill in defaults for - it is simply not loadable here.</summary>
        [Test]
        public void AVersionNewerThanCurrent_Fails()
        {
            var raw = new JObject { ["Version"] = SaveData.CurrentVersion + 1 };

            bool migrated = SaveService.TryMigrateToCurrentVersion(raw);

            Assert.IsFalse(migrated);
        }

        /// <summary>A blob with no Version key at all reads as Version 0 - as unbridgeable as any other unregistered Version, never as CurrentVersion by default.</summary>
        [Test]
        public void AMissingVersionKey_IsTreatedAsUnbridgeable_NeverAsCurrent()
        {
            var raw = new JObject { ["CoreDefinitionId"] = "core" };

            bool migrated = SaveService.TryMigrateToCurrentVersion(raw);

            Assert.IsFalse(migrated);
        }
    }
}
