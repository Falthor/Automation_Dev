using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Instantiates the decor of the chunks around the camera, and only those.
    ///
    /// <b>A window, not a world.</b> Scattering the whole map was what could not survive it growing:
    /// at one item per hundred cells, a 10 000-cell map holds half a million objects. Outside the
    /// window a rock is not an absent object but a function nobody has evaluated
    /// (<see cref="DecorRuntime"/>); entering the window is what evaluates it.
    ///
    /// <b>The window is a rectangle of chunks</b>, which gives the hysteresis for free: it only moves
    /// when the camera crosses a chunk boundary, so panning inside one costs a comparison. The fog's
    /// window needs an explicit slack because its unit is the texel; this one's unit is already 64
    /// cells wide.
    ///
    /// <b>Objects are pooled rather than created and destroyed.</b> A pan at full zoom-out crosses
    /// chunks steadily, and instantiating on every crossing is what produces stutter.
    ///
    /// <b>Registration with the depth ladder is the delicate part</b>, and it is where this subsystem
    /// has already produced its one real bug: a startup sweep ran before the generator, so 527 rocks
    /// kept sortingOrder 0 and sank into the ground band. There is no sweep now - a sprite registers
    /// when it enters the window and unregisters when it leaves - which removes the ordering problem but
    /// introduces its own: leaving and returning must not leave an object absent, nor registered
    /// twice. A test does exactly that round trip.
    /// </summary>
    public sealed class DecorVisualSync : MonoBehaviour
    {
        DecorRuntime _decor;
        GridRuntime _grid;
        DepthSortLadder _depthSort;
        DecorSettings _settings;
        Camera _camera;

        float _viewHalfWidth;
        float _viewHalfHeight;

        /// <summary>The live chunks, and what each one spawned - the unit of both spawning and despawning.</summary>
        readonly Dictionary<int, List<GameObject>> _live = new Dictionary<int, List<GameObject>>();

        readonly Stack<GameObject> _pool = new Stack<GameObject>();

        // Reused every window move, so following the camera allocates nothing.
        readonly List<DecorItem> _items = new List<DecorItem>();
        readonly List<int> _leaving = new List<int>();

        RectInt _window;
        bool _anchored;

        public int LiveChunkCount => _live.Count;

        public int PooledCount => _pool.Count;

        /// <summary>How many decor objects are currently instantiated. What a test counts to see that a round trip left exactly one of each.</summary>
        public int LiveItemCount
        {
            get
            {
                int count = 0;
                foreach (var chunk in _live) count += chunk.Value.Count;
                return count;
            }
        }

        /// <summary>Window moves so far. Watched by a test: panning inside a chunk must not move it.</summary>
        public int WindowMoveCount { get; private set; }

        public void Initialize(DecorRuntime decor, GridRuntime grid, DepthSortLadder depthSort, DecorSettings settings,
            Camera worldCamera, float maxOrthographicSize)
        {
            _decor = decor;
            _grid = grid;
            _depthSort = depthSort;
            _settings = settings;
            _camera = worldCamera;

            float aspect = worldCamera != null && worldCamera.aspect > 0f ? worldCamera.aspect : 16f / 9f;
            _viewHalfHeight = Mathf.Max(0f, maxOrthographicSize);
            _viewHalfWidth = _viewHalfHeight * aspect;

            _anchored = false;
        }

        void LateUpdate()
        {
            if (_camera == null) return;
            FollowCamera(_camera.transform.position);
        }

        /// <summary>
        /// Moves the window if the camera has left the chunks it covers, and answers whether it did.
        /// Public so a test can drive it without a camera or a frame loop.
        /// </summary>
        public bool FollowCamera(Vector2 cameraWorld)
        {
            if (_decor == null || _grid == null || _settings == null) return false;

            RectInt wanted = WindowFor(cameraWorld);
            if (_anchored && wanted.Equals(_window)) return false;

            _window = wanted;
            _anchored = true;
            WindowMoveCount++;

            Despawn(wanted);
            Spawn(wanted);
            return true;
        }

        /// <summary>The chunk rectangle covering the widest possible view plus the configured margin. Measured against the zoom-out cap, not the current zoom, so zooming out never outruns the window.</summary>
        RectInt WindowFor(Vector2 cameraWorld)
        {
            float cellSize = Mathf.Max(_grid.CellSize, 0.0001f);
            float margin = _settings.WindowMarginCells * cellSize;

            int chunk = _decor.ChunkSizeCells;
            int minX = Mathf.FloorToInt((cameraWorld.x - _viewHalfWidth - margin) / cellSize / chunk);
            int maxX = Mathf.FloorToInt((cameraWorld.x + _viewHalfWidth + margin) / cellSize / chunk);
            int minY = Mathf.FloorToInt((cameraWorld.y - _viewHalfHeight - margin) / cellSize / chunk);
            int maxY = Mathf.FloorToInt((cameraWorld.y + _viewHalfHeight + margin) / cellSize / chunk);

            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>Returns to the pool everything whose chunk has left the window. Unregistering here rather than at destruction is what keeps the ladder's list from growing with every pan.</summary>
        void Despawn(RectInt window)
        {
            _leaving.Clear();

            foreach (var entry in _live)
            {
                int chunkX = entry.Key % _decor.ChunksPerAxis;
                int chunkY = entry.Key / _decor.ChunksPerAxis;
                if (!window.Contains(new Vector2Int(chunkX, chunkY))) _leaving.Add(entry.Key);
            }

            foreach (int chunkIndex in _leaving)
            {
                foreach (GameObject go in _live[chunkIndex]) Recycle(go);
                _live.Remove(chunkIndex);
            }
        }

        void Spawn(RectInt window)
        {
            for (int chunkY = window.yMin; chunkY < window.yMax; chunkY++)
            {
                for (int chunkX = window.xMin; chunkX < window.xMax; chunkX++)
                {
                    if (!_decor.ContainsChunk(chunkX, chunkY)) continue;

                    int chunkIndex = chunkY * _decor.ChunksPerAxis + chunkX;
                    if (_live.ContainsKey(chunkIndex)) continue;   // already live: not respawned, not duplicated

                    _items.Clear();
                    _decor.CollectChunk(chunkX, chunkY, _items);
                    if (_items.Count == 0) continue;

                    var spawned = new List<GameObject>(_items.Count);
                    foreach (DecorItem item in _items) spawned.Add(Place(item));
                    _live[chunkIndex] = spawned;
                }
            }
        }

        GameObject Place(DecorItem item)
        {
            GameObject go = _pool.Count > 0 ? _pool.Pop() : NewObject();
            go.SetActive(true);

            var renderer = go.GetComponent<SpriteRenderer>();
            DecorSettings.Kind kind = _settings.Kinds[Mathf.Clamp(item.Kind, 0, _settings.Kinds.Length - 1)];

            Sprite[] sprites = kind.Sprites;
            renderer.sprite = sprites != null && sprites.Length > 0 ? sprites[(int)(item.Draw / 256u % (uint)sprites.Length)] : null;
            renderer.color = kind.Tint;

            float scale = Mathf.Lerp(kind.ScaleRange.x, kind.ScaleRange.y, item.Scale01);
            go.transform.localScale = new Vector3(scale, scale, 1f);
            go.transform.position = _grid.CellCenterToWorld(item.Cell);
            go.name = kind.Id + " " + item.Cell;

            if (kind.Raised && _depthSort != null)
            {
                // Keyed on the bottom of what is actually drawn: decor owns no footprint to measure.
                _depthSort.Register(renderer, renderer.bounds.min.y, SortingBands.SubSprite);
            }
            else
            {
                renderer.sortingOrder = SortingBands.FlatVegetation;
            }

            return go;
        }

        /// <summary>
        /// Back to the pool, and off the ladder. Unregistering is not optional: the ladder re-applies
        /// a rank to everything it holds on every re-anchoring, so a pooled object still on it would be
        /// ranked while invisible - and would be registered a second time when reused.
        /// </summary>
        void Recycle(GameObject go)
        {
            if (go == null) return;

            var renderer = go.GetComponent<SpriteRenderer>();
            if (renderer != null) _depthSort?.Unregister(renderer);

            if (_pool.Count >= _settings.PoolSize)
            {
                DestroyObject(go);
                return;
            }

            go.SetActive(false);
            _pool.Push(go);
        }

        /// <summary>Destroy is a no-op until the end of the frame and is refused outside Play Mode; an EditMode test driving this component needs the immediate form. Same split ConstructionSiteVisualSync already makes.</summary>
        static void DestroyObject(GameObject go)
        {
            if (go == null) return;

            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        GameObject NewObject()
        {
            var go = new GameObject("Decor");
            go.transform.SetParent(transform, false);
            go.AddComponent<SpriteRenderer>();
            return go;
        }

        /// <summary>
        /// Clears everything, pool included. What a caller uses when the world underneath changes -
        /// a new game, a load - so that nothing survives from the previous one.
        /// </summary>
        public void Clear()
        {
            foreach (var entry in _live)
            {
                foreach (GameObject go in entry.Value)
                {
                    if (go == null) continue;
                    var renderer = go.GetComponent<SpriteRenderer>();
                    if (renderer != null) _depthSort?.Unregister(renderer);
                    DestroyObject(go);
                }
            }

            _live.Clear();

            while (_pool.Count > 0) DestroyObject(_pool.Pop());

            _anchored = false;
        }

        /// <summary>
        /// Forgets one cell's decor immediately, without waiting for the window to move - what a
        /// building placement calls after clearing the ground. The runtime records the removal; this
        /// takes the object off the screen.
        /// </summary>
        public void ForgetCell(GridCoord cell)
        {
            if (_decor == null) return;

            int chunkIndex = cell.Y / _decor.ChunkSizeCells * _decor.ChunksPerAxis + cell.X / _decor.ChunkSizeCells;
            if (!_live.TryGetValue(chunkIndex, out List<GameObject> spawned)) return;

            Vector3 target = _grid.CellCenterToWorld(cell);
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                GameObject go = spawned[i];
                if (go == null || (go.transform.position - target).sqrMagnitude > 0.01f) continue;

                Recycle(go);
                spawned.RemoveAt(i);
            }
        }
    }
}
