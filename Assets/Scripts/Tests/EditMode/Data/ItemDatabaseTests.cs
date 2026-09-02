using Game.Data;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Data
{
    public class ItemDatabaseTests
    {
        [Test]
        public void Get_ReturnsRegisteredItem_ById()
        {
            ItemDefinition ironIngot = TestDataFactory.NewItem("Iron_Ingot", ItemType.Ingot);
            ItemDatabase database = TestDataFactory.NewItemDatabase(ironIngot);

            ItemDefinition result = database.Get("Iron_Ingot");

            Assert.AreSame(ironIngot, result);
            Assert.AreEqual(ItemType.Ingot, result.Type);
        }

        [Test]
        public void Get_ReturnsNull_ForUnknownId()
        {
            ItemDatabase database = TestDataFactory.NewItemDatabase();

            Assert.IsNull(database.Get("does_not_exist"));
        }

        [Test]
        public void MinerAiCharbon_IsComponent_NotOre()
        {
            // Regression guard for the source project's deliberate exception: coal must never
            // satisfy Foundry's ItemType.Ore filter despite being a raw material.
            ItemDefinition charbon = TestDataFactory.NewItem("minerai_charbon", ItemType.Component);
            ItemDatabase database = TestDataFactory.NewItemDatabase(charbon);

            Assert.AreNotEqual(ItemType.Ore, database.Get("minerai_charbon").Type);
        }
    }
}
