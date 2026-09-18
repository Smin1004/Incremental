using System.IO;
using UnityEditor;
using UnityEngine;

namespace Incremental.EditorTools
{
    /// <summary>Menu: Incremental / Save. Open the persistentDataPath folder, or delete save.json / save.bak / save.tmp.</summary>
    public static class SaveMenu
    {
        [MenuItem("Incremental/Save/Open Folder")]
        public static void OpenFolder()
        {
            string dir = Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            EditorUtility.OpenWithDefaultApp(dir);
        }

        [MenuItem("Incremental/Save/Delete Save")]
        public static void DeleteSave()
        {
            if (!EditorUtility.DisplayDialog("Delete Save",
                    "Delete save.json, save.bak and save.tmp in\n" + Application.persistentDataPath + "?\n\nsettings.json and run logs are kept.",
                    "Delete", "Cancel"))
                return;

            // In play mode the running game would write its state back on quit, so reset it through the game instead.
            var root = EditorApplication.isPlaying ? Object.FindFirstObjectByType<GameRoot>() : null;
            if (root != null)
            {
                root.ResetProgress();
                return;
            }
            new SaveStore(Application.persistentDataPath).DeleteSave();
            Debug.Log("[Save] save deleted: " + Application.persistentDataPath);
        }
    }
}
