using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeliveryLab.EditorTools
{
    public static class BuildEntry
    {
        [Serializable] sealed class Receipt { public string result,unity,finishedUtc,config; public int errors,warnings; public long bytes; }
        public static void Build()
        {
            string output=LabApp.Arg("-labBuildPath");
            if(string.IsNullOrEmpty(output)) throw new ArgumentException("-labBuildPath is required");
            output=Path.GetFullPath(output); string allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Builds"))+Path.DirectorySeparatorChar;
            if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Output must remain in this lab's Builds folder");
            Directory.CreateDirectory(Path.GetDirectoryName(output)); Directory.CreateDirectory("Assets/Scenes");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Camera",typeof(Camera)).GetComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.03f,.05f,.08f); camera.orthographic=true; camera.tag="MainCamera";
            new GameObject("InternalDemo",typeof(LabApp));
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/InventoryLab.unity");
            PlayerSettings.companyName="DeliveryLab"; PlayerSettings.productName="Inventory & Save Lab - Internal Demo";
            PlayerSettings.defaultScreenWidth=1280; PlayerSettings.defaultScreenHeight=720; PlayerSettings.defaultIsNativeResolution=false;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed; PlayerSettings.resizableWindow=true; PlayerSettings.runInBackground=true; PlayerSettings.SplashScreen.show=false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            QualitySettings.vSyncCount=0; QualitySettings.antiAliasing=0; AssetDatabase.SaveAssets();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/InventoryLab.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            var r=new Receipt{result=report.summary.result.ToString(),unity=Application.unityVersion,finishedUtc=DateTime.UtcNow.ToString("O"),config=Resources.Load<TextAsset>("LabConfig").text,errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,bytes=(long)report.summary.totalSize};
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"build-result.json"),JsonUtility.ToJson(r,true));
            if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Build failed: "+report.summary.result);
            Debug.Log("[LAB_BUILD] "+JsonUtility.ToJson(r));
        }
    }
}
