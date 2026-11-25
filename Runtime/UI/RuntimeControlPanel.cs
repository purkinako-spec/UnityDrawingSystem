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
    /// Canvas はシーンルートに生成し、必ず最前面に表示する。
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
        [SerializeField] private float panelWidth = 320f;

        // ─── UI References ───────────────────────────────────────────────────
        private Canvas uiCanvas;
        private GameObject uiRoot;
        private GameObject panelRoot;
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
        private Button eyedropperBtn;
        private Image eyedropperBtnImg;

        // HSV Color picker
        private Slider hueSlider;
        private Slider satSlider;
        private Slider valSlider;
        private Image colorPreview;
        private Text hexLabel;
        private float currentHue = 0f, currentSat = 0f, currentVal = 0f;

        // Layer list
        private RectTransform layerListContainer;
        private LayoutElement layerListLayoutElement;
        private Slider layerOpacitySlider;
        private Text layerOpacityLabel;
        private Button[] blendModeButtons;
        private static readonly string[] BlendModeNames = { "通常", "乗算", "加算", "オーバーレイ" };

        private int lastKnownLayerCount = -1;
        private int lastKnownActiveIndex = -1;

        // Scoring
        private Slider difficultySlider;
        private Text difficultyLabel;
        private Toggle realtimeScoreToggle;
        private Slider realtimeIntervalSlider;
        private Text realtimeIntervalLabel;

        // ─── Lifecycle ────────────────────────────────────────────────────────
        private void Awake()
        {
            FindAndInitCanvas();
        }

        private void Start()
        {
            if (!Application.isPlaying) return;

            FindAndInitCanvas();

            if (autoGenerateUI)
            {
                BuildRuntimeUI();
            }

            if (targetCanvas != null)
            {
                targetCanvas.OnScoreEvaluated += HandleScoreEvaluated;
                targetCanvas.OnToolModeChanged -= HandleToolModeChanged;
                targetCanvas.OnToolModeChanged += HandleToolModeChanged;
                targetCanvas.OnColorPicked -= HandleColorPicked;
                targetCanvas.OnColorPicked += HandleColorPicked;
            }

            SubscribeLayerEvents();
        }

        private void FindAndInitCanvas()
        {
            if (targetCanvas == null)
            {
                targetCanvas = GetComponent<DrawingCanvas>();
            }

            if (targetCanvas == null)
            {
#if UNITY_2023_1_OR_NEWER
                targetCanvas = FindFirstObjectByType<DrawingCanvas>();
#else
                targetCanvas = FindObjectOfType<DrawingCanvas>();
#endif
            }

            if (targetCanvas != null)
            {
                targetCanvas.EnsureInitialized();
            }
        }

        private void SubscribeLayerEvents()
        {
            if (targetCanvas?.Layers != null)
            {
                targetCanvas.Layers.OnLayersChanged -= HandleLayersChanged;
                targetCanvas.Layers.OnLayersChanged += HandleLayersChanged;
            }
        }

        private void UnsubscribeLayerEvents()
        {
            if (targetCanvas?.Layers != null)
            {
                targetCanvas.Layers.OnLayersChanged -= HandleLayersChanged;
            }
        }

        private void HandleLayersChanged()
        {
            RebuildLayerList();
        }

        private void OnDestroy()
        {
            if (targetCanvas != null)
            {
                targetCanvas.OnScoreEvaluated -= HandleScoreEvaluated;
                targetCanvas.OnToolModeChanged -= HandleToolModeChanged;
                targetCanvas.OnColorPicked -= HandleColorPicked;
            }

            UnsubscribeLayerEvents();

            if (uiRoot != null)
            {
                Destroy(uiRoot);
            }

            cachedFont = null;
            cachedWhiteSprite = null;
        }

        private void HandleToolModeChanged(bool isEraser, float size)
        {
            if (brushSizeSlider != null) brushSizeSlider.SetValueWithoutNotify(size);
            if (brushSizeLabel != null)
            {
                string title = isEraser ? "消しゴム太さ" : "ブラシ太さ";
                brushSizeLabel.text = $"{title}: {Mathf.RoundToInt(size)} px";
            }
            if (eraserBtn != null)
            {
                var txt = eraserBtn.GetComponentInChildren<Text>();
                if (txt != null) txt.text = isEraser ? "消しゴム: ON" : "消しゴム: OFF";
            }
            if (eraserBtnImg != null)
            {
                eraserBtnImg.color = isEraser ? new Color(0.85f, 0.3f, 0.3f) : StyleColor.ButtonBg;
            }
            if (eyedropperBtnImg != null && (targetCanvas == null || !targetCanvas.IsEyedropperMode))
            {
                eyedropperBtnImg.color = StyleColor.ButtonBg;
            }
        }

        private void HandleColorPicked(Color col)
        {
            ApplyColor(col);
            if (eyedropperBtnImg != null)
            {
                eyedropperBtnImg.color = StyleColor.ButtonBg;
            }
        }

        private void Update()
        {
            // Toggle panel via Tab key (or any configured key)
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

            // レイヤー状態の同期・不整合検知時の自動復帰
            if (targetCanvas != null && layerListContainer != null)
            {
                var lm = targetCanvas.Layers;
                if (lm != null && lm.Layers != null)
                {
                    int currentCount  = lm.Layers.Count;
                    int currentActive = lm.ActiveLayerIndex;

                    if (currentCount != lastKnownLayerCount || currentActive != lastKnownActiveIndex)
                    {
                        RebuildLayerList();
                    }
                }
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
            FindAndInitCanvas();

            if (uiRoot != null)
            {
                Destroy(uiRoot);
                uiRoot = null;
            }

            var oldUI = GameObject.Find("DrawingSystem_RuntimeUI");
            if (oldUI != null)
            {
                Destroy(oldUI);
            }

            cachedFont = null;
            cachedWhiteSprite = null;

            // ── Root Canvas (シーンルートに Overlay Canvas として生成) ──
            uiRoot = new GameObject("DrawingSystem_RuntimeUI");

            uiCanvas = uiRoot.AddComponent<Canvas>();
            uiCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            uiCanvas.sortingOrder = 999;

            var scaler = uiRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            uiRoot.AddComponent<GraphicRaycaster>();

            // EventSystem が存在しない場合は自動生成
            var es = FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }

            // ── Floating Toggle Button ───────────────────────────────────────
            var toggleGo = new GameObject("PanelToggleButton");
            toggleGo.transform.SetParent(uiRoot.transform, false);
            var tRt = toggleGo.AddComponent<RectTransform>();
            tRt.anchorMin        = new Vector2(0, 1);
            tRt.anchorMax        = new Vector2(0, 1);
            tRt.pivot            = new Vector2(0, 1);
            tRt.anchoredPosition = new Vector2(8, -8);
            tRt.sizeDelta        = new Vector2(100, 32);

            var floatImg = AddUIImage(toggleGo, new Color(0.20f, 0.30f, 0.50f, 1f));
            var floatBtn = toggleGo.AddComponent<Button>();
            floatBtn.targetGraphic = floatImg;
            floatBtn.onClick.AddListener(TogglePanel);
            var floatTxt = CreateLabel(toggleGo.transform, "☰ メニュー", 11, FontStyle.Bold, Color.white);
            floatTxt.alignment = TextAnchor.MiddleCenter;
            var ftRt = floatTxt.GetComponent<RectTransform>();
            ftRt.anchorMin = Vector2.zero; ftRt.anchorMax = Vector2.one; ftRt.sizeDelta = Vector2.zero;
            floatTxt.raycastTarget = false;

            // ── Outer Panel ──────────────────────────────────────────────────
            panelRoot = new GameObject("ControlPanel");
            panelRoot.transform.SetParent(uiRoot.transform, false);
            var panelRt = panelRoot.AddComponent<RectTransform>();
            panelRt.anchorMin        = new Vector2(0, 0);
            panelRt.anchorMax        = new Vector2(0, 1);
            panelRt.pivot            = new Vector2(0, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta        = new Vector2(panelWidth, 0);

            AddUIImage(panelRoot, new Color(0.14f, 0.16f, 0.22f, 0.98f));

            // ── ScrollRect ───────────────────────────────────────────────────
            var scrollGo = new GameObject("ScrollRect");
            scrollGo.transform.SetParent(panelRoot.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal        = false;
            scroll.vertical          = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType      = ScrollRect.MovementType.Clamped;
            scroll.inertia           = true;
            scroll.decelerationRate  = 0.135f;

            // Viewport
            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.sizeDelta = Vector2.zero;
            vpRt.pivot     = new Vector2(0, 1);
            vpGo.AddComponent<RectMask2D>();
            scroll.viewport = vpRt;

            // Content
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(vpGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin        = new Vector2(0, 1);
            contentRt.anchorMax        = new Vector2(1, 1);
            contentRt.pivot            = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta        = Vector2.zero;

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding              = new RectOffset(10, 10, 10, 20);
            vlg.spacing              = 6;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;

            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = contentRt;
            Transform root = contentGo.transform;

            // ════════════════════════════════════════════════════════════════
            // 1. ヘッダー & デバイスステータス
            // ════════════════════════════════════════════════════════════════
            var headGo = new GameObject("Header");
            headGo.transform.SetParent(root, false);
            AddUIImage(headGo, new Color(0.10f, 0.12f, 0.18f, 1f));
            var headLe = headGo.AddComponent<LayoutElement>();
            headLe.minHeight = 36;
            var headHlg = headGo.AddComponent<HorizontalLayoutGroup>();
            headHlg.padding              = new RectOffset(10, 6, 4, 4);
            headHlg.spacing              = 4;
            headHlg.childControlWidth    = true;
            headHlg.childControlHeight   = true;
            headHlg.childForceExpandWidth  = false;
            headHlg.childForceExpandHeight = true;

            var titleTxt = CreateLabel(headGo.transform, "🎨 お絵描き操作盤", 14, FontStyle.Bold, new Color(1f, 0.9f, 0.4f));
            titleTxt.GetComponent<LayoutElement>().flexibleWidth = 1;

            var closeBtnGo = CreateButton(headGo.transform, "✕", TogglePanel);
            var closeLe = closeBtnGo.GetComponent<LayoutElement>();
            closeLe.minWidth    = 28;
            closeLe.minHeight   = 28;
            closeLe.flexibleWidth = 0;

            statsText = CreateLabel(root, "デバイス検出中...", 10, FontStyle.Normal, new Color(0.5f, 0.8f, 1f));
            statsText.GetComponent<LayoutElement>().minHeight = 16;

            // ════════════════════════════════════════════════════════════════
            // 2. 描画ツール
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "🖊 描画ツール");

            var toolRow1 = CreateHRow(root, 34);
            CreateButton(toolRow1.transform, "↩ 元に戻す", () => targetCanvas?.Undo());
            CreateButton(toolRow1.transform, "↪ やり直す", () => targetCanvas?.Redo());
            CreateButton(toolRow1.transform, "🗑 全消去",  () => targetCanvas?.Clear());

            var toolRow2 = CreateHRow(root, 34);
            eraserBtn    = CreateButton(toolRow2.transform, targetCanvas != null && targetCanvas.IsEraserMode ? "消しゴム: ON" : "消しゴム: OFF", null);
            eraserBtnImg = eraserBtn.GetComponent<Image>();
            if (targetCanvas != null && targetCanvas.IsEraserMode)
            {
                eraserBtnImg.color = new Color(0.85f, 0.3f, 0.3f);
            }
            eraserBtn.onClick.AddListener(() =>
            {
                if (targetCanvas == null) return;
                targetCanvas.IsEyedropperMode = false;
                if (eyedropperBtnImg != null) eyedropperBtnImg.color = StyleColor.ButtonBg;
                targetCanvas.IsEraserMode = !targetCanvas.IsEraserMode;
            });

            eyedropperBtn = CreateButton(toolRow2.transform, "🔍 スポイト", null);
            eyedropperBtnImg = eyedropperBtn.GetComponent<Image>();
            eyedropperBtn.onClick.AddListener(() =>
            {
                if (targetCanvas == null) return;
                targetCanvas.IsEyedropperMode = !targetCanvas.IsEyedropperMode;
                if (eyedropperBtnImg != null)
                {
                    eyedropperBtnImg.color = targetCanvas.IsEyedropperMode
                        ? new Color(0.25f, 0.65f, 0.85f)
                        : StyleColor.ButtonBg;
                }
            });

            // ════════════════════════════════════════════════════════════════
            // 3. ブラシ設定
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "📐 ブラシ設定");

            string currentToolTitle = (targetCanvas != null && targetCanvas.IsEraserMode) ? "消しゴム太さ" : "ブラシ太さ";
            brushSizeLabel = CreateLabel(root, $"{currentToolTitle}: {Mathf.RoundToInt(targetCanvas?.CurrentSize ?? 12)} px", 12, FontStyle.Normal, Color.white);

            // 1px 単位のクイックプリセットボタン行
            var sizePresetRow = CreateHRow(root, 26);
            int[] sizePresets = { 1, 2, 3, 5, 10, 20 };
            foreach (int sp in sizePresets)
            {
                int capturedSp = sp;
                CreateButton(sizePresetRow.transform, $"{capturedSp}px", () =>
                {
                    if (targetCanvas != null) targetCanvas.CurrentSize = capturedSp;
                    if (brushSizeSlider != null) brushSizeSlider.SetValueWithoutNotify(capturedSp);
                    if (brushSizeLabel != null)
                    {
                        string title = (targetCanvas != null && targetCanvas.IsEraserMode) ? "消しゴム太さ" : "ブラシ太さ";
                        brushSizeLabel.text = $"{title}: {capturedSp} px";
                    }
                });
            }

            // ±1px ステップ微調整と 1px 単位スライダー行
            var sizeSliderRow = CreateHRow(root, 28);
            var decBtn = CreateButton(sizeSliderRow.transform, "－1", () =>
            {
                if (targetCanvas == null) return;
                float newSize = Mathf.Max(1f, targetCanvas.CurrentSize - 1f);
                targetCanvas.CurrentSize = newSize;
                if (brushSizeSlider != null) brushSizeSlider.SetValueWithoutNotify(newSize);
                if (brushSizeLabel != null)
                {
                    string title = targetCanvas.IsEraserMode ? "消しゴム太さ" : "ブラシ太さ";
                    brushSizeLabel.text = $"{title}: {Mathf.RoundToInt(newSize)} px";
                }
            });
            var decLe = decBtn.GetComponent<LayoutElement>();
            decLe.minWidth = 36; decLe.preferredWidth = 36; decLe.flexibleWidth = 0;

            brushSizeSlider = CreateSlider(sizeSliderRow.transform, 1f, 100f, targetCanvas?.CurrentSize ?? 12f, val =>
            {
                val = Mathf.Round(val);
                if (targetCanvas != null) targetCanvas.CurrentSize = val;
                if (brushSizeLabel != null)
                {
                    string title = (targetCanvas != null && targetCanvas.IsEraserMode) ? "消しゴム太さ" : "ブラシ太さ";
                    brushSizeLabel.text = $"{title}: {Mathf.RoundToInt(val)} px";
                }
            });
            brushSizeSlider.wholeNumbers = true;
            var slLe = brushSizeSlider.GetComponent<LayoutElement>();
            slLe.flexibleWidth = 1;

            var incBtn = CreateButton(sizeSliderRow.transform, "＋1", () =>
            {
                if (targetCanvas == null) return;
                float newSize = Mathf.Min(100f, targetCanvas.CurrentSize + 1f);
                targetCanvas.CurrentSize = newSize;
                if (brushSizeSlider != null) brushSizeSlider.SetValueWithoutNotify(newSize);
                if (brushSizeLabel != null)
                {
                    string title = targetCanvas.IsEraserMode ? "消しゴム太さ" : "ブラシ太さ";
                    brushSizeLabel.text = $"{title}: {Mathf.RoundToInt(newSize)} px";
                }
            });
            var incLe = incBtn.GetComponent<LayoutElement>();
            incLe.minWidth = 36; incLe.preferredWidth = 36; incLe.flexibleWidth = 0;

            smoothLabel = CreateLabel(root, $"手振れ補正: {((targetCanvas?.Stabilization ?? 0.5f) * 100f):F0}%", 12, FontStyle.Normal, Color.white);
            smoothSlider = CreateSlider(root, 0f, 1f, targetCanvas?.Stabilization ?? 0.5f, val =>
            {
                if (targetCanvas != null) targetCanvas.Stabilization = val;
                if (smoothLabel != null) smoothLabel.text = $"手振れ補正: {(val * 100f):F0}%";
            });

            // 背景色
            CreateLabel(root, "キャンバス背景色:", 11, FontStyle.Normal, new Color(0.75f, 0.75f, 0.75f));
            var bgRow = CreateHRow(root, 28);
            Color[] bgPresets = { Color.white, new Color(0.96f, 0.94f, 0.90f), new Color(0.75f, 0.75f, 0.75f), new Color(0.2f, 0.2f, 0.2f), Color.black };
            string[] bgNames  = { "白", "紙色", "灰", "暗灰", "黒" };
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
            // 4. カラーピッカー
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "🎨 カラーピッカー");

            // プリセット
            var presetRow = CreateHRow(root, 30);
            Color[] colorPresets =
            {
                Color.black,
                new Color(0.9f, 0.1f, 0.1f),
                new Color(0.1f, 0.4f, 0.95f),
                new Color(0.1f, 0.75f, 0.2f),
                new Color(0.95f, 0.85f, 0.1f),
                new Color(1f, 0.5f, 0.1f),
                new Color(0.65f, 0.2f, 0.85f),
                Color.white,
            };
            foreach (var col in colorPresets)
            {
                var capturedCol = col;
                var cBtn = CreateButton(presetRow.transform, "", () => ApplyColor(capturedCol));
                cBtn.GetComponent<Image>().color = capturedCol;
            }

            // プレビュー & HEX
            var colorInfoRow = CreateHRow(root, 32);
            var prevGo = new GameObject("ColorPreview");
            prevGo.transform.SetParent(colorInfoRow.transform, false);
            colorPreview = AddUIImage(prevGo, targetCanvas?.CurrentColor ?? Color.black);
            var prevLe = prevGo.AddComponent<LayoutElement>();
            prevLe.minWidth = 48; prevLe.minHeight = 28; prevLe.flexibleWidth = 0;

            hexLabel = CreateLabel(colorInfoRow.transform, ColorToHex(targetCanvas?.CurrentColor ?? Color.black), 13, FontStyle.Bold, Color.white);

            Color.RGBToHSV(targetCanvas?.CurrentColor ?? Color.black, out currentHue, out currentSat, out currentVal);

            CreateLabel(root, "色相 (Hue):", 11, FontStyle.Normal, new Color(0.78f, 0.78f, 0.78f));
            hueSlider = CreateSlider(root, 0f, 1f, currentHue, val => { currentHue = val; RefreshHSVColor(); });

            CreateLabel(root, "彩度 (Saturation):", 11, FontStyle.Normal, new Color(0.78f, 0.78f, 0.78f));
            satSlider = CreateSlider(root, 0f, 1f, currentSat, val => { currentSat = val; RefreshHSVColor(); });

            CreateLabel(root, "明度 (Value):", 11, FontStyle.Normal, new Color(0.78f, 0.78f, 0.78f));
            valSlider = CreateSlider(root, 0f, 1f, currentVal, val => { currentVal = val; RefreshHSVColor(); });

            // ════════════════════════════════════════════════════════════════
            // 5. レイヤー管理
            // ════════════════════════════════════════════════════════════════
            BuildLayerManagementSection(root);

            // ════════════════════════════════════════════════════════════════
            // 6. 採点システム
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "🎯 採点");

            float initDiff = 0.5f;
            if (targetCanvas?.DefaultScorerAsset is NormalizedShapeScorer nss) initDiff = nss.Difficulty;

            difficultyLabel  = CreateLabel(root, $"難易度: {initDiff:F2}", 12, FontStyle.Normal, Color.white);
            difficultySlider = CreateSlider(root, 0f, 1f, initDiff, val =>
            {
                if (targetCanvas?.DefaultScorerAsset is NormalizedShapeScorer ns) ns.Difficulty = val;
                if (difficultyLabel != null) difficultyLabel.text = $"難易度: {val:F2}";
            });

            var rtRow = CreateHRow(root, 28);
            CreateLabel(rtRow.transform, "常時自動採点:", 11, FontStyle.Normal, Color.white);
            realtimeScoreToggle = CreateToggle(rtRow.transform, targetCanvas?.EnableRealtimeScoring ?? false, isOn =>
            {
                if (targetCanvas != null) targetCanvas.EnableRealtimeScoring = isOn;
            });

            realtimeIntervalLabel  = CreateLabel(root, $"採点間隔: {(targetCanvas?.RealtimeScoringInterval ?? 0.5f):F1}秒", 11, FontStyle.Normal, Color.white);
            realtimeIntervalSlider = CreateSlider(root, 0.1f, 3f, targetCanvas?.RealtimeScoringInterval ?? 0.5f, val =>
            {
                if (targetCanvas != null) targetCanvas.RealtimeScoringInterval = val;
                if (realtimeIntervalLabel != null) realtimeIntervalLabel.text = $"採点間隔: {val:F1}秒";
            });

            var scoreRow = CreateHRow(root, 36);
            var evalBtn  = CreateButton(scoreRow.transform, "🎯 今すぐ採点！", () =>
            {
                if (targetCanvas != null)
                {
                    var res = targetCanvas.EvaluateCurrentDrawing();
                    HandleScoreEvaluated(res);
                }
            });
            evalBtn.GetComponent<Image>().color = new Color(0.18f, 0.52f, 0.3f);

            scoreText = CreateLabel(root, "スコア: 未採点", 13, FontStyle.Bold, new Color(1f, 0.9f, 0.2f));

            // ════════════════════════════════════════════════════════════════
            // 7. 保存・エクスポート
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "💾 保存・エクスポート");

            var expRow = CreateHRow(root, 32);
            CreateButton(expRow.transform, "PNG 保存", () => ExportDrawing("png"));
            CreateButton(expRow.transform, "JPG 保存", () => ExportDrawing("jpg"));
            CreateButton(expRow.transform, "SVG 保存", () => ExportDrawing("svg"));

            notificationText = CreateLabel(root, "", 11, FontStyle.Italic, new Color(0.4f, 1f, 0.5f));

            // ════════════════════════════════════════════════════════════════
            // 8. ゲーム終了
            // ════════════════════════════════════════════════════════════════
            CreateSectionHeader(root, "⏹ アプリケーション");

            var quitRow = CreateHRow(root, 40);
            var quitBtn = CreateButton(quitRow.transform, "⏹ ゲームを終了する", () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
            quitBtn.GetComponent<Image>().color = new Color(0.72f, 0.16f, 0.16f);

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) SetLayerRecursively(uiRoot, uiLayer);
        }

        // ─── Layer Management UI (完全再構築) ─────────────────────────────────

        private LayerManager GetLayerManager()
        {
            FindAndInitCanvas();
            return targetCanvas?.Layers;
        }

        private void BuildLayerManagementSection(Transform root)
        {
            CreateSectionHeader(root, "📋 レイヤー管理");

            // 操作ボタン（新規、削除、上へ、下へ）
            var row1 = CreateHRow(root, 30);
            CreateButton(row1.transform, "＋ 新規レイヤー", () =>
            {
                var lm = GetLayerManager();
                if (lm == null) return;
                lm.AddLayer();
                targetCanvas?.RefreshComposite();
                RebuildLayerList();
            });
            CreateButton(row1.transform, "－ 削除", () =>
            {
                var lm = GetLayerManager();
                if (lm == null || lm.Layers.Count <= 1) return;
                lm.RemoveLayer(lm.ActiveLayerIndex);
                targetCanvas?.RefreshComposite();
                RebuildLayerList();
            });

            var row2 = CreateHRow(root, 30);
            CreateButton(row2.transform, "▲ 上へ", () =>
            {
                var lm = GetLayerManager();
                if (lm == null) return;
                int idx = lm.ActiveLayerIndex;
                if (idx < lm.Layers.Count - 1)
                {
                    lm.MoveLayer(idx, idx + 1);
                    targetCanvas?.RefreshComposite();
                    RebuildLayerList();
                }
            });
            CreateButton(row2.transform, "▼ 下へ", () =>
            {
                var lm = GetLayerManager();
                if (lm == null) return;
                int idx = lm.ActiveLayerIndex;
                if (idx > 0)
                {
                    lm.MoveLayer(idx, idx - 1);
                    targetCanvas?.RefreshComposite();
                    RebuildLayerList();
                }
            });

            // レイヤーリストコンテナ（背景付きの明確な領域）
            var listGo = new GameObject("LayerListContainer");
            listGo.transform.SetParent(root, false);
            layerListContainer = listGo.AddComponent<RectTransform>();
            AddUIImage(listGo, new Color(0.10f, 0.12f, 0.16f, 0.90f));

            var vlg = listGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 3;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            layerListLayoutElement = listGo.AddComponent<LayoutElement>();
            layerListLayoutElement.minHeight = 40;
            layerListLayoutElement.preferredHeight = 40;
            layerListLayoutElement.flexibleWidth = 1;

            // 選択レイヤーの不透明度
            layerOpacityLabel = CreateLabel(root, "レイヤー不透明度: 100%", 12, FontStyle.Normal, Color.white);
            layerOpacitySlider = CreateSlider(root, 0f, 1f, 1f, val =>
            {
                var lm = GetLayerManager();
                var active = lm?.ActiveLayer;
                if (active == null) return;
                active.Opacity = val;
                targetCanvas?.RefreshComposite();
                if (layerOpacityLabel != null) layerOpacityLabel.text = $"レイヤー不透明度: {(val * 100f):F0}%";
                UpdateLayerRowOpacityBadge(lm.ActiveLayerIndex, val);
            });

            // 選択レイヤーの合成モード
            CreateLabel(root, "合成モード:", 12, FontStyle.Normal, Color.white);
            var blendRow = CreateHRow(root, 30);
            blendModeButtons = new Button[BlendModeNames.Length];
            for (int i = 0; i < BlendModeNames.Length; i++)
            {
                int capturedIdx = i;
                var b = CreateButton(blendRow.transform, BlendModeNames[i], () =>
                {
                    var lm = GetLayerManager();
                    var active = lm?.ActiveLayer;
                    if (active == null) return;
                    active.BlendMode = (LayerBlendMode)capturedIdx;
                    targetCanvas?.RefreshComposite();
                    UpdateBlendModeButtons(capturedIdx);
                });
                blendModeButtons[i] = b;
            }

            RebuildLayerList();
            SubscribeLayerEvents();
        }

        public void RebuildLayerList()
        {
            if (layerListContainer == null) return;

            // 既存行をすべて即座にデタッチして破棄
            for (int i = layerListContainer.childCount - 1; i >= 0; i--)
            {
                var child = layerListContainer.GetChild(i);
                child.SetParent(null);
                Destroy(child.gameObject);
            }

            var lm = GetLayerManager();
            var layers = lm?.Layers;

            if (lm == null || layers == null || layers.Count == 0)
            {
                lastKnownLayerCount = 0;
                lastKnownActiveIndex = -1;

                var emptyRow = new GameObject("EmptyLayerRow");
                emptyRow.transform.SetParent(layerListContainer, false);
                var emptyLe = emptyRow.AddComponent<LayoutElement>();
                emptyLe.minHeight = 32;
                emptyLe.preferredHeight = 32;
                emptyLe.flexibleWidth = 1;

                var lbl = CreateLabel(emptyRow.transform, "⚠ レイヤーがありません", 11, FontStyle.Italic, new Color(0.9f, 0.7f, 0.2f));
                lbl.alignment = TextAnchor.MiddleCenter;
                var lrt = lbl.GetComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;

                if (layerListLayoutElement != null)
                {
                    layerListLayoutElement.minHeight = 40;
                    layerListLayoutElement.preferredHeight = 40;
                }
                return;
            }

            lastKnownLayerCount  = layers.Count;
            lastKnownActiveIndex = lm.ActiveLayerIndex;

            const float RowHeight = 34f;
            const float Spacing   = 3f;
            const float PaddingY  = 8f;
            float totalHeight = (layers.Count * RowHeight) + ((layers.Count - 1) * Spacing) + PaddingY;

            if (layerListLayoutElement != null)
            {
                layerListLayoutElement.minHeight = totalHeight;
                layerListLayoutElement.preferredHeight = totalHeight;
            }

            // 上位レイヤー（描画優先度が高い）を一番上に表示
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                int layerIdx = i;
                var layer    = layers[i];
                bool isActive = (i == lm.ActiveLayerIndex);

                var rowGo = new GameObject($"LayerRow_{i}");
                rowGo.transform.SetParent(layerListContainer, false);

                var rowRt = rowGo.AddComponent<RectTransform>();
                rowRt.sizeDelta = new Vector2(0, RowHeight);

                var rowLe = rowGo.AddComponent<LayoutElement>();
                rowLe.minHeight = RowHeight;
                rowLe.preferredHeight = RowHeight;
                rowLe.flexibleWidth = 1;

                Color rowBgColor = isActive
                    ? new Color(0.20f, 0.40f, 0.70f, 0.95f)
                    : new Color(0.16f, 0.18f, 0.24f, 0.90f);
                AddUIImage(rowGo, rowBgColor);

                var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(4, 4, 3, 3);
                hlg.spacing = 4;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
                hlg.childAlignment = TextAnchor.MiddleCenter;

                // 1. [👁 / 🚫] 表示/非表示トグルボタン
                var visGo = new GameObject("VisBtn");
                visGo.transform.SetParent(rowGo.transform, false);
                var visRt = visGo.AddComponent<RectTransform>();
                visRt.sizeDelta = new Vector2(30, 28);
                var visImg = AddUIImage(visGo, layer.IsVisible ? new Color(0.25f, 0.50f, 0.35f, 1f) : new Color(0.45f, 0.20f, 0.20f, 1f));
                var visBtn = visGo.AddComponent<Button>();
                visBtn.targetGraphic = visImg;
                var visLe = visGo.AddComponent<LayoutElement>();
                visLe.minWidth = 30; visLe.preferredWidth = 30;

                var visTxt = CreateLabel(visGo.transform, layer.IsVisible ? "👁" : "🚫", 12, FontStyle.Normal, Color.white);
                visTxt.alignment = TextAnchor.MiddleCenter;
                var vtRt = visTxt.GetComponent<RectTransform>();
                vtRt.anchorMin = Vector2.zero; vtRt.anchorMax = Vector2.one; vtRt.sizeDelta = Vector2.zero;

                visBtn.onClick.AddListener(() =>
                {
                    layer.IsVisible = !layer.IsVisible;
                    targetCanvas?.RefreshComposite();
                    RebuildLayerList();
                });

                // 2. レイヤー選択メインボタン
                var selGo = new GameObject("SelectBtn");
                selGo.transform.SetParent(rowGo.transform, false);
                var selRt = selGo.AddComponent<RectTransform>();
                selRt.sizeDelta = new Vector2(140, 28);
                var selImg = AddUIImage(selGo, isActive ? new Color(0.25f, 0.52f, 0.90f, 1f) : new Color(0.22f, 0.25f, 0.32f, 1f));
                var selBtn = selGo.AddComponent<Button>();
                selBtn.targetGraphic = selImg;
                var selLe = selGo.AddComponent<LayoutElement>();
                selLe.minWidth = 100; selLe.preferredWidth = 160; selLe.flexibleWidth = 1;

                string lName = string.IsNullOrEmpty(layer.LayerName) ? $"レイヤー {layerIdx + 1}" : layer.LayerName;
                string title = isActive ? $"▶ {lName}" : $"  {lName}";
                var selTxt = CreateLabel(selGo.transform, title, 12, isActive ? FontStyle.Bold : FontStyle.Normal, Color.white);
                selTxt.alignment = TextAnchor.MiddleLeft;
                var stRt = selTxt.GetComponent<RectTransform>();
                stRt.anchorMin = Vector2.zero; stRt.anchorMax = Vector2.one;
                stRt.offsetMin = new Vector2(8, 0); stRt.offsetMax = new Vector2(-4, 0);

                selBtn.onClick.AddListener(() =>
                {
                    lm.SetActiveLayer(layerIdx);
                    SyncActiveLayerControls();
                    RebuildLayerList();
                });

                // 3. 不透明度バッジテキスト (右端)
                var opBadgeGo = new GameObject("OpacityBadge");
                opBadgeGo.transform.SetParent(rowGo.transform, false);
                var opRt = opBadgeGo.AddComponent<RectTransform>();
                opRt.sizeDelta = new Vector2(36, 28);
                var opLe = opBadgeGo.AddComponent<LayoutElement>();
                opLe.minWidth = 36; opLe.preferredWidth = 36;

                var opBadgeTxt = CreateLabel(opBadgeGo.transform, $"{(layer.Opacity * 100f):F0}%", 10, FontStyle.Normal, new Color(0.8f, 0.85f, 0.9f, 0.8f));
                opBadgeTxt.alignment = TextAnchor.MiddleRight;
                var otRt = opBadgeTxt.GetComponent<RectTransform>();
                otRt.anchorMin = Vector2.zero; otRt.anchorMax = Vector2.one; otRt.sizeDelta = Vector2.zero;
            }

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) SetLayerRecursively(layerListContainer.gameObject, uiLayer);

            SyncActiveLayerControls();

            // 親の Content のレイアウトを強制再計算
            var contentRt = layerListContainer.parent as RectTransform;
            if (contentRt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
            }
        }

        private void UpdateLayerRowOpacityBadge(int activeIdx, float opacity)
        {
            if (layerListContainer == null) return;
            var row = layerListContainer.Find($"LayerRow_{activeIdx}");
            if (row != null)
            {
                var badge = row.Find("OpacityBadge");
                var txt = badge?.GetComponentInChildren<Text>();
                if (txt != null) txt.text = $"{(opacity * 100f):F0}%";
            }
        }

        private void SyncActiveLayerControls()
        {
            var lm     = GetLayerManager();
            var active = lm?.ActiveLayer;
            if (active == null) return;

            if (layerOpacitySlider != null) layerOpacitySlider.SetValueWithoutNotify(active.Opacity);
            if (layerOpacityLabel  != null) layerOpacityLabel.text = $"レイヤー不透明度: {(active.Opacity * 100f):F0}%";
            UpdateBlendModeButtons((int)active.BlendMode);
        }

        private void UpdateBlendModeButtons(int activeIdx)
        {
            if (blendModeButtons == null) return;
            for (int i = 0; i < blendModeButtons.Length; i++)
            {
                if (blendModeButtons[i] == null) continue;
                var img = blendModeButtons[i].GetComponent<Image>();
                if (img != null) img.color = (i == activeIdx) ? new Color(0.26f, 0.56f, 0.95f) : StyleColor.ButtonBg;
            }
        }

        // ─── HSV ─────────────────────────────────────────────────────────────
        private void RefreshHSVColor()
        {
            ApplyColor(Color.HSVToRGB(currentHue, currentSat, currentVal));
        }

        private void ApplyColor(Color col)
        {
            if (targetCanvas != null)
            {
                targetCanvas.CurrentColor  = col;
                targetCanvas.IsEraserMode  = false;
                if (eraserBtnImg != null) eraserBtnImg.color = StyleColor.ButtonBg;
                var eraserTxt = eraserBtn?.GetComponentInChildren<Text>();
                if (eraserTxt != null) eraserTxt.text = "消しゴム: OFF";
            }

            if (colorPreview != null) colorPreview.color = col;
            if (hexLabel     != null) hexLabel.text      = ColorToHex(col);

            Color.RGBToHSV(col, out currentHue, out currentSat, out currentVal);
            if (hueSlider != null) hueSlider.SetValueWithoutNotify(currentHue);
            if (satSlider != null) satSlider.SetValueWithoutNotify(currentSat);
            if (valSlider != null) valSlider.SetValueWithoutNotify(currentVal);
        }

        private static string ColorToHex(Color c) =>
            $"#{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}";

        // ─── Score & Export ───────────────────────────────────────────────────
        private void HandleScoreEvaluated(ScoringResult result)
        {
            if (scoreText != null)
                scoreText.text = $"得点: {result.Score * 100f:F1} 点 ({result.ScorerName})";
        }

        private void ExportDrawing(string format)
        {
            if (targetCanvas == null) return;
            string dir  = Path.Combine(Application.persistentDataPath, "Drawings");
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
            public static readonly Color ButtonBg   = new Color(0.26f, 0.38f, 0.56f, 1f);
            public static readonly Color SectionBg  = new Color(0.20f, 0.23f, 0.32f, 1f);
            public static readonly Color HeaderText = new Color(0.98f, 0.85f, 0.3f, 1f);
        }

        private static Sprite cachedWhiteSprite;
        private static Font   cachedFont;

        private static Sprite GetWhiteSprite()
        {
            if (cachedWhiteSprite != null) return cachedWhiteSprite;
            var tex = Texture2D.whiteTexture;
            cachedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            cachedWhiteSprite.name      = "DS_UIWhiteSprite";
            cachedWhiteSprite.hideFlags = HideFlags.DontSave;
            return cachedWhiteSprite;
        }

        private static Image AddUIImage(GameObject go, Color color)
        {
            var img      = go.AddComponent<Image>();
            img.sprite   = GetWhiteSprite();
            img.material = Canvas.GetDefaultCanvasMaterial();
            img.color    = color;
            img.raycastTarget = true;
            return img;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform c in go.transform)
                if (c != null) SetLayerRecursively(c.gameObject, layer);
        }

        private static Font GetSafeFont()
        {
            if (cachedFont != null) return cachedFont;

            string[] osFonts = { "Yu Gothic UI", "Meiryo", "Segoe UI", "Arial", "MS Gothic", "Helvetica Neue", "Helvetica" };
            foreach (var fn in osFonts)
            {
                try
                {
                    var f = Font.CreateDynamicFontFromOSFont(fn, 13);
                    if (f != null) { cachedFont = f; return cachedFont; }
                }
                catch { }
            }

#if UNITY_EDITOR
            try
            {
                var libFont = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
                if (libFont != null) { cachedFont = libFont; return cachedFont; }
            }
            catch { }
#endif

            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cachedFont != null) return cachedFont;

            cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (cachedFont != null) return cachedFont;

            var found = Resources.FindObjectsOfTypeAll<Font>();
            foreach (var f in found) if (f != null) { cachedFont = f; return cachedFont; }

            return null;
        }

        // ── Widget factories ─────────────────────────────────────────────────

        private static void CreateSectionHeader(Transform parent, string title)
        {
            var go  = new GameObject("SectionHeader_" + title);
            go.transform.SetParent(parent, false);
            AddUIImage(go, StyleColor.SectionBg);
            var le  = go.AddComponent<LayoutElement>();
            le.minHeight = 22;

            var txt = CreateLabel(go.transform, title, 11, FontStyle.Bold, StyleColor.HeaderText);
            var rt  = txt.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.GetComponent<LayoutElement>().minHeight = 0;
        }

        private static Text CreateLabel(Transform parent, string text, int fontSize, FontStyle style, Color color)
        {
            var go  = new GameObject("Lbl");
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
            txt.raycastTarget      = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow   = VerticalWrapMode.Overflow;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = fontSize + 6;
            return txt;
        }

        private static Button CreateButton(Transform parent, string title, Action onClick)
        {
            var go  = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            var img = AddUIImage(go, StyleColor.ButtonBg);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = new ColorBlock
            {
                normalColor      = StyleColor.ButtonBg,
                highlightedColor = new Color(0.36f, 0.50f, 0.72f, 1f),
                pressedColor     = new Color(0.18f, 0.28f, 0.44f, 1f),
                selectedColor    = StyleColor.ButtonBg,
                disabledColor    = new Color(0.4f, 0.4f, 0.4f, 0.5f),
                colorMultiplier  = 1f,
                fadeDuration     = 0.1f,
            };

            var le = go.AddComponent<LayoutElement>();
            le.minHeight    = 28;
            le.flexibleWidth = 1;

            if (!string.IsNullOrEmpty(title))
            {
                var txt   = CreateLabel(go.transform, title, 11, FontStyle.Bold, Color.white);
                txt.alignment = TextAnchor.MiddleCenter;
                var txtRt = txt.GetComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = new Vector2(3, 0);
                txtRt.offsetMax = new Vector2(-3, 0);
            }

            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        private static GameObject CreateHRow(Transform parent, float minH = 32f)
        {
            var go = new GameObject("HRow");
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing               = 4;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = minH;
            return go;
        }

        private static Slider CreateSlider(Transform parent, float min, float max, float current, Action<float> onChanged)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 20;
            le.preferredHeight = 20;

            // Background
            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(go.transform, false);
            AddUIImage(bgGo, new Color(0.25f, 0.27f, 0.36f, 1f));
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 0.3f);
            bgRt.anchorMax = new Vector2(1, 0.7f);
            bgRt.sizeDelta = Vector2.zero;

            // Fill Area
            var faGo = new GameObject("FillArea");
            faGo.transform.SetParent(go.transform, false);
            var faRt = faGo.AddComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0, 0.3f);
            faRt.anchorMax = new Vector2(1, 0.7f);
            faRt.offsetMin = new Vector2(5, 0);
            faRt.offsetMax = new Vector2(-5, 0);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(faGo.transform, false);
            var fillImg = AddUIImage(fillGo, new Color(0.28f, 0.62f, 1.0f, 1f));
            var fillRt  = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0, 0);
            fillRt.anchorMax = new Vector2(0, 1);
            fillRt.sizeDelta = Vector2.zero;

            // Handle Slide Area
            var haGo = new GameObject("HandleSlideArea");
            haGo.transform.SetParent(go.transform, false);
            var haRt = haGo.AddComponent<RectTransform>();
            haRt.anchorMin = Vector2.zero;
            haRt.anchorMax = Vector2.one;
            haRt.offsetMin = new Vector2(10, 0);
            haRt.offsetMax = new Vector2(-10, 0);

            var hGo = new GameObject("Handle");
            hGo.transform.SetParent(haGo.transform, false);
            var hImg = AddUIImage(hGo, Color.white);
            var hRt  = hGo.GetComponent<RectTransform>();
            hRt.sizeDelta = new Vector2(16, 0);

            var slider       = go.AddComponent<Slider>();
            slider.minValue  = min;
            slider.maxValue  = max;
            slider.fillRect  = fillRt;
            slider.handleRect = hRt;
            slider.direction = Slider.Direction.LeftToRight;
            slider.value     = current;

            if (onChanged != null)
                slider.onValueChanged.AddListener(v => onChanged(v));

            return slider;
        }

        private static Toggle CreateToggle(Transform parent, bool initialValue, Action<bool> onChanged)
        {
            var go = new GameObject("Toggle");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = 28; le.minHeight = 24; le.flexibleWidth = 0;

            var bgImg = AddUIImage(go, new Color(0.18f, 0.18f, 0.24f, 1f));

            var chkGo  = new GameObject("Checkmark");
            chkGo.transform.SetParent(go.transform, false);
            var chkImg = AddUIImage(chkGo, new Color(0.18f, 0.80f, 0.40f, 1f));
            var chkRt  = chkGo.GetComponent<RectTransform>();
            chkRt.anchorMin = new Vector2(0.15f, 0.15f);
            chkRt.anchorMax = new Vector2(0.85f, 0.85f);
            chkRt.sizeDelta = Vector2.zero;

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bgImg;
            toggle.graphic       = chkImg;
            toggle.isOn          = initialValue;
            if (onChanged != null)
                toggle.onValueChanged.AddListener(v => onChanged(v));

            return toggle;
        }

        private static T FindAnyObjectByType<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindAnyObjectByType<T>();
#else
            return UnityEngine.Object.FindObjectOfType<T>();
#endif
        }
    }
}
