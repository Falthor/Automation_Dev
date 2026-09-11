using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Save
{
    /// <summary>
    /// Reads/writes the player's preferences: <c>preferences.json</c>, beside the save file in the
    /// same folder.
    ///
    /// <b>Beside the save and not inside it, because they answer to different lifetimes.</b> A
    /// keyboard layout is a property of the person playing, not of the run: it has to survive
    /// starting a new game, and it must not travel with a save file. Putting it in
    /// <see cref="SaveData"/> would have tied "which key rotates a building" to "which world am I
    /// in", and a New Game would silently hand the keys back to their defaults.
    ///
    /// <b>One file, a JSON object, one key per concern.</b> Only input bindings live in it today.
    /// The shape is an object rather than the binding blob alone so that the next preference is a
    /// key rather than a second file, and so that reading tolerates a file written before it existed.
    ///
    /// Same failure discipline as <see cref="SaveService"/>: disk I/O is a real boundary, and a
    /// locked or unwritable file is logged and swallowed rather than taking initialisation down with
    /// it. A missing or corrupt file means "no preferences", which is the truthful default.
    /// </summary>
    public static class PreferencesService
    {
        const string FileName = "preferences.json";

        /// <summary>The Input System's own binding-override blob, verbatim. Opaque here on purpose - its format belongs to the package, not to this file.</summary>
        const string InputBindingsKey = "inputBindings";

        public static string PreferencesPath => Path.Combine(Application.persistentDataPath, FileName);

        public static bool Exists() => File.Exists(PreferencesPath);

        /// <summary>The stored binding overrides, or null when there are none - which is what a fresh install and an unreadable file both mean.</summary>
        public static string ReadInputBindings()
        {
            JObject root = Read();
            string stored = (string)root?[InputBindingsKey];
            return string.IsNullOrEmpty(stored) ? null : stored;
        }

        /// <summary>
        /// Stores the binding overrides, or clears them when given null or an empty string - which is
        /// what "everything is back to its default" looks like, and has to erase rather than keep the
        /// last non-default value.
        /// </summary>
        public static void WriteInputBindings(string overridesJson)
        {
            JObject root = Read() ?? new JObject();

            if (string.IsNullOrEmpty(overridesJson)) root.Remove(InputBindingsKey);
            else root[InputBindingsKey] = overridesJson;

            Write(root);
        }

        static JObject Read()
        {
            try
            {
                if (!File.Exists(PreferencesPath)) return null;
                return JObject.Parse(File.ReadAllText(PreferencesPath));
            }
            catch (Exception e)
            {
                // Not an error: a file someone hand-edited into invalid JSON should cost the
                // preferences, not the session.
                Debug.LogWarning($"PreferencesService could not read {PreferencesPath}, treating it as empty: {e.Message}");
                return null;
            }
        }

        static void Write(JObject root)
        {
            try
            {
                File.WriteAllText(PreferencesPath, root.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError($"PreferencesService.Write failed: {e}");
            }
        }
    }
}
