using UnityEngine;
using UnityEditor;
using DrawingSystem.Layers;

namespace DrawingSystem.Editor
{
    [CustomEditor(typeof(DrawingCanvas))]
    public class DrawingCanvasEditor : UnityEditor.Editor
    {
        private bool layerFoldout = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var canvas = (DrawingCanvas)target;
            if (canvas == null) return;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Editor Actions (Play Mode Only)", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);

            // ── デバイス & 筆圧 ──────────────────────────────
            EditorGUILayout.LabelField("リアルタイム ペン設定", EditorStyles.boldLabel);
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("認識中デバイス", canvas.ActiveDeviceName, EditorStyles.helpBox);
                EditorGUILayout.LabelField("直近の入力元", canvas.LastInputDevice, EditorStyles.helpBox);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), canvas.LastInputPressure, $"筆圧: {canvas.LastInputPressure * 100f:F0}%");
                EditorGUILayout.Space(3);
            }

            canvas.CurrentColor = EditorGUILayout.ColorField("ペンの色 (Color)", canvas.CurrentColor);
            canvas.CurrentSize = EditorGUILayout.Slider("太さ (Size)", canvas.CurrentSize, 1f, 100f);
            canvas.Stabilization = EditorGUILayout.Slider("手振れ補正 (Smooth)", canvas.Stabilization, 0f, 1f);
            canvas.IsEraserMode = EditorGUILayout.Toggle("消しゴムモード", canvas.IsEraserMode);

            EditorGUILayout.Space(5);
            if (GUILayout.Button("合成テクスチャを強制更新")) canvas.RefreshComposite();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Undo")) canvas.Undo();
            if (GUILayout.Button("Redo")) canvas.Redo();
            EditorGUILayout.EndHorizontal();

            // ── レイヤーパネル ────────────────────────────────
            EditorGUILayout.Space(8);
            layerFoldout = EditorGUILayout.Foldout(layerFoldout, "レイヤー", true, EditorStyles.foldoutHeader);
            if (layerFoldout && Application.isPlaying && canvas.Layers != null)
            {
                var lm = canvas.Layers;
                var layers = lm.Layers;

                // 追加・削除ボタン
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("＋ レイヤー追加", GUILayout.Height(22)))
                {
                    lm.AddLayer();
                    canvas.RefreshComposite();
                }
                using (new EditorGUI.DisabledGroupScope(layers.Count <= 1))
                {
                    if (GUILayout.Button("－ 選択削除", GUILayout.Height(22)))
                    {
                        lm.RemoveLayer(lm.ActiveLayerIndex);
                        canvas.RefreshComposite();
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);

                // レイヤー一覧（上が最前面 = 末尾から表示）
                for (int i = layers.Count - 1; i >= 0; i--)
                {
                    var layer = layers[i];
                    bool isActive = (i == lm.ActiveLayerIndex);

                    // アクティブ行をハイライト
                    var rowStyle = new GUIStyle(EditorStyles.helpBox);
                    if (isActive)
                    {
                        GUI.backgroundColor = new Color(0.4f, 0.7f, 1f, 1f);
                    }
                    EditorGUILayout.BeginVertical(rowStyle);
                    GUI.backgroundColor = Color.white;

                    EditorGUILayout.BeginHorizontal();

                    // 表示/非表示トグル
                    bool newVisible = EditorGUILayout.Toggle(layer.IsVisible, GUILayout.Width(18));
                    if (newVisible != layer.IsVisible)
                    {
                        layer.IsVisible = newVisible;
                        canvas.RefreshComposite();
                    }

                    // レイヤー名（クリックで選択）
                    if (GUILayout.Button(layer.LayerName, isActive ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.ExpandWidth(true)))
                    {
                        lm.SetActiveLayer(i);
                    }

                    // 順序移動
                    using (new EditorGUI.DisabledGroupScope(i == layers.Count - 1))
                    {
                        if (GUILayout.Button("↑", GUILayout.Width(22)))
                        {
                            lm.MoveLayer(i, i + 1);
                            canvas.RefreshComposite();
                        }
                    }
                    using (new EditorGUI.DisabledGroupScope(i == 0))
                    {
                        if (GUILayout.Button("↓", GUILayout.Width(22)))
                        {
                            lm.MoveLayer(i, i - 1);
                            canvas.RefreshComposite();
                        }
                    }

                    EditorGUILayout.EndHorizontal();

                    // 不透明度スライダー（アクティブ行のみ展開表示）
                    if (isActive)
                    {
                        EditorGUI.indentLevel++;
                        float newOpacity = EditorGUILayout.Slider("不透明度", layer.Opacity, 0f, 1f);
                        if (!Mathf.Approximately(newOpacity, layer.Opacity))
                        {
                            layer.Opacity = newOpacity;
                            canvas.RefreshComposite();
                        }

                        LayerBlendMode newBlend = (LayerBlendMode)EditorGUILayout.EnumPopup("ブレンドモード", layer.BlendMode);
                        if (newBlend != layer.BlendMode)
                        {
                            layer.BlendMode = newBlend;
                            canvas.RefreshComposite();
                        }

                        // レイヤー名変更
                        string newName = EditorGUILayout.TextField("レイヤー名", layer.LayerName);
                        if (newName != layer.LayerName) layer.LayerName = newName;

                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(1);
                }
            }
            else if (layerFoldout && !Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play モード中に表示されます。", MessageType.Info);
            }

            // ── 採点 & エクスポート ───────────────────────────
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("採点テスト", EditorStyles.boldLabel);
            if (GUILayout.Button("見本と比較して採点を実行"))
            {
                var result = canvas.EvaluateCurrentDrawing();
                EditorUtility.DisplayDialog("採点結果", $"スコア: {result.Score * 100f:F1}%\nアルゴリズム: {result.ScorerName}", "OK");
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("エクスポートテスト", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("PNG 保存"))
            {
                string path = EditorUtility.SaveFilePanel("Save PNG", "", "drawing.png", "png");
                if (!string.IsNullOrEmpty(path)) { canvas.ExportPNG(path); EditorUtility.RevealInFinder(path); }
            }
            if (GUILayout.Button("JPG 保存"))
            {
                string path = EditorUtility.SaveFilePanel("Save JPG", "", "drawing.jpg", "jpg");
                if (!string.IsNullOrEmpty(path)) { canvas.ExportJPG(path); EditorUtility.RevealInFinder(path); }
            }
            if (GUILayout.Button("SVG 保存"))
            {
                string path = EditorUtility.SaveFilePanel("Save SVG", "", "drawing.svg", "svg");
                if (!string.IsNullOrEmpty(path)) { canvas.ExportSVG(path); EditorUtility.RevealInFinder(path); }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();

            // 毎フレーム再描画（筆圧バー・レイヤー状態を即時反映）
            if (Application.isPlaying) Repaint();
        }
    }
}
