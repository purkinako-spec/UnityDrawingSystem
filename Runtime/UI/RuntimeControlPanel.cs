using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DrawingSystem.Brushes;
using DrawingSystem.Layers;
using DrawingSystem.Scoring;

namespace DrawingSystem.UI
{
    /// <summary>
    /// ビルド後の実行環境でもインスペクターで調整可能なほぼすべての機能
    /// （ブラシ、レイヤー、カラーピッカー、採点難易度、保存、終了）を操作可能にする完全版ランタイムUI。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Drawing System/Runtime Control Panel")]
    public class RuntimeControlPanel : MonoBehaviour
    {
        // ─── Inspector Fields ────────────────────────────────────────────────
        [Header("キャンバス参照")]
        [SerializeField] private DrawingCanvas targetCanvas;

        [Header("UI パネルの自動生成")]
        [Tooltip("実行時にUIを動的生成します。")]
        [SerializeField] private bool autoGenerateUI = true;

        [Header("表示設定")]
        [SerializeField] private bool showStats = true;
        [SerializeField] private KeyCode togglePanelKey = KeyCode.Tab;
        [SerializeField] private float panelWidth = 330f;

        // ─── UI References ───────────────────────────────────────────────────
        private Canvas uiCanvas;
        private GameObject panelRoot;
        private GameObject toggleFloatingBtnGo;
        private Text statsText;
        private Text scoreText;
        private Text notificationText;

        // Brush
        private Slider brushSizeSlider;
        private Text brushSizeLabel;
        private Slider smoothSlider;
        private Text smoothLabel;
        private Button eraserBtn;
        private Image eraserBtnImg;

        // HSV Color picker
        private Slider hueSlider;
        private Slider satSlider;
        private Slider valSlider;
        private Image colorPreview;
        private Text hexLabel;
        private float currentHue = 0f, currentSat = 0f, currentVal = 0f;

        // Layer list
        private Transform layerListContainer;
        private Slider layerOpacitySlider;
        private Text layerOpacityLabel;
        private Button[] blendModeButtons;
        private static readonly string[] BlendModeNames = { "通常", "乗算", "加算/Screen", "オーバーレイ" };

        // Scoring
        private Slider difficultySlider;
        private Text difficultyLabel;
        private Toggle realtimeScoreToggle;
        private Slider realtimeIntervalSlider;
        private Text realtimeIntervalLabel;

        // ─── Lifecycle ────────────────────────────────────────────────────────
        private void Awake()
        {
            if (targetCanvas == null)
            {
#if UNITY_2023_1_OR_NEWER
                targetCanvas = FindFirstObjectByType<DrawingCanvas>();
#else
                targetCanvas = FindObjectOfType<DrawingCanvas>();
#endif
            }
        }

        private void Start()
        {
            if (!Application.isPlaying) return;

            if (targetCanvas == null)
            {
#if UNITY_2023_1_OR_NEWER
                targetCanvas = FindFirstObjectByType<DrawingCanvas>();
#else
                targetCanvas = FindObjectOfType<DrawingCanvas>();
#endif
            }

            if (autoGenerateUI)
            {
                BuildRuntimeUI();
            }

            if (targetCanvas != null)
            {
                targetCanvas.OnScoreEvaluated += HandleScoreEvaluated;
            }
        }

        private void OnDestroy()
        {
            if (targetCanvas != null)
            {
                targetCanvas.OnScoreEvaluated -= HandleScoreEvaluated;
            }
        }

        private void Update()
        {
            // Toggle panel via Tab key
            if (Input.GetKeyDown(togglePanelKey))
            {
                TogglePanel();
            }

            // Realtime Device & Pressure Display
            if (showStats && statsText != null && targetCanvas != null)
            {
                int layerCount = targetCanvas.Layers?.Layers.Count ?? 1;
                int activeIdx  = targetCanvas.Layers?.ActiveLayerIndex ?? 0;
                statsText.text = $"[デバイス: {targetCanvas.ActiveDeviceName}]  [筆圧: {targetCanvas.LastInputPressure * 100f:F0}%]  [レイヤー: {activeIdx + 1}/{layerCount}]";
            }
        }

        public void TogglePanel()
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(!panelRoot.activeSelf);
            }
        }

        // ─── Build UI ─────────────────────────────────────────────────────────
        public void BuildRuntimeUI()
        {
            // 既存のUI階層があれば破棄
            var oldCanvas = transform.Find("DrawingSystem_RuntimeUI");
            if (oldCanvas != null)
            {
                if (Application.isPlaying) Destroy(oldCanvas.gameObject);
                else DestroyImmediate(oldCanvas.gameObject);
            }

            // ── Root Canvas ──────────────────────────────────────────────────
            var canvasGo = new GameObject("DrawingSystem_RuntimeUI");
            canvasGo.transform.SetParent(transform, false);

            uiCanvas = canvasGo.AddComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.sortingOrder = 999; // 確実に最前面で表示

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // ── Floating Toggle Button (キーボードのない環境やワンタップ開閉用) ───
            toggleFloatingBtnGo = new GameObject("PanelToggleButton");
            toggleFloatingBtnGo.transform.SetParent(canvasGo.transform, false);
            var btnRt = toggleFloatingBtnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0, 1);
            btnRt.anchorMax = new Vector2(0, 1);
            btnRt.pivot = new Vector2(0, 1);
            btnRt.anchoredPosition = new Vector2(10, -10);
            btnRt.sizeDelta = new Vector2(110, 36);

            var floatImg = AddUIImage(toggleFloatingBtnGo, new Color(0.24f, 0.34f, 0.50f, 0.95f));

            var floatBtn = toggleFloatingBtnGo.AddComponent<Button>();
            floatBtn.targetGraphic = floatImg;
            floatBtn.onClick.AddListener(TogglePanel);

            var floatTxt = CreateLabel(toggleFloatingBtnGo.transform, "☰ パネル(Tab)", 12, FontStyle.Bold, Color.white);
            floatTxt.alignment = TextAnchor.MiddleCenter;
            var ftRt = floatTxt.GetComponent<RectTransform>();
            ftRt.anchorMin = Vector2.zero; ftRt.anchorMax = Vector2.one; ftRt.sizeDelta = Vector2.zero;

            // ── Outer Panel (左側フルハイト) ─────────────────────────────────
            panelRoot = new GameObject("ControlPanel");
            panelRoot.transform.SetParent(canvasGo.transform, false);
            var panelRt = panelRoot.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0, 0);
            panelRt.anchorMax = new Vector2(0, 1);
            panelRt.pivot     = new Vector2(0, 0.5f);
            panelRt.sizeDelta = new Vector2(panelWidth, 0);
            panelRt.anchoredPosition = Vector2.zero;

            // 明るく視認性の高いダークスレート背景
            var panelBg = AddUIImage(panelRoot, new Color(0.18f, 0.20f, 0.26f, 0.98f));

            // ── ScrollRect (スクロール可能領域) ──────────────────────────────
            var scrollObj = new GameObject("ScrollRect");
            scrollObj.transform.SetParent(panelRoot.transform, false);
            var scrollRt = scrollObj.AddComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.sizeDelta = Vector2.zero;
            scrollRt.offsetMin = new Vector2(0, 0);
            scrollRt.offsetMax = new Vector2(0, -48); // 上部に少し余白

            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical   = true;
            scroll.scrollSensitivity = 25f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // Viewport (RectMask2D: ステンシル不要・アルファ不要・URP完全対応)
            var vpObj = new GameObject("Viewport");
            vpObj.transform.SetParent(scrollObj.transform, false);
            var vpRt = vpObj.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.sizeDelta = Vector2.zero;
            vpObj.AddComponent<RectMask2D>();
            scroll.viewport = vpRt;

            // Content Container
            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(vpObj.transform, false);
            var contentRt = contentObj.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot     = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;

            var vlg = contentObj.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 8;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth  = true;
            vlg.childControlHeight = true;

            var csf = contentObj.AddComponent<ContentSizeFitter>();
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = contentRt;
            Transform root = contentObj.transform;

            // ════════════════════════════════════════════════════════════════
            // 1. ヘッダー & デバイスステータス
            // ════════════════════════════════════════════════════════════════
            var headRow = CreateHRow(root, 28);
            CreateLabel(headRow.transform, "🎨 お絵描きシステム 操作盤", 15, FontStyle.Bold, Color.white);
            var closeBtn = CreateButton(headRow.transform, "✕ 閉じる", TogglePanel);
            closeBtn.GetComponent<LayoutElement>().minWidth = 70;
            closeBtn.GetComponent<LayoutElement>().flexibleWidth = 0;

            statsText = CreateLabel(root, "デバイス検出中...", 11, FontStyle.Normal, new Color(0.6f, 0.85f, 1f));

            // ════════════════════════════════════════════════════════════════
            // 2. 基本ツール (Undo / Redo / Clear / Eraser)
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "🖊 描画ツール");

            var toolRow1 = CreateHRow(root, 34);
            CreateButton(toolRow1.transform, "↩ 元に戻す", () => targetCanvas?.Undo());
            CreateButton(toolRow1.transform, "↪ やり直す", () => targetCanvas?.Redo());
            CreateButton(toolRow1.transform, "🗑 全消去", () => targetCanvas?.Clear());

            var toolRow2 = CreateHRow(root, 34);
            eraserBtn = CreateButton(toolRow2.transform, "消しゴム: OFF", null);
            eraserBtnImg = eraserBtn.GetComponent<Image>();
            eraserBtn.onClick.AddListener(() =>
            {
                if (targetCanvas == null) return;
                targetCanvas.IsEraserMode = !targetCanvas.IsEraserMode;
                bool on = targetCanvas.IsEraserMode;
                eraserBtnImg.color = on ? new Color(0.9f, 0.35f, 0.35f) : StyleColor.ButtonBg;
                var txt = eraserBtn.GetComponentInChildren<Text>();
                if (txt != null) txt.text = on ? "消しゴム: ON" : "消しゴム: OFF";
            });

            // ════════════════════════════════════════════════════════════════
            // 3. ブラシサイズ & 手振れ補正 & 背景色
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "📐 ブラシ＆キャンバス設定");

            brushSizeLabel = CreateLabel(root, $"ブラシ太さ: {Mathf.RoundToInt(targetCanvas?.CurrentSize ?? 12)} px", 12, FontStyle.Normal, Color.white);
            brushSizeSlider = CreateSlider(root, 1f, 150f, targetCanvas?.CurrentSize ?? 12f, val =>
            {
                if (targetCanvas != null) targetCanvas.CurrentSize = val;
                if (brushSizeLabel != null) brushSizeLabel.text = $"ブラシ太さ: {Mathf.RoundToInt(val)} px";
            });

            smoothLabel = CreateLabel(root, $"手振れ補正 (Stabilization): {((targetCanvas?.Stabilization ?? 0.5f) * 100f):F0}%", 12, FontStyle.Normal, Color.white);
            smoothSlider = CreateSlider(root, 0f, 1f, targetCanvas?.Stabilization ?? 0.5f, val =>
            {
                if (targetCanvas != null) targetCanvas.Stabilization = val;
                if (smoothLabel != null) smoothLabel.text = $"手振れ補正 (Stabilization): {(val * 100f):F0}%";
            });

            // 背景色変更
            CreateLabel(root, "キャンバス背景色:", 11, FontStyle.Normal, new Color(0.8f, 0.8f, 0.8f));
            var bgRow = CreateHRow(root, 26);
            Color[] bgPresets = { Color.white, new Color(0.96f, 0.94f, 0.90f), new Color(0.8f, 0.8f, 0.8f), new Color(0.18f, 0.18f, 0.18f), Color.black };
            string[] bgNames = { "白", "紙色", "灰", "暗灰", "黒" };
            for (int i = 0; i < bgPresets.Length; i++)
            {
                Color bgC = bgPresets[i];
                var btn = CreateButton(bgRow.transform, bgNames[i], () =>
                {
                    if (targetCanvas != null) targetCanvas.BackgroundColor = bgC;
                });
                btn.GetComponent<Image>().color = bgC;
                var t = btn.GetComponentInChildren<Text>();
                if (t != null) t.color = (bgC.r + bgC.g + bgC.b) > 1.5f ? Color.black : Color.white;
            }

            // ════════════════════════════════════════════════════════════════
            // 4. カラーパレット & HSVカラーピッカー（完全自由変更）
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "🎨 カラーピッカー (色を自由に変更)");

            // カラープリセット
            var presetRow = CreateHRow(root, 28);
            Color[] colorPresets =
            {
                Color.black,
                new Color(0.85f, 0.15f, 0.15f), // 赤
                new Color(0.15f, 0.45f, 0.95f), // 青
                new Color(0.15f, 0.75f, 0.25f), // 緑
                new Color(0.95f, 0.85f, 0.15f), // 黄
                new Color(1f, 0.5f, 0.1f),      // 橙
                new Color(0.65f, 0.2f, 0.8f),   // 紫
                Color.white
            };
            foreach (var col in colorPresets)
            {
                var capturedCol = col;
                var cBtn = CreateButton(presetRow.transform, "", () => ApplyColor(capturedCol));
                cBtn.GetComponent<Image>().color = capturedCol;
            }

            // プレビュー＆HEXコード表示行
            var colorInfoRow = CreateHRow(root, 30);
            var prevGo = new GameObject("ColorPreview");
            prevGo.transform.SetParent(colorInfoRow.transform, false);
            colorPreview = AddUIImage(prevGo, targetCanvas?.CurrentColor ?? Color.black);
            var prevLe = prevGo.AddComponent<LayoutElement>();
            prevLe.minWidth = 50; prevLe.minHeight = 28; prevLe.flexibleWidth = 0;

            hexLabel = CreateLabel(colorInfoRow.transform, ColorToHex(targetCanvas?.CurrentColor ?? Color.black), 13, FontStyle.Bold, Color.white);

            Color.RGBToHSV(targetCanvas?.CurrentColor ?? Color.black, out currentHue, out currentSat, out currentVal);

            // 色相 (Hue: 0~1)
            CreateLabel(root, "色相 (Hue / 色の種類):", 11, FontStyle.Normal, new Color(0.8f, 0.8f, 0.8f));
            hueSlider = CreateSlider(root, 0f, 1f, currentHue, val =>
            {
                currentHue = val;
                RefreshHSVColor();
            });

            // 彩度 (Saturation: 0~1)
            CreateLabel(root, "彩度 (Saturation / 鮮やかさ):", 11, FontStyle.Normal, new Color(0.8f, 0.8f, 0.8f));
            satSlider = CreateSlider(root, 0f, 1f, currentSat, val =>
            {
                currentSat = val;
                RefreshHSVColor();
            });

            // 明度 (Value: 0~1)
            CreateLabel(root, "明度 (Value / 明るさ):", 11, FontStyle.Normal, new Color(0.8f, 0.8f, 0.8f));
            valSlider = CreateSlider(root, 0f, 1f, currentVal, val =>
            {
                currentVal = val;
                RefreshHSVColor();
            });

            // ════════════════════════════════════════════════════════════════
            // 5. レイヤー管理 (一覧・選択・非表示・追加削除・上下移動・不透明度・ブレンド)
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "📋 レイヤー完全操作");

            var layerRow1 = CreateHRow(root, 32);
            CreateButton(layerRow1.transform, "＋ 新規追加", () =>
            {
                targetCanvas?.Layers?.AddLayer();
                targetCanvas?.RefreshComposite();
                RebuildLayerList();
            });
            CreateButton(layerRow1.transform, "－ 削除", () =>
            {
                var lm = targetCanvas?.Layers;
                if (lm != null && lm.Layers.Count > 1)
                {
                    lm.RemoveLayer(lm.ActiveLayerIndex);
                    targetCanvas?.RefreshComposite();
                    RebuildLayerList();
                }
            });

            var layerRow2 = CreateHRow(root, 30);
            CreateButton(layerRow2.transform, "▲ レイヤーを上へ移動", () =>
            {
                var lm = targetCanvas?.Layers;
                if (lm == null) return;
                int idx = lm.ActiveLayerIndex;
                if (idx < lm.Layers.Count - 1)
                {
                    lm.MoveLayer(idx, idx + 1);
                    targetCanvas?.RefreshComposite();
                    RebuildLayerList();
                }
            });
            CreateButton(layerRow2.transform, "▼ レイヤーを下へ移動", () =>
            {
                var lm = targetCanvas?.Layers;
                if (lm == null) return;
                int idx = lm.ActiveLayerIndex;
                if (idx > 0)
                {
                    lm.MoveLayer(idx, idx - 1);
                    targetCanvas?.RefreshComposite();
                    RebuildLayerList();
                }
            });

            // レイヤーリストの動的コンテナ
            var listContainer = new GameObject("LayerListContainer");
            listContainer.transform.SetParent(root, false);
            layerListContainer = listContainer.transform;
            var listVlg = listContainer.AddComponent<VerticalLayoutGroup>();
            listVlg.spacing = 3;
            listVlg.childForceExpandWidth  = true;
            listVlg.childForceExpandHeight = false;
            listVlg.childControlWidth  = true;
            listVlg.childControlHeight = true;
            listContainer.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            listContainer.AddComponent<LayoutElement>();

            // 選択中レイヤーの不透明度
            layerOpacityLabel = CreateLabel(root, "選択中レイヤー不透明度: 100%", 12, FontStyle.Normal, Color.white);
            layerOpacitySlider = CreateSlider(root, 0f, 1f, 1f, val =>
            {
                var lm = targetCanvas?.Layers;
                if (lm?.ActiveLayer == null) return;
                lm.ActiveLayer.Opacity = val;
                targetCanvas?.RefreshComposite();
                if (layerOpacityLabel != null) layerOpacityLabel.text = $"選択中レイヤー不透明度: {(val * 100f):F0}%";
            });

            // 選択中レイヤーのブレンドモード切り替えボタン行
            CreateLabel(root, "合成モード (Blend Mode):", 12, FontStyle.Normal, Color.white);
            var blendRow = CreateHRow(root, 30);
            blendModeButtons = new Button[BlendModeNames.Length];
            for (int i = 0; i < BlendModeNames.Length; i++)
            {
                int capturedIdx = i;
                var b = CreateButton(blendRow.transform, BlendModeNames[i], () =>
                {
                    var lm = targetCanvas?.Layers;
                    if (lm?.ActiveLayer == null) return;
                    lm.ActiveLayer.BlendMode = (LayerBlendMode)capturedIdx;
                    targetCanvas?.RefreshComposite();
                    UpdateBlendModeButtons(capturedIdx);
                });
                blendModeButtons[i] = b;
            }

            // 初期レイヤーリスト生成
            RebuildLayerList();

            // ════════════════════════════════════════════════════════════════
            // 6. 採点・ゲーム評価システム
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "🎯 採点＆難易度調整");

            float initDiff = 0.5f;
            if (targetCanvas?.DefaultScorerAsset is NormalizedShapeScorer nss) initDiff = nss.Difficulty;

            difficultyLabel = CreateLabel(root, $"判定厳しさ (難易度): {initDiff:F2}", 12, FontStyle.Normal, Color.white);
            difficultySlider = CreateSlider(root, 0f, 1f, initDiff, val =>
            {
                if (targetCanvas?.DefaultScorerAsset is NormalizedShapeScorer ns)
                    ns.Difficulty = val;
                if (difficultyLabel != null) difficultyLabel.text = $"判定厳しさ (難易度): {val:F2}";
            });

            // リアルタイム採点設定
            var rtRow = CreateHRow(root, 28);
            CreateLabel(rtRow.transform, "常時自動採点:", 11, FontStyle.Normal, Color.white);
            realtimeScoreToggle = CreateToggle(rtRow.transform, targetCanvas?.EnableRealtimeScoring ?? false, isOn =>
            {
                if (targetCanvas != null) targetCanvas.EnableRealtimeScoring = isOn;
            });

            realtimeIntervalLabel = CreateLabel(root, $"採点間隔: {(targetCanvas?.RealtimeScoringInterval ?? 0.5f):F1} 秒", 11, FontStyle.Normal, Color.white);
            realtimeIntervalSlider = CreateSlider(root, 0.1f, 3f, targetCanvas?.RealtimeScoringInterval ?? 0.5f, val =>
            {
                if (targetCanvas != null) targetCanvas.RealtimeScoringInterval = val;
                if (realtimeIntervalLabel != null) realtimeIntervalLabel.text = $"採点間隔: {val:F1} 秒";
            });

            var scoreRow = CreateHRow(root, 36);
            var evalBtn = CreateButton(scoreRow.transform, "🎯 今すぐ採点する！", () =>
            {
                if (targetCanvas != null)
                {
                    var res = targetCanvas.EvaluateCurrentDrawing();
                    HandleScoreEvaluated(res);
                }
            });
            evalBtn.GetComponent<Image>().color = new Color(0.2f, 0.55f, 0.35f);

            scoreText = CreateLabel(root, "スコア: まだ採点されていません", 14, FontStyle.Bold, new Color(1f, 0.9f, 0.2f));

            // ════════════════════════════════════════════════════════════════
            // 7. 保存・画像エクスポート
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "💾 作品の保存・エクスポート");

            var expRow = CreateHRow(root, 32);
            CreateButton(expRow.transform, "PNG 保存", () => ExportDrawing("png"));
            CreateButton(expRow.transform, "JPG 保存", () => ExportDrawing("jpg"));
            CreateButton(expRow.transform, "SVG 保存", () => ExportDrawing("svg"));

            notificationText = CreateLabel(root, "", 11, FontStyle.Italic, new Color(0.4f, 1f, 0.5f));

            // ════════════════════════════════════════════════════════════════
            // 8. ゲーム終了ボタン (ユーザー要望)
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "⏹ アプリケーション");

            var quitRow = CreateHRow(root, 40);
            var quitBtn = CreateButton(quitRow.transform, "⏹ ゲームを終了する", () =>
            {
                Debug.Log("[DrawingSystem] ゲーム終了リクエストを受信しました。");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            var qImg = quitBtn.GetComponent<Image>();
            if (qImg != null) qImg.color = new Color(0.75f, 0.18f, 0.18f);

            // 全UI要素のレイヤーを "UI" レイヤーに確実に設定
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) SetLayerRecursively(canvasGo, uiLayer);
        }

        // ─── Layer List Rebuild ───────────────────────────────────────────────
        private void RebuildLayerList()
        {
            if (layerListContainer == null) return;

            for (int i = layerListContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(layerListContainer.GetChild(i).gameObject);
            }

            var lm = targetCanvas?.Layers;
            if (lm == null) return;

            // 上位レイヤーをリストの上側に表示
            for (int i = lm.Layers.Count - 1; i >= 0; i--)
            {
                int layerIndex = i;
                var layer = lm.Layers[i];
                bool isActive = (i == lm.ActiveLayerIndex);

                var rowGo = new GameObject($"LayerRow_{i}");
                rowGo.transform.SetParent(layerListContainer, false);
                var rowImg = AddUIImage(rowGo, isActive ? new Color(0.25f, 0.45f, 0.75f, 0.5f) : new Color(0.15f, 0.16f, 0.20f, 0.5f));

                var rowHlg = rowGo.AddComponent<HorizontalLayoutGroup>();
                rowHlg.padding = new RectOffset(4, 4, 3, 3);
                rowHlg.spacing = 6;
                rowHlg.childForceExpandWidth  = false;
                rowHlg.childForceExpandHeight = true;
                rowHlg.childControlWidth  = true; // 子のサイズを確実に制御
                rowHlg.childControlHeight = true;

                var rowLe = rowGo.AddComponent<LayoutElement>();
                rowLe.minHeight = 32;

                // 👁 表示/非表示トグルボタン
                var visBtn = CreateButton(rowGo.transform, layer.IsVisible ? "👁" : "🚫", null);
                var visLe = visBtn.GetComponent<LayoutElement>();
                visLe.minWidth = 36;
                visLe.preferredWidth = 36;
                visLe.flexibleWidth = 0;
                visBtn.onClick.AddListener(() =>
                {
                    layer.IsVisible = !layer.IsVisible;
                    targetCanvas?.RefreshComposite();
                    RebuildLayerList();
                });

                // レイヤー選択ボタン（クリックで行全体がアクティブ化）
                string layerName = string.IsNullOrEmpty(layer.LayerName) ? $"レイヤー {layerIndex + 1}" : layer.LayerName;
                string displayName = isActive ? $"▶ {layerName}" : $"   {layerName}";
                var selectBtn = CreateButton(rowGo.transform, displayName, () =>
                {
                    lm.SetActiveLayer(layerIndex);
                    SyncActiveLayerControls();
                    RebuildLayerList();
                });
                var selLe = selectBtn.GetComponent<LayoutElement>();
                selLe.minWidth = 140;
                selLe.flexibleWidth = 1; // 横幅いっぱいに広げてクリックしやすくする
                var selTxt = selectBtn.GetComponentInChildren<Text>();
                if (selTxt != null)
                {
                    selTxt.alignment = TextAnchor.MiddleLeft;
                    selTxt.fontSize  = 13;
                    selTxt.color     = Color.white;
                    selTxt.fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal;
                    selTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    selTxt.verticalOverflow   = VerticalWrapMode.Overflow;
                    var sRt = selTxt.GetComponent<RectTransform>();
                    sRt.offsetMin = new Vector2(10, 0); // 左側に適度なパディング
                    sRt.offsetMax = new Vector2(-6, 0);
                }

                var selImg = selectBtn.GetComponent<Image>();
                if (selImg != null)
                {
                    selImg.color = isActive ? new Color(0.25f, 0.55f, 0.95f) : StyleColor.ButtonBg;
                }

                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer >= 0) SetLayerRecursively(rowGo, uiLayer);
            }

            SyncActiveLayerControls();
        }

        private void SyncActiveLayerControls()
        {
            var lm = targetCanvas?.Layers;
            var active = lm?.ActiveLayer;
            if (active == null) return;

            if (layerOpacitySlider != null)
                layerOpacitySlider.SetValueWithoutNotify(active.Opacity);
            if (layerOpacityLabel != null)
                layerOpacityLabel.text = $"選択中レイヤー不透明度: {(active.Opacity * 100f):F0}%";

            UpdateBlendModeButtons((int)active.BlendMode);
        }

        private void UpdateBlendModeButtons(int activeIndex)
        {
            if (blendModeButtons == null) return;
            for (int i = 0; i < blendModeButtons.Length; i++)
            {
                if (blendModeButtons[i] == null) continue;
                var img = blendModeButtons[i].GetComponent<Image>();
                if (img != null)
                {
                    img.color = (i == activeIndex)
                        ? new Color(0.28f, 0.58f, 0.95f) // 選択中ハイライト
                        : StyleColor.ButtonBg;           // 通常
                }
            }
        }

        // ─── HSV Color Helpers ────────────────────────────────────────────────
        private void RefreshHSVColor()
        {
            var col = Color.HSVToRGB(currentHue, currentSat, currentVal);
            ApplyColor(col);
        }

        private void ApplyColor(Color col)
        {
            if (targetCanvas != null)
            {
                targetCanvas.CurrentColor = col;
                targetCanvas.IsEraserMode = false;
                if (eraserBtnImg != null) eraserBtnImg.color = StyleColor.ButtonBg;
                var eraserTxt = eraserBtn?.GetComponentInChildren<Text>();
                if (eraserTxt != null) eraserTxt.text = "消しゴム: OFF";
            }

            if (colorPreview != null) colorPreview.color = col;
            if (hexLabel != null) hexLabel.text = ColorToHex(col);

            Color.RGBToHSV(col, out currentHue, out currentSat, out currentVal);
            if (hueSlider != null) hueSlider.SetValueWithoutNotify(currentHue);
            if (satSlider != null) satSlider.SetValueWithoutNotify(currentSat);
            if (valSlider != null) valSlider.SetValueWithoutNotify(currentVal);
        }

        private static string ColorToHex(Color c)
        {
            return $"#{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}";
        }

        // ─── Score & Export ───────────────────────────────────────────────────
        private void HandleScoreEvaluated(ScoringResult result)
        {
            if (scoreText != null)
            {
                scoreText.text = $"得点: {result.Score * 100f:F1} 点\n({result.ScorerName})";
            }
        }

        private void ExportDrawing(string format)
        {
            if (targetCanvas == null) return;

            string dir = Path.Combine(Application.persistentDataPath, "Drawings");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string file = Path.Combine(dir, $"drawing_{DateTime.Now:yyyyMMdd_HHmmss}.{format}");
            switch (format.ToLowerInvariant())
            {
                case "png": targetCanvas.ExportPNG(file); break;
                case "jpg": targetCanvas.ExportJPG(file); break;
                case "svg": targetCanvas.ExportSVG(file); break;
            }
            ShowNotification($"保存完了: {Path.GetFileName(file)}");
        }

        private void ShowNotification(string msg)
        {
            Debug.Log($"[RuntimeControlPanel] {msg}");
            if (notificationText != null) notificationText.text = msg;
        }

        // ════════════════════════════════════════════════════════════════════
        //  UI Factory Helpers
        // ════════════════════════════════════════════════════════════════════
        private static class StyleColor
        {
            public static readonly Color ButtonBg   = new Color(0.28f, 0.40f, 0.58f);
            public static readonly Color SectionBg  = new Color(0.23f, 0.26f, 0.34f);
            public static readonly Color HeaderText = new Color(0.98f, 0.85f, 0.35f);
        }

        private static Sprite cachedWhiteSprite;
        private static Sprite GetWhiteSprite()
        {
            if (cachedWhiteSprite != null) return cachedWhiteSprite;
            var tex = Texture2D.whiteTexture;
            cachedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            cachedWhiteSprite.name = "UI_WhiteSprite";
            cachedWhiteSprite.hideFlags = HideFlags.DontSave;
            return cachedWhiteSprite;
        }

        /// <summary>
        /// URP/Built-in 両対応: 白スプライトと標準マテリアルを確実にアサインした Image を追加
        /// </summary>
        private static Image AddUIImage(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.sprite = GetWhiteSprite();
            img.material = Canvas.GetDefaultCanvasMaterial();
            img.color = color;
            return img;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                if (child != null) SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static void CreateSectionHeader(Transform parent, string title)
        {
            var go = new GameObject("SectionHeader");
            go.transform.SetParent(parent, false);
            var img = AddUIImage(go, StyleColor.SectionBg);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = string.IsNullOrEmpty(title) ? 6 : 24;

            if (!string.IsNullOrEmpty(title))
            {
                var txt = CreateLabel(go.transform, title, 12, FontStyle.Bold, StyleColor.HeaderText);
                var txtRt = txt.GetComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.sizeDelta = Vector2.zero;
                txt.alignment   = TextAnchor.MiddleLeft;
                txt.GetComponent<LayoutElement>().minHeight = 0;
            }
        }

        private static Font cachedFont;

        private static Font GetSafeFont()
        {
            if (cachedFont != null) return cachedFont;

            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cachedFont != null) return cachedFont;

            cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (cachedFont != null) return cachedFont;

            string[] osFonts = { "Arial", "Segoe UI", "Yu Gothic UI", "Meiryo", "Helvetica" };
            foreach (var fontName in osFonts)
            {
                try
                {
                    cachedFont = Font.CreateDynamicFontFromOSFont(fontName, 14);
                    if (cachedFont != null) return cachedFont;
                }
                catch { }
            }

            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            if (fonts != null && fonts.Length > 0)
            {
                for (int i = 0; i < fonts.Length; i++)
                {
                    if (fonts[i] != null)
                    {
                        cachedFont = fonts[i];
                        return cachedFont;
                    }
                }
            }

            return null;
        }

        private static Text CreateLabel(Transform parent, string text, int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text               = text;
            txt.font               = GetSafeFont();
            txt.material           = Canvas.GetDefaultCanvasMaterial();
            txt.fontSize           = fontSize;
            txt.fontStyle          = style;
            txt.color              = color;
            txt.alignment          = TextAnchor.MiddleLeft;
            txt.supportRichText    = false;
            txt.raycastTarget      = false; // ラベルがクリック判定を遮らないよう無効化
            txt.horizontalOverflow = HorizontalWrapMode.Overflow; // 文字列が枠サイズで消えるのを完全に防ぐ
            txt.verticalOverflow   = VerticalWrapMode.Overflow;   // 縦幅不足による非表示（Truncate）を防止

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = fontSize + 8;
            return txt;
        }

        private static Button CreateButton(Transform parent, string title, Action onClick)
        {
            var go  = new GameObject("Btn_" + title);
            go.transform.SetParent(parent, false);
            var img = AddUIImage(go, StyleColor.ButtonBg);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var cb = new ColorBlock
            {
                normalColor      = StyleColor.ButtonBg,
                highlightedColor = new Color(0.38f, 0.52f, 0.74f),
                pressedColor     = new Color(0.20f, 0.30f, 0.45f),
                selectedColor    = StyleColor.ButtonBg,
                disabledColor    = new Color(0.4f, 0.4f, 0.4f, 0.5f),
                colorMultiplier  = 1f,
                fadeDuration     = 0.1f
            };
            btn.colors = cb;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight    = 30;
            le.flexibleWidth = 1;

            if (!string.IsNullOrEmpty(title))
            {
                var txt   = CreateLabel(go.transform, title, 11, FontStyle.Bold, Color.white);
                txt.alignment = TextAnchor.MiddleCenter;
                var txtRt = txt.GetComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = new Vector2(4, 0);
                txtRt.offsetMax = new Vector2(-4, 0);
            }

            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        private static GameObject CreateHRow(Transform parent, float minH = 32f)
        {
            var go = new GameObject("HRow");
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing               = 5;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth  = true;
            hlg.childControlHeight = true;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = minH;
            return go;
        }

        private static Slider CreateSlider(Transform parent, float min, float max, float current, Action<float> onChanged)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 22;

            // スライダー溝の背景（明るく視認性の高いグレー）
            var bgGo  = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, false);
            AddUIImage(bgGo, new Color(0.28f, 0.30f, 0.38f));
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 0.25f);
            bgRt.anchorMax = new Vector2(1, 0.75f);
            bgRt.sizeDelta = Vector2.zero;

            // 塗りつぶし領域
            var faGo = new GameObject("Fill Area");
            faGo.transform.SetParent(go.transform, false);
            var faRt = faGo.AddComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0, 0.25f);
            faRt.anchorMax = new Vector2(1, 0.75f);
            faRt.sizeDelta = Vector2.zero;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(faGo.transform, false);
            var fillImg = AddUIImage(fillGo, new Color(0.30f, 0.65f, 1.0f));
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.sizeDelta = Vector2.zero;

            // つまみ領域
            var haGo = new GameObject("Handle Slide Area");
            haGo.transform.SetParent(go.transform, false);
            var haRt = haGo.AddComponent<RectTransform>();
            haRt.anchorMin = new Vector2(0, 0);
            haRt.anchorMax = new Vector2(1, 1);
            haRt.sizeDelta = new Vector2(-14, 0);

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(haGo.transform, false);
            var handleImg = AddUIImage(handleGo, Color.white);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(14, 0);

            var slider = go.AddComponent<Slider>();
            slider.minValue   = min;
            slider.maxValue   = max;
            slider.fillRect   = fillRt;
            slider.handleRect = handleRt;
            slider.direction  = Slider.Direction.LeftToRight;
            slider.value      = current;

            if (onChanged != null)
                slider.onValueChanged.AddListener(val => onChanged(val));

            return slider;
        }

        private static Toggle CreateToggle(Transform parent, bool initialValue, Action<bool> onChanged)
        {
            var go = new GameObject("Toggle");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 30; le.minHeight = 26; le.flexibleWidth = 0;

            var bgImg = AddUIImage(go, new Color(0.2f, 0.2f, 0.25f));

            var checkGo = new GameObject("Checkmark");
            checkGo.transform.SetParent(go.transform, false);
            var checkImg = AddUIImage(checkGo, new Color(0.2f, 0.8f, 0.4f));
            var chkRt = checkGo.GetComponent<RectTransform>();
            chkRt.anchorMin = new Vector2(0.2f, 0.2f);
            chkRt.anchorMax = new Vector2(0.8f, 0.8f);
            chkRt.sizeDelta = Vector2.zero;

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.isOn = initialValue;

            if (onChanged != null)
                toggle.onValueChanged.AddListener(val => onChanged(val));

            return toggle;
        }
    }
}
