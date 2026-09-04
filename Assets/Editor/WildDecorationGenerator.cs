using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.Presentation;
using Game.Grid;
using Game.Core;
using Game.Gameplay.Buildings;

/// <summary>
/// Play Mode only: every 0.3s, destroys any wild-decoration sprite whose grid cell has become
/// occupied by a real BuildingRuntime. Installed automatically by WildDecorationGenerator.
/// </summary>
public sealed class WildDecorationConstructionCleanup : MonoBehaviour
{
    static readonly string[] GroupNames =
    {
        "BrownAsteriskWildTest", "DesertClearBushWildTest", "DesertFlowerWildTest", "DesertBushWildTest",
        "DesertTreeWildTest", "DesertSmallRockWildTest", "DesertLargeRockWildTest", "DesertBigRockWildTest"
    };

    GameRuntime _gameRuntime;
    float _timer;

    void Update()
    {
        if (!Application.isPlaying) return;
        _timer += Time.unscaledDeltaTime;
        if (_timer < 0.3f) return;
        _timer = 0f;

        if (_gameRuntime == null)
        {
            _gameRuntime = Object.FindFirstObjectByType<GameRuntime>();
            if (_gameRuntime == null || _gameRuntime.Grid == null) return;
        }

        GridRuntime grid = _gameRuntime.Grid;
        foreach (string groupName in GroupNames)
        {
            GameObject group = GameObject.Find(groupName);
            if (group == null) continue;

            var renderers = group.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Transform t = renderers[i].transform;
                if (t == null) continue;
                GridCoord cell = grid.WorldToCell(t.position);
                if (grid.GetOccupant(cell) is BuildingRuntime) Object.Destroy(t.gameObject);
            }
        }
    }
}

/// <summary>
/// Test/preview-only wild vegetation & rock scattering for the desert ground biomes.
/// Not part of the accepted game architecture - purely a live Play Mode visual test tool,
/// requested explicitly so its rules survive a Play Mode restart (e.g. to compare a new
/// terrain seed) without having to be reconstructed by hand each time.
///
/// Biome classification is NOT a hand-ported reimplementation of the ShadedGroundTiled noise
/// math anymore - an earlier version tried that and was confirmed (by rendering a debug-color
/// clone of the real material and comparing GPU vs CPU output for the exact same world point)
/// to diverge from the actual shader at large seed values, because the shader's seed-scaled
/// noise-coordinate offsets (seed*1.91+500, etc.) push the noise input into the thousands, where
/// CPU (System.Single) and GPU (HLSL float) floor()/frac() rounding can disagree by one lattice
/// cell - and adjacent cells hash to uncorrelated values, so a tiny precision difference flips
/// the classification outright. Fix: render the REAL ground material (cloned, with its three
/// ground textures swapped for solid red/green/blue and relief lighting disabled) once per
/// regenerate into an offscreen bitmap, and look up each candidate position's classification
/// directly from that bitmap. This is exactly what the GPU actually decided, by construction -
/// no reimplementation, no precision mismatch possible.
///
/// Requires Play Mode: needs the live Ground material (Custom/ShadedGroundTiled) and live ore
/// deposits off GameRuntime.World, since both only exist once TerrainView/WorldGenerator have
/// run at Play Mode start. Re-running "Regenerate All" after changing the terrain seed
/// (GroundTextureProfile) and re-entering Play Mode reproduces the same rules against the new
/// biome layout.
///
/// Deliberately excludes BrownAsteriskWildTest: its source folder (Assets/Art/Wild/brown-asterisk)
/// was removed from disk, so any existing instances are left untouched, not regenerated.
/// Also excludes Desert-Brown-bush: imported but no placement rule has been defined for it yet.
/// </summary>
public static class WildDecorationGenerator
{
    const float ReduceFactor = 0.6f; // -40% size pass, baked in
    const float WorldMargin = 10f;
    const float BiomeMapPixelsPerUnit = 4f;

    [MenuItem("Tools/Wild Decoration/Regenerate All")]
    public static void RegenerateAll()
    {
        if (!Application.isPlaying)
        {
            Debug.LogError("WildDecorationGenerator requires Play Mode (needs live Ground material + WorldGenerator deposits).");
            return;
        }

        var gameRuntime = Object.FindFirstObjectByType<GameRuntime>();
        if (gameRuntime == null || gameRuntime.World == null || gameRuntime.Grid == null)
        {
            Debug.LogError("GameRuntime/World/Grid not ready.");
            return;
        }

        GameObject terrain = GameObject.Find("Terrain");
        Transform groundTransform = terrain != null ? terrain.transform.Find("Ground") : null;
        if (groundTransform == null)
        {
            Debug.LogError("Terrain/Ground not found - has TerrainView.Initialize run yet?");
            return;
        }

        var groundRenderer = groundTransform.GetComponent<SpriteRenderer>();
        float worldSize = gameRuntime.Terrain.Size * gameRuntime.Grid.CellSize;
        float min = WorldMargin, max = worldSize - WorldMargin;

        BiomeMap biomeMap = BuildBiomeMap(groundRenderer, worldSize);

        var depositRects = new List<Rect>();
        foreach (var dep in gameRuntime.World.OreDeposits)
            depositRects.Add(new Rect(dep.Origin.X, dep.Origin.Y, dep.Definition.FootprintSize.x, dep.Definition.FootprintSize.y));

        bool InDeposit(Vector2 pos)
        {
            foreach (var r in depositRects) if (r.Contains(pos)) return true;
            return false;
        }

        DestroyIfExists("DesertClearBushWildTest");
        DestroyIfExists("DesertFlowerWildTest");
        DestroyIfExists("DesertBushWildTest");
        DestroyIfExists("DesertTreeWildTest");
        DestroyIfExists("DesertSmallRockWildTest");
        DestroyIfExists("DesertLargeRockWildTest");
        DestroyIfExists("DesertBigRockWildTest");

        var smallRockSprites = LoadSprites("Assets/Art/Wild/Deser-Small-Rock", "desert-small_rock_0", 1, 9);
        var largeRockSprites = LoadSprites("Assets/Art/Wild/Desert-Large-Rock", "desert-large-rock_0", 1, 5);
        var bigRockSprites = LoadSprites("Assets/Art/Wild/Desert-big-rock", "desert-big-rock_0", 1, 8);
        var clearBushSprites = LoadSpritesById("Assets/Art/Wild/desert-clear-bush", "desert_clear_bush_", new[] { "01", "02", "04", "05", "06", "07", "08" });
        var bushSprites = LoadSpritesById("Assets/Art/Wild/Desert-bush", "desert_bush_", new[] { "02", "03", "04", "05", "06", "07" });
        var treeSprites = LoadSprites("Assets/Art/Wild/Desert-Tree", "dessert_wood_0", 1, 8);
        var flowerGravel02Sprites = LoadSpritesById("Assets/Art/Wild/Desert-Flower", "Desert-flower_", new[] { "01", "02", "03", "04", "05", "10", "11", "12", "13", "14", "17" });
        var flowerMarsGravel04Sprites = LoadSpritesById("Assets/Art/Wild/Desert-Flower", "Desert-flower_", new[] { "06", "07", "08", "09", "15", "16" });

        var smallRockGroup = new GameObject("DesertSmallRockWildTest");
        var largeRockGroup = new GameObject("DesertLargeRockWildTest");

        GenerateFlowers(biomeMap, min, max, InDeposit, flowerGravel02Sprites, flowerMarsGravel04Sprites);
        GenerateClearBush(biomeMap, min, max, InDeposit, clearBushSprites, smallRockSprites, largeRockSprites);
        GenerateBush(biomeMap, min, max, InDeposit, bushSprites, largeRockSprites);
        GenerateTrees(biomeMap, min, max, InDeposit, treeSprites);
        GenerateSmallRockOutcrops(smallRockGroup, biomeMap, min, max, InDeposit, smallRockSprites);
        GenerateLargeRocks(largeRockGroup, smallRockGroup, biomeMap, min, max, InDeposit, largeRockSprites, smallRockSprites);
        GenerateBigRocks(min, max, InDeposit, bigRockSprites);

        InstallCleanupWatcher();
        biomeMap.Dispose();

        Debug.Log("WildDecorationGenerator: all 7 groups regenerated.");
    }

    static void InstallCleanupWatcher()
    {
        if (GameObject.Find("WildDecorationCleanupWatcher") != null) return;
        var watcher = new GameObject("WildDecorationCleanupWatcher");
        watcher.AddComponent<WildDecorationConstructionCleanup>();
    }

    /// <summary>
    /// An offscreen render of the real ground material with its 3 textures swapped for pure
    /// red (Mars) / green (SoilGravel04) / blue (SoilGravel02) and relief lighting disabled, so
    /// every pixel unambiguously encodes which layer the real shader picked at that world point.
    /// </summary>
    sealed class BiomeMap
    {
        public Texture2D Texture;
        public float PixelsPerUnit;
        public Color MarsGroundColor;
        public Color Gravel04GroundColor;
        public Color DarkerGroundColor;

        public string Classify(Vector2 world)
        {
            int px = Mathf.Clamp(Mathf.RoundToInt(world.x * PixelsPerUnit), 0, Texture.width - 1);
            int py = Mathf.Clamp(Mathf.RoundToInt(world.y * PixelsPerUnit), 0, Texture.height - 1);
            Color c = Texture.GetPixel(px, py);
            if (c.r >= c.g && c.r >= c.b) return "Mars";
            if (c.g >= c.b) return "SoilGravel04";
            return "SoilGravel02";
        }

        public void Dispose()
        {
            if (Texture != null) Object.DestroyImmediate(Texture);
        }
    }

    static BiomeMap BuildBiomeMap(SpriteRenderer groundRenderer, float worldSize)
    {
        Material realMat = groundRenderer.sharedMaterial;
        var debugMat = new Material(realMat);
        debugMat.SetTexture("_BiomeTex0", SolidTex(Color.red));
        debugMat.SetTexture("_BiomeTex1", SolidTex(Color.green));
        debugMat.SetTexture("_AccentTex0", SolidTex(Color.blue));
        debugMat.SetFloat("_ReliefAmbient", 1f);
        debugMat.SetFloat("_ReliefLightIntensity", 0f);

        var debugGo = new GameObject("__BiomeMapDebugQuad");
        debugGo.transform.position = groundRenderer.transform.position;
        debugGo.transform.localScale = groundRenderer.transform.localScale;
        var debugRenderer = debugGo.AddComponent<SpriteRenderer>();
        debugRenderer.sprite = groundRenderer.sprite;
        debugRenderer.sharedMaterial = debugMat;
        debugRenderer.sortingOrder = 10000;

        int texSize = Mathf.RoundToInt(worldSize * BiomeMapPixelsPerUnit);
        var camGo = new GameObject("__BiomeMapCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = worldSize * 0.5f;
        cam.transform.position = new Vector3(worldSize * 0.5f, worldSize * 0.5f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = ~0;

        var rt = RenderTexture.GetTemporary(texSize, texSize, 24);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, texSize, texSize), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(debugGo);
        Object.DestroyImmediate(debugMat);

        Color marsGround = AverageTextureColor((Texture2D)realMat.GetTexture("_BiomeTex0"));
        Color gravel04Ground = AverageTextureColor((Texture2D)realMat.GetTexture("_BiomeTex1"));
        float marsLuma = marsGround.r + marsGround.g + marsGround.b;
        float gravel04Luma = gravel04Ground.r + gravel04Ground.g + gravel04Ground.b;
        Color darker = gravel04Luma <= marsLuma ? gravel04Ground : marsGround;

        return new BiomeMap
        {
            Texture = tex,
            PixelsPerUnit = BiomeMapPixelsPerUnit,
            MarsGroundColor = marsGround,
            Gravel04GroundColor = gravel04Ground,
            DarkerGroundColor = darker
        };
    }

    static Color AverageTextureColor(Texture2D source)
    {
        var rt = RenderTexture.GetTemporary(32, 32);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color[] pixels = readable.GetPixels();
        float r = 0, g = 0, b = 0;
        foreach (var p in pixels) { r += p.r; g += p.g; b += p.b; }
        int n = pixels.Length;
        Object.DestroyImmediate(readable);
        return new Color(r / n, g / n, b / n, 1f);
    }

    static Texture2D SolidTex(Color c)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.SetPixels(new[] { c, c, c, c });
        t.Apply();
        return t;
    }

    static void DestroyIfExists(string name)
    {
        GameObject g = GameObject.Find(name);
        if (g != null) Object.Destroy(g);
    }

    static List<Sprite> LoadSprites(string folder, string prefix, int from, int to)
    {
        var list = new List<Sprite>();
        for (int i = from; i <= to; i++)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/" + prefix + i + ".png");
            if (s != null) list.Add(s);
        }
        return list;
    }

    static List<Sprite> LoadSpritesById(string folder, string prefix, string[] ids)
    {
        var list = new List<Sprite>();
        foreach (string id in ids)
        {
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/" + prefix + id + ".png");
            if (s != null) list.Add(s);
        }
        return list;
    }

    static List<T> Shuffled<T>(List<T> src, System.Random rng)
    {
        var list = new List<T>(src);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    static readonly Dictionary<Sprite, Color> _rockAverageColorCache = new Dictionary<Sprite, Color>();

    static Color RockAverageColor(Sprite sprite)
    {
        if (_rockAverageColorCache.TryGetValue(sprite, out Color cached)) return cached;

        // sprite.texture is import-time non-readable (no Read/Write Enabled) - GetPixel would
        // throw. Blit through a RenderTexture instead, which works regardless of that setting.
        Texture2D source = sprite.texture;
        var rt = RenderTexture.GetTemporary(source.width, source.height);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Rect r = sprite.textureRect;
        int x0 = Mathf.FloorToInt(r.x), y0 = Mathf.FloorToInt(r.y);
        int w = Mathf.FloorToInt(r.width), h = Mathf.FloorToInt(r.height);
        int step = Mathf.Max(1, Mathf.Min(w, h) / 48);

        float sr = 0, sg = 0, sb = 0; int n = 0;
        for (int y = y0; y < y0 + h; y += step)
        {
            for (int x = x0; x < x0 + w; x += step)
            {
                Color p = readable.GetPixel(x, y);
                if (p.a < 0.5f) continue;
                sr += p.r; sg += p.g; sb += p.b; n++;
            }
        }
        Object.DestroyImmediate(readable);

        Color avg = n > 0 ? new Color(sr / n, sg / n, sb / n, 1f) : Color.white;
        _rockAverageColorCache[sprite] = avg;
        return avg;
    }

    /// <summary>
    /// Calibrated per-sprite: multiplier = target ground color / this sprite's own raw average
    /// color, capped at 1.0 per channel (SpriteRenderer.color can only darken a texture, never
    /// brighten past its own raw value without clipping to white - an uncapped version briefly
    /// tried multipliers above 4 on a sprite whose raw blue channel was very low, blowing that
    /// channel out and making the rock look bleached instead of uniformly muted). Channels
    /// already at or below the target are left alone; only channels above it get pulled down
    /// toward it, which is what actually reads as "the same muted tone" across every rock file.
    /// </summary>
    static Color RockTint(BiomeMap biomeMap, Sprite sprite)
    {
        Color target = biomeMap.DarkerGroundColor;
        Color raw = RockAverageColor(sprite);
        float mr = Mathf.Min(1f, target.r / Mathf.Max(raw.r, 0.02f));
        float mg = Mathf.Min(1f, target.g / Mathf.Max(raw.g, 0.02f));
        float mb = Mathf.Min(1f, target.b / Mathf.Max(raw.b, 0.02f));
        return new Color(mr, mg, mb, 1f);
    }

    static GameObject Spawn(Transform parent, string name, Vector2 pos, float scale, Sprite sprite, int sortingOrder, Color? color = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = sortingOrder;
        if (color.HasValue) sr.color = color.Value;
        return go;
    }

    static void GenerateFlowers(BiomeMap biome, float min, float max, System.Func<Vector2, bool> inDeposit, List<Sprite> gravel02Sprites, List<Sprite> marsGravel04Sprites)
    {
        var group = new GameObject("DesertFlowerWildTest");
        var rng = new System.Random(20260904);

        int placedG02 = 0, attempts = 0;
        while (placedG02 < 800 && attempts < 200000)
        {
            attempts++;
            Vector2 pos = RandomPos(rng, min, max);
            if (biome.Classify(pos) != "SoilGravel02" || inDeposit(pos)) continue;
            Spawn(group.transform, "Flower_Gravel02_" + placedG02.ToString("000"), pos, RandRange(rng, 0.8f, 1.3f) * ReduceFactor, gravel02Sprites[rng.Next(gravel02Sprites.Count)], 5);
            placedG02++;
        }

        int anchorsPlaced = 0, anchorAttempts = 0, clustersMade = 0, isolatedMade = 0;
        while (anchorsPlaced < 65 && anchorAttempts < 100000)
        {
            anchorAttempts++;
            Vector2 anchor = RandomPos(rng, min, max);
            string b = biome.Classify(anchor);
            if ((b != "Mars" && b != "SoilGravel04") || inDeposit(anchor)) continue;
            anchorsPlaced++;

            if (rng.NextDouble() >= 0.92)
            {
                isolatedMade++;
                Spawn(group.transform, "Flower_MarsGravel04_Isolated_" + anchorsPlaced.ToString("000"), anchor, RandRange(rng, 0.8f, 1.3f) * ReduceFactor, marsGravel04Sprites[rng.Next(marsGravel04Sprites.Count)], 5);
                continue;
            }

            clustersMade++;
            int memberCount = 3 + rng.Next(4);
            var order = Shuffled(marsGravel04Sprites, rng);
            for (int m = 0; m < memberCount; m++)
            {
                Vector2 mpos = anchor + RandomOffsetInDisc(rng, 1.2f);
                if (inDeposit(mpos)) continue;
                Spawn(group.transform, "Flower_MarsGravel04_Bosquet" + clustersMade.ToString("000") + "_" + m.ToString("00"), mpos, RandRange(rng, 0.8f, 1.3f) * ReduceFactor, order[m % order.Count], 5);
            }
        }
    }

    static void GenerateClearBush(BiomeMap biome, float min, float max, System.Func<Vector2, bool> inDeposit, List<Sprite> clearBushSprites, List<Sprite> smallRockSprites, List<Sprite> largeRockSprites)
    {
        var group = new GameObject("DesertClearBushWildTest");
        var rng = new System.Random(77201);
        int anchorsPlaced = 0, attempts = 0;

        while (anchorsPlaced < 80 && attempts < 100000)
        {
            attempts++;
            Vector2 anchor = RandomPos(rng, min, max);
            string b = biome.Classify(anchor);
            if ((b != "Mars" && b != "SoilGravel04") || inDeposit(anchor)) continue;
            anchorsPlaced++;

            if (rng.NextDouble() >= 0.82)
            {
                Spawn(group.transform, "ClearBush_Isolated_" + anchorsPlaced.ToString("000"), anchor, RandRange(rng, 0.8f, 1.3f) * ReduceFactor, clearBushSprites[rng.Next(clearBushSprites.Count)], 5);
                continue;
            }

            int memberCount = 3 + rng.Next(8);
            var order = Shuffled(clearBushSprites, rng);
            for (int m = 0; m < memberCount; m++)
            {
                Vector2 mpos = anchor + RandomOffsetInDisc(rng, 2.0f);
                if (inDeposit(mpos)) continue;
                Spawn(group.transform, "ClearBush_Cluster" + anchorsPlaced.ToString("000") + "_" + m.ToString("00"), mpos, RandRange(rng, 0.8f, 1.3f) * ReduceFactor, order[m % order.Count], 5);
            }

            if (smallRockSprites.Count > 0 && rng.NextDouble() < 0.55)
            {
                int rockCount = 1 + rng.Next(2);
                var rockOrder = Shuffled(smallRockSprites, rng);
                for (int r = 0; r < rockCount; r++)
                {
                    Vector2 rpos = anchor + RandomOffsetInDisc(rng, 2.0f, 0.5f);
                    if (inDeposit(rpos)) continue;
                    Sprite rockSprite = rockOrder[r % rockOrder.Count];
                    Spawn(group.transform, "ClearBush_" + anchorsPlaced.ToString("000") + "_SmallRock" + r, rpos, RandRange(rng, 0.8f, 1.2f) * ReduceFactor, rockSprite, 7, RockTint(biome, rockSprite));
                }
            }

            if (largeRockSprites.Count > 0 && rng.NextDouble() < 0.30)
            {
                Vector2 rpos = anchor + RandomOffsetInDisc(rng, 2.0f, 0.5f);
                if (!inDeposit(rpos))
                {
                    Sprite rockSprite = largeRockSprites[rng.Next(largeRockSprites.Count)];
                    Spawn(group.transform, "ClearBush_" + anchorsPlaced.ToString("000") + "_LargeRock", rpos, RandRange(rng, 0.85f, 1.2f) * ReduceFactor, rockSprite, 7, RockTint(biome, rockSprite));
                }
            }
        }
    }

    static void GenerateBush(BiomeMap biome, float min, float max, System.Func<Vector2, bool> inDeposit, List<Sprite> bushSprites, List<Sprite> largeRockSprites)
    {
        var group = new GameObject("DesertBushWildTest");
        var rng = new System.Random(55901);
        var centers = new List<Vector2>();
        int bosquetsMade = 0, attempts = 0, totalSprites = 0;

        while (bosquetsMade < 20 && attempts < 50000)
        {
            attempts++;
            Vector2 anchor = RandomPos(rng, min, max);
            string b = biome.Classify(anchor);
            if ((b != "SoilGravel02" && b != "SoilGravel04") || inDeposit(anchor)) continue;

            bool tooClose = false;
            foreach (var c in centers) if (Vector2.Distance(c, anchor) < 8f) { tooClose = true; break; }
            if (tooClose) continue;
            centers.Add(anchor); bosquetsMade++;

            float radius = RandRange(rng, 3.0f, 5.0f);
            int memberCount = 18 + rng.Next(28);
            var order = Shuffled(bushSprites, rng);
            for (int m = 0; m < memberCount; m++)
            {
                Vector2 mpos = anchor + RandomOffsetInDisc(rng, radius);
                if (inDeposit(mpos)) continue;
                Spawn(group.transform, "DesertBush_Bosquet" + bosquetsMade.ToString("000") + "_" + m.ToString("00"), mpos, RandRange(rng, 0.8f, 1.4f) * ReduceFactor, order[m % order.Count], 5);
                totalSprites++;
            }

            if (largeRockSprites.Count > 0 && rng.NextDouble() < 0.30)
            {
                Vector2 rpos = anchor + RandomOffsetInDisc(rng, radius * 0.6f);
                if (!inDeposit(rpos))
                {
                    Sprite rockSprite = largeRockSprites[rng.Next(largeRockSprites.Count)];
                    Spawn(group.transform, "DesertBush_Bosquet" + bosquetsMade.ToString("000") + "_LargeRock", rpos, RandRange(rng, 0.85f, 1.2f) * ReduceFactor, rockSprite, 7, RockTint(biome, rockSprite));
                    totalSprites++;
                }
            }
        }

        int isolatedTarget = Mathf.RoundToInt(totalSprites * 0.15f);
        int isolatedMade = 0, isoAttempts = 0;
        while (isolatedMade < isolatedTarget && isoAttempts < 50000)
        {
            isoAttempts++;
            Vector2 pos = RandomPos(rng, min, max);
            string b = biome.Classify(pos);
            if ((b != "SoilGravel02" && b != "SoilGravel04") || inDeposit(pos)) continue;
            Spawn(group.transform, "DesertBush_Isolated_" + isolatedMade.ToString("000"), pos, RandRange(rng, 0.8f, 1.3f) * ReduceFactor, bushSprites[rng.Next(bushSprites.Count)], 5);
            isolatedMade++;
        }
    }

    static void GenerateTrees(BiomeMap biome, float min, float max, System.Func<Vector2, bool> inDeposit, List<Sprite> treeSprites)
    {
        var group = new GameObject("DesertTreeWildTest");
        var rng = new System.Random(31337);
        int placed = 0, attempts = 0;
        while (placed < 45 && attempts < 50000)
        {
            attempts++;
            Vector2 pos = RandomPos(rng, min, max);
            if (biome.Classify(pos) == "SoilGravel02" || inDeposit(pos)) continue;
            Spawn(group.transform, "Tree_" + placed.ToString("000"), pos, RandRange(rng, 1.0f, 1.5f) * ReduceFactor, treeSprites[rng.Next(treeSprites.Count)], 5);
            placed++;
        }
    }

    static void GenerateSmallRockOutcrops(GameObject group, BiomeMap biome, float min, float max, System.Func<Vector2, bool> inDeposit, List<Sprite> smallRockSprites)
    {
        var rng = new System.Random(90210);
        int outcropsMade = 0, attempts = 0;
        while (outcropsMade < 45 && attempts < 50000)
        {
            attempts++;
            Vector2 anchor = RandomPos(rng, min, max);
            if (inDeposit(anchor)) continue;
            outcropsMade++;
            int memberCount = 2 + rng.Next(3);
            var order = Shuffled(smallRockSprites, rng);
            int useCount = Mathf.Min(memberCount, order.Count);
            for (int m = 0; m < useCount; m++)
            {
                Vector2 mpos = anchor + RandomOffsetInDisc(rng, 1.2f);
                if (inDeposit(mpos)) continue;
                Sprite rockSprite = order[m];
                Spawn(group.transform, "SmallRock_Outcrop" + outcropsMade.ToString("000") + "_" + m, mpos, RandRange(rng, 0.8f, 1.3f) * ReduceFactor, rockSprite, 7, RockTint(biome, rockSprite));
            }
        }
    }

    static void GenerateLargeRocks(GameObject largeRockGroup, GameObject smallRockGroup, BiomeMap biome, float min, float max, System.Func<Vector2, bool> inDeposit, List<Sprite> largeRockSprites, List<Sprite> smallRockSprites)
    {
        var rng = new System.Random(41209);
        int placed = 0, attempts = 0;
        while (placed < 60 && attempts < 50000)
        {
            attempts++;
            Vector2 pos = RandomPos(rng, min, max);
            if (inDeposit(pos)) continue;
            Sprite largeSprite = largeRockSprites[rng.Next(largeRockSprites.Count)];
            Spawn(largeRockGroup.transform, "LargeRock_" + placed.ToString("000"), pos, RandRange(rng, 0.85f, 1.2f) * ReduceFactor, largeSprite, 7, RockTint(biome, largeSprite));
            placed++;

            if (smallRockSprites.Count > 0)
            {
                int companions = 2 + rng.Next(3);
                for (int c = 0; c < companions; c++)
                {
                    Vector2 cpos = pos + RandomOffsetInDisc(rng, 1.8f, 0.8f);
                    if (inDeposit(cpos)) continue;
                    Sprite companionSprite = smallRockSprites[rng.Next(smallRockSprites.Count)];
                    Spawn(smallRockGroup.transform, "LargeRock_" + (placed - 1).ToString("000") + "_Companion" + c, cpos, RandRange(rng, 0.8f, 1.3f) * ReduceFactor, companionSprite, 7, RockTint(biome, companionSprite));
                }
            }
        }
    }

    static void GenerateBigRocks(float min, float max, System.Func<Vector2, bool> inDeposit, List<Sprite> bigRockSprites)
    {
        var group = new GameObject("DesertBigRockWildTest");
        var rng = new System.Random(60622);
        int placed = 0, attempts = 0;
        while (placed < 45 && attempts < 50000)
        {
            attempts++;
            Vector2 pos = RandomPos(rng, min, max);
            if (inDeposit(pos)) continue;
            Spawn(group.transform, "BigRock_" + placed.ToString("000"), pos, RandRange(rng, 0.85f, 1.2f) * ReduceFactor, bigRockSprites[rng.Next(bigRockSprites.Count)], 7);
            placed++;
        }
    }

    static Vector2 RandomPos(System.Random rng, float min, float max)
    {
        return new Vector2(min + (float)rng.NextDouble() * (max - min), min + (float)rng.NextDouble() * (max - min));
    }

    static float RandRange(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    static Vector2 RandomOffsetInDisc(System.Random rng, float radius, float minRadius = 0f)
    {
        float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
        float dist = minRadius + Mathf.Sqrt((float)rng.NextDouble()) * (radius - minRadius);
        return new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
    }
}
