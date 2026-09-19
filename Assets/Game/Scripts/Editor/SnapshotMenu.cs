using System.IO;
using UnityEditor;
using UnityEngine;

namespace Incremental.EditorTools
{
    /// <summary>
    /// Menu: Incremental / Snapshots (15 §6.2). The bot copies the save to snapshots/ring_k.json right after buying the gate
    /// of ring k; loading one starts the balance pass of that ring without replaying the game from run 1. In play mode the
    /// running game switches to the snapshot (the run in flight is dropped); in edit mode the snapshot becomes save.json
    /// for the next play.
    /// </summary>
    public static class SnapshotMenu
    {
        static string Dir => Path.Combine(Application.persistentDataPath, GameRoot.SnapshotDir);

        [MenuItem("Incremental/Snapshots/Load...")]
        public static void LoadMenu()
        {
            Directory.CreateDirectory(Dir);
            string path = EditorUtility.OpenFilePanel("Load ring snapshot", Dir, "json");
            if (string.IsNullOrEmpty(path)) return;
            if (!Application.isPlaying &&
                !EditorUtility.DisplayDialog("Load snapshot", "Replace save.json in\n" + Application.persistentDataPath + "\nwith\n" + Path.GetFileName(path) + "?", "Replace", "Cancel"))
                return;
            LoadFile(path);
        }

        [MenuItem("Incremental/Snapshots/Open Folder")]
        public static void OpenFolder()
        {
            Directory.CreateDirectory(Dir);
            EditorUtility.RevealInFinder(Dir);
        }

        /// <summary>Loads snapshots/ring_k.json (no dialog). Returns false when it is missing or unreadable.</summary>
        public static bool LoadRing(int ring) => LoadFile(GameRoot.SnapshotPath(Application.persistentDataPath, ring));

        /// <summary>Loads a save file into the running game, or makes it save.json when not playing (no dialog).</summary>
        public static bool LoadFile(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError("[Snapshot] not found: " + path);
                return false;
            }
            if (Application.isPlaying)
            {
                var root = Object.FindFirstObjectByType<GameRoot>();
                return root != null && root.LoadSaveFile(path);
            }
            var data = SaveStore.ReadFile(path);
            if (data == null) return false;
            new SaveStore(Application.persistentDataPath).Write(data);
            Debug.Log($"[Snapshot] {Path.GetFileName(path)} is now save.json (run {data.meta.runCount}, max tier {data.meta.unlockedMaxTier})");
            return true;
        }
    }
}
