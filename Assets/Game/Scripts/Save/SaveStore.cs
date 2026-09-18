using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Incremental
{
    public enum LoadStatus
    {
        /// <summary>No save.json and no save.bak: first launch.</summary>
        NoSave,
        Loaded,
        /// <summary>save.json was missing or broken; save.bak was used.</summary>
        RestoredFromBackup,
        /// <summary>Both files were broken; they were kept as save.corrupt_*.json and the game starts fresh.</summary>
        Corrupt,
    }

    /// <summary>
    /// Reads and writes save.json and settings.json in one directory (persistentDataPath in the game, a temp folder in tests).
    /// Write: save.tmp first, then the old save.json becomes save.bak and save.tmp becomes save.json.
    /// Read: save.json, else save.bak, else start fresh. A file that fails to parse is renamed to
    /// save.corrupt_&lt;time&gt;.json and a warning is logged.
    /// </summary>
    public sealed class SaveStore
    {
        public const string SaveFile = "save.json";
        public const string TmpFile = "save.tmp";
        public const string BakFile = "save.bak";
        public const string SettingsFile = "settings.json";

        static readonly Encoding Utf8 = new UTF8Encoding(false);

        public string Dir { get; }
        public string SavePath => Path.Combine(Dir, SaveFile);
        public string TmpPath => Path.Combine(Dir, TmpFile);
        public string BakPath => Path.Combine(Dir, BakFile);
        public string SettingsPath => Path.Combine(Dir, SettingsFile);

        public SaveStore(string dir) { Dir = dir; }

        public bool HasSave => File.Exists(SavePath) || File.Exists(BakPath);

        // ---------------- save.json ----------------

        public void Write(SaveData data)
        {
            Directory.CreateDirectory(Dir);
            // Compact JSON: with ~70 runs of history pretty printing is 4x the size (130 KB) and the pending save runs every 5 s.
            File.WriteAllText(TmpPath, JsonUtility.ToJson(data, false), Utf8);
            if (File.Exists(SavePath))
            {
                if (File.Exists(BakPath)) File.Delete(BakPath);
                File.Move(SavePath, BakPath);
            }
            File.Move(TmpPath, SavePath);
        }

        /// <summary>Loads and migrates the save. Returns null for <see cref="LoadStatus.NoSave"/> and <see cref="LoadStatus.Corrupt"/>.</summary>
        public SaveData Load(out LoadStatus status)
        {
            bool hasMain = File.Exists(SavePath);
            bool hasBak = File.Exists(BakPath);
            if (!hasMain && !hasBak)
            {
                status = LoadStatus.NoSave;
                return null;
            }

            string error;
            if (hasMain)
            {
                var data = TryRead(SavePath, out error);
                if (data != null)
                {
                    status = LoadStatus.Loaded;
                    return Migrate(data, SavePath);
                }
                string kept = Quarantine(SavePath);
                Debug.LogWarning($"[Save] {SaveFile} could not be read ({error}); kept as {Path.GetFileName(kept)}. Trying {BakFile}.");
            }

            if (hasBak)
            {
                var data = TryRead(BakPath, out error);
                if (data != null)
                {
                    status = LoadStatus.RestoredFromBackup;
                    Debug.LogWarning($"[Save] restored from {BakFile}. Progress since the previous save is lost.");
                    return Migrate(data, BakPath);
                }
                string kept = Quarantine(BakPath);
                Debug.LogWarning($"[Save] {BakFile} could not be read either ({error}); kept as {Path.GetFileName(kept)}.");
            }

            Debug.LogWarning("[Save] no readable save; starting a new game.");
            status = LoadStatus.Corrupt;
            return null;
        }

        /// <summary>Migrates; a save that migration replaces with a new game is first copied to save.v&lt;n&gt;_&lt;time&gt;.json.</summary>
        SaveData Migrate(SaveData data, string sourcePath)
        {
            if (SaveMigration.ResetsProgress(data.version))
            {
                string copy = Path.Combine(Dir, "save.v" + data.version + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".json");
                try
                {
                    File.Copy(sourcePath, copy, true);
                    Debug.LogWarning("[Save] old save kept as " + Path.GetFileName(copy));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Save] could not keep a copy of the old save: " + e.Message);
                }
            }
            return SaveMigration.Migrate(data);
        }

        /// <summary>Deletes save.json, save.bak and save.tmp. Settings and quarantined files are kept.</summary>
        public void DeleteSave()
        {
            foreach (var p in new[] { SavePath, BakPath, TmpPath })
                if (File.Exists(p)) File.Delete(p);
        }

        static SaveData TryRead(string path, out string error)
        {
            error = null;
            try
            {
                string json = File.ReadAllText(path, Utf8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "empty file";
                    return null;
                }
                // A syntactically valid but unrelated JSON ("{}") would deserialize into an empty save; require the root keys.
                if (json.IndexOf("\"meta\"", StringComparison.Ordinal) < 0 || json.IndexOf("\"version\"", StringComparison.Ordinal) < 0)
                {
                    error = "not a save file";
                    return null;
                }
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null || data.meta == null)
                {
                    error = "no data";
                    return null;
                }
                return data;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        /// <summary>Renames a broken file to save.corrupt_&lt;time&gt;.json (unique) and returns the new path.</summary>
        string Quarantine(string path)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string target = Path.Combine(Dir, "save.corrupt_" + stamp + ".json");
            for (int i = 2; File.Exists(target); i++) target = Path.Combine(Dir, "save.corrupt_" + stamp + "_" + i + ".json");
            try
            {
                File.Move(path, target);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] could not move the broken file aside: " + e.Message);
                return path;
            }
            return target;
        }

        // ---------------- settings.json ----------------

        public Settings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new Settings();
                var s = JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath, Utf8));
                return s ?? new Settings();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] {SettingsFile} could not be read ({e.Message}); using defaults.");
                return new Settings();
            }
        }

        public void WriteSettings(Settings settings)
        {
            Directory.CreateDirectory(Dir);
            string tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(settings, true), Utf8);
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
            File.Move(tmp, SettingsPath);
        }
    }
}
