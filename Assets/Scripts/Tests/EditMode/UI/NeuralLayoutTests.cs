using System.Collections.Generic;
using Game.Data;
using Game.Tests.EditMode.TestSupport;
using Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.UI
{
    /// <summary>
    /// The placement rules of the research network (GDD §5.4), checked on NeuralLayout alone - no
    /// panel, no UIDocument. The one that matters most is the sector containment: it is what makes it
    /// impossible for two synapses to cross.
    /// </summary>
    public class NeuralLayoutTests
    {
        const float Step = 90f;
        const float Tolerance = 1e-4f;

        static ResearchDefinition Core(string id) => TestDataFactory.NewResearch(id, 0f);

        static ResearchDefinition Research(string id, int tier, params ResearchDefinition[] prerequisites)
        {
            ResearchDefinition research = TestDataFactory.NewResearch(id, 100f, 1_000_000f, prerequisites);
            var so = new SerializedObject(research);
            so.FindProperty("tier").intValue = tier;
            so.ApplyModifiedPropertiesWithoutUndo();
            return research;
        }

        static NeuralLayout.Placement Find(List<NeuralLayout.Placement> placements, ResearchDefinition definition)
        {
            foreach (NeuralLayout.Placement placement in placements)
            {
                if (ReferenceEquals(placement.Definition, definition)) return placement;
            }
            Assert.Fail(definition.Id + " was not placed.");
            return default;
        }

        [Test]
        public void TheCores_ShareTheCircle_InEqualSectorsThatDoNotOverlap()
        {
            ResearchDefinition[] cores = { Core("research"), Core("buildings"), Core("armament") };

            List<NeuralLayout.Placement> placements = NeuralLayout.Compute(cores, new ResearchDefinition[0], Step, 0);

            Assert.AreEqual(3, placements.Count);
            for (int i = 0; i < placements.Count; i++)
            {
                Assert.IsTrue(placements[i].IsCore);
                Assert.AreEqual(2f * Mathf.PI / 3f, placements[i].SectorEnd - placements[i].SectorStart, Tolerance);
                if (i > 0) Assert.AreEqual(placements[i - 1].SectorEnd, placements[i].SectorStart, Tolerance, "Each sector starts where the previous one ends.");
            }
        }

        /// <summary>A child never leaves its parent's interval, and siblings split it without overlapping - the rule that guarantees no crossing synapse.</summary>
        [Test]
        public void EveryResearch_StaysInsideItsParentsSector_AndSiblingsDoNotOverlap()
        {
            ResearchDefinition research = Core("research"), buildings = Core("buildings"), armament = Core("armament");
            ResearchDefinition bay1 = Research("bay1", 2, research);
            ResearchDefinition bay2 = Research("bay2", 3, bay1);
            ResearchDefinition foundry = Research("foundry", 2, buildings);
            ResearchDefinition steel = Research("steel", 2, buildings);

            List<NeuralLayout.Placement> placements = NeuralLayout.Compute(
                new[] { research, buildings, armament }, new[] { bay1, bay2, foundry, steel }, Step, 0);

            foreach (NeuralLayout.Placement placement in placements)
            {
                if (placement.IsCore) continue;
                NeuralLayout.Placement parent = placements[placement.ParentIndex];
                Assert.GreaterOrEqual(placement.SectorStart, parent.SectorStart - Tolerance);
                Assert.LessOrEqual(placement.SectorEnd, parent.SectorEnd + Tolerance);
            }

            NeuralLayout.Placement first = Find(placements, foundry), second = Find(placements, steel);
            Assert.LessOrEqual(first.SectorEnd, second.SectorStart + Tolerance, "Two branches under one core get two intervals, one after the other.");
        }

        [Test]
        public void TheDistanceFromTheCentre_IsTheTierTimesTheStep()
        {
            ResearchDefinition research = Core("research");
            ResearchDefinition bay1 = Research("bay1", 2, research);
            ResearchDefinition bay2 = Research("bay2", 3, bay1);

            List<NeuralLayout.Placement> placements = NeuralLayout.Compute(new[] { research }, new[] { bay1, bay2 }, Step, 0);

            Assert.AreEqual(Step, Find(placements, research).Position.magnitude, 1e-3f, "A core is on the first ring.");
            Assert.AreEqual(2f * Step, Find(placements, bay1).Position.magnitude, 1e-3f);
            Assert.AreEqual(3f * Step, Find(placements, bay2).Position.magnitude, 1e-3f);
        }

        /// <summary>The radius is the progression, so a child is always further out than what it hangs from - even when its asset says otherwise.</summary>
        [Test]
        public void AChild_IsNeverOnItsParentsRing_WhateverItsTierSays()
        {
            ResearchDefinition research = Core("research");
            ResearchDefinition bay1 = Research("bay1", 2, research);
            ResearchDefinition misfiled = Research("misfiled", 1, bay1);

            List<NeuralLayout.Placement> placements = NeuralLayout.Compute(new[] { research }, new[] { bay1, misfiled }, Step, 0);

            Assert.AreEqual(3, Find(placements, misfiled).Tier);
        }

        /// <summary>
        /// A research nothing on the network leads to is an orphan and stays off it. An empty core -
        /// the armament today - still holds its sector, with unknown nodes rather than nothing.
        /// </summary>
        [Test]
        public void AnOrphan_IsNotPlaced_AndAnEmptyCore_GetsUnknownNodes()
        {
            ResearchDefinition research = Core("research"), armament = Core("armament");
            ResearchDefinition bay1 = Research("bay1", 2, research);
            ResearchDefinition orphan = Research("orphan", 2);

            List<NeuralLayout.Placement> placements = NeuralLayout.Compute(new[] { research, armament }, new[] { bay1, orphan }, Step, 3);

            Assert.IsFalse(placements.Exists(p => ReferenceEquals(p.Definition, orphan)), "No prerequisite on the network: not placed.");

            List<NeuralLayout.Placement> unknown = placements.FindAll(p => p.Definition == null);
            Assert.AreEqual(3, unknown.Count);
            foreach (NeuralLayout.Placement node in unknown)
            {
                Assert.AreSame(armament, placements[node.ParentIndex].Definition, "Only the empty core gets them.");
            }
        }
    }
}
