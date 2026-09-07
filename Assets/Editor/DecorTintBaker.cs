using Game.Data;
using Game.Presentation;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Bakes each decor sprite's tint into <see cref="DecorSettings"/>, once, from the art itself.
    ///
    /// <b>Why a bake and not a startup pass.</b> A sprite's average colour is a constant: the old
    /// whole-map scatter recomputed all of them on every Play Mode entry, blitting sixty textures
    /// through the GPU to arrive at the same numbers each time. Baking moves that to authoring time
    /// and leaves the running game reading a colour out of an asset.
    ///
    /// <b>Re-run it when the ground textures change</b>, since the tints mute towards the ground's own
    /// tone. Forgetting to is caught by `DecorTintBakeTests`, which recomputes and compares rather
    /// than trusting that nobody forgot.
    /// </summary>
    public static class DecorTintBaker
    {
        public const string DecorSettingsPath = "Assets/Data/World/DecorSettings.asset";

        [MenuItem("Tools/Decor/Bake Sprite Tints")]
        public static void BakeMenuItem()
        {
            var settings = AssetDatabase.LoadAssetAtPath<DecorSettings>(DecorSettingsPath);
            GroundTextureProfile profile = FindGroundProfile();

            if (settings == null || profile == null)
            {
                Debug.LogError("Bake Sprite Tints: DecorSettings or the ground profile could not be found.");
                return;
            }

            int baked = Bake(settings, profile);
            Debug.Log($"Bake Sprite Tints: {baked} sprite tints written from {profile.name}.");
        }

        public const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        /// <summary>
        /// The ground profile the shipped scene actually uses, read from that scene's dependencies
        /// rather than by asset name.
        ///
        /// Two profiles exist and only one is wired up. Picking by path would keep baking against the
        /// wrong one the day the scene was pointed at the other, and nothing would say so - the rocks
        /// would simply stop matching their ground. Reading the shipped scene is the same rule the
        /// sector-name test learned: a check on the shipped game must read the shipped game.
        /// </summary>
        public static GroundTextureProfile FindGroundProfile()
        {
            foreach (string path in AssetDatabase.GetDependencies(BootstrapScenePath, true))
            {
                var profile = AssetDatabase.LoadAssetAtPath<GroundTextureProfile>(path);
                if (profile != null) return profile;
            }

            return null;
        }

        /// <summary>Writes one tint per sprite of every kind, and answers how many. Public so the guard test can bake into a copy and compare without duplicating the walk.</summary>
        public static int Bake(DecorSettings settings, GroundTextureProfile profile)
        {
            Color ground = GroundToneOf(profile);

            var serialized = new SerializedObject(settings);
            SerializedProperty kinds = serialized.FindProperty("kinds");
            int baked = 0;

            for (int k = 0; k < kinds.arraySize; k++)
            {
                SerializedProperty kind = kinds.GetArrayElementAtIndex(k);
                SerializedProperty sprites = kind.FindPropertyRelative("sprites");
                SerializedProperty tints = kind.FindPropertyRelative("spriteTints");

                tints.arraySize = sprites.arraySize;

                for (int s = 0; s < sprites.arraySize; s++)
                {
                    var sprite = sprites.GetArrayElementAtIndex(s).objectReferenceValue as Sprite;
                    Color raw = SpriteTintBaking.AverageColorOf(sprite);
                    tints.GetArrayElementAtIndex(s).colorValue = SpriteTintBaking.MuteTowards(raw, ground);
                    baked++;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            return baked;
        }

        /// <summary>The tone every decor sprite is muted towards - the darker of the profile's first two base textures.</summary>
        public static Color GroundToneOf(GroundTextureProfile profile)
        {
            Texture2D first = profile.baseTextures.Length > 0 ? profile.baseTextures[0] : null;
            Texture2D second = profile.baseTextures.Length > 1 ? profile.baseTextures[1] : first;
            return SpriteTintBaking.GroundToneOf(first, second);
        }
    }
}
