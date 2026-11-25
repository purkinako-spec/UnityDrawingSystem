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
        private float penSize = 12f;
        private float eraserSize = 24f;
        private bool isEraserMode;
        private bool isEyedropperMode;
        private Texture2D eyedropperReadTex; // スポイト読み取り用の再利用テクスチャ

        private Vector2? lastStrokeUV;
        private float lastStrokePressure;
        private int lastStrokePixelX = -1;
        private int lastStrokePixelY = -1;
        private bool isCompositeDirty = false;
        private bool isPointerPressed = false;
        private bool isBlockedByUI = false;
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
        public event Action<bool, float> OnToolModeChanged; // (isEraser, currentSize)
        public event Action<Color> OnColorPicked; // スポイトで色を取得した時

        // プロパティ
        public LayerManager Layers
        {
            get
            {
                EnsureInitialized();
                return layerManager;
            }
        }
        public StrokeRecorder Recorder => strokeRecorder;
        public UndoRedoManager UndoRedo => undoRedoManager;
        public RenderTexture OutputTexture => finalOutputTexture;
        public Color CurrentColor { get => currentColor; set => currentColor = value; }

        public float PenSize
        {
            get => penSize;
            set
            {
                penSize = Mathf.Max(1f, value);
                if (!isEraserMode)
                {
                    currentSize = penSize;
                    OnToolModeChanged?.Invoke(false, currentSize);
                }
            }
        }

        public float EraserSize
        {
            get => eraserSize;
            set
            {
                eraserSize = Mathf.Max(1f, value);
                if (isEraserMode)
                {
                    currentSize = eraserSize;
                    OnToolModeChanged?.Invoke(true, currentSize);
                }
            }
        }

        public float CurrentSize
        {
            get => currentSize;
            set
            {
                float val = Mathf.Max(1f, value);
                currentSize = val;
                if (isEraserMode) eraserSize = val;
                else penSize = val;
            }
        }

        public bool IsEraserMode
        {
            get => isEraserMode;
            set
            {
                if (isEraserMode == value) return;
                isEraserMode = value;
                currentSize = isEraserMode ? eraserSize : penSize;
                OnToolModeChanged?.Invoke(isEraserMode, currentSize);
            }
        }
        public BrushProfile CurrentBrush { get => currentBrush; set => currentBrush = value; }

        /// <summary>
        /// スポイト（カラーピッカー）モードの ON/OFF。
        /// ON にするとクリックで描画せず色を取得し、自動的に OFF に戻ります。
        /// </summary>
        public bool IsEyedropperMode
        {
            get => isEyedropperMode;
            set => isEyedropperMode = value;
        }

        public ScriptableObject DefaultScorerAsset { get => defaultScorerAsset; set => defaultScorerAsset = value; }
        public Texture2D ReferenceImage { get => referenceImage; set => referenceImage = value; }
        public Color BackgroundColor
        {
            get => backgroundColor;
            set
            {
                backgroundColor = value;
                RefreshComposite();
            }
        }
        public bool EnableRealtimeScoring { get => enableRealtimeScoring; set => enableRealtimeScoring = value; }
        public float RealtimeScoringInterval { get => realtimeScoringInterval; set => realtimeScoringInterval = Mathf.Max(0.05f, value); }
        public RawImage TargetRawImage => targetRawImage;

        private bool isCurrentlyDrawingStroke = false;

        /// <summary>
        /// 現在のアクティブレイヤーをクリア（全消去）します。
        /// Undo/Redo スタックにも記録します。
        /// </summary>
        public void Clear()
        {
            var activeLayer = layerManager?.ActiveLayer;
            if (activeLayer == null) return;

            // 描画中のストロークがあれば強制終了してからクリア
            if (isCurrentlyDrawingStroke)
            {
                EndCurrentStroke();
            }

            // ポインター押下状態をリセット（Clear後に誤って点が描かれないようにする）
            isPointerPressed = false;
            isBlockedByUI = false;
            isCurrentlyDrawingStroke = false;
            lastStrokeUV = null;
            lastStrokePixelX = -1;
            lastStrokePixelY = -1;

            // ClearLayerCommand で Undo 可能な形でクリアを実行
            var cmd = new ClearLayerCommand(
                layerManager.ActiveLayerIndex,
                activeLayer.RenderTexture,
                strokeRecorder,
                RefreshComposite);
            undoRedoManager.ExecuteCommand(cmd);
        }

        /// <summary>
        /// キャンバスおよびモジュールが初期化されていることを保証します。
        /// </summary>
        public void EnsureInitialized()
        {
            if (layerManager == null)
            {
                InitializeCanvas();
            }
        }

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
            if (layerManager != null) return;
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
            penSize = defaultBrushSize;
            currentSize = defaultBrushSize;
            eraserSize = Mathf.Max(24f, defaultBrushSize * 2f);

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

        private void LateUpdate()
        {
            if (isCompositeDirty)
            {
                RefreshComposite();
                isCompositeDirty = false;
            }
        }

        public void RequestCompositeRefresh()
        {
            isCompositeDirty = true;
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

            if (eyedropperReadTex != null)
            {
                Destroy(eyedropperReadTex);
            }
        }

        /// <summary>
        /// 外部入力イベントのハンドラー
        /// </summary>
        public void HandleInput(DrawingInputData input)
        {
            if (layerManager?.ActiveLayer == null) return;

            // 1. ポインターダウン時（押下開始）
            if (input.Phase == InputPhase.Began)
            {
                // UI上のクリックなら描画を完全にブロック
                if (IsPointerOverBlockingUI(input.Position))
                {
                    isBlockedByUI = true;
                    isPointerPressed = true;
                    isCurrentlyDrawingStroke = false;
                    return;
                }

                isBlockedByUI = false;

                // スポイトモード: キャンバス上の色を取得して自動OFF
                if (isEyedropperMode)
                {
                    if (TryGetUVCoordinates(input.Position, out Vector2 eyeUV))
                    {
                        Color pickedColor = PickColorAtUV(eyeUV);
                        currentColor = pickedColor;
                        isEraserMode = false;
                        currentSize = penSize;
                        OnColorPicked?.Invoke(pickedColor);
                        OnToolModeChanged?.Invoke(false, currentSize);
                    }
                    isEyedropperMode = false;
                    isPointerPressed = false;
                    return;
                }

                isPointerPressed = true;

                // キャンバス内であればストローク開始
                if (TryGetUVCoordinates(input.Position, out Vector2 uvBegan))
                {
                    // 前回のストロークが残っていれば確実に終了させる（BeginStrokeAtの内部ガードで処理されるが念のため）
                    if (isCurrentlyDrawingStroke) EndCurrentStroke();
                    isCurrentlyDrawingStroke = true;
                    currentSmoothedUV = uvBegan;
                    float initPressure = Mathf.Clamp01(Mathf.Max(0.25f, input.Pressure));
                    currentSmoothedPressure = initPressure;
                    isStabilizing = stabilization > 0.001f;
                    BeginStrokeAt(uvBegan, initPressure, input.Tilt, input.IsEraser || isEraserMode);
                }
                else
                {
                    // キャンバス外から押下開始：ドラッグで入ってくるのを待機
                    isCurrentlyDrawingStroke = false;
                    lastStrokeUV = null;
                }
                return;
            }

            // 2. ポインターアップ時（押下終了）
            if (input.Phase == InputPhase.Ended || input.Phase == InputPhase.Cancelled)
            {
                isPointerPressed = false;
                isBlockedByUI = false;
                isStabilizing = false;
                if (isCurrentlyDrawingStroke)
                {
                    EndCurrentStroke();
                }
                return;
            }

            // 3. 移動・滞留中（Moved / Stationary）
            if (isBlockedByUI) return;
            // ポインターが押されていない状態の移動は描画しない
            if (!isPointerPressed) return;

            // キャンバス内のUV座標を判定
            bool insideCanvas = TryGetUVCoordinates(input.Position, out Vector2 uv);

            if (insideCanvas)
            {
                float pressure = Mathf.Clamp01(Mathf.Max(0.20f, input.Pressure));
                lastInputPressure = pressure;
                if (!string.IsNullOrEmpty(input.DeviceName)) lastInputDevice = input.DeviceName;
                bool useEraser = input.IsEraser || isEraserMode;

                // キャンバス外からドラッグして入ってきた場合、ここでストロークを自動開始
                if (!isCurrentlyDrawingStroke)
                {
                    isCurrentlyDrawingStroke = true;
                    currentSmoothedUV = uv;
                    currentSmoothedPressure = pressure;
                    isStabilizing = stabilization > 0.001f;
                    BeginStrokeAt(uv, pressure, input.Tilt, useEraser);
                }
                else if (!lastStrokeUV.HasValue)
                {
                    // キャンバス外に出てから再侵入した場合：
                    // isCurrentlyDrawingStroke=true だが lastStrokeUV がリセット済み。
                    // この時点でStrokeRecorderは前回のストロークを引き続き記録中なので
                    // BeginStrokeAt内のガードで自動的にEndStroke→BeginStrokeされる。
                    currentSmoothedUV = uv;
                    currentSmoothedPressure = pressure;
                    BeginStrokeAt(uv, pressure, input.Tilt, useEraser);
                }
                else
                {
                    // 通常のストローク継続
                    if (isStabilizing)
                    {
                        float lerpRate = Mathf.Lerp(1.0f, 0.12f, stabilization);
                        currentSmoothedUV = Vector2.Lerp(currentSmoothedUV, uv, lerpRate);
                        currentSmoothedPressure = Mathf.Lerp(currentSmoothedPressure, pressure, lerpRate);
                        ContinueStrokeTo(currentSmoothedUV, currentSmoothedPressure, input.Tilt, useEraser);
                    }
                    else
                    {
                        ContinueStrokeTo(uv, pressure, input.Tilt, useEraser);
                    }
                }
            }
            else
            {
                // キャンバス外に抜けた場合：ストロークの接続を一時リセット
                // （isPointerPressed は維持されるので、再び入ってきたら即座に描画再開可能）
                if (isCurrentlyDrawingStroke && lastStrokeUV.HasValue)
                {
                    lastStrokeUV = null;
                }
            }
        }

        private bool IsPointerOverBlockingUI(Vector2 screenPosition)
        {
            if (UnityEngine.EventSystems.EventSystem.current == null) return false;

            var pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
            {
                position = screenPosition
            };
            var results = new List<UnityEngine.EventSystems.RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, results);

            for (int i = 0; i < results.Count; i++)
            {
                var go = results[i].gameObject;
                if (go == null) continue;

                // targetRawImage 自体へのヒットはキャンバス描画面なのでブロックしない
                if (targetRawImage != null && (go == targetRawImage.gameObject || go.transform.IsChildOf(targetRawImage.transform)))
                {
                    continue;
                }

                // それ以外のUI要素（パネル、ボタン、スライダー等）の上ならブロック
                return true;
            }
            return false;
        }

        private void BeginStrokeAt(Vector2 uv, float pressure, Vector2 tilt, bool isEraser)
        {
            var activeLayer = layerManager.ActiveLayer;
            if (activeLayer == null) return;

            // 既にStrokeRecorder が記録中なら先にEndStroke()してからBeginする
            if (strokeRecorder.IsRecording)
            {
                strokeRecorder.EndStroke();
            }

            // 描き初めの筆圧補正：タブレット接地瞬間は極小筆圧（0.001〜0.05）になりがち。
            // ただし過度に引き上げると常に太くなるため、0.5f を下限とする。
            float startPressure = Mathf.Max(0.5f, pressure);

            string brushId = currentBrush != null ? currentBrush.BrushId : "DefaultPen";
            strokeRecorder.BeginStroke(brushId, currentColor, currentSize, layerManager.ActiveLayerIndex, isEraser);
            
            var point = new StrokePoint
            {
                Position = uv,
                Pressure = startPressure,
                Tilt = tilt,
                Timestamp = Time.time
            };
            strokeRecorder.AddPoint(point);

            if (isEraser)
            {
                brushEngine.Erase(activeLayer.RenderTexture, currentBrush, uv, startPressure, currentSize);
            }
            else
            {
                brushEngine.Stamp(activeLayer.RenderTexture, currentBrush, uv, currentColor, startPressure, currentSize);
            }

            lastStrokeUV = uv;
            lastStrokePressure = startPressure;
            int targetW = activeLayer.Width > 0 ? activeLayer.Width : resolutionWidth;
            int targetH = activeLayer.Height > 0 ? activeLayer.Height : resolutionHeight;
            lastStrokePixelX = Mathf.FloorToInt(uv.x * targetW);
            lastStrokePixelY = Mathf.FloorToInt(uv.y * targetH);
            RequestCompositeRefresh();
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

            // 同一ピクセル判定（ズーム時の重複スタンプ・無駄な再描画をスキップ）
            int targetW = activeLayer.Width > 0 ? activeLayer.Width : resolutionWidth;
            int targetH = activeLayer.Height > 0 ? activeLayer.Height : resolutionHeight;
            int currPixelX = Mathf.FloorToInt(uv.x * targetW);
            int currPixelY = Mathf.FloorToInt(uv.y * targetH);

            if (lastStrokePixelX == currPixelX && lastStrokePixelY == currPixelY && Mathf.Abs(pressure - lastStrokePressure) < 0.05f)
            {
                return;
            }

            lastStrokePixelX = currPixelX;
            lastStrokePixelY = currPixelY;

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
            RequestCompositeRefresh();
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
            lastStrokePixelX = -1;
            lastStrokePixelY = -1;
            isCurrentlyDrawingStroke = false;

            if (isCompositeDirty)
            {
                RefreshComposite();
                isCompositeDirty = false;
            }
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
            Texture2D refReadable = PNGExporter.EnsureReadable(reference);
            bool ownsRef = refReadable != reference;

            ScoringResult result;
            try
            {
                result = scorer.Evaluate(refReadable, drawnReadable);
            }
            finally
            {
                Destroy(drawnReadable);
                if (ownsRef && refReadable != null)
                {
#if UNITY_EDITOR
                    DestroyImmediate(refReadable);
#else
                    Destroy(refReadable);
#endif
                }
            }

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

        /// <summary>
        /// 指定UV座標のピクセル色を合成結果テクスチャから取得します。
        /// </summary>
        private Color PickColorAtUV(Vector2 uv)
        {
            if (finalOutputTexture == null) return Color.black;
            int px = Mathf.Clamp(Mathf.FloorToInt(uv.x * finalOutputTexture.width), 0, finalOutputTexture.width - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt(uv.y * finalOutputTexture.height), 0, finalOutputTexture.height - 1);
            if (eyedropperReadTex == null || eyedropperReadTex.width != 1)
                eyedropperReadTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);

            RenderTexture prev = RenderTexture.active;
            try
            {
                RenderTexture.active = finalOutputTexture;
                eyedropperReadTex.ReadPixels(new Rect(px, py, 1, 1), 0, 0);
                eyedropperReadTex.Apply(false);
            }
            finally
            {
                RenderTexture.active = prev;
            }
            return eyedropperReadTex.GetPixel(0, 0);
        }
    }
}
