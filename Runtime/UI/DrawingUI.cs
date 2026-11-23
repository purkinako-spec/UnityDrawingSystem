using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using DrawingSystem.Brushes;
using DrawingSystem.Scoring;

namespace DrawingSystem.UI
{
    /// <summary>
    /// お絵描きシステムのデフォルトUIコントローラー。
    /// DrawingCanvasと各UI要素（ブラシ設定、カラー、Undo/Redo、保存、採点表示）をバインドします。
    /// </summary>
    public class DrawingUI : MonoBehaviour
    {
        [Header("キャンバス参照")]
        [SerializeField] private DrawingCanvas targetCanvas;

        [Header("ツール操作")]
        [SerializeField] private Button undoButton;
        [SerializeField] private Button redoButton;
        [SerializeField] private Toggle eraserToggle;
        [SerializeField] private Slider brushSizeSlider;
        [SerializeField] private Text brushSizeText;

        [Header("カラーピッカー")]
        [SerializeField] private ColorPickerUI colorPicker;

        [Header("保存・エクスポート")]
        [SerializeField] private Button exportPngButton;
        [SerializeField] private Button exportJpgButton;
        [SerializeField] private Button exportSvgButton;
        [SerializeField] private Text statusNotificationText;

        [Header("採点機能 (ゲーム用)")]
        [SerializeField] private Button evaluateButton;
        [SerializeField] private Text scoreResultText;
        [SerializeField] private RawImage heatmapDisplay;

        private void Start()
        {
            if (targetCanvas == null)
            {
                targetCanvas = FindObjectOfType<DrawingCanvas>();
            }

            SetupUI();
        }

        private void SetupUI()
        {
            if (targetCanvas == null)
            {
                Debug.LogWarning("[DrawingUI] Target DrawingCanvas is not assigned!");
                return;
            }

            // Undo / Redo
            if (undoButton != null) undoButton.onClick.AddListener(() => targetCanvas.Undo());
            if (redoButton != null) redoButton.onClick.AddListener(() => targetCanvas.Redo());

            // 消しゴム
            if (eraserToggle != null)
            {
                eraserToggle.onValueChanged.AddListener(val => targetCanvas.IsEraserMode = val);
            }

            // ブラシサイズ
            if (brushSizeSlider != null)
            {
                brushSizeSlider.minValue = 1f;
                brushSizeSlider.maxValue = 100f;
                brushSizeSlider.value = targetCanvas.CurrentSize;
                brushSizeSlider.onValueChanged.AddListener(val =>
                {
                    targetCanvas.CurrentSize = val;
                    if (brushSizeText != null) brushSizeText.text = $"Size: {Mathf.RoundToInt(val)}";
                });
            }

            // カラーピッカー
            if (colorPicker != null)
            {
                colorPicker.OnColorChanged += col => targetCanvas.CurrentColor = col;
            }

            // エクスポート
            if (exportPngButton != null) exportPngButton.onClick.AddListener(OnExportPNGClicked);
            if (exportJpgButton != null) exportJpgButton.onClick.AddListener(OnExportJPGClicked);
            if (exportSvgButton != null) exportSvgButton.onClick.AddListener(OnExportSVGClicked);

            // 採点
            if (evaluateButton != null) evaluateButton.onClick.AddListener(OnEvaluateClicked);
            targetCanvas.OnScoreEvaluated += HandleScoreEvaluated;
        }

        private void OnExportPNGClicked()
        {
            string path = Path.Combine(Application.persistentDataPath, $"drawing_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            targetCanvas.ExportPNG(path);
            ShowNotification($"Saved PNG: {path}");
        }

        private void OnExportJPGClicked()
        {
            string path = Path.Combine(Application.persistentDataPath, $"drawing_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
            targetCanvas.ExportJPG(path);
            ShowNotification($"Saved JPG: {path}");
        }

        private void OnExportSVGClicked()
        {
            string path = Path.Combine(Application.persistentDataPath, $"drawing_{DateTime.Now:yyyyMMdd_HHmmss}.svg");
            targetCanvas.ExportSVG(path);
            ShowNotification($"Saved SVG: {path}");
        }

        private void OnEvaluateClicked()
        {
            ScoringResult result = targetCanvas.EvaluateCurrentDrawing();
            HandleScoreEvaluated(result);
        }

        private void HandleScoreEvaluated(ScoringResult result)
        {
            if (scoreResultText != null)
            {
                scoreResultText.text = $"Score: {result.Score * 100f:F1}% ({result.ScorerName})";
            }

            if (heatmapDisplay != null && result.Heatmap != null)
            {
                heatmapDisplay.gameObject.SetActive(true);
                heatmapDisplay.texture = result.Heatmap;
            }
        }

        private void ShowNotification(string msg)
        {
            Debug.Log($"[DrawingUI] {msg}");
            if (statusNotificationText != null)
            {
                statusNotificationText.text = msg;
            }
        }
    }
}
