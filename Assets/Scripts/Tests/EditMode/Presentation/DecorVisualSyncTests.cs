using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Grid;
using Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// The decor window: what exists as an object, and what happens to it when the camera leaves.
    ///
    /// <b>Registration with the depth ladder is what this file is really about.</b> This subsystem's
    /// one real bug so far was a registration that never happened - a startup sweep running before
    /// the generator, leaving 527 rocks at order 0. The window removes that ordering problem and
    /// creates its own: an object now registers on entering and unregisters on leaving, so a round
    /// trip can leave it absent, or on the ladder twice. Both are asserted.
    /// </summary>
    public class DecorVisualSyncTests
    {
        const int MapSize = 640;
        const int ChunkSize = 64;
        const int Seed = 20260907;

        /// <summary>The zoom-out cap. Kept small so a window holds a handful of chunks rather than hundreds.</summary>
        const float MaxOrthographicSize = 30f;

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

        DecorSettings NewSettings(bool raised = true, int poolSize = 512, int windowMarginCells = 0)
        {
            var settings = ScriptableObject.CreateInstance<DecorSettings>();
            _spawned.Add(settings);

            var so = new SerializedObject(settings);
            so.FindProperty("itemsPerChunk").floatValue = 20f;
            so.FindProperty("bandEdgeExclusion").floatValue = 0f;
            so.FindProperty("windowMarginCells").intValue = windowMarginCells;
            so.FindProperty("poolSize").intValue = poolSize;

            SerializedProperty kinds = so.FindProperty("kinds");
            kinds.arraySize = 1;
            SerializedProperty kind = kinds.GetArrayElementAtIndex(0);
            kind.FindPropertyRelative("id").stringValue = "rock";
            kind.FindPropertyRelative("raised").boolValue = raised;
            kind.FindPropertyRelative("scaleRange").vector2Value = new Vector2(1f, 1f);

            SerializedProperty weights = kind.FindPropertyRelative("bandWeights");
            weights.arraySize = 3;
            for (int i = 0; i < 3; i++) weights.GetArrayElementAtIndex(i).floatValue = 1f;

            SerializedProperty sprites = kind.FindPropertyRelative("sprites");
            sprites.arraySize = 1;
            sprites.GetArrayElementAtIndex(0).objectReferenceValue = NewSprite();

            so.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }

        Sprite NewSprite()
        {
            var texture = new Texture2D(4, 4);
            texture.SetPixels(new Color[16]);
            texture.Apply();
            _spawned.Add(texture);

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            _spawned.Add(sprite);
            return sprite;
        }

        DecorRuntime NewDecor()
        {
            var biome = new BiomeField(Vector2.zero, 40f, 6426f, new[] { 1f, 1f, 0f }, 2);
            return new DecorRuntime(MapSize, ChunkSize, Seed, biome, new[] { new[] { 1f, 1f, 1f } }, 20f, 0f);
        }

        DecorVisualSync NewSync(out DecorRuntime decor, out DepthSortLadder ladder, DecorSettings settings = null)
        {
            var go = new GameObject("DecorVisualSync");
            _spawned.Add(go);

            var sync = go.AddComponent<DecorVisualSync>();
            decor = NewDecor();
            ladder = new DepthSortLadder(MaxOrthographicSize);

            sync.Initialize(decor, new GridRuntime(1f), ladder, settings ?? NewSettings(), null, MaxOrthographicSize);
            return sync;
        }

        // ---- The window ----

        [Test]
        public void NothingExistsUntilTheWindowIsPlaced()
        {
            DecorVisualSync sync = NewSync(out _, out DepthSortLadder ladder);

            Assert.AreEqual(0, sync.LiveItemCount);
            Assert.AreEqual(0, ladder.TrackedCount);
        }

        [Test]
        public void PlacingTheWindowSpawnsTheChunksAroundTheCamera()
        {
            DecorVisualSync sync = NewSync(out _, out DepthSortLadder ladder);

            Assert.IsTrue(sync.FollowCamera(new Vector2(320f, 320f)));
            Assert.Greater(sync.LiveChunkCount, 0);
            Assert.Greater(sync.LiveItemCount, 0);
            Assert.AreEqual(sync.LiveItemCount, ladder.TrackedCount, "every raised sprite belongs on the ladder");
        }

        /// <summary>
        /// The chunk grid is the hysteresis: the window moves about once per chunk crossed, not once
        /// per frame - which is what makes a pan affordable.
        ///
        /// Note what decides it: the window covers the <b>view</b>, so it moves when a view edge
        /// crosses a chunk boundary, not when the camera's centre does. And there are two edges,
        /// offset by the view's width, each crossing on its own schedule - so the rate is about two
        /// moves per chunk travelled, not one. Measured at 20 over ten chunks.
        ///
        /// The property worth asserting is the rate, not a number: a move must cost a chunk of
        /// travel, not a frame.
        /// </summary>
        [Test]
        public void PanningMovesTheWindowOncePerChunk_NotOncePerFrame()
        {
            DecorVisualSync sync = NewSync(out _, out _);
            sync.FollowCamera(new Vector2(320f, 320f));
            int before = sync.WindowMoveCount;

            const int Distance = 10 * ChunkSize;
            for (int i = 1; i <= Distance; i++) sync.FollowCamera(new Vector2(320f + i, 320f));

            int moves = sync.WindowMoveCount - before;
            Assert.Less(moves, Distance / 16,
                $"{Distance} cells of panning moved the window {moves} times - it is tracking frames rather than chunks");
            Assert.Greater(moves, 5, "the window did not follow the camera at all");
        }

        [Test]
        public void CrossingAChunkBoundary_MovesTheWindow()
        {
            DecorVisualSync sync = NewSync(out _, out _);
            sync.FollowCamera(new Vector2(320f, 320f));
            int moves = sync.WindowMoveCount;

            sync.FollowCamera(new Vector2(320f + 3 * ChunkSize, 320f));

            Assert.Greater(sync.WindowMoveCount, moves);
        }

        // ---- The round trip ----

        /// <summary>
        /// The test this file exists for. Leave the window, come back, and the decor must be neither
        /// absent nor doubled - on screen or on the ladder.
        /// </summary>
        [Test]
        public void LeavingAndReturning_LeavesExactlyWhatWasThere()
        {
            DecorVisualSync sync = NewSync(out _, out DepthSortLadder ladder);

            var home = new Vector2(320f, 320f);
            sync.FollowCamera(home);

            int itemsAtHome = sync.LiveItemCount;
            int trackedAtHome = ladder.TrackedCount;
            Assert.Greater(itemsAtHome, 0);

            // Far enough that not one chunk of the original window survives.
            sync.FollowCamera(new Vector2(320f + 20 * ChunkSize, 320f));
            sync.FollowCamera(home);

            Assert.AreEqual(itemsAtHome, sync.LiveItemCount, "coming back spawned a different number of objects");
            Assert.AreEqual(trackedAtHome, ladder.TrackedCount,
                "the ladder holds a different number after a round trip - either something was left off it, or registered twice");
        }

        [Test]
        public void LeavingUnregistersEverythingItTakesAway()
        {
            DecorVisualSync sync = NewSync(out _, out DepthSortLadder ladder);

            sync.FollowCamera(new Vector2(320f, 320f));
            Assert.Greater(ladder.TrackedCount, 0);

            // Off the map entirely: no chunk of it is live.
            sync.FollowCamera(new Vector2(-5000f, -5000f));

            Assert.AreEqual(0, sync.LiveItemCount);
            Assert.AreEqual(0, ladder.TrackedCount, "objects left the window but stayed on the ladder");
        }

        /// <summary>Ten crossings back and forth: the ladder must not grow by one entry each time.</summary>
        [Test]
        public void RepeatedCrossings_DoNotGrowTheLadder()
        {
            DecorVisualSync sync = NewSync(out _, out DepthSortLadder ladder);

            var home = new Vector2(320f, 320f);
            var away = new Vector2(320f + 20 * ChunkSize, 320f);

            sync.FollowCamera(home);
            int trackedAtHome = ladder.TrackedCount;

            for (int i = 0; i < 10; i++)
            {
                sync.FollowCamera(away);
                sync.FollowCamera(home);
            }

            Assert.AreEqual(trackedAtHome, ladder.TrackedCount);
        }

        [Test]
        public void ObjectsArePooledRatherThanDestroyed()
        {
            DecorVisualSync sync = NewSync(out _, out _);

            sync.FollowCamera(new Vector2(320f, 320f));
            sync.FollowCamera(new Vector2(-5000f, -5000f));

            Assert.Greater(sync.PooledCount, 0, "everything was destroyed instead of pooled");
        }

        // ---- What the player cleared ----

        [Test]
        public void AClearedCellDisappearsAtOnce_AndDoesNotComeBackOnAReturn()
        {
            DecorVisualSync sync = NewSync(out DecorRuntime decor, out DepthSortLadder ladder);

            var home = new Vector2(320f, 320f);
            sync.FollowCamera(home);
            int before = sync.LiveItemCount;

            // Whatever the chunk under the camera grew first.
            var items = new List<DecorItem>();
            decor.CollectChunk(5, 5, items);
            Assert.Greater(items.Count, 0);
            GridCoord cleared = items[0].Cell;

            decor.Remove(cleared);
            sync.ForgetCell(cleared);

            Assert.AreEqual(before - 1, sync.LiveItemCount, "clearing did not remove the object on screen");
            Assert.AreEqual(sync.LiveItemCount, ladder.TrackedCount, "the cleared object stayed on the ladder");

            sync.FollowCamera(new Vector2(320f + 20 * ChunkSize, 320f));
            sync.FollowCamera(home);

            Assert.AreEqual(before - 1, sync.LiveItemCount, "a cleared decoration grew back after a window round trip");
        }

        // ---- Flat decor ----

        [Test]
        public void FlatDecorTakesAFixedGroundOrder_AndNeverTouchesTheLadder()
        {
            DecorSettings flat = NewSettings(raised: false);
            DecorVisualSync sync = NewSync(out _, out DepthSortLadder ladder, flat);

            sync.FollowCamera(new Vector2(320f, 320f));

            Assert.Greater(sync.LiveItemCount, 0);
            Assert.AreEqual(0, ladder.TrackedCount, "flat decor has no depth to sort and must stay off the ladder");
        }

        [Test]
        public void ClearEmptiesEverythingIncludingThePool()
        {
            DecorVisualSync sync = NewSync(out _, out DepthSortLadder ladder);

            sync.FollowCamera(new Vector2(320f, 320f));
            sync.Clear();

            Assert.AreEqual(0, sync.LiveItemCount);
            Assert.AreEqual(0, sync.PooledCount);
            Assert.AreEqual(0, ladder.TrackedCount);
        }
    }
}
