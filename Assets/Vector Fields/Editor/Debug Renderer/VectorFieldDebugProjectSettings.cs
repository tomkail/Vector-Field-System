using UnityEditor;
using UnityEngine;

namespace VectorFields {
    // Project-wide defaults for the vector field debug arrows, shown under Project Settings > Vector Field Debug.
    // Stored in the ProjectSettings/ folder (not Assets/) so it's version-controlled with the project and shared by the
    // team, the modern Unity pattern for project settings (ScriptableSingleton + FilePath in the ProjectFolder).
    [UnityEditor.FilePath("ProjectSettings/VectorFieldDebugSettings.asset", UnityEditor.FilePathAttribute.Location.ProjectFolder)]
    public class VectorFieldDebugProjectSettings : UnityEditor.ScriptableSingleton<VectorFieldDebugProjectSettings> {
        [SerializeField] VectorFieldDebugAppearance _appearance = new VectorFieldDebugAppearance();
        public VectorFieldDebugAppearance appearance => _appearance ??= new VectorFieldDebugAppearance();

        void OnEnable() { _appearance ??= new VectorFieldDebugAppearance(); }

        // ScriptableSingleton.Save is protected; expose it so the settings provider can persist edits.
        public void SaveChanges() => Save(true);
    }
}
