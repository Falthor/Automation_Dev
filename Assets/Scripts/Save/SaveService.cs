using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Save
{
    /// <summary>
    /// Reads/writes the single save file (CONTRACTS.md §14 - mono-save: one fixed filename,
    /// always overwritten in place, no save slots). Written by New Game (initial state) and by
    /// GameRuntime.OnApplicationQuit (current state); read by Load.
    /// </summary>
    public static class SaveService
    {
        const string SaveFileName = "save.json";

        public static string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static bool SaveExists() => File.Exists(SavePath);

        /// <summary>
        /// Writes the save file. Disk I/O is a real system boundary (locked file, full disk,
        /// permissions) - a failure here is logged and swallowed rather than left to bubble up
        /// out of GameRuntime.Awake()/OnApplicationQuit and take the rest of initialization or
        /// shutdown down with it.
        /// </summary>
        public static void Save(SaveData data)
        {
            try
            {
                data.SavedAtUtc = DateTime.UtcNow.ToString("o");
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(SavePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"SaveService.Save failed: {e}");
            }
        }

        /// <summary>Returns null if no save file exists, or if it exists but fails to read/parse - callers must handle null rather than assume a save is always present/valid.</summary>
        public static SaveData Load()
        {
            if (!SaveExists()) return null;

            try
            {
                string json = File.ReadAllText(SavePath);
                return JsonConvert.DeserializeObject<SaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"SaveService.Load failed: {e}");
                return null;
            }
        }
    }
}
