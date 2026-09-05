using System;
using Game.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// Every shader used by Game.Presentation is reached through a serialized asset reference, never
    /// through Shader.Find. A shader only reachable by name is stripped from a player build unless it
    /// is also listed in Always Included Shaders - a list nothing enforces, and forgetting one entry
    /// once already cost a build that started with no Core, no action radius and no fog (docs/BUILD.md).
    ///
    /// An asset reference cannot be stripped, but it can be left empty in the scene, which fails just
    /// as silently in the editor as a missing shader did in a build. That is what these tests guard.
    /// </summary>
    public class ShaderReferenceTests
    {
        const string ScenePath = "Assets/Scenes/Bootstrap.unity";

        Scene _scene;

        [SetUp]
        public void SetUp()
        {
            // Additive, so running the suite does not close whatever the developer had open.
            _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }

        [TearDown]
        public void TearDown()
        {
            if (_scene.isLoaded) EditorSceneManager.CloseScene(_scene, true);
        }

        /// <summary>Restricted to the scene under test: the suite may run with other scenes loaded.</summary>
        Component FindInScene(Type componentType)
        {
            foreach (UnityEngine.Object found in UnityEngine.Object.FindObjectsByType(componentType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (found is Component component && component.gameObject.scene == _scene) return component;
            }
            return null;
        }

        [TestCase(typeof(TerrainView), "groundShader", "Custom/ShadedGroundTiled")]
        [TestCase(typeof(TerrainView), "cloudShader", "Custom/CloudShadowOverlay")]
        [TestCase(typeof(GridLineView), "overlayShader", "Custom/GridLinesOverlay")]
        [TestCase(typeof(ActionRadiusView), "overlayShader", "Custom/ActionRadiusOverlay")]
        [TestCase(typeof(FogOfWarView), "fogShader", "Custom/FogOfWar")]
        [TestCase(typeof(GameRuntime), "groundSlabShader", "Custom/BuildingGroundSlab")]
        public void ShaderReference_IsAssignedAndPointsAtTheRightShader(Type componentType, string fieldName, string expectedShaderName)
        {
            Component component = FindInScene(componentType);
            Assert.IsNotNull(component, $"No {componentType.Name} in {ScenePath}.");

            SerializedProperty property = new SerializedObject(component).FindProperty(fieldName);
            Assert.IsNotNull(property, $"{componentType.Name} has no serialized field '{fieldName}'.");

            var shader = property.objectReferenceValue as Shader;
            Assert.IsNotNull(shader,
                $"{componentType.Name}.{fieldName} is empty in {ScenePath}. Assign {expectedShaderName} in the inspector; " +
                "it is no longer resolved by name, so an empty field means no material at all.");
            Assert.AreEqual(expectedShaderName, shader.name);
        }

        /// <summary>The slab shader is the one that does not live on its own component: it rides along in GroundSlabSettings, which ProceduralSpriteFactory receives. If it fails to arrive, the slab is skipped rather than throwing.</summary>
        [Test]
        public void TheSlabSettings_RefuseToRenderWithoutAShader()
        {
            var settings = new GroundSlabSettings
            {
                SlabDiffuse = new Texture2D(2, 2),
                SlabNormal = new Texture2D(2, 2)
            };

            Assert.IsFalse(settings.CanRenderSlab, "No shader must disable the slab, not produce a null material.");

            settings.SlabShader = Shader.Find("Custom/BuildingGroundSlab");
            Assert.IsTrue(settings.CanRenderSlab);

            UnityEngine.Object.DestroyImmediate(settings.SlabDiffuse);
            UnityEngine.Object.DestroyImmediate(settings.SlabNormal);
        }
    }
}
