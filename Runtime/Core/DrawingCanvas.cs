using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DrawingSystem.Brushes;
using DrawingSystem.Layers;
using DrawingSystem.Export;
using DrawingSystem.Scoring;

namespace DrawingSystem
{
    public enum CanvasRenderMode
    {
        ScreenSpaceUI,
        WorldSpaceObject
    }

    /// <summary>
    /// お絵描きシステムのメインキャンバスクラス。
    /// 入力受付、レイヤー合成、ストローク描画、Undo/Redo、採点、エクスポートを統括します。
    /// </summary>
    [DisallowMultipleComponent]
    public class DrawingCanvas : MonoBehaviour
    {
        [Header("キャンバス基本設定")]
        [SerializeField] private int resolutionWidth = 1024;
        [SerializeField] private int resolutionHeight = 1024;
        [SerializeField] private Color backgroundColor = Color.white;
        [SerializeField] private CanvasRenderMode renderMode = CanvasRenderMode.ScreenSpaceUI;

        [Header("レンダリング先参照")]
        [Tooltip("UIモード時に描画結果を表示するRawImage")]
        [SerializeField] private RawImage targetRawImage;
        [Tooltip("WorldSpaceモード時に描画結果を表示するRenderer")]
        [SerializeField] private Renderer targetRenderer;
        [Tooltip("WorldSpaceモード時のマテリアルテクスチャプロパティ名")]
        [SerializeField] private string targetTextureProperty = "_MainTex";

        [Header("初期ブラシ・入力")]
        [SerializeField] private BrushProfile defaultBrushProfile;
        [SerializeField] private Color defaultDrawColor = Color.black;
        [SerializeField] private float defaultBrushSize = 12f;

        [Header("手振れ補正設定")]
        [Tooltip("手振れ補正の強度（0 = オフ、0.5 = 標準、1.0 = 強力）")]
        [SerializeField, Range(0f, 1f)] private float stabilization = 0.5f;

        public float Stabilization
        {
            get => stabilization;
            set => stabilization = Mathf.Clamp01(value);
        }

        [Header("採点システム設定 (任意)")]
        [SerializeField] private ScriptableObject defaultScorerAsset; // IScoringEngine
        [SerializeField] private Texture2D referenceImage;
        [SerializeField] private bool enableRealtimeScoring = false;
        [SerializeField, Range(0.1f, 3f)] private float realtimeScoringInterval = 0.5f;

        // コアコンポーネント
        private LayerManager layerManager;
        private BrushEngine brushEngine;
        private StrokeRecorder strokeRecorder;
        private UndoRedoManager undoRedoManager;
        private RenderTexture finalOutputTexture;

        // 現在の描画ステート
        private BrushProfile currentBrush;
        private Color currentColor;
        private float currentSize;
        private bool isEraserMode;

        private Vector2? lastStrokeUV;
        private float lastStrokePressure;
        private float realtimeScoringTimer;

        // 手振れ補正用ステート
        private Vector2 currentSmoothedUV;
        private float currentSmoothedPressure;
        private bool isStabilizing;
        private float lastInputPressure = 1f;
        private string lastInputDevice = "None";

        public float LastInputPressure => lastInputPressure;
        public string LastInputDevice => lastInputDevice;

        public string ActiveDeviceName
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (UnityEngine.InputSystem.Pen.current != null)
                {
                    return $"Pen ({UnityEngine.InputSystem.Pen.current.displayName})";
                }
                if (UnityEngine.InputSystem.Mouse.current != null)
                {
                    return "Mouse";
                }
#endif
                return "Default Pointer";
            }
        }

        // イベント
        public event Action<RenderTexture> OnCanvasUpdated;
        public event Action<StrokeData> OnStrokeCompleted;
        public event Action<ScoringResult> OnScoreEvaluated;

        // プロパティ
        public LayerManager Layers => layerManager;
        public StrokeRecorder Recorder => strokeRecorder;
        public UndoRedoManager UndoRedo => undoRedoManager;
        public RenderTexture OutputTexture => finalOutputTexture;
        public Color CurrentColor { get => currentColor; set => currentColor = value; }
        public float CurrentSize { get => currentSize; set => currentSize = value; }
        public bool IsEraserMode { get => isEraserMode; set => isEraserMode = value; }
        public BrushProfile CurrentBrush { get => currentBrush; set => currentBrush = value; }

        private void Awake()
        {
            InitializeCanvas();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                currentColor = defaultDrawColor;
                currentSize = defaultBrushSize;
                if (defaultBrushProfile != null)
                {
                    currentBrush = defaultBrushProfile;
                }
            }
        }

        private void InitializeCanvas()
        {
            // 自動でRawImageを検索して割り当て（未設定の場合のフォールバック）
            if (renderMode == CanvasRenderMode.ScreenSpaceUI && targetRawImage == null)
            {
#if UNITY_2023_1_OR_NEWER
                targetRawImage = FindFirstObjectByType<RawImage>();
#else
                targetRawImage = FindObjectOfType<RawImage>();
#endif
            }

            // RenderTexture 作成
            finalOutputTexture = new RenderTexture(resolutionWidth, resolutionHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "DrawingCanvas_FinalOutput",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            finalOutputTexture.Create();

            // モジュール初期化
            layerManager = new LayerManager(resolutionWidth, resolutionHeight);
            brushEngine = new BrushEngine();
            strokeRecorder = new StrokeRecorder();
            undoRedoManager = new UndoRedoManager();

            currentBrush = defaultBrushProfile != null
                ? defaultBrushProfile
                : ScriptableObject.CreateInstance<BrushProfile>();
            currentColor = defaultDrawColor;
            currentSize = defaultBrushSize;

            layerManager.OnLayersChanged += RefreshComposite;

            // UIまたはRendererにテクスチャを紐付け
            BindTargetTexture();

            // 初回クリア＆合成
            RefreshComposite();
        }

        private void Start()
        {
            // targetRawImageの再確認（Awake時にUIが未初期化だった場合）
            if (renderMode == CanvasRenderMode.ScreenSpaceUI && targetRawImage == null)
            {
#if UNITY_2023_1_OR_NEWER
                targetRawImage = FindFirstObjectByType<RawImage>();
#else
                targetRawImage = FindObjectOfType<RawImage>();
#endif
                BindTargetTexture();
                RefreshComposite();
            }

            // InputProvider を自動探索またはアタッチ
            var inputProvider = GetComponent<InputProviderBase>();
            if (inputProvider == null)
            {
#if ENABLE_INPUT_SYSTEM
                // ペンタブ（XP-Pen等）とマウスの両方に自動対応するプロバイダーを追加
                inputProvider = gameObject.AddComponent<PenTabletInputProvider>();
#else
                inputProvider = gameObject.AddComponent<MouseInputProvider>();
#endif
            }
            inputProvider.OnInput += HandleInput;
        }

        private void Update()
        {
            HandleKeyboardShortcuts();

            if (enableRealtimeScoring && referenceImage != null)
            {
                realtimeScoringTimer += Time.deltaTime;
                if (realtimeScoringTimer >= realtimeScoringInterval)
                {
                    realtimeScoringTimer = 0f;
                    EvaluateCurrentDrawing();
                }
            }
        }

        private void HandleKeyboardShortcuts()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                bool ctrl = kb.ctrlKey.isPressed;
                bool shift = kb.shiftKey.isPressed;

                if (ctrl && kb.zKey.wasPressedThisFrame)
                {
                    if (shift) Redo();
                    else Undo();
                }
                else if (ctrl && kb.yKey.wasPressedThisFrame)
                {
                    Redo();
                }
                return;
            }
#endif
            bool legacyCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool legacyShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (legacyCtrl && Input.GetKeyDown(KeyCode.Z))
            {
                if (legacyShift) Redo();
                else Undo();
            }
            else if (legacyCtrl && Input.GetKeyDown(KeyCode.Y))
            {
                Redo();
            }
        }

        private void OnDestroy()
        {
            if (layerManager != null)
            {
                layerManager.OnLayersChanged -= RefreshComposite;
                layerManager.Dispose();
            }
            brushEngine?.Dispose();

            if (finalOutputTexture != null)
            {
                if (finalOutputTexture.IsCreated()) finalOutputTexture.Release();
                Destroy(finalOutputTexture);
            }
        }

        /// <summary>
        /// 外部入力イベントのハンドラー
        /// </summary>
        public void HandleInput(DrawingInputData input)
        {
            if (layerManager.ActiveLayer == null) return;

            // 入力座標をキャンバスのUV座標(0~1)に変換
            if (!TryGetUVCoordinates(input.Position, out Vector2 uv))
            {
                if (input.Phase == InputPhase.Ended || input.Phase == InputPhase.Cancelled)
                {
                    EndCurrentStroke();
                }
                return;
            }

            float pressure = Mathf.Clamp01(input.Pressure);
            lastInputPressure = pressure;
            if (!string.IsNullOrEmpty(input.DeviceName))
            {
                lastInputDevice = input.DeviceName;
            }
            bool useEraser = input.IsEraser || isEraserMode;

            switch (input.Phase)
            {
                case InputPhase.Began:
                    if (lastStrokeUV.HasValue)
                    {
                        EndCurrentStroke();
                    }
                    currentSmoothedUV = uv;
                    currentSmoothedPressure = pressure;
                    isStabilizing = stabilization > 0.001f;
                    BeginStrokeAt(uv, pressure, input.Tilt, useEraser);
                    break;

                case InputPhase.Moved:
                case InputPhase.Stationary:
                    if (isStabilizing)
                    {
                        // 指数平滑化 (EMA): stabilization に応じて追従速度を調整し、手の微細な震えを吸収
                        float lerpRate = Mathf.Lerp(1.0f, 0.12f, stabilization);
                        currentSmoothedUV = Vector2.Lerp(currentSmoothedUV, uv, lerpRate);
                        currentSmoothedPressure = Mathf.Lerp(currentSmoothedPressure, pressure, lerpRate);

                        ContinueStrokeTo(currentSmoothedUV, currentSmoothedPressure, input.Tilt, useEraser);
                    }
                    else
                    {
                        ContinueStrokeTo(uv, pressure, input.Tilt, useEraser);
                    }
                    break;

                case InputPhase.Ended:
                case InputPhase.Cancelled:
                    isStabilizing = false;
                    EndCurrentStroke();
                    break;
            }
        }

        private void BeginStrokeAt(Vector2 uv, float pressure, Vector2 tilt, bool isEraser)
        {
            var activeLayer = layerManager.ActiveLayer;
            if (activeLayer == null) return;

            string brushId = currentBrush != null ? currentBrush.BrushId : "DefaultPen";
            strokeRecorder.BeginStroke(brushId, currentColor, currentSize, layerManager.ActiveLayerIndex, isEraser);
            
            var point = new StrokePoint
            {
                Position = uv,
                Pressure = pressure,
                Tilt = tilt,
                Timestamp = Time.time
            };
            strokeRecorder.AddPoint(point);

            if (isEraser)
            {
                brushEngine.Erase(activeLayer.RenderTexture, currentBrush, uv, pressure, currentSize);
            }
            else
            {
                brushEngine.Stamp(activeLayer.RenderTexture, currentBrush, uv, currentColor, pressure, currentSize);
            }

            lastStrokeUV = uv;
            lastStrokePressure = pressure;
            RefreshComposite();
        }

        private void ContinueStrokeTo(Vector2 uv, float pressure, Vector2 tilt, bool isEraser)
        {
            var activeLayer = layerManager.ActiveLayer;
            if (activeLayer == null) return;

            if (!lastStrokeUV.HasValue)
            {
                BeginStrokeAt(uv, pressure, tilt, isEraser);
                return;
            }

            var point = new StrokePoint
            {
                Position = uv,
                Pressure = pressure,
                Tilt = tilt,
                Timestamp = Time.time
            };
            strokeRecorder.AddPoint(point);

            brushEngine.DrawStrokeBetween(
                activeLayer.RenderTexture,
                currentBrush,
                lastStrokeUV.Value,
                uv,
                currentColor,
                lastStrokePressure,
                pressure,
                currentSize,
                isEraser
            );

            lastStrokeUV = uv;
            lastStrokePressure = pressure;
            RefreshComposite();
        }

        private void EndCurrentStroke()
        {
            if (strokeRecorder.IsRecording)
            {
                var completedStroke = strokeRecorder.EndStroke();
                if (completedStroke != null)
                {
                    // Undo/Redo用のコマンドを記録
                    var cmd = new DrawStrokeCommand(completedStroke, strokeRecorder, ReRenderStrokes);
                    undoRedoManager.PushExecutedCommand(cmd);

                    OnStrokeCompleted?.Invoke(completedStroke);
                }
            }

            lastStrokeUV = null;
        }

        /// <summary>
        /// 全レイヤーを合成して表示用テクスチャを更新します
        /// </summary>
        public void RefreshComposite()
        {
            if (layerManager == null || finalOutputTexture == null) return;
            layerManager.CompositeTo(finalOutputTexture, backgroundColor);
            OnCanvasUpdated?.Invoke(finalOutputTexture);
        }

        /// <summary>
        /// Undo操作
        /// </summary>
        public void Undo()
        {
            if (undoRedoManager.CanUndo)
            {
                undoRedoManager.Undo();
            }
        }

        /// <summary>
        /// Redo操作
        /// </summary>
        public void Redo()
        {
            if (undoRedoManager.CanRedo)
            {
                undoRedoManager.Redo();
            }
        }

        /// <summary>
        /// 全ストロークを最初から再描画（Undo/Redo用）
        /// </summary>
        private void ReRenderStrokes()
        {
            layerManager.ClearAllLayers();

            var strokes = strokeRecorder.GetAllStrokes();
            foreach (var stroke in strokes)
            {
                if (stroke.LayerIndex >= layerManager.Layers.Count) continue;
                var layer = layerManager.Layers[stroke.LayerIndex];
                if (layer == null || stroke.Points.Count == 0) continue;

                Vector2? prevPos = null;
                float prevPressure = 1f;

                foreach (var pt in stroke.Points)
                {
                    if (prevPos.HasValue)
                    {
                        brushEngine.DrawStrokeBetween(
                            layer.RenderTexture,
                            currentBrush,
                            prevPos.Value,
                            pt.Position,
                            stroke.Color,
                            prevPressure,
                            pt.Pressure,
                            stroke.BrushSize,
                            stroke.IsEraser
                        );
                    }
                    else
                    {
                        if (stroke.IsEraser)
                        {
                            brushEngine.Erase(layer.RenderTexture, currentBrush, pt.Position, pt.Pressure, stroke.BrushSize);
                        }
                        else
                        {
                            brushEngine.Stamp(
                                layer.RenderTexture,
                                currentBrush,
                                pt.Position,
                                stroke.Color,
                                pt.Pressure,
                                stroke.BrushSize
                            );
                        }
                    }
                    prevPos = pt.Position;
                    prevPressure = pt.Pressure;
                }
            }

            RefreshComposite();
        }

        /// <summary>
        /// スクリーン座標または3DレイキャストからキャンバスのUV座標(0~1)を取得
        /// </summary>
        private bool TryGetUVCoordinates(Vector2 screenPos, out Vector2 uv)
        {
            uv = Vector2.zero;

            if (renderMode == CanvasRenderMode.ScreenSpaceUI)
            {
                if (targetRawImage == null)
                {
#if UNITY_2023_1_OR_NEWER
                    targetRawImage = FindFirstObjectByType<RawImage>();
#else
                    targetRawImage = FindObjectOfType<RawImage>();
#endif
                    if (targetRawImage != null)
                    {
                        BindTargetTexture();
                    }
                }

                if (targetRawImage == null) return false;

                RectTransform rectTransform = targetRawImage.rectTransform;
                Canvas parentCanvas = targetRawImage.canvas;
                Camera uiCamera = (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? (parentCanvas.worldCamera ?? Camera.main)
                    : null;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPos, uiCamera, out Vector2 localPoint))
                {
                    Rect rect = rectTransform.rect;
                    if (rect.width <= 0f || rect.height <= 0f) return false;

                    float u = (localPoint.x - rect.xMin) / rect.width;
                    float v = (localPoint.y - rect.yMin) / rect.height;

                    if (u >= 0f && u <= 1f && v >= 0f && v <= 1f)
                    {
                        uv = new Vector2(u, v);
                        return true;
                    }
                }
                return false;
            }
            else
            {
                // WorldSpace Object モード: 3Dレイキャスト
                Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(screenPos) : default;
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    if (targetRenderer != null && hit.collider.gameObject == targetRenderer.gameObject)
                    {
                        uv = hit.textureCoord;
                        return true;
                    }
                }
                return false;
            }
        }

        private void BindTargetTexture()
        {
            if (renderMode == CanvasRenderMode.ScreenSpaceUI && targetRawImage != null)
            {
                targetRawImage.texture = finalOutputTexture;
            }
            else if (renderMode == CanvasRenderMode.WorldSpaceObject && targetRenderer != null)
            {
                targetRenderer.material.SetTexture(targetTextureProperty, finalOutputTexture);
            }
        }

        #region 採点システム連携

        /// <summary>
        /// 現在の描画を見本画像と比較して採点します
        /// </summary>
        public ScoringResult EvaluateCurrentDrawing(Texture2D refImage = null)
        {
            Texture2D reference = refImage != null ? refImage : referenceImage;
            if (reference == null)
            {
                Debug.LogWarning("[DrawingSystem] No reference image specified for scoring.");
                return default;
            }

            var scorer = defaultScorerAsset as IScoringEngine;
            if (scorer == null)
            {
                Debug.LogWarning("[DrawingSystem] No valid IScoringEngine assigned.");
                return default;
            }

            Texture2D drawnReadable = PNGExporter.ReadableFromRenderTexture(finalOutputTexture);
            ScoringResult result = scorer.Evaluate(reference, drawnReadable);
            Destroy(drawnReadable);

            OnScoreEvaluated?.Invoke(result);
            return result;
        }

        #endregion

        #region エクスポート連携

        public void ExportPNG(string filePath, ExportSettings settings = null)
        {
            var exporter = new PNGExporter();
            Texture2D readable = PNGExporter.ReadableFromRenderTexture(finalOutputTexture);
            exporter.ExportToFile(readable, filePath, settings);
            Destroy(readable);
        }

        public void ExportJPG(string filePath, ExportSettings settings = null)
        {
            var exporter = new JPGExporter();
            Texture2D readable = PNGExporter.ReadableFromRenderTexture(finalOutputTexture);
            exporter.ExportToFile(readable, filePath, settings);
            Destroy(readable);
        }

        public void ExportSVG(string filePath)
        {
            var exporter = new SVGExporter();
            exporter.ExportToFile(
                new List<StrokeData>(strokeRecorder.GetAllStrokes()),
                resolutionWidth,
                resolutionHeight,
                filePath,
                backgroundColor
            );
        }

        #endregion
    }
}
