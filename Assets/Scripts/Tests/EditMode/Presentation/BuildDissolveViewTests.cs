using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Sites;
using Game.Presentation;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// directive-materialisation-nano.md §10, restricted to step 1 of §11 (dissolve only - the
    /// ground coverage tests belong to step 2, which is not implemented).
    ///
    /// BuildDissolveView.Tick takes its own deltaTime precisely so the smoothing and the flash are
    /// testable here rather than needing a frame loop; nothing below asserts anything about how the
    /// shader actually renders.
    /// </summary>
    public class BuildDissolveViewTests
    {
        /// <summary>Footprint cells per second, the shipped value - a speed, so the progress rate a view actually runs at depends on how big it is.</summary>
        const float AssemblyRate = 1.8f;

        /// <summary>The gas power plant this effect was tuned on, and BuildDissolveView's own default footprint.</summary>
        const int ReferenceFootprint = 9;

        /// <summary>Progress per second for the reference building: 1.8 / 9 = 0.2. The value the whole effect was tuned at by eye.</summary>
        const float ReferenceRate = AssemblyRate / ReferenceFootprint;

        const float MinAssemblyDuration = 0.25f;
        const float FlashDuration = 0.40f;
        const float FlashIntensity = 0.28f;

        readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object spawned in _spawned)
            {
                if (spawned != null) Object.DestroyImmediate(spawned);
            }
            _spawned.Clear();
        }

        /// <summary>The asset's fields have no production setter on purpose - it is a definition, not runtime state - so tests write them the same way TestDataFactory does.</summary>
        NanoConstructionSettings NewSettings(float assemblyRate = AssemblyRate, float flashDuration = FlashDuration, float flashIntensity = FlashIntensity, float noiseScale = 6.3f)
        {
            var settings = ScriptableObject.CreateInstance<NanoConstructionSettings>();
            _spawned.Add(settings);
            SetSettings(settings, assemblyRate, flashDuration, flashIntensity, noiseScale);
            return settings;
        }

        static void SetSettings(NanoConstructionSettings settings, float assemblyRate, float flashDuration, float flashIntensity, float noiseScale)
        {
            var so = new SerializedObject(settings);
            so.FindProperty("assemblyRate").floatValue = assemblyRate;
            so.FindProperty("minAssemblyDuration").floatValue = MinAssemblyDuration;
            so.FindProperty("deliveryFlashDuration").floatValue = flashDuration;
            so.FindProperty("deliveryFlashIntensity").floatValue = flashIntensity;
            so.FindProperty("noiseScale").floatValue = noiseScale;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        BuildDissolveView NewView(NanoConstructionSettings settings)
        {
            var go = new GameObject("Building");
            _spawned.Add(go);

            var texture = new Texture2D(4, 4);
            _spawned.Add(texture);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            _spawned.Add(sprite);
            go.AddComponent<SpriteRenderer>().sprite = sprite;

            var view = go.AddComponent<BuildDissolveView>();
            view.Settings = settings;
            return view;
        }

        static float ShaderProgress(BuildDissolveView view)
        {
            var block = new MaterialPropertyBlock();
            view.GetComponent<SpriteRenderer>().GetPropertyBlock(block);
            return block.GetFloat("_Progress");
        }

        static float ShaderRimBoost(BuildDissolveView view)
        {
            var block = new MaterialPropertyBlock();
            view.GetComponent<SpriteRenderer>().GetPropertyBlock(block);
            return block.GetFloat("_RimBoost");
        }

        static Vector4 ShaderBuildBounds(BuildDissolveView view)
        {
            var block = new MaterialPropertyBlock();
            view.GetComponent<SpriteRenderer>().GetPropertyBlock(block);
            return block.GetVector("_BuildBounds");
        }

        /// <summary>
        /// The contract the reveal direction rests on. The shader normalises world Y over
        /// _BuildBounds as (worldPos.y - _BuildBounds.y) / _BuildBounds.w, so .y must be the world
        /// AABB's <b>bottom</b> edge for normalized.y to read 0 at the building's base and 1 at its
        /// top - which is what makes _RevealMode 0 grow the building out of the ground.
        ///
        /// Writing the centre or the top edge instead would invert the reveal silently, and would
        /// take the radial mode down with it, since both modes read the same normalized value. That
        /// is why this is asserted at the source rather than patched in the shader.
        /// </summary>
        [Test]
        public void BuildBounds_CarriesTheWorldAabbsBottomLeftCorner_NotItsCentre()
        {
            BuildDissolveView view = NewView(NewSettings());
            view.transform.position = new Vector3(10f, 20f, 0f);
            view.transform.localScale = new Vector3(3f, 3f, 1f);

            view.Tick(0.05f);

            Bounds bounds = view.GetComponent<SpriteRenderer>().bounds;
            Vector4 written = ShaderBuildBounds(view);

            Assert.AreEqual(bounds.min.x, written.x, 0.0001f, "x is the AABB's left edge.");
            Assert.AreEqual(bounds.min.y, written.y, 0.0001f, "y is the AABB's bottom edge - not its centre, not its top.");
            Assert.AreEqual(bounds.size.x, written.z, 0.0001f);
            Assert.AreEqual(bounds.size.y, written.w, 0.0001f);

            Assert.Less(written.y, bounds.center.y, "A centre would put the building's base at normalized.y 0.5 and clip its lower half away.");

            float atBase = (bounds.min.y - written.y) / written.w;
            float atTop = (bounds.max.y - written.y) / written.w;
            Assert.AreEqual(0f, atBase, 0.0001f, "normalized.y is 0 at the base, so the base is revealed first.");
            Assert.AreEqual(1f, atTop, 0.0001f, "and 1 at the top, so the top is revealed last.");
        }

        static void Advance(BuildDissolveView view, float seconds, float step = 0.05f)
        {
            for (float elapsed = 0f; elapsed < seconds; elapsed += step)
            {
                if (view == null) return;
                view.Tick(step);
            }
        }

        [Test]
        public void DisplayedProgress_ChasesTheTargetAtTheConfiguredRate()
        {
            BuildDissolveView view = NewView(NewSettings());
            view.TargetProgress = 1f;

            view.Tick(1f);

            Assert.AreEqual(ReferenceRate, view.DisplayedProgress, 0.0001f);
        }

        [Test]
        public void DisplayedProgress_NeverExceedsTheTarget()
        {
            BuildDissolveView view = NewView(NewSettings());
            view.TargetProgress = 0.3f;

            // Far more time than the rate needs to cover 0.3, so an unbounded chase would overshoot.
            Advance(view, 10f);

            Assert.AreEqual(0.3f, view.DisplayedProgress, 0.0001f);
        }

        /// <summary>The point of the whole design: a lot of ten components must take longer to assemble than a lot of two, instead of both snapping into place.</summary>
        [Test]
        public void AStepFromZeroToTwoThirds_SpreadsOverTheRateImpliedTime()
        {
            BuildDissolveView view = NewView(NewSettings());
            view.TargetProgress = 0.67f;

            view.Tick(0.05f);
            Assert.Less(view.DisplayedProgress, 0.05f, "A single frame must not carry the whole step.");

            float expectedSeconds = 0.67f / ReferenceRate;
            Advance(view, expectedSeconds * 0.5f);
            Assert.That(view.DisplayedProgress, Is.GreaterThan(0.25f).And.LessThan(0.42f),
                "Halfway through, roughly half the step should be covered.");

            Advance(view, expectedSeconds);
            Assert.AreEqual(0.67f, view.DisplayedProgress, 0.0001f);
        }

        [Test]
        public void ADelivery_TriggersTheFlash()
        {
            BuildDissolveView view = NewView(NewSettings());

            Assert.AreEqual(0f, view.CurrentFlashBoost(), 0.0001f);

            view.TargetProgress = 0.4f;
            view.Tick(0f);

            Assert.AreEqual(FlashDuration, view.FlashRemaining, 0.0001f);
            Assert.AreEqual(FlashIntensity, view.CurrentFlashBoost(), 0.0001f);
            Assert.AreEqual(FlashIntensity, ShaderRimBoost(view), 0.0001f);
        }

        [Test]
        public void TheFlash_FallsBackToZeroAfterItsDuration()
        {
            BuildDissolveView view = NewView(NewSettings());
            view.TargetProgress = 0.4f;
            view.Tick(0f);

            Advance(view, FlashDuration + 0.05f);

            Assert.AreEqual(0f, view.FlashRemaining, 0.0001f);
            Assert.AreEqual(0f, view.CurrentFlashBoost(), 0.0001f);
            Assert.AreEqual(0f, ShaderRimBoost(view), 0.0001f);
        }

        [Test]
        public void Removes_ItselfWhenDisplayedReachesOne_NotWhenMaterialsAreDelivered()
        {
            BuildDissolveView view = NewView(NewSettings());
            var renderer = view.GetComponent<SpriteRenderer>();

            view.TargetProgress = 1f;
            view.Tick(0.05f);

            Assert.IsTrue(view != null, "Everything is delivered, but nothing is assembled yet.");
            Assert.Less(view.DisplayedProgress, 1f);

            Advance(view, 1f / ReferenceRate + 0.2f);

            Assert.IsTrue(view == null, "The component must remove itself once the sprite is whole.");

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.AreEqual(1f, block.GetFloat("_Progress"), 0.0001f, "It must leave the sprite fully revealed.");
            Assert.AreEqual(0f, block.GetFloat("_RimBoost"), 0.0001f, "It must not leave a flash burning.");
        }

        /// <summary>
        /// The rendered result is judged by eye, not here. What is asserted is the contract that
        /// drives it: the shader receives exactly 0 and exactly 1 at the two ends, which is what
        /// makes its clip() hide everything and then nothing.
        /// </summary>
        [Test]
        public void TheShaderReceives_ExactlyZeroAtTheStartAndExactlyOneAtTheEnd()
        {
            BuildDissolveView view = NewView(NewSettings());
            var renderer = view.GetComponent<SpriteRenderer>();

            view.Tick(0f);
            Assert.AreEqual(0f, ShaderProgress(view), 0.0001f);

            view.TargetProgress = 1f;
            Advance(view, 1f / ReferenceRate + 0.2f);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.AreEqual(1f, block.GetFloat("_Progress"), 0.0001f);
        }

        /// <summary>Two buildings of the same type under construction at once must not share a progress value - the reason this writes a property block rather than the material.</summary>
        [Test]
        public void TwoBuildings_AtDifferentProgress_DoNotShareAValue()
        {
            NanoConstructionSettings settings = NewSettings();
            BuildDissolveView first = NewView(settings);
            BuildDissolveView second = NewView(settings);

            first.TargetProgress = 1f;
            second.TargetProgress = 1f;

            Advance(first, 2f);
            second.Tick(0.05f);

            Assert.Greater(Mathf.Abs(ShaderProgress(first) - ShaderProgress(second)), 0.1f,
                "Two buildings under construction must not share one progress value.");
            Assert.AreSame(first.GetComponent<SpriteRenderer>().sharedMaterial,
                second.GetComponent<SpriteRenderer>().sharedMaterial,
                "No dissolve shader assigned here, so both must still be on the same stock material - proving the values above are kept apart by the property block, not by separate materials.");
        }

        [Test]
        public void ChangingTheSettingsAsset_ReachesTheShaderProperties()
        {
            NanoConstructionSettings settings = NewSettings(noiseScale: 6.3f);
            BuildDissolveView view = NewView(settings);
            view.Tick(0.05f);

            var block = new MaterialPropertyBlock();
            view.GetComponent<SpriteRenderer>().GetPropertyBlock(block);
            Assert.AreEqual(6.3f, block.GetFloat("_NoiseScale"), 0.0001f);

            SetSettings(settings, AssemblyRate, FlashDuration, FlashIntensity, noiseScale: 11f);
            view.Tick(0.05f);

            view.GetComponent<SpriteRenderer>().GetPropertyBlock(block);
            Assert.AreEqual(11f, block.GetFloat("_NoiseScale"), 0.0001f, "One asset must retune every building.");
        }

        [Test]
        public void ChangingTheAssemblyRate_ChangesHowFastTheBuildingAssembles()
        {
            NanoConstructionSettings settings = NewSettings(assemblyRate: 4.5f);
            BuildDissolveView view = NewView(settings);
            view.TargetProgress = 1f;

            view.Tick(1f);

            Assert.AreEqual(0.5f, view.DisplayedProgress, 0.0001f, "4.5 cells/s over the 9-cell default footprint.");
        }

        /// <summary>
        /// The correction that made the setting a speed rather than a progress rate: a conveyor and
        /// a power plant used to take exactly as long, which made a ten-belt drag interminable since
        /// a drag's segments assemble in series.
        /// </summary>
        [Test]
        public void ASmallBuilding_AssemblesFasterThanABigOne_InProportionToItsFootprint()
        {
            NanoConstructionSettings settings = NewSettings();

            BuildDissolveView conveyor = NewView(settings);
            conveyor.FootprintCells = 1;
            BuildDissolveView powerPlant = NewView(settings);
            powerPlant.FootprintCells = ReferenceFootprint;

            Assert.AreEqual(AssemblyRate, conveyor.ProgressRate, 0.0001f, "One cell takes the whole rate: 0.56 s end to end.");
            Assert.AreEqual(ReferenceRate, powerPlant.ProgressRate, 0.0001f, "Nine cells: 0.2 progress/s, the value the effect was tuned at.");
            Assert.AreEqual(ReferenceFootprint, conveyor.ProgressRate / powerPlant.ProgressRate, 0.0001f, "Strictly proportional to footprint area.");

            conveyor.TargetProgress = 1f;
            powerPlant.TargetProgress = 1f;
            Advance(conveyor, 0.7f);
            Advance(powerPlant, 0.7f);

            Assert.IsTrue(conveyor == null, "0.7 s is past the conveyor's 0.56 s, so it finished and removed itself.");
            Assert.Less(powerPlant.DisplayedProgress, 0.2f, "The power plant is barely started.");
        }

        /// <summary>
        /// The floor caps the derived rate so nothing pops into existence in one frame. It does not
        /// bind at the shipped assemblyRate - a 1-cell building already takes 0.56 s - so this
        /// raises the rate until it does, which is the case the floor exists to guard.
        /// </summary>
        [Test]
        public void TheMinimumDuration_CapsHowFastAOneCellBuildingCanAssemble()
        {
            NanoConstructionSettings shipped = NewSettings();
            Assert.AreEqual(AssemblyRate, shipped.ProgressRateFor(1), 0.0001f);
            Assert.Less(shipped.ProgressRateFor(1), 1f / MinAssemblyDuration,
                "At the shipped rate the floor is inert even on the smallest possible building.");

            NanoConstructionSettings settings = NewSettings(assemblyRate: 100f);
            Assert.AreEqual(1f / MinAssemblyDuration, settings.ProgressRateFor(1), 0.0001f,
                "100 cells/s over one cell would be near-instant; the floor holds it to 0.25 s.");

            BuildDissolveView view = NewView(settings);
            view.FootprintCells = 1;
            view.TargetProgress = 1f;

            view.Tick(0.05f);

            Assert.IsTrue(view != null, "A single frame must never carry the whole assembly.");
            Assert.AreEqual(0.2f, view.DisplayedProgress, 0.0001f);
        }

        [Test]
        public void TheFootprint_IsClampedToAtLeastOneCell()
        {
            BuildDissolveView view = NewView(NewSettings());

            view.FootprintCells = 0;

            Assert.AreEqual(1, view.FootprintCells, "A zero footprint would divide the rate by zero.");
            Assert.AreEqual(AssemblyRate, view.ProgressRate, 0.0001f);
        }

        [Test]
        public void ProgressOf_ReadsDeliveredOverTotalCost()
        {
            ItemDefinition plate = TestDataFactory.NewItem("iron_plate");
            StorageDefinition definition = TestDataFactory.NewStorage(cost: new[] { (plate, 15) });
            var segment = new StorageRuntime(definition, new GridCoord(0, 0), Direction.North);
            var site = new ConstructionSiteRuntime(1, segment);

            Assert.AreEqual(0f, BuildDissolveView.ProgressOf(site), 0.0001f);

            site.RegisterDelivery("iron_plate", 10);
            Assert.AreEqual(10f / 15f, BuildDissolveView.ProgressOf(site), 0.0001f);

            site.RegisterDelivery("iron_plate", 5);
            Assert.AreEqual(1f, BuildDissolveView.ProgressOf(site), 0.0001f);
        }

        [Test]
        public void ABoundSite_DrivesTheTargetProgress()
        {
            ItemDefinition plate = TestDataFactory.NewItem("iron_plate");
            StorageDefinition definition = TestDataFactory.NewStorage(cost: new[] { (plate, 15) });
            var segment = new StorageRuntime(definition, new GridCoord(0, 0), Direction.North);
            var site = new ConstructionSiteRuntime(1, segment);

            BuildDissolveView view = NewView(NewSettings());
            view.Bind(site);

            site.RegisterDelivery("iron_plate", 10);
            view.Tick(0f);

            Assert.AreEqual(10f / 15f, view.TargetProgress, 0.0001f);
            Assert.AreEqual(FlashDuration, view.FlashRemaining, 0.0001f, "The delivery must announce itself.");
        }

        /// <summary>Guards the shader's name, which the settings asset resolves as an asset reference: a rename would otherwise only surface as an untextured building.</summary>
        [Test]
        public void TheDissolveShader_ExistsUnderItsExpectedName()
        {
            Assert.IsNotNull(Shader.Find("Custom/BuildDissolve"));
        }
    }
}
