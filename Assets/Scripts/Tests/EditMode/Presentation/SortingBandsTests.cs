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
            // Depth below the window's top, so a bigger number is further down the screen.
            int low = SortingBands.SortedFromDepth(9f, SortingBands.SubSprite);
            int high = SortingBands.SortedFromDepth(4f, SortingBands.SubSprite);

            Assert.Greater(low, high, "Lower on the grid means nearer the camera.");
        }

        [Test]
        public void SubLayers_StackSilhouetteShadowSpriteOverlay_WithinOneRow()
        {
            int silhouette = SortingBands.SortedFromDepth(7f, SortingBands.SubSilhouette);
            int shadow = SortingBands.SortedFromDepth(7f, SortingBands.SubShadow);
            int sprite = SortingBands.SortedFromDepth(7f, SortingBands.SubSprite);
            int overlay = SortingBands.SortedFromDepth(7f, SortingBands.SubOverlay);

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
            int nearestSubLayerOfAFarRow = SortingBands.SortedFromDepth(7f, SortingBands.SubOverlay);
            int lowestSubLayerOfANearRow = SortingBands.SortedFromDepth(8f, SortingBands.SubSilhouette);

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
            Assert.Greater(SortingBands.SortedFromDepth(0f, SortingBands.SubSilhouette), SortingBands.Deposit);
            Assert.Greater(SortingBands.SortedFromDepth(SortingBands.AddressableRows, SortingBands.SubSilhouette), SortingBands.Deposit,
                "True at the far edge of the window too, not just at its top.");
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
            // Unity stores sortingOrder as a short. The ladder is sized for the view window, not
            // for the world, so this stays true at any map size - which is the whole point of the
            // change. Asserted anyway: AddressableRows is the one knob that can break it.
            Assert.Less(SortingBands.Fog, short.MaxValue);
            Assert.GreaterOrEqual(SortingBands.TerrainBase, short.MinValue);
        }

        [Test]
        public void OutOfRangeCoordinates_ClampInsteadOfWrappingIntoAnotherBand()
        {
            int belowWorld = SortingBands.SortedFromDepth(-50f, SortingBands.SubSprite);
            int beyondWorld = SortingBands.SortedFromDepth(100000f, SortingBands.SubSprite);

            Assert.GreaterOrEqual(belowWorld, SortingBands.SortedFirst);
            Assert.LessOrEqual(belowWorld, SortingBands.SortedLast);
            Assert.GreaterOrEqual(beyondWorld, SortingBands.SortedFirst);
            Assert.LessOrEqual(beyondWorld, SortingBands.SortedLast);
        }

        /// <summary>
        /// No scene may carry a sorted-band rank at all - a stronger rule than the one this replaces,
        /// and a simpler one.
        ///
        /// It used to recompute each baked rank and compare. That comparison stopped being possible
        /// the moment ranks became relative to a moving window: a number frozen in a scene is only
        /// true for the window it was computed against, and there is no way to check it after the
        /// fact because the window it belonged to is gone. So the rank cannot be stored at all - it
        /// has to be handed out at runtime by <see cref="DepthSortLadder"/>.
        ///
        /// Scans every scene rather than one by name, so a scene gaining decor is covered without
        /// anyone remembering to add it here.
        /// </summary>
        [Test]
        public void NoSceneCarriesASortedBandRank()
        {
            var baked = new List<string>();

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

                            baked.Add($"{path}:{renderer.name} carries {stored}");
                        }
                    }
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.IsEmpty(baked,
                "A sorted-band rank is stored in a scene. It is measured against a depth window that "
                + "follows the camera, so a frozen copy is wrong as soon as the camera moves. Whatever "
                + "wrote these must register with DepthSortLadder at runtime instead:\n"
                + string.Join("\n", baked));
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
