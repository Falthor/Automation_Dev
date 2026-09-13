using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        /// One in-place JObject rewrite per historical Version bump, keyed by the Version it
        /// migrates FROM. <see cref="Load"/> applies them in sequence until the blob reaches
        /// <see cref="SaveData.CurrentVersion"/>, each step touching only the keys that version's
        /// format change actually altered - everything else in the blob (every unlock, every
        /// building, every list) passes through untouched. A Version with no entry here still
        /// cannot be loaded (SAUVEGARDE.md).
        /// </summary>
        static readonly Dictionary<int, Action<JObject>> Migrations = new Dictionary<int, Action<JObject>>
        {
            // Version 3 -> 4: the single Compute reserve split into Building Compute and Research
            // Compute. Building Compute inherits the old reserve's value verbatim (it inherits every
            // role the single reserve had); Research Compute starts at 0, which is exactly what a
            // save written before it existed means - nothing had produced any yet.
            [3] = raw =>
            {
                raw["BuildingComputeReserve"] = raw["ComputeReserve"] ?? 0f;
                raw["ResearchComputeReserve"] = 0f;
                raw.Remove("ComputeReserve");
            },
        };

        /// <summary>
        /// Migrates a raw save blob up to <see cref="SaveData.CurrentVersion"/> in place, applying
        /// each bridging step from <see cref="Migrations"/> in sequence starting at whatever
        /// <c>Version</c> the blob currently carries. Returns false, leaving <c>raw</c> only
        /// partially migrated, the moment a <c>Version</c> along the way (including the starting one)
        /// has no entry in <see cref="Migrations"/> - <see cref="Load"/> treats that as a refusal. A
        /// <c>Version</c> newer than <c>CurrentVersion</c> also returns false without touching
        /// anything: there is no such thing as downgrading a save.
        ///
        /// Pulled out of <see cref="Load"/> so it can be exercised directly on a hand-built
        /// <see cref="JObject"/> - SaveServiceTests must never touch the real save path (SAUVEGARDE.md),
        /// and this is the part actually worth pinning: that every untouched key (every unlock, every
        /// building, every list already earned) survives a migration byte for byte.
        /// </summary>
        public static bool TryMigrateToCurrentVersion(JObject raw)
        {
            int version = raw.Value<int?>("Version") ?? 0;
            if (version > SaveData.CurrentVersion) return false;

            while (version < SaveData.CurrentVersion)
            {
                if (!Migrations.TryGetValue(version, out Action<JObject> migrate)) return false;

                migrate(raw);
                version++;
                raw["Version"] = version;
            }

            return true;
        }

        /// <summary>
        /// Returns null if that save does not exist, fails to read/parse, carries a Version newer
        /// than <see cref="SaveData.CurrentVersion"/>, or carries an older Version with no migration
        /// path to it in <see cref="Migrations"/> - callers must handle null rather than assume a
        /// save is always present and valid. An unbridgeable Version is refused outright rather than
        /// tolerated with defaults filled in (SAUVEGARDE.md): silently loading a structurally
        /// different save just postpones the incompatibility to wherever it happens to surface next,
        /// in a form far harder to diagnose than a clear refusal here. A bridgeable one is migrated
        /// in memory only - the file on disk stays at its old Version until the player saves again.
        /// </summary>
        public static SaveData Load(string name)
        {
            string path = PathFor(name);
            if (!File.Exists(path)) return null;

            try
            {
                JObject raw = JObject.Parse(File.ReadAllText(path));
                int originalVersion = raw.Value<int?>("Version") ?? 0;

                if (!TryMigrateToCurrentVersion(raw))
                {
                    if (originalVersion > SaveData.CurrentVersion)
                        Debug.LogError($"SaveService.Load refused '{name}': save Version {originalVersion} is newer than the current format (expected {SaveData.CurrentVersion}). This save was written by a newer build and cannot be loaded.");
                    else
                        Debug.LogError($"SaveService.Load refused '{name}': save Version {originalVersion} does not match the current format (expected {SaveData.CurrentVersion}) and there is no migration path to it. This save predates an incompatible change and cannot be loaded.");
                    return null;
                }

                return raw.ToObject<SaveData>();
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
