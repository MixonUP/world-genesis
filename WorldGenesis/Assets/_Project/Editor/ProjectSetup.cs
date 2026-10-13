using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorldGenesis.Editor
{
    /// <summary>Reproducible scene and Windows build entry points.</summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";

        [MenuItem("World Genesis/Prepare project")]
        public static void Prepare()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
            PlayerSettings.companyName = "World Genesis Project";
            PlayerSettings.productName = "World Genesis";
            PlayerSettings.bundleVersion = "0.9.0";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            UnityEngine.Object[] graphicsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsAssets.Length > 0)
            {
                SerializedObject graphics = new SerializedObject(graphicsAssets[0]);
                SerializedProperty shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
                foreach (string shaderName in new[] { "Sprites/Default", "UI/Default" })
                {
                    Shader shader = Shader.Find(shaderName);
                    if (shader == null) throw new InvalidOperationException("Missing built-in shader " + shaderName);
                    bool found = false;
                    for (int i = 0; i < shaders.arraySize; i++)
                        if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) found = true;
                    if (!found) { int i = shaders.arraySize; shaders.InsertArrayElementAtIndex(i); shaders.GetArrayElementAtIndex(i).objectReferenceValue = shader; }
                }
                graphics.ApplyModifiedPropertiesWithoutUndo();
            }
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // Runtime composition is owned by GameBootstrap; the scene has no duplicate state.
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("WORLD_GENESIS_PREPARED " + ScenePath);
        }

        [MenuItem("World Genesis/Open game scene")]
        public static void OpenGame()
        {
            Prepare();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("World Genesis/Build Windows player")]
        public static void BuildWindows()
        {
            Prepare();
            string root = Directory.GetParent(Application.dataPath).Parent.FullName;
            string[] arguments = Environment.GetCommandLineArgs();
            int rootIndex = Array.IndexOf(arguments, "--artifact-root");
            if (rootIndex >= 0 && rootIndex + 1 < arguments.Length)
                root = Path.GetFullPath(arguments[rootIndex + 1]);
            string relativeOutput = "Builds/Windows-" + PlayerSettings.bundleVersion + "/WorldGenesis.exe";
            string output = Path.Combine(root, relativeOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            string receipt = Path.Combine(root, "Evidence", "build-report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(receipt));
            File.WriteAllText(receipt, JsonUtility.ToJson(new BuildReceipt
            {
                utc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
                result = report.summary.result.ToString(), errors = report.summary.totalErrors,
                warnings = report.summary.totalWarnings, bytes = report.summary.totalSize,
                seconds = report.summary.totalTime.TotalSeconds,
                output = relativeOutput
            }, true));
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result);
            Debug.Log("WORLD_GENESIS_BUILD_SUCCEEDED " + output);
        }

        [Serializable]
        private sealed class BuildReceipt
        {
            public string utc, unityVersion, result, output;
            public int errors, warnings;
            public ulong bytes;
            public double seconds;
        }
    }
}
