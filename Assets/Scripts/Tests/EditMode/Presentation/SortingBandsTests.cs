using System.Collections.Generic;
using Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// The draw-order rule itself: an element lower on the grid draws in front of one above it,
    /// because its art may overhang the cell above.
    ///
    /// Tested as arithmetic rather than as pixels, which is the whole reason the band is a computed
    /// sortingOrder and not Unity's Transparency Sort Mode. Custom Axis would have sorted on each
    /// transform's own position - the footprint's <b>centre</b> for every building here, i.e. exactly
    /// the key the rule forbids - and it cannot be asserted outside a running camera.
    /// </summary>
    public class SortingBandsTests
    {
        [Test]
        public void ALowerRow_DrawsInFrontOfAHigherOne()
        {
            int low = SortingBands.Sorted(4f, SortingBands.SubSprite);
            int high = SortingBands.Sorted(9f, SortingBands.SubSprite);

            Assert.Greater(low, high, "Lower on the grid means nearer the camera.");
        }

        [Test]
        public void SubLayers_StackSilhouetteShadowSpriteOverlay_WithinOneRow()
        {
            int silhouette = SortingBands.Sorted(7f, SortingBands.SubSilhouette);
            int shadow = SortingBands.Sorted(7f, SortingBands.SubShadow);
            int sprite = SortingBands.Sorted(7f, SortingBands.SubSprite);
            int overlay = SortingBands.Sorted(7f, SortingBands.SubOverlay);

            Assert.Less(silhouette, shadow);
            Assert.Less(shadow, sprite);
            Assert.Less(sprite, overlay);
        }

        /// <summary>
        /// The band's whole point: a row's difference must outweigh any sub-layer difference, or a
        /// building's arrow would beat a building standing in front of it.
        /// </summary>
        [Test]
        public void ARowAlwaysOutranksASubLayer()
        {
            int nearestSubLayerOfAFarRow = SortingBands.Sorted(8f, SortingBands.SubOverlay);
            int lowestSubLayerOfANearRow = SortingBands.Sorted(7f, SortingBands.SubSilhouette);

            Assert.Greater(lowestSubLayerOfANearRow, nearestSubLayerOfAFarRow);
        }

        [Test]
        public void TheGroundBandIsEntirelyBelowTheSortedBand()
        {
            int[] ground =
            {
                SortingBands.TerrainBase, SortingBands.TerrainTop, SortingBands.GroundCoverage,
                SortingBands.FlatDecor, SortingBands.FlatVegetation, SortingBands.GroundSlab,
                SortingBands.DepositGlow, SortingBands.Deposit, SortingBands.GridLines,
                SortingBands.ActionRadius, SortingBands.Conveyor + SortingBands.ConveyorSeamParity,
                SortingBands.TransportedItem, SortingBands.CrossPiece
            };

            foreach (int order in ground)
            {
                Assert.Less(order, SortingBands.SortedFirst, $"Ground order {order} leaks into the sorted band.");
            }
        }

        /// <summary>
        /// The ground band's own fixed order, where every neighbour pair means something: concrete
        /// poured over a flower covers it, a deposit's halo reads behind the ore, and an item is
        /// above the belt carrying it but below the junction that covers it at the seam.
        /// </summary>
        [Test]
        public void TheGroundBandStacksInTheDecidedOrder()
        {
            Assert.Less(SortingBands.TerrainTop, SortingBands.GroundCoverage);
            Assert.Less(SortingBands.GroundCoverage, SortingBands.FlatDecor);
            Assert.Less(SortingBands.FlatDecor, SortingBands.FlatVegetation);
            Assert.Less(SortingBands.FlatVegetation, SortingBands.GroundSlab, "Concrete poured over a flower has to cover it.");
            Assert.Less(SortingBands.GroundSlab, SortingBands.DepositGlow);
            Assert.Less(SortingBands.DepositGlow, SortingBands.Deposit, "The halo reads behind the ore, not over it.");
            Assert.Less(SortingBands.Deposit, SortingBands.GridLines);
            Assert.Less(SortingBands.GridLines, SortingBands.ActionRadius);
            Assert.Less(SortingBands.ActionRadius, SortingBands.Conveyor);
            Assert.Less(SortingBands.Conveyor + SortingBands.ConveyorSeamParity, SortingBands.TransportedItem);
            Assert.Less(SortingBands.TransportedItem, SortingBands.CrossPiece);
        }

        /// <summary>
        /// A deposit lies flat, below everything in the sorted band, so an Extractor's construction
        /// silhouette can never end up hidden behind the ore it is being built on - the exception
        /// that would otherwise have to be written down somewhere and remembered.
        /// </summary>
        [Test]
        public void AnExtractorSilhouette_IsNeverHiddenBehindItsDeposit()
        {
            Assert.Greater(SortingBands.Sorted(0f, SortingBands.SubSilhouette), SortingBands.Deposit);
            Assert.Greater(SortingBands.Sorted(511f, SortingBands.SubSilhouette), SortingBands.Deposit,
                "True at the far edge of the world too, not just near the origin.");
        }

        [Test]
        public void InformationAndFogSitAboveEverythingInTheWorld()
        {
            Assert.Greater(SortingBands.PlacementPreview, SortingBands.SortedLast);
            Assert.Greater(SortingBands.PlacementPreviewArrow, SortingBands.PlacementPreview);
            Assert.Greater(SortingBands.HoverOutline, SortingBands.PlacementPreviewArrow);
            Assert.Greater(SortingBands.Fog, SortingBands.HoverOutline);
        }

        [Test]
        public void TheWholeLadderFitsInASortingOrder()
        {
            // Unity stores sortingOrder as a short. The band is sized for a world ten times the
            // current 60 cells, so this is headroom being asserted, not a close call.
            Assert.Less(SortingBands.Fog, short.MaxValue);
            Assert.GreaterOrEqual(SortingBands.TerrainBase, short.MinValue);
        }

        [Test]
        public void OutOfRangeCoordinates_ClampInsteadOfWrappingIntoAnotherBand()
        {
            int belowWorld = SortingBands.Sorted(-50f, SortingBands.SubSprite);
            int beyondWorld = SortingBands.Sorted(100000f, SortingBands.SubSprite);

            Assert.GreaterOrEqual(belowWorld, SortingBands.SortedFirst);
            Assert.LessOrEqual(belowWorld, SortingBands.SortedLast);
            Assert.GreaterOrEqual(beyondWorld, SortingBands.SortedFirst);
            Assert.LessOrEqual(beyondWorld, SortingBands.SortedLast);
        }

        /// <summary>
        /// The door the baked decor could otherwise leave open. WildDecorationGenerator writes
        /// GameObjects into a scene, so every rank it assigns is frozen there: it is the one place a
        /// stale draw order cannot be corrected at load, because nothing recomputes it. The generator
        /// calls SortingBands rather than copying the formula; this recomputes what is actually
        /// stored and fails if the two have drifted apart.
        ///
        /// It scans every scene in the build rather than one by name, so a scene gaining baked decor
        /// is covered without anyone remembering to add it here.
        /// </summary>
        [Test]
        public void BakedSceneRanks_MatchWhatTheLadderComputesToday()
        {
            var mismatches = new List<string>();
            int checkedRenderers = 0;

            foreach (string path in ScenePaths())
            {
                Scene scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                        {
                            int stored = renderer.sortingOrder;
                            if (stored < SortingBands.SortedFirst || stored > SortingBands.SortedLast) continue;

                            checkedRenderers++;
                            int recomputed = SortingBands.SortedFromBounds(renderer, stored % SortingBands.SubLayers);
                            if (recomputed == stored) continue;

                            mismatches.Add($"{path}:{renderer.name} stored {stored}, recomputed {recomputed}");
                        }
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.IsEmpty(mismatches,
                "A baked rank no longer matches the ladder. Re-run Tools/Wild Decoration/Regenerate All, "
                + "or fix whatever wrote these by hand:\n" + string.Join("\n", mismatches));

            // Said out loud rather than left to a silently green run: nothing is baked into a scene
            // today (the wild scatter is regenerated on every Play Mode entry and discarded on exit),
            // so this currently guards a door that is not yet open. It closes the moment decor is
            // saved into a scene, which is the point.
            if (checkedRenderers == 0) Assert.Pass("No baked sorted-band renderer in any scene yet.");
        }

        static IEnumerable<string> ScenePaths()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                yield return AssetDatabase.GUIDToAssetPath(guid);
            }
        }
    }
}
