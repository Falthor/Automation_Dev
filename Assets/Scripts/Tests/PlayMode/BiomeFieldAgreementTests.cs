using System.Collections.Generic;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.PlayMode
{
    /// <summary>
    /// <see cref="BiomeField"/> must keep agreeing with Custom/ShadedGroundTiled, and only a GPU can
    /// say whether it does - hence PlayMode, a real render and a readback.
    ///
    /// <b>Why this needs a permanent test rather than the one-off measurement that established it.</b>
    /// The day someone edits the ground shader's noise, nothing errors: decor simply starts appearing
    /// in the wrong biome, gradually, on a map nobody inspects cell by cell. Invisible, late, and hard
    /// to trace back to its cause - the exact profile of a defect that needs a guard rather than
    /// vigilance.
    ///
    /// <b>A tolerance, not zero.</b> The two are expected to disagree on the cells that sit exactly on
    /// a band boundary, where a last-bit difference tips the comparison; that rate depends on the GPU,
    /// the driver and the sample. Demanding zero would make this fail for reasons that are not the one
    /// it exists to catch. The measured rate is about 1 in 3 000; the threshold is set thirty times
    /// looser, which still catches everything that matters - a shader change decorrelates the two
    /// entirely and lands near 50 %, and even a subtle one lands in the percents.
    /// </summary>
    public sealed class BiomeFieldAgreementTests
    {
        /// <summary>Measured at 0.03 %. Set far looser so a boundary cell or a driver difference cannot fail this, while a decorrelated field (~50 %) or a subtly changed one (percents) still does.</summary>
        const float MaxDisagreementRatio = 0.01f;

        const int Pixels = 512;
        const float RegionSize = 400f;

        /// <summary>Far from the origin on purpose: this is where float32 runs out of digits, and where the earlier CPU port was found to diverge.</summary>
        static readonly Vector2 RegionMin = new Vector2(4800f, 4800f);

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

        [Test]
        public void TheCpuPortAgreesWithTheShader()
        {
            const float CellSize = 40f;
            const float Seed = 6426f;

            Shader shader = Shader.Find("Custom/ShadedGroundTiled");
            Assert.IsNotNull(shader, "Custom/ShadedGroundTiled not found - Shader.Find is fine here, this is an editor-side test (docs/BUILD.md).");

            // Base textures replaced by flat red and green, so a pixel's colour IS the band the GPU
            // chose. Accents off, relief off, edge softness at its minimum: no blending to misread.
            var material = new Material(shader);
            material.SetTexture("_BiomeTex0", Solid(Color.red));
            material.SetTexture("_BiomeTex1", Solid(Color.green));
            material.SetFloat("_BiomeTexCount", 2f);
            material.SetFloat("_BiomeWeight0", 1f);
            material.SetFloat("_BiomeWeight1", 1f);
            material.SetFloat("_BiomeCellSize", CellSize);
            material.SetFloat("_BiomeSeed", Seed);
            material.SetFloat("_BiomeEdgeSoftness", 0.0001f);
            material.SetFloat("_AccentTexCount", 0f);
            material.SetFloat("_ReliefAmbient", 1f);
            material.SetFloat("_ReliefLightIntensity", 0f);
            material.SetVector("_VariationOrigin", Vector4.zero);
            material.SetVector("_TextureWorldSize", new Vector4(22f, 22f, 0f, 0f));
            _spawned.Add(material);

            Texture2D rendered = RenderRegion(material);

            var field = new BiomeField(Vector2.zero, CellSize, Seed, new[] { 1f, 1f, 0f }, 2);

            int compared = 0;
            int disagreements = 0;
            int unreadable = 0;

            for (int py = 0; py < Pixels; py += 2)
            {
                for (int px = 0; px < Pixels; px += 2)
                {
                    Color c = rendered.GetPixel(px, py);

                    int gpuBand;
                    if (c.r > 0.6f && c.g < 0.4f) gpuBand = 0;
                    else if (c.g > 0.6f && c.r < 0.4f) gpuBand = 1;
                    else { unreadable++; continue; }

                    var world = new Vector2(
                        RegionMin.x + (px + 0.5f) / Pixels * RegionSize,
                        RegionMin.y + (py + 0.5f) / Pixels * RegionSize);

                    compared++;
                    if (field.BandAt(world) != gpuBand) disagreements++;
                }
            }

            Object.DestroyImmediate(rendered);

            Assert.Greater(compared, 10000, "too few readable samples to conclude anything");
            Assert.Less(unreadable, compared / 10, "most of the render was neither band - the debug material is not set up as assumed");

            float ratio = disagreements / (float)compared;
            Assert.Less(ratio, MaxDisagreementRatio,
                $"BiomeField disagrees with the shader on {disagreements} of {compared} samples ({ratio:P2}). "
                + "Either the ground shader's noise changed and the port has to follow, or the port was "
                + "'cleaned up' - most likely into double, which measures 43 % because the hash is chaotic "
                + "rather than imprecise. See BiomeField's own summary.");
        }

        /// <summary>
        /// The port must stay in float. Asserted through its observable consequence rather than by
        /// reading the source: at a distance from the origin, a double-width evaluation of the same
        /// formula lands somewhere else entirely, so a port that still agrees with the shader here
        /// cannot have been widened.
        /// </summary>
        [Test]
        public void ADoubleWidthEvaluationWouldDisagree_WhichIsWhyThePortIsFloat()
        {
            const float CellSize = 40f;
            const float Seed = 6426f;

            var field = new BiomeField(Vector2.zero, CellSize, Seed, new[] { 1f, 1f, 0f }, 2);

            int differences = 0;
            const int Samples = 4000;

            for (int i = 0; i < Samples; i++)
            {
                var world = new Vector2(4800f + i * 0.1f, 5200f - i * 0.07f);
                if (field.FieldAt(world) != (float)DoubleField(world, CellSize, Seed)) differences++;
            }

            Assert.Greater(differences, Samples / 4,
                "a double evaluation now matches the float one, which means the port has been widened - "
                + "it will have stopped agreeing with the GPU");
        }

        static double DoubleField(Vector2 world, double cellSize, double seed)
        {
            double px = world.x / cellSize + seed;
            double py = world.y / cellSize + (-seed * 1.37);
            return DoubleNoise(px, py);
        }

        static double DoubleFrac(double v) => v - System.Math.Floor(v);

        static double DoubleHash(double px, double py)
        {
            double a = DoubleFrac(px * 0.1031), b = DoubleFrac(py * 0.1031), c = DoubleFrac(px * 0.1031);
            double d = a * (b + 33.33) + b * (c + 33.33) + c * (a + 33.33);
            a += d; b += d; c += d;
            return DoubleFrac((a + b) * c);
        }

        static double DoubleNoise(double px, double py)
        {
            double ix = System.Math.Floor(px), iy = System.Math.Floor(py);
            double fx = px - ix, fy = py - iy;
            double a = DoubleHash(ix, iy), b = DoubleHash(ix + 1, iy), c = DoubleHash(ix, iy + 1), e = DoubleHash(ix + 1, iy + 1);
            double ux = fx * fx * (3.0 - 2.0 * fx), uy = fy * fy * (3.0 - 2.0 * fy);
            return a + (b - a) * ux + (c - a) * uy * (1.0 - ux) + (e - b) * ux * uy;
        }

        Texture2D RenderRegion(Material material)
        {
            var quad = new GameObject("__BiomeAgreementQuad");
            _spawned.Add(quad);
            quad.transform.position = new Vector3(RegionMin.x + RegionSize * 0.5f, RegionMin.y + RegionSize * 0.5f, 0f);
            quad.transform.localScale = new Vector3(RegionSize, RegionSize, 1f);

            var renderer = quad.AddComponent<SpriteRenderer>();
            renderer.sprite = UnitSprite();
            renderer.sharedMaterial = material;

            var cameraGo = new GameObject("__BiomeAgreementCamera");
            _spawned.Add(cameraGo);
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = RegionSize * 0.5f;
            camera.transform.position = new Vector3(RegionMin.x + RegionSize * 0.5f, RegionMin.y + RegionSize * 0.5f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            RenderTexture target = RenderTexture.GetTemporary(Pixels, Pixels, 24);
            camera.targetTexture = target;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var readback = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, Pixels, Pixels), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;

            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);

            return readback;
        }

        Sprite UnitSprite()
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            _spawned.Add(texture);

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _spawned.Add(sprite);
            return sprite;
        }

        Texture2D Solid(Color color)
        {
            var texture = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            _spawned.Add(texture);
            return texture;
        }
    }
}
