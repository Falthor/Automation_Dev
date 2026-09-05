using System.Collections;
using System.Collections.Generic;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// DropShadow builds a real child SpriteRenderer and reads a shared settings asset, so it is
    /// rendering integration rather than pure domain logic - PlayMode, per
    /// PROJECT_ARCHITECTURE.md's test split. Nothing here asserts what the shadow looks like on
    /// screen, only the wiring: same sprite, right depth, right offset, and that the global
    /// settings are genuinely the single source.
    /// </summary>
    public sealed class DropShadowTests
    {
        readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            _spawned.Clear();
        }

        BuildingShadowSettings NewSettings(float alpha = 0.45f, Vector2? offset = null, int sortingOrder = 8)
        {
            var settings = ScriptableObject.CreateInstance<BuildingShadowSettings>();
            _spawned.Add(settings);
            SetSettings(settings, alpha, offset ?? new Vector2(0.25f, -0.25f), sortingOrder);
            return settings;
        }

        /// <summary>
        /// Writes the asset's private serialized fields, which have no production setter on
        /// purpose (BuildingShadowSettings is a definition, not runtime state). Same
        /// SerializedObject technique the EditMode TestDataFactory uses; editor-only, and these
        /// tests are only ever run in the editor.
        /// </summary>
        static void SetSettings(BuildingShadowSettings settings, float alpha, Vector2 offset, int sortingOrder)
        {
#if UNITY_EDITOR
            var so = new UnityEditor.SerializedObject(settings);
            so.FindProperty("alpha").floatValue = alpha;
            so.FindProperty("offset").vector2Value = offset;
            so.FindProperty("sortingOrder").intValue = sortingOrder;
            so.ApplyModifiedPropertiesWithoutUndo();
#else
            Assert.Ignore("DropShadowTests configures its settings asset through the editor API.");
#endif
        }

        Sprite NewSprite()
        {
            var texture = new Texture2D(4, 4);
            _spawned.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            _spawned.Add(sprite);
            return sprite;
        }

        /// <summary>A caster shaped like a real building view: a sprite scaled to fit its footprint, which is exactly the case where a naive local offset would come out the wrong size.</summary>
        DropShadow NewCaster(BuildingShadowSettings settings, Sprite sprite, Vector3 position, Vector3 scale)
        {
            var go = new GameObject("Caster");
            _spawned.Add(go);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.AddComponent<SpriteRenderer>().sprite = sprite;

            var shadow = go.AddComponent<DropShadow>();
            shadow.Settings = settings;
            shadow.Apply();
            return shadow;
        }

        [Test]
        public void CreatesAChildRendererShowingTheCastersOwnSprite()
        {
            Sprite sprite = NewSprite();
            DropShadow shadow = NewCaster(NewSettings(), sprite, Vector3.zero, Vector3.one);

            Assert.IsNotNull(shadow.ShadowRenderer, "No shadow renderer was created.");
            Assert.AreSame(shadow.transform, shadow.ShadowRenderer.transform.parent);
            Assert.AreSame(sprite, shadow.ShadowRenderer.sprite, "The shadow must reuse the caster's sprite, not a new asset.");
        }

        [Test]
        public void UsesTheSortingLayerOfItsCasterAndTheConfiguredOrder()
        {
            DropShadow shadow = NewCaster(NewSettings(sortingOrder: 8), NewSprite(), Vector3.zero, Vector3.one);
            var caster = shadow.GetComponent<SpriteRenderer>();

            // 10 is what BuildingSpawner/WorldContentSpawner give a building's own sprite.
            caster.sortingOrder = 10;

            Assert.AreEqual(caster.sortingLayerID, shadow.ShadowRenderer.sortingLayerID);
            Assert.AreEqual(8, shadow.ShadowRenderer.sortingOrder);
            Assert.Less(shadow.ShadowRenderer.sortingOrder, caster.sortingOrder,
                "The shadow must draw under the building casting it.");
        }

        [Test]
        public void IsBlackAtTheConfiguredAlpha()
        {
            DropShadow shadow = NewCaster(NewSettings(alpha: 0.45f), NewSprite(), Vector3.zero, Vector3.one);

            Color color = shadow.ShadowRenderer.color;
            Assert.AreEqual(0f, color.r, 0.0001f);
            Assert.AreEqual(0f, color.g, 0.0001f);
            Assert.AreEqual(0f, color.b, 0.0001f);
            Assert.AreEqual(0.45f, color.a, 0.0001f);
        }

        [Test]
        public void OffsetIsTheConfiguredOneInWorldUnits()
        {
            var offset = new Vector2(0.25f, -0.25f);
            DropShadow shadow = NewCaster(NewSettings(offset: offset), NewSprite(), new Vector3(3f, 7f, 0f), Vector3.one);

            Vector3 worldOffset = shadow.ShadowRenderer.transform.position - shadow.transform.position;
            Assert.AreEqual(offset.x, worldOffset.x, 0.0001f);
            Assert.AreEqual(offset.y, worldOffset.y, 0.0001f);
        }

        /// <summary>
        /// The regression that matters: every building view in this project scales its sprite child
        /// to the footprint, so a shadow parented to it inherits that scale. The offset must stay
        /// the same physical distance whatever the building's size.
        /// </summary>
        [Test]
        public void OffsetIgnoresTheCastersScale()
        {
            var offset = new Vector2(0.25f, -0.25f);
            DropShadow shadow = NewCaster(NewSettings(offset: offset), NewSprite(), Vector3.zero, new Vector3(0.5f, 0.5f, 1f));

            Vector3 worldOffset = shadow.ShadowRenderer.transform.position - shadow.transform.position;
            Assert.AreEqual(offset.x, worldOffset.x, 0.0001f);
            Assert.AreEqual(offset.y, worldOffset.y, 0.0001f);
        }

        /// <summary>The sun turns; the buildings do not follow it. A rotated caster (Splitter, conveyor corner) must still drop its shadow in the global sun direction.</summary>
        [Test]
        public void OffsetIgnoresTheCastersRotation()
        {
            var offset = new Vector2(0.25f, -0.25f);
            DropShadow shadow = NewCaster(NewSettings(offset: offset), NewSprite(), Vector3.zero, Vector3.one);
            shadow.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            shadow.Apply();

            Vector3 worldOffset = shadow.ShadowRenderer.transform.position - shadow.transform.position;
            Assert.AreEqual(offset.x, worldOffset.x, 0.0001f);
            Assert.AreEqual(offset.y, worldOffset.y, 0.0001f);
        }

        [Test]
        public void FollowsTheCastersSpriteWhenItChanges()
        {
            DropShadow shadow = NewCaster(NewSettings(), NewSprite(), Vector3.zero, Vector3.one);

            Sprite nextFrame = NewSprite();
            shadow.GetComponent<SpriteRenderer>().sprite = nextFrame;
            shadow.Apply();

            Assert.AreSame(nextFrame, shadow.ShadowRenderer.sprite, "An animated building's shadow must follow its current frame.");
        }

        /// <summary>Two buildings, one settings asset: moving the sun there must move both, through the normal frame loop rather than an explicit refresh call.</summary>
        [UnityTest]
        public IEnumerator EveryShadowFollowsTheGlobalSettings()
        {
            BuildingShadowSettings settings = NewSettings(alpha: 0.45f, offset: new Vector2(0.25f, -0.25f));
            DropShadow first = NewCaster(settings, NewSprite(), Vector3.zero, Vector3.one);
            DropShadow second = NewCaster(settings, NewSprite(), new Vector3(10f, 0f, 0f), Vector3.one);

            SetSettings(settings, 0.8f, new Vector2(-0.5f, 0.5f), 6);
            yield return null;

            foreach (DropShadow shadow in new[] { first, second })
            {
                Assert.AreEqual(0.8f, shadow.ShadowRenderer.color.a, 0.0001f);
                Assert.AreEqual(6, shadow.ShadowRenderer.sortingOrder);

                Vector3 worldOffset = shadow.ShadowRenderer.transform.position - shadow.transform.position;
                Assert.AreEqual(-0.5f, worldOffset.x, 0.0001f);
                Assert.AreEqual(0.5f, worldOffset.y, 0.0001f);
            }
        }

        [Test]
        public void DoesNothingWithoutASettingsAsset()
        {
            var go = new GameObject("Caster");
            _spawned.Add(go);
            go.AddComponent<SpriteRenderer>().sprite = NewSprite();

            var shadow = go.AddComponent<DropShadow>();

            Assert.IsNull(shadow.ShadowRenderer, "No settings asset must mean no shadow, not a hardcoded fallback look.");
        }
    }
}
