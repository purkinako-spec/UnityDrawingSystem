using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using DrawingSystem;

namespace DrawingSystem.Editor
{
    [InitializeOnLoad]
    public static class PackageSampleExporter
    {
        static PackageSampleExporter()
        {
            EditorApplication.delayCall += () =>
            {
                // まだPrefabが存在しない場合、自動的に生成
                string packagePath = Path.GetFullPath("Packages/com.custom.drawing-system");
                string runtimePrefabPath = Path.Combine(packagePath, "Runtime/Prefabs/DrawingSystem.prefab");
                if (!File.Exists(runtimePrefabPath))
                {
                    ExportPrefabAndScene();
                }
            };
        }

        [MenuItem("Tools/Drawing System/Export Prefab and Scene to Package")]
        public static void ExportPrefabAndScene()
        {
            string packagePath = Path.GetFullPath("Packages/com.custom.drawing-system");
            if (!Directory.Exists(packagePath))
            {
                Debug.LogWarning("[PackageSampleExporter] Package path not found: " + packagePath);
                return;
            }

            string prefabsDir = Path.Combine(packagePath, "Runtime/Prefabs");
            string scenesDir = Path.Combine(packagePath, "Runtime/Scenes");
            string samplesDir = Path.Combine(packagePath, "Samples~/BasicSetup");

            Directory.CreateDirectory(prefabsDir);
            Directory.CreateDirectory(scenesDir);
            Directory.CreateDirectory(samplesDir);

            // 1. プレハブの作成 (DrawingSystem.prefab)
            CreateDrawingSystemPrefab("Packages/com.custom.drawing-system/Runtime/Prefabs/DrawingSystem.prefab");

            // 2. シーンの保存・エクスポート
            ExportCurrentOrAssetScene(packagePath);

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log("[Drawing System] Prefab と Scene を Package および Samples~ にエクスポートしました！");
        }

        private static void CreateDrawingSystemPrefab(string assetPath)
        {
            // 既存の DrawingCanvas をシーンから探す
            var existingCanvas = Object.FindFirstObjectByType<DrawingCanvas>();
            var existingRawImage = Object.FindFirstObjectByType<RawImage>();

            GameObject rootGO = new GameObject("DrawingSystem");

            // Canvas を子として作成
            GameObject canvasGO = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(rootGO.transform, false);

            Canvas canvasComp = canvasGO.GetComponent<Canvas>();
            canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // RawImage を Canvas の子として作成
            GameObject rawImageGO = new GameObject("RawImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rawImageGO.transform.SetParent(canvasGO.transform, false);

            RectTransform rawRT = rawImageGO.GetComponent<RectTransform>();
            rawRT.anchorMin = Vector2.zero;
            rawRT.anchorMax = Vector2.one;
            rawRT.offsetMin = Vector2.zero;
            rawRT.offsetMax = Vector2.zero;

            RawImage rawImageComp = rawImageGO.GetComponent<RawImage>();
            rawImageComp.color = Color.white;

            // DrawingCanvas を追加
            DrawingCanvas drawingCanvasComp = rootGO.AddComponent<DrawingCanvas>();
            
            // RawImage の参照を設定
            var so = new SerializedObject(drawingCanvasComp);
            var targetRawImageProp = so.FindProperty("targetRawImage");
            if (targetRawImageProp != null)
            {
                targetRawImageProp.objectReferenceValue = rawImageComp;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // InputProvider を追加
            rootGO.AddComponent<PenTabletInputProvider>();

            // Prefab として保存
            PrefabUtility.SaveAsPrefabAsset(rootGO, assetPath);
            Object.DestroyImmediate(rootGO);

            // Samples~ にもコピー
            string fullAssetPath = Path.GetFullPath(assetPath);
            string samplesPrefabPath = Path.Combine(Path.GetDirectoryName(fullAssetPath), "../../Samples~/BasicSetup/DrawingSystem.prefab");
            samplesPrefabPath = Path.GetFullPath(samplesPrefabPath);
            File.Copy(fullAssetPath, samplesPrefabPath, true);
        }

        private static void ExportCurrentOrAssetScene(string packagePath)
        {
            string sourceScenePath = "Assets/DrawingSystem/DrawingSystemScene.unity";
            if (!File.Exists(sourceScenePath))
            {
                // 現在のアクティブシーンが DrawingSystemScene なら保存
                var activeScene = EditorSceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(activeScene.path))
                {
                    sourceScenePath = activeScene.path;
                }
            }

            if (File.Exists(sourceScenePath))
            {
                string destSceneInRuntime = Path.Combine(packagePath, "Runtime/Scenes/DrawingSystemScene.unity");
                string destSceneInSamples = Path.Combine(packagePath, "Samples~/BasicSetup/DrawingSystemScene.unity");

                File.Copy(sourceScenePath, destSceneInRuntime, true);
                File.Copy(sourceScenePath, destSceneInSamples, true);

                string sourceMeta = sourceScenePath + ".meta";
                if (File.Exists(sourceMeta))
                {
                    File.Copy(sourceMeta, destSceneInRuntime + ".meta", true);
                }
            }
        }
    }
}
