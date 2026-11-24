using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using DrawingSystem.Brushes;
using DrawingSystem.Layers;
using DrawingSystem.Scoring;

namespace DrawingSystem.UI
{
    /// <summary>
    /// Runtime control panel providing in-game/build access to all DrawingSystem parameters
    /// previously only tweakable in the Unity Editor Inspector.
    /// Can automatically generate standard uGUI controls at runtime if not wired in Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Drawing System/Runtime Control Panel")]
    public class RuntimeControlPanel : MonoBehaviour
    {
        [Header("キャンバス参照")]
        [SerializeField] private DrawingCanvas targetCanvas;

        [Header("UI パネルの自動生成")]
        [Tooltip("実行時にUI階層が空の場合、自動で使いやすいオーバーレイUIを動的生成します。")]
        [SerializeField] private bool autoGenerateUI = true;

        [Header("表示設定")]
        [SerializeField] private bool showStats = true;
        [SerializeField] private KeyCode togglePanelKey = KeyCode.Tab;

        // References to dynamic UI elements
        private Canvas uiCanvas;
        private GameObject panelRoot;
        private Text statsText;
        private Text scoreText;
        private RawImage heatmapImage;
        private Text notificationText;
        private Slider difficultySlider;
        private Text difficultyLabel;
        private Slider brushSizeSlider;
        private Text brushSizeLabel;
        private Slider smoothSlider;
        private Text smoothLabel;

        private void Awake()
        {
            if (targetCanvas == null)
            {
                targetCanvas = FindFirstObjectByType<DrawingCanvas>();
            }
        }

        private void Start()
        {
            if (targetCanvas == null)
            {
                targetCanvas = FindFirstObjectByType<DrawingCanvas>();
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
            // Toggle panel visibility
            if (Input.GetKeyDown(togglePanelKey) && panelRoot != null)
            {
                panelRoot.SetActive(!panelRoot.activeSelf);
            }

            // Update realtime device & pressure telemetry
            if (showStats && statsText != null && targetCanvas != null)
            {
                statsText.text = $"[Device: {targetCanvas.ActiveDeviceName}]  [Pressure: {targetCanvas.LastInputPressure * 100f:F0}%]  [Layers: {targetCanvas.Layers?.Layers.Count ?? 1}]";
            }
        }

        /// <summary>
        /// Builds a clean, responsive runtime UI overlay dynamically without needing external prefabs.
        /// </summary>
        private void BuildRuntimeUI()
        {
            // Create root UI Canvas if not already existing
            var canvasGo = new GameObject("DrawingSystem_RuntimeUI");
            canvasGo.transform.SetParent(this.transform, false);

            uiCanvas = canvasGo.AddComponent<Canvas>();
            uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            uiCanvas.sortingOrder = 999; // On top of drawing surface

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // Panel Root (Sidebar on Left / Top)
            panelRoot = new GameObject("ControlPanelRoot");
            panelRoot.transform.SetParent(canvasGo.transform, false);
            var rootRt = panelRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0, 0);
            rootRt.anchorMax = new Vector2(0, 1);
            rootRt.pivot = new Vector2(0, 0.5f);
            rootRt.sizeDelta = new Vector2(320, 0);
            rootRt.anchoredPosition = Vector2.zero;

            var bg = panelRoot.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.15f, 0.85f);

            var vlg = panelRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 14, 14);
            vlg.spacing = 8;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            var csf = panelRoot.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Header Title
            CreateLabel(panelRoot.transform, "🎨 描画システム操作パネル (Tabで開閉)", 16, FontStyle.Bold, Color.white);

            // Stats / Device Info
            statsText = CreateLabel(panelRoot.transform, "Device: Detecting...", 12, FontStyle.Normal, new Color(0.7f, 0.9f, 1f));

            // Tool buttons row (Undo / Redo / Clear / Eraser)
            var toolRow = CreateHorizontalRow(panelRoot.transform);
            CreateButton(toolRow.transform, "↩ Undo", () => targetCanvas?.Undo());
            CreateButton(toolRow.transform, "↪ Redo", () => targetCanvas?.Redo());
            CreateButton(toolRow.transform, "🗑 Clear", () => targetCanvas?.Clear());

            var eraserBtn = CreateButton(toolRow.transform, "消しゴム", null);
            var eraserBtnImg = eraserBtn.GetComponent<Image>();
            eraserBtn.onClick.AddListener(() =>
            {
                if (targetCanvas == null) return;
                targetCanvas.IsEraserMode = !targetCanvas.IsEraserMode;
                eraserBtnImg.color = targetCanvas.IsEraserMode ? new Color(1f, 0.4f, 0.4f) : Color.white;
            });

            // Sliders: Size & Smoothing
            brushSizeLabel = CreateLabel(panelRoot.transform, $"ブラシ太さ: {Mathf.RoundToInt(targetCanvas?.CurrentSize ?? 12)} px", 12, FontStyle.Normal, Color.white);
            brushSizeSlider = CreateSlider(panelRoot.transform, 1f, 100f, targetCanvas?.CurrentSize ?? 12f, val =>
            {
                if (targetCanvas != null) targetCanvas.CurrentSize = val;
                if (brushSizeLabel != null) brushSizeLabel.text = $"ブラシ太さ: {Mathf.RoundToInt(val)} px";
            });

            smoothLabel = CreateLabel(panelRoot.transform, $"手振れ補正: {((targetCanvas?.Stabilization ?? 0.5f) * 100f):F0}%", 12, FontStyle.Normal, Color.white);
            smoothSlider = CreateSlider(panelRoot.transform, 0f, 1f, targetCanvas?.Stabilization ?? 0.5f, val =>
            {
                if (targetCanvas != null) targetCanvas.Stabilization = val;
                if (smoothLabel != null) smoothLabel.text = $"手振れ補正: {(val * 100f):F0}%";
            });

            // Color Presets Row
            CreateLabel(panelRoot.transform, "カラーパレット:", 12, FontStyle.Bold, Color.white);
            var colorRow = CreateHorizontalRow(panelRoot.transform);
            Color[] presets = { Color.black, Color.red, Color.blue, new Color(0.1f, 0.8f, 0.2f), Color.yellow, Color.white };
            foreach (var col in presets)
            {
                var cBtn = CreateButton(colorRow.transform, "", () =>
                {
                    if (targetCanvas != null)
                    {
                        targetCanvas.CurrentColor = col;
                        targetCanvas.IsEraserMode = false;
                        if (eraserBtnImg != null) eraserBtnImg.color = Color.white;
                    }
                });
                cBtn.GetComponent<Image>().color = col;
            }

            // Layer Management Section
            CreateLabel(panelRoot.transform, "─── レイヤー操作 ───", 13, FontStyle.Bold, new Color(0.8f, 0.8f, 0.8f));
            var layerRow = CreateHorizontalRow(panelRoot.transform);
            CreateButton(layerRow.transform, "＋ 新規レイヤー", () =>
            {
                targetCanvas?.Layers?.AddLayer();
                targetCanvas?.RefreshComposite();
            });
            CreateButton(layerRow.transform, "－ 削除", () =>
            {
                var lm = targetCanvas?.Layers;
                if (lm != null && lm.Layers.Count > 1)
                {
                    lm.RemoveLayer(lm.ActiveLayerIndex);
                    targetCanvas?.RefreshComposite();
                }
            });

            // Scoring Section
            CreateLabel(panelRoot.transform, "─── 採点・ゲーム機能 ───", 13, FontStyle.Bold, new Color(0.8f, 0.8f, 0.8f));

            // Difficulty slider
            float currentDiff = 0.5f;
            if (targetCanvas?.DefaultScorerAsset is NormalizedShapeScorer nss) currentDiff = nss.Difficulty;

            difficultyLabel = CreateLabel(panelRoot.transform, $"採点難易度: {currentDiff:F2}", 12, FontStyle.Normal, Color.white);
            difficultySlider = CreateSlider(panelRoot.transform, 0f, 1f, currentDiff, val =>
            {
                if (targetCanvas?.DefaultScorerAsset is NormalizedShapeScorer ns)
                {
                    ns.Difficulty = val;
                }
                if (difficultyLabel != null) difficultyLabel.text = $"採点難易度: {val:F2}";
            });

            var scoreRow = CreateHorizontalRow(panelRoot.transform);
            CreateButton(scoreRow.transform, "🎯 採点する", () =>
            {
                if (targetCanvas != null)
                {
                    var res = targetCanvas.EvaluateCurrentDrawing();
                    HandleScoreEvaluated(res);
                }
            });

            scoreText = CreateLabel(panelRoot.transform, "スコア: 未測定", 15, FontStyle.Bold, new Color(1f, 0.9f, 0.2f));

            // Heatmap Display Container
            var hmObj = new GameObject("HeatmapView");
            hmObj.transform.SetParent(panelRoot.transform, false);
            var hmRt = hmObj.AddComponent<RectTransform>();
            hmRt.sizeDelta = new Vector2(160, 160);
            heatmapImage = hmObj.AddComponent<RawImage>();
            heatmapImage.color = Color.white;
            hmObj.SetActive(false);

            // Export Section
            CreateLabel(panelRoot.transform, "─── 保存・書き出し ───", 13, FontStyle.Bold, new Color(0.8f, 0.8f, 0.8f));
            var expRow = CreateHorizontalRow(panelRoot.transform);
            CreateButton(expRow.transform, "PNG保存", () => ExportDrawing("png"));
            CreateButton(expRow.transform, "JPG保存", () => ExportDrawing("jpg"));
            CreateButton(expRow.transform, "SVG保存", () => ExportDrawing("svg"));

            // Notifications Toast
            notificationText = CreateLabel(panelRoot.transform, "", 11, FontStyle.Italic, new Color(0.4f, 1f, 0.5f));
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

            ShowNotification($"保存完了: {file}");
        }

        private void HandleScoreEvaluated(ScoringResult result)
        {
            if (scoreText != null)
            {
                scoreText.text = $"得点: {result.Score * 100f:F1} 点\n({result.ScorerName})";
            }

            if (heatmapImage != null && result.Heatmap != null)
            {
                heatmapImage.gameObject.SetActive(true);
                heatmapImage.texture = result.Heatmap;
            }
        }

        private void ShowNotification(string msg)
        {
            Debug.Log($"[RuntimeControlPanel] {msg}");
            if (notificationText != null)
            {
                notificationText.text = msg;
            }
        }

        #region UI Helper Factories

        private static Text CreateLabel(Transform parent, string text, int fontSize, FontStyle style, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleLeft;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = fontSize + 8;
            return txt;
        }

        private static Button CreateButton(Transform parent, string title, Action onClick)
        {
            var go = new GameObject("Btn_" + title);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.35f, 0.5f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 30;
            le.flexibleWidth = 1;

            if (!string.IsNullOrEmpty(title))
            {
                var txt = CreateLabel(go.transform, title, 12, FontStyle.Bold, Color.white);
                txt.alignment = TextAnchor.MiddleCenter;
                var txtRt = txt.GetComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.sizeDelta = Vector2.zero;
            }

            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        private static GameObject CreateHorizontalRow(Transform parent)
        {
            var go = new GameObject("HRow");
            go.transform.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 32;
            return go;
        }

        private static Slider CreateSlider(Transform parent, float min, float max, float current, Action<float> onChanged)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(parent, false);
            var slider = go.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = current;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 22;

            // Background
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, false);
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.25f);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 0.25f);
            bgRt.anchorMax = new Vector2(1, 0.75f);
            bgRt.sizeDelta = Vector2.zero;

            // Fill Area
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0, 0.25f);
            fillAreaRt.anchorMax = new Vector2(1, 0.75f);
            fillAreaRt.sizeDelta = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.2f, 0.6f, 1f);
            var fillRt = fill.GetComponent<RectTransform>();
            fillRt.sizeDelta = Vector2.zero;

            slider.fillRect = fillRt;

            if (onChanged != null)
            {
                slider.onValueChanged.AddListener(val => onChanged(val));
            }

            return slider;
        }

        #endregion
    }
}
