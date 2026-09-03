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

        public static void Save(SaveData data)
        {
            data.SavedAtUtc = DateTime.UtcNow.ToString("o");
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(SavePath, json);
        }

        /// <summary>Returns null if no save file exists - callers must check SaveExists() (or handle null) rather than assume a save is always present.</summary>
        public static SaveData Load()
        {
            if (!SaveExists()) return null;
            string json = File.ReadAllText(SavePath);
            return JsonConvert.DeserializeObject<SaveData>(json);
        }
    }
}
