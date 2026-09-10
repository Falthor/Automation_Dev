using System.Collections.Generic;
using Game.Data;
using Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// How a building's art meets its box, and which buildings are drawn to it rather than fitted
    /// into it.
    ///
    /// <b>The arithmetic is the point.</b> A box that does not carry the art's own proportion means
    /// one of two very different pictures, and neither is obvious from reading the value: fitted
    /// uniformly the sprite <i>covers</i> the box and overflows the narrow axis, drawn per axis it
    /// lands on the box and is distorted. Both are pinned here with real numbers, because the
    /// Constructor asks for the second and the other twenty buildings depend on the first.
    /// </summary>
    public class ArtBoxFittingTests
    {
        /// <summary>The Constructor's frames. 0.8 wide per tall, which is what makes its box a decision rather than a description.</summary>
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
        /// Drawn to the box: the art is exactly the size asked for, and the price is distortion.
        /// 64 x 96 over frames that are 0.8 wide per tall is 20% of extra height.
        /// </summary>
        [Test]
        public void DrawnToTheBox_TheArtIsExactlyTheBox()
        {
            SpriteRenderer renderer = NewRenderer();
            var box = new Vector2(64f / PixelsPerUnit, 96f / PixelsPerUnit);

            BuildingSpawner.FitArt(renderer, NewFrame(), box, stretch: true);

            Assert.AreEqual(box.x, renderer.bounds.size.x, 0.001f, "width");
            Assert.AreEqual(box.y, renderer.bounds.size.y, 0.001f, "height");
        }

        /// <summary>
        /// Fitted into the same box: the art keeps its proportion and <b>covers</b> the box, so it
        /// overflows the axis the box is relatively narrow on. 96 tall at 0.8 is 76.8 wide - which is
        /// why 2x3 cells could not simply be handed to the uniform path and left there, and why the
        /// Constructor needed a flag rather than a new number.
        /// </summary>
        [Test]
        public void FittedIntoTheSameBox_ItCoversAndOverflows()
        {
            SpriteRenderer renderer = NewRenderer();
            var box = new Vector2(64f / PixelsPerUnit, 96f / PixelsPerUnit);

            BuildingSpawner.FitArt(renderer, NewFrame(), box, stretch: false);

            Assert.AreEqual(96f / PixelsPerUnit, renderer.bounds.size.y, 0.001f, "the tall axis is what fills");
            Assert.AreEqual(76.8f / PixelsPerUnit, renderer.bounds.size.x, 0.001f,
                "and the width overflows the box, which is the whole reason a stretch was asked for");
        }

        /// <summary>
        /// A box that does carry the art's proportion: both paths agree, which is the normal case and
        /// the reason the uniform path is the default.
        /// </summary>
        [Test]
        public void WhenTheBoxCarriesTheArtsProportion_BothPathsAgree()
        {
            var box = new Vector2(64f / PixelsPerUnit, 80f / PixelsPerUnit);

            SpriteRenderer stretched = NewRenderer();
            SpriteRenderer fitted = NewRenderer();

            BuildingSpawner.FitArt(stretched, NewFrame(), box, stretch: true);
            BuildingSpawner.FitArt(fitted, NewFrame(), box, stretch: false);

            Assert.AreEqual(stretched.bounds.size.x, fitted.bounds.size.x, 0.001f);
            Assert.AreEqual(stretched.bounds.size.y, fitted.bounds.size.y, 0.001f);
            Assert.AreEqual(box.y, fitted.bounds.size.y, 0.001f);
        }

        // ---- Which buildings ask for it ----

        /// <summary>
        /// <b>The stretch is the exception, and this is what keeps it one.</b> Read off the shipped
        /// assets rather than the types, because that is where a value gets set by hand: a second
        /// building quietly given a distorted box fails here rather than being noticed on screen
        /// months later.
        /// </summary>
        [Test]
        public void OnlyTheConstructorIsDrawnToItsBox()
        {
            var stretching = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:BuildingDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
                if (definition != null && definition.StretchArtToBox) stretching.Add(definition.name);
            }

            CollectionAssert.AreEqual(new[] { "ConstructorDefinition" }, stretching,
                "a building drawn to its box instead of fitted into it is a decision about how it looks, "
                + "not a description of its art - see BuildingDefinition.StretchArtToBox");
        }

        /// <summary>
        /// The Constructor's own numbers, as literals. 2x3 cells is what produces 64x96 at 32 pixels
        /// to the cell; changing either is a change to how the building reads, and it should fail
        /// here rather than be noticed by eye.
        /// </summary>
        [Test]
        public void TheConstructorsBoxIsTwoByThreeCells()
        {
            var constructor = AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Data/Buildings/ConstructorDefinition.asset");
            Assert.IsNotNull(constructor, "the Constructor definition is missing");

            Assert.AreEqual(new Vector2Int(2, 2), constructor.FootprintSize, "the footprint stays 2x2");
            Assert.AreEqual(new Vector2(2f, 3f), constructor.ArtCellSize, "2x3 cells is the 64x96 that was asked for");
            Assert.IsTrue(constructor.StretchArtToBox);
            Assert.AreEqual(12, constructor.AnimationFrames.Length, "twelve frames, and the re-slice has to have kept every reference");
        }
    }
}
