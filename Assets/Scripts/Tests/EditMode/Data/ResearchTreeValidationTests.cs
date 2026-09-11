using System.Collections.Generic;
using Game.Data;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests.EditMode.Data
{
    /// <summary>
    /// The two failures a research tree cannot recover from (ResearchTreeValidation): a cycle of
    /// prerequisites, and a research no chain leads back to the cores. Each is shown detected on a
    /// small tree built for it, and the shipped tree is held to both.
    /// </summary>
    public class ResearchTreeValidationTests
    {
        static ResearchDefinition Research(string id) => TestDataFactory.NewResearch(id, 10f);

        /// <summary>Sets a research's prerequisites after the fact - how a cycle gets made, since NewResearch can only point backwards.</summary>
        static void Require(ResearchDefinition research, params ResearchDefinition[] prerequisites)
        {
            var so = new SerializedObject(research);
            SerializedProperty array = so.FindProperty("prerequisites");
            array.arraySize = prerequisites.Length;
            for (int i = 0; i < prerequisites.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = prerequisites[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- Cycles ----

        /// <summary>A needs B needs C needs A: none of the three can ever start. All three are named, and nothing outside the loop is.</summary>
        [Test]
        public void ACycleOfPrerequisites_IsDetected()
        {
            ResearchDefinition core = Research("core");
            ResearchDefinition a = Research("a"), b = Research("b"), c = Research("c"), outside = Research("outside");
            Require(a, core, c);
            Require(b, a);
            Require(c, b);
            Require(outside, core);

            List<ResearchDefinition> cycle = ResearchTreeValidation.FindCycles(new[] { core, a, b, c, outside });

            CollectionAssert.AreEquivalent(new[] { a, b, c }, cycle);
        }

        [Test]
        public void AResearchRequiringItself_IsACycle()
        {
            ResearchDefinition core = Research("core");
            ResearchDefinition selfish = Research("selfish");
            Require(selfish, core, selfish);

            CollectionAssert.AreEquivalent(new[] { selfish }, ResearchTreeValidation.FindCycles(new[] { core, selfish }));
        }

        /// <summary>A diamond - two branches meeting again - shares a prerequisite without looping. It must not be read as a cycle.</summary>
        [Test]
        public void ADiamond_IsNotACycle()
        {
            ResearchDefinition core = Research("core");
            ResearchDefinition left = Research("left"), right = Research("right"), joined = Research("joined");
            Require(left, core);
            Require(right, core);
            Require(joined, left, right);

            Assert.IsEmpty(ResearchTreeValidation.FindCycles(new[] { core, left, right, joined }));
        }

        // ---- Reachability ----

        /// <summary>
        /// Four ways to be cut off from the cores: no prerequisite at all, a prerequisite that is itself
        /// cut off, one prerequisite on the tree beside one that is not, and a cycle. The chain that does
        /// lead back to a core is not reported.
        /// </summary>
        [Test]
        public void AResearchNoChainLeadsBackToTheCores_IsReported()
        {
            ResearchDefinition core = Research("core");
            ResearchDefinition onTree = Research("on_tree"), further = Research("further");
            ResearchDefinition orphan = Research("orphan"), behindOrphan = Research("behind_orphan");
            ResearchDefinition halfConnected = Research("half_connected");
            ResearchDefinition loopA = Research("loop_a"), loopB = Research("loop_b");
            Require(onTree, core);
            Require(further, onTree);
            Require(behindOrphan, orphan);
            Require(halfConnected, onTree, orphan);
            Require(loopA, core, loopB);
            Require(loopB, loopA);

            List<ResearchDefinition> unreachable = ResearchTreeValidation.FindUnreachable(
                new[] { core }, new[] { onTree, further, orphan, behindOrphan, halfConnected, loopA, loopB });

            CollectionAssert.AreEquivalent(new[] { orphan, behindOrphan, halfConnected, loopA, loopB }, unreachable);
        }

        /// <summary>A research can hang from any of the cores - reachability does not favour the first.</summary>
        [Test]
        public void EveryCore_IsAStartingPoint()
        {
            ResearchDefinition first = Research("core_a"), second = Research("core_b"), third = Research("core_c");
            ResearchDefinition underThird = Research("under_third");
            Require(underThird, third);

            Assert.IsEmpty(ResearchTreeValidation.FindUnreachable(new[] { first, second, third }, new[] { underThird }));
        }

        // ---- The shipped tree ----

        /// <summary>The tree the game ships has neither failure. The assets are read, not restated: this fails the day an edit breaks them.</summary>
        [Test]
        public void TheShippedTree_HasNoCycle_AndNothingUnreachable()
        {
            var database = AssetDatabase.LoadAssetAtPath<ResearchDatabase>("Assets/Data/Research/ResearchDatabase.asset");
            Assert.IsNotNull(database, "the shipped research database");

            var everything = new List<ResearchDefinition>(database.GetAll());
            everything.AddRange(database.GetCores());

            Assert.IsEmpty(ResearchTreeValidation.FindCycles(everything), "a prerequisite cycle in the shipped tree");
            Assert.IsEmpty(ResearchTreeValidation.FindUnreachable(database.GetCores(), database.GetAll()), "a shipped research the cores never lead to");
        }
    }
}
