using System;
using System.IO;
using MuseumBrowser.App;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MuseumBrowser.EditorTools
{
    public static class PanoramaSetup
    {
        private const string ScenePath = "Assets/MuseumBrowser/Scenes/BudapestPanorama.unity";
        private const string DefaultPlayKey = "MuseumBrowser.BudapestDefaultPlay";

        [InitializeOnLoadMethod]
        private static void ConfigureDefaultPlayScene()
        {
            // Start Play in Budapest without replacing or discarding the scene being edited.
            EditorApplication.delayCall += ApplyDefaultPlayScene;
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredEditMode) ApplyDefaultPlayScene();
            };
        }

        private static void ApplyDefaultPlayScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorSceneManager.playModeStartScene = EditorPrefs.GetBool(DefaultPlayKey, true)
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) : null;
        }

        [MenuItem("Museum Browser/Play Starts In/Budapest Panorama")]
        public static void UseBudapestForPlay()
        {
            EditorPrefs.SetBool(DefaultPlayKey, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        [MenuItem("Museum Browser/Play Starts In/Current Scene")]
        public static void UseCurrentSceneForPlay()
        {
            EditorPrefs.SetBool(DefaultPlayKey, false);
            EditorSceneManager.playModeStartScene = null;
        }

        [MenuItem("Museum Browser/Open Budapest Panorama")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else CreateScene();
        }

        private static void CreateScene()
        {
            const string materialPath = "Assets/MuseumBrowser/Scenes/BudapestSkybox.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) {
                var shader = Shader.Find("Skybox/Panoramic");
                if (shader == null) throw new InvalidOperationException("Panoramic shader missing.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Panorama Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 75;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.gameObject.AddComponent<FolioPanoramaViewer>().SkyboxTemplate = material;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }

        // Dedicated validation player within MuseumBrowser; does not modify its build scene list.
        public static void Build()
        {
            CreateScene();
            var target = NamedBuildTarget.Standalone;
            var previous = PlayerSettings.GetScriptingBackend(target);
            try {
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { ScenePath }, target = BuildTarget.StandaloneOSX,
                    locationPathName = "Builds/Panorama/MuseumBrowser.app",
                    options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new Exception("Panorama build failed: " + report.summary.result);
            } finally { PlayerSettings.SetScriptingBackend(target, previous); }
        }
    }
}
