using Game.Gameplay.Items;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Items
{
    public class PooledItemStockTests
    {
        [Test]
        public void Add_ThenGetAmount_ReturnsAddedAmount()
        {
            var stock = new PooledItemStock(20);

            stock.Add("Iron_Ingot", 5);

            Assert.AreEqual(5, stock.GetAmount("Iron_Ingot"));
        }

        [Test]
        public void Add_UnlimitedDistinctIds_EachCappedIndependently()
        {
            var stock = new PooledItemStock(10);

            stock.Add("a", 10);
            stock.Add("b", 10);
            stock.Add("c", 10);

            Assert.AreEqual(10, stock.GetAmount("a"));
            Assert.AreEqual(10, stock.GetAmount("b"));
            Assert.AreEqual(10, stock.GetAmount("c"));
        }

        [Test]
        public void CanAccept_RejectsWhenOverCap()
        {
            var stock = new PooledItemStock(10);
            stock.Add("Iron_Ingot", 8);

            Assert.IsTrue(stock.CanAccept("Iron_Ingot", 2));
            Assert.IsFalse(stock.CanAccept("Iron_Ingot", 3));
        }

        [Test]
        public void Take_RemovesUpToAvailable_ReturnsActualAmountTaken()
        {
            var stock = new PooledItemStock(20);
            stock.Add("Iron_Ingot", 3);

            int taken = stock.Take("Iron_Ingot", 10);

            Assert.AreEqual(3, taken);
            Assert.AreEqual(0, stock.GetAmount("Iron_Ingot"));
        }

        [Test]
        public void Contents_ReflectsCurrentAmounts()
        {
            var stock = new PooledItemStock(20);
            stock.Add("Iron_Ingot", 4);
            stock.Add("lingot_cuivre", 2);

            Assert.AreEqual(4, stock.Contents["Iron_Ingot"]);
            Assert.AreEqual(2, stock.Contents["lingot_cuivre"]);
        }
    }
}
