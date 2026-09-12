using System.Collections.Generic;
using Game.Data;
using Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// How a building's art meets its box.
    ///
    /// <b>The arithmetic is the point, and it is not what reading the value suggests.</b> The fit is
    /// uniform and it <i>covers</i> the box rather than fitting inside it - so a box that does not
    /// carry the art's own proportion makes the sprite overflow the narrow axis rather than leaving
    /// a margin on it. That is why artCellSize is a statement about the art and not a decision about
    /// how a building should look, and it is pinned here with real numbers.
    /// </summary>
    public class ArtBoxFittingTests
    {
        /// <summary>The Constructor's own frame size - not square, so a box carrying the wrong proportion shows up in the numbers.</summary>
        const int FrameWidth = 512;
        const int FrameHeight = 640;

        /// <summary>Unity's default sprite import scale, and what the Constructor sheet uses.</summary>
        const float PixelsPerUnit = 100f;

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        SpriteRenderer NewRenderer()
        {
            var go = new GameObject("art");
            _created.Add(go);
            return go.AddComponent<SpriteRenderer>();
        }

        Sprite NewFrame()
        {
            var texture = new Texture2D(FrameWidth, FrameHeight);
            _created.Add(texture);

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, FrameWidth, FrameHeight), new Vector2(0.5f, 0.5f), PixelsPerUnit);
            _created.Add(sprite);
            return sprite;
        }

        // ---- The two ways art can meet a box ----

        /// <summary>
        /// Fitted into the same box: the art keeps its proportion and <b>covers</b> the box, so it
        /// overflows the axis the box is relatively narrow on. 96 tall at 0.8 is 76.8 wide - so a box
        /// of 2x3 cells over a 0.8 frame does not give a building 64 wide, it gives one 76.8 wide that
        /// overhangs its own footprint. That is the number to reach for when a box looks wrong.
        /// </summary>
        [Test]
        public void FittedIntoTheSameBox_ItCoversAndOverflows()
        {
            SpriteRenderer renderer = NewRenderer();
            var box = new Vector2(64f / PixelsPerUnit, 96f / PixelsPerUnit);

            BuildingSpawner.FitSpriteUniform(renderer, NewFrame(), box);

            Assert.AreEqual(96f / PixelsPerUnit, renderer.bounds.size.y, 0.001f, "the tall axis is what fills");
            Assert.AreEqual(76.8f / PixelsPerUnit, renderer.bounds.size.x, 0.001f,
                "and the width overflows the box rather than leaving a margin in it");
        }

        /// <summary>
        /// A box that does carry the art's proportion: both paths agree, which is the normal case and
        /// the reason the uniform path is the default.
        /// </summary>
        [Test]
        public void WhenTheBoxCarriesTheArtsProportion_TheArtLandsOnIt()
        {
            var box = new Vector2(64f / PixelsPerUnit, 80f / PixelsPerUnit);

            SpriteRenderer fitted = NewRenderer();
            BuildingSpawner.FitSpriteUniform(fitted, NewFrame(), box);

            Assert.AreEqual(box.x, fitted.bounds.size.x, 0.001f);
            Assert.AreEqual(box.y, fitted.bounds.size.y, 0.001f);
        }

        // ---- The Constructor ----

        /// <summary>
        /// The Constructor drawn end to end, from its own asset: the box comes out of the art
        /// (BuildingSpawner.ArtWorldSize) and the fit lands exactly on it, because that box carries
        /// the frame's proportion by construction rather than by someone typing it.
        ///
        /// 512x640 frames on a 3x3 footprint: 3 cells wide - never more, whatever the sheet - and
        /// 3.75 tall, the extra reaching upward.
        /// </summary>
        [Test]
        public void TheConstructorIsDrawnItsFootprintWide_AndTallerFromItsOwnFrames()
        {
            var constructor = AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Data/Buildings/ConstructorDefinition.asset");
            Assert.IsNotNull(constructor, "the Constructor definition is missing");

            Assert.AreEqual(new Vector2Int(3, 3), constructor.FootprintSize, "the footprint is 3x3");
            Assert.AreEqual(12, constructor.AnimationFrames.Length, "twelve frames, and the re-slice has to have kept every reference");

            Vector2 box = BuildingSpawner.ArtWorldSize(constructor, 1f, constructor.Sprite);
            Assert.AreEqual(3f, box.x, 0.001f, "as wide as the cells it stands on");
            Assert.AreEqual(3.75f, box.y, 0.001f, "and 640/512 of that tall");

            SpriteRenderer renderer = NewRenderer();
            BuildingSpawner.FitSpriteUniform(renderer, NewFrame(), box);

            Assert.AreEqual(box.x, renderer.bounds.size.x, 0.001f, "the fit lands on the box rather than covering it");
            Assert.AreEqual(box.y, renderer.bounds.size.y, 0.001f);
        }
    }
}
