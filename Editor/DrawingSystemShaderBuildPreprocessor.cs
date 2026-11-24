using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrawingSystem.Editor
{
    /// <summary>
    /// スタンドアロンビルド時に DrawingSystem 必須シェーダーがストリップされないよう、
    /// Always Included Shaders に自動登録するエディタ拡張。
    /// </summary>
    [InitializeOnLoad]
    public class DrawingSystemShaderBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        private static readonly string[] RequiredShaderGuids = new[]
        {
            "5e9a1dbf222cd8848a47edeb00441d44", // BrushStamp.shader
            "4606b57b27f49f848af749bad05d0f1b"  // LayerComposite.shader
        };

        private static readonly string[] RequiredShaderNames = new[]
        {
            "Hidden/DrawingSystem/BrushStamp",
            "Hidden/DrawingSystem/LayerComposite"
        };

        static DrawingSystemShaderBuildPreprocessor()
        {
            EditorApplication.delayCall += EnsureShadersIncluded;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            EnsureShadersIncluded();
        }

        [MenuItem("Tools/DrawingSystem/Ensure Shaders Included in Build")]
        public static void EnsureShadersIncluded()
        {
            var graphicsSettingsObj = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            if (graphicsSettingsObj == null) return;

            var serializedObj = new SerializedObject(graphicsSettingsObj);
            var arrayProp = serializedObj.FindProperty("m_AlwaysIncludedShaders");
            if (arrayProp == null) return;

            bool modified = false;

            for (int i = 0; i < RequiredShaderGuids.Length; i++)
            {
                string guid = RequiredShaderGuids[i];
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);

                if (shader == null && i < RequiredShaderNames.Length)
                {
                    shader = Shader.Find(RequiredShaderNames[i]);
                }

                if (shader == null) continue;

                // 既に登録済みかチェック
                bool alreadyIncluded = false;
                for (int j = 0; j < arrayProp.arraySize; j++)
                {
                    var elem = arrayProp.GetArrayElementAtIndex(j);
                    if (elem.objectReferenceValue == shader)
                    {
                        alreadyIncluded = true;
                        break;
                    }
                }

                if (!alreadyIncluded)
                {
                    int newIndex = arrayProp.arraySize;
                    arrayProp.InsertArrayElementAtIndex(newIndex);
                    var newElem = arrayProp.GetArrayElementAtIndex(newIndex);
                    newElem.objectReferenceValue = shader;
                    modified = true;
                    Debug.Log($"[DrawingSystem] Added '{shader.name}' to Always Included Shaders for standalone build.");
                }
            }

            if (modified)
            {
                serializedObj.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
        }
    }
}
