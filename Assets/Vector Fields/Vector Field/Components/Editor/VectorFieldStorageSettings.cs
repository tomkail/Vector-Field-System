using UnityEditor;
using UnityEngine;

// Project setting for how drawable vector fields serialize into the scene (see VectorFieldStorage). Stored in
// ProjectSettings/ (versioned, shared by the team — not an asset in Assets/), the same pattern as the debug settings.
// Fully qualified: the project has another FilePathAttribute in scope, so an unqualified [FilePath] binds to the wrong
// one. (Same reason the debug settings qualify it.)
[UnityEditor.FilePath("ProjectSettings/VectorFieldStorageSettings.asset", UnityEditor.FilePathAttribute.Location.ProjectFolder)]
public class VectorFieldStorageSettings : UnityEditor.ScriptableSingleton<VectorFieldStorageSettings> {
    [SerializeField] public VectorFieldStorage.Format format = VectorFieldStorage.Format.Vector2Array;
    public void SaveChanges() => Save(true);
}

// Push the persisted format into the runtime static on every domain reload, before anything serializes, so the
// component (which can't touch a ScriptableSingleton from its serialization callback) reads the right value.
[InitializeOnLoad]
static class VectorFieldStorageSettingsLoader {
    static VectorFieldStorageSettingsLoader() => VectorFieldStorage.format = VectorFieldStorageSettings.instance.format;
}

static class VectorFieldStorageSettingsProvider {
    [SettingsProvider]
    public static SettingsProvider Create() {
        return new SettingsProvider("Project/Vector Fields/Storage", SettingsScope.Project) {
            label = "Storage",
            guiHandler = _ => {
                var settings = VectorFieldStorageSettings.instance;
                EditorGUILayout.HelpBox(
                    "How drawable vector fields serialize into the scene/prefab. The data always stays on the " +
                    "component — never an asset.\n\n" +
                    "• Vector2 Array: human-readable YAML, but one line per cell (a 128×128 field is ~16k lines) " +
                    "— large scenes, slow save/load, merge-hostile diffs.\n" +
                    "• Byte Array: the same data packed compactly, one base64 line per grid row — small scenes AND " +
                    "local diffs (an edit rewrites only its row), but not human-readable.\n\n" +
                    "Changing this re-serializes each field the next time its scene/prefab is saved; existing fields " +
                    "load either way.",
                    MessageType.Info);

                EditorGUI.BeginChangeCheck();
                var fmt = (VectorFieldStorage.Format)EditorGUILayout.EnumPopup("Scene Storage Format", settings.format);
                if (EditorGUI.EndChangeCheck()) {
                    settings.format = fmt;
                    VectorFieldStorage.format = fmt;
                    settings.SaveChanges();
                }
            },
            keywords = new[] { "vector", "field", "storage", "serialize", "paint", "byte", "compact", "scene" },
        };
    }
}
