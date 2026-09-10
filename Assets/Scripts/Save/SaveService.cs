using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Save
{
    /// <summary>
    /// Reads and writes named saves: one folder per save under
    /// <see cref="Application.persistentDataPath"/>, each holding a <c>save.json</c>.
    ///
    /// <b>A save name becomes a directory name, so it is validated rather than trusted.</b> The
    /// name comes from a text field the player types into, and it is the one place in this project
    /// where a string typed by a person is turned into a filesystem path. <see cref="Sanitise"/> is
    /// what stands between the two: it strips path separators and every character the platform
    /// rejects, refuses the relative names (<c>.</c>, <c>..</c>) outright, and caps the length. A
    /// name that survives it addresses exactly one folder inside the save root and cannot climb out
    /// of it.
    ///
    /// <b>The old single save.json at the root is not read any more and not deleted either.</b> It
    /// predates named saves; it is left where it is rather than migrated under a guessed name.
    /// </summary>
    public static class SaveService
    {
        const string SaveFileName = "save.json";

        /// <summary>Long enough for a sentence, short enough to stay well inside the platform's path limit once the root and the file name are added.</summary>
        public const int MaxNameLength = 48;

        /// <summary>Offered when the player has not typed anything - a save with no name is still better than no save.</summary>
        public const string DefaultName = "Partie";

        static string SavesRoot => Application.persistentDataPath;

        /// <summary>The folder a save lives in. Public so a caller can show the player where its data is.</summary>
        public static string FolderFor(string name) => Path.Combine(SavesRoot, Sanitise(name));

        public static string PathFor(string name) => Path.Combine(FolderFor(name), SaveFileName);

        public static bool Exists(string name) => File.Exists(PathFor(name));

        /// <summary>
        /// Every save on disk, most recently written first - which is the order a player looks for
        /// one in. A folder without a readable <c>save.json</c> is not a save and is skipped, so a
        /// stray directory in the save root cannot appear as an empty entry.
        /// </summary>
        public static IReadOnlyList<string> List()
        {
            var names = new List<string>();

            try
            {
                if (!Directory.Exists(SavesRoot)) return names;

                var found = new List<(string Name, DateTime WrittenUtc)>();
                foreach (string folder in Directory.GetDirectories(SavesRoot))
                {
                    string file = Path.Combine(folder, SaveFileName);
                    if (!File.Exists(file)) continue;

                    found.Add((Path.GetFileName(folder), File.GetLastWriteTimeUtc(file)));
                }

                found.Sort((a, b) => b.WrittenUtc.CompareTo(a.WrittenUtc));
                foreach ((string name, DateTime _) in found) names.Add(name);
            }
            catch (Exception e)
            {
                // Listing is a read of the filesystem and can fail on its own (permissions, a
                // vanished directory). An empty list is the truthful answer to "what can I load"
                // when the answer cannot be obtained, and it leaves the menu usable.
                Debug.LogError($"SaveService.List failed: {e}");
            }

            return names;
        }

        /// <summary>
        /// Writes the save into its own folder, creating it if needed. Disk I/O is a real system
        /// boundary (locked file, full disk, permissions) - a failure here is logged and swallowed
        /// rather than left to bubble out of GameRuntime and take the rest of shutdown with it.
        /// Returns whether the write actually landed, so a caller that wants to tell the player can.
        /// </summary>
        public static bool Save(SaveData data, string name)
        {
            try
            {
                string folder = FolderFor(name);
                Directory.CreateDirectory(folder);

                data.SavedAtUtc = DateTime.UtcNow.ToString("o");
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(Path.Combine(folder, SaveFileName), json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"SaveService.Save failed for '{name}': {e}");
                return false;
            }
        }

        /// <summary>
        /// Returns null if that save does not exist, fails to read/parse, or carries a Version that
        /// is not <see cref="SaveData.CurrentVersion"/> - callers must handle null rather than
        /// assume a save is always present and valid. A Version mismatch is refused outright rather
        /// than tolerated with defaults filled in (TASK_03_DATACENTER.md's decision): silently
        /// loading a structurally different save just postpones the incompatibility to wherever it
        /// happens to surface next, in a form far harder to diagnose than a clear refusal here.
        /// </summary>
        public static SaveData Load(string name)
        {
            string path = PathFor(name);
            if (!File.Exists(path)) return null;

            try
            {
                SaveData data = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(path));
                if (data != null && data.Version != SaveData.CurrentVersion)
                {
                    Debug.LogError($"SaveService.Load refused '{name}': save Version {data.Version} does not match the current format (expected {SaveData.CurrentVersion}). This save predates an incompatible change and cannot be loaded.");
                    return null;
                }
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"SaveService.Load failed for '{name}': {e}");
                return null;
            }
        }

        /// <summary>
        /// Turns what the player typed into a single safe folder name.
        ///
        /// Rejects, rather than escapes: a directory separator, a drive colon or a character the
        /// platform refuses is dropped, not encoded, so two names cannot collapse onto one folder
        /// through some escaping scheme nobody remembers. Trailing dots and spaces go too - Windows
        /// silently strips them, which would make "Partie." and "Partie" the same folder while
        /// looking like two saves. Anything that ends up empty, or is one of the two relative
        /// names, falls back to <see cref="DefaultName"/>.
        /// </summary>
        public static string Sanitise(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return DefaultName;

            char[] invalid = Path.GetInvalidFileNameChars();
            var kept = new StringBuilder(name.Length);

            foreach (char c in name.Trim())
            {
                if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar) continue;
                if (Array.IndexOf(invalid, c) >= 0) continue;
                if (char.IsControl(c)) continue;

                kept.Append(c);
                if (kept.Length >= MaxNameLength) break;
            }

            string result = kept.ToString().TrimEnd('.', ' ');
            if (result.Length == 0 || result == "." || result == "..") return DefaultName;

            return result;
        }
    }
}
