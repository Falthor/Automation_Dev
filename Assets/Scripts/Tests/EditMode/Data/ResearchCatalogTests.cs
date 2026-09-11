using Game.Data;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Data
{
    /// <summary>The Core's furthest reach, derived from the research effects rather than written down (CONTRACTS.md §8/§11).</summary>
    public class ResearchCatalogTests
    {
        static ResearchDefinition Radius(int cells)
            => TestDataFactory.WithEffects(TestDataFactory.NewResearch("radius_" + cells, 10f), new ResearchEffect(ResearchEffectKind.ActionRadius, value: cells));

        [Test]
        public void TheFurthestReach_IsTheHighestRadiusAnyResearchCarries()
        {
            var catalog = new ResearchCatalog(new[] { Radius(42), Radius(80), Radius(60) });

            Assert.AreEqual(80, catalog.HighestActionRadius(22));
        }

        /// <summary>No research going further than the start leaves the start - never zero, which would exclude nothing and size a texture on nothing.</summary>
        [Test]
        public void WithNoRadiusResearch_TheFurthestReach_IsTheStartingRadius()
        {
            ResearchDefinition other = TestDataFactory.WithEffects(TestDataFactory.NewResearch("cap", 10f), new ResearchEffect(ResearchEffectKind.BuildingCap, value: 500));
            var catalog = new ResearchCatalog(new[] { other });

            Assert.AreEqual(22, catalog.HighestActionRadius(22), "A cap figure is not a radius, however large.");
        }
    }
}
