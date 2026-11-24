using UnityEngine;
using UnityEditor;
using DrawingSystem.UI;

namespace DrawingSystem.Editor
{
    [CustomEditor(typeof(RuntimeControlPanel))]
    public class RuntimeControlPanelEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var panel = (RuntimeControlPanel)target;
            if (panel == null) return;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("ランタイムUI 生成・管理", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("💡 [▶ 再生 (Play)] ボタンを押すと、ゲーム画面左側に「描画操作パネル」と「☰ パネル(Tab)」開閉ボタンが完全展開されます。", MessageType.Info);
            }
            else
            {
                if (GUILayout.Button("🎨 UI パネルを再生成 (Rebuild UI)"))
                {
                    panel.BuildRuntimeUI();
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("含まれる機能一覧 (ビルド・インゲーム対応):", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("・ペン太さ & 手振れ補正スライダー", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("・消しゴムモード & Undo / Redo / Clear", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("・無段階 HSV カラーピッカー (色相/彩度/明度/HEX)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("・キャンバス背景色ワンタップ変更 (白/紙/灰/黒)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("・完全なレイヤー管理 (一覧/可視/選択/上下移動/不透明度/ブレンド)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("・難易度 float 調整 & リアルタイム自動採点", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("・PNG / JPG / SVG ファイル書き出し", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("・ゲーム終了ボタン (Quit)", EditorStyles.miniLabel);
        }
    }
}
