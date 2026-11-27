# 🎨 Unity Drawing System (com.custom.drawing-system)

他のUnityプロジェクトに簡単にインポート・移植できる汎用お絵描きシステムパッケージです。  
ペンタブレット筆圧検知、レイヤー、Undo/Redo、PNG/JPG/SVGエクスポート、見本比較による採点システムを備えています。

---

## 🌟 主な機能

- **マルチ入力対応**: 
  - ペンタブレット（筆圧・傾き・消しゴム側判定、New Input System 対応）
  - マウス（Legacy Input Manager 対応）
  - タッチスクリーン（iOS / Android 筆圧対応）
- **2D / 3D 両対応**:
  - `ScreenSpaceUI`: 画面上の Canvas / RawImage に描画
  - `WorldSpaceObject`: 3Dモデル表面（メッシュコライダー）にレイキャストで直接ペイント
- **高機能ブラシシステム**:
  - `BrushProfile` (ScriptableObject) によるブラシ定義
  - 筆圧に応じた太さ・透明度のカーブ調整
  - 消しゴムモード
- **レイヤー機能 & 合成**:
  - 複数レイヤーの追加・削除・表示切替・透明度・ブレンドモード（Normal, Multiply, Screen, Overlay）
  - GPUシェーダー合成
- **ストローク記録 & Undo/Redo**:
  - Commandパターンによるストローク単位の無制限Undo/Redo
- **画像エクスポート**:
  - PNG（アルファ透明度対応）
  - JPG（品質指定可能）
  - SVG（記録されたストロークからベジェ曲線ベクターデータを生成）
  - プロジェクトJSONシリアライズ
- **ゲーム用 類似度採点システム**:
  - `PixelSimilarityScorer`: ピクセル一致率・MSE比較
  - `StructuralScorer`: SSIM（構造的類似度）による局所特徴比較
  - `ShapeScorer`: 二値化とIoU（輪郭の一致度）比較
  - `CompositeScorer`: 複数アルゴリズムの重み付き合成
  - 差分ヒートマップテクスチャ生成、リアルタイム採点対応

---

## 📦 他プロジェクトへの導入（移植手順）

### 方法1: Unity Package Manager (UPM) でローカル追加（推奨）

1. 移行先端のUnityプロジェクトを開く
2. `Window > Package Manager` を開く
3. 左上の `+` ボタンをクリックし、**「Add package from disk...」** を選択
4. 本パッケージの `package.json` を選択
   ```
   UnityDrawingSystem/package.json
   ```

### 方法2: Packages フォルダへコピー
本フォルダ `UnityDrawingSystem` を移植先プロジェクトの `Packages/com.custom.drawing-system` としてそのまま配置してください。

---

## 🚀 クイックスタート

### 最速セットアップ (Prefab または Sample Scene を使う)

#### 方式 A: プレハブを配置するだけ（推奨）
1. お使いのシーンに **`Runtime/Prefabs/DrawingSystem.prefab`** をドラッグ＆ドロップするだけ！
2. Canvas、RawImage、入力プロバイダー、手振れ補正がすべてセットアップ済みです。Play を押せばすぐに描画できます。

#### 方式 B: サンプルシーンを開く
- **`Runtime/Scenes/DrawingSystemScene.unity`** を開いて再生ボタンを押すだけで、カメラ・ライト・UI が揃ったお絵描き環境をすぐに体験できます。
- UPM の Package Manager ウィンドウから **Samples > Import** を押してプロジェクト内にインポートすることも可能です。

---

### 手動でのセットアップ

### 1. UIモードでのお絵描き設定（2D）

1. シーンに `Canvas` と `RawImage` を作成
2. 空の GameObject を作成し、`DrawingCanvas` コンポーネントをアタッチ
3. `Target Raw Image` に作成した RawImage を割り当て
4. `Render Mode` を `ScreenSpaceUI` に設定
5. 再生ボタンを押すと、マウスやペンタブで描画が可能になります

### 2. 3Dオブジェクトへのペイント設定（World Space）

1. シーン内の描画したい3Dオブジェクト（CubeやSphereなど）に `MeshCollider` をアタッチ
2. `DrawingCanvas` の `Render Mode` を `WorldSpaceObject` に設定
3. `Target Renderer` に対象オブジェクトの MeshRenderer を割り当て

### 3. 採点ゲームの作成手順

1. `Assets > Create > Drawing System > Scoring` から採点アセットを作成
   - 例: `Composite Scorer`（総合評価）
2. `DrawingCanvas` の `Default Scorer Asset` に作成したアセットを設定
3. `Reference Image` に見本となるテクスチャ（Read/Write Enabled をオンにしたもの）を設定
4. コードから評価を実行:
   ```csharp
   using DrawingSystem;
   using DrawingSystem.Scoring;

   public class GameController : MonoBehaviour
   {
       [SerializeField] private DrawingCanvas canvas;

       public void OnFinishDrawing()
       {
           ScoringResult result = canvas.EvaluateCurrentDrawing();
           Debug.Log($"スコア: {result.Score * 100f:F1}% ({result.ScorerName})");

           if (result.Score >= 0.8f)
           {
               Debug.Log("クリア！");
           }
       }
   }
   ```

---

## 📁 パッケージ構成

```
UnityDrawingSystem/
├── package.json
├── Runtime/
│   ├── Core/
│   │   ├── DrawingCanvas.cs          # メインキャンバスマネージャー
│   │   ├── InputData.cs              # 入力データ構造・ストローク構造体
│   │   ├── InputProvider.cs          # マウス・タッチ・ペンタブ入力抽象化
│   │   ├── StrokeRecorder.cs         # ストローク記録・復元
│   │   └── UndoRedoManager.cs        # Undo/Redo管理
│   ├── Brushes/
│   │   ├── BrushProfile.cs           # ブラシパラメータ(ScriptableObject)
│   │   └── BrushEngine.cs            # スタンプ描画・補間描画処理
│   ├── Layers/
│   │   ├── DrawingLayer.cs           # レイヤー定義
│   │   ├── LayerCompositor.cs        # シェーダー合成処理
│   │   └── LayerManager.cs           # レイヤー追加・削除・順序制御
│   ├── Scoring/
│   │   ├── IScoringEngine.cs         # 採点共通インターフェース
│   │   ├── PixelSimilarityScorer.cs  # ピクセル一致率
│   │   ├── StructuralScorer.cs       # SSIM構造類似度
│   │   ├── ShapeScorer.cs            # IoU形状輪郭比較
│   │   └── CompositeScorer.cs        # 複合採点
│   ├── Export/
│   │   ├── IExporter.cs
│   │   ├── PNGExporter.cs            # PNG保存
│   │   ├── JPGExporter.cs            # JPG保存
│   │   ├── SVGExporter.cs            # ベジェ変換SVG保存
│   │   └── ProjectSerializer.cs      # プロジェクトJSON保存
│   ├── Shaders/
│   │   ├── BrushStamp.shader         # ブラシ先端描画シェーダー
│   │   ├── LayerComposite.shader     # レイヤーブレンドシェーダー
│   │   └── ScoringDiff.compute       # 差分・SSIM計算コンピュートシェーダー
│   └── UI/
│       ├── ColorPickerUI.cs          # 簡易カラーピッカー
│       └── DrawingUI.cs              # デフォルトUI統合コントローラー
└── Editor/
    └── DrawingCanvasEditor.cs        # インスペクター拡張GUI
```


---

## 🎛️ UI連携・ボタン / スライダー割り当てリファレンス

各UIスクリプト（`DrawingUI.cs`, `ColorPickerUI.cs`, `LayerPanelUI.cs`, `RuntimeControlPanel.cs`）における、機能ごとのUI変数と接続先のセッタ・メソッド一覧です。

### 1. ブラシ・描画ツール機能
| 操作内容 | UI側の変数 | 割り当てるセッタ / メソッド（`DrawingCanvas` 等） |
| :--- | :--- | :--- |
| **ブラシサイズ** | `brushSizeSlider` (`Slider`) | `targetCanvas.CurrentSize = val` |
| **スムージング** | `smoothSlider` (`Slider`) | `RuntimeControlPanel` 内で生成・設定 |
| **消しゴム切替** | `eraserToggle` (`Toggle`) / `eraserBtn` (`Button`) | `targetCanvas.IsEraserMode = val` |
| **スポイト切替** | `eyedropperBtn` (`Button`) | `RuntimeControlPanel` 内でツールモード切替 |
| **Undo / Redo** | `undoButton` / `redoButton` (`Button`) | `targetCanvas.Undo()` / `targetCanvas.Redo()` |

### 2. カラー選択機能（`ColorPickerUI.cs` / `RuntimeControlPanel.cs`）
| 操作内容 | UI側の変数 | 割り当てるセッタ / メソッド |
| :--- | :--- | :--- |
| **RGBスライダー** | `redSlider`, `greenSlider`, `blueSlider` (`Slider`) | `UpdateColorFromSliders()` → `OnColorChanged` → `targetCanvas.CurrentColor = col` |
| **HSVスライダー** | `hueSlider`, `satSlider`, `valSlider` (`Slider`) | `RuntimeControlPanel` 内でHSVからRGB変換して色設定 |
| **プリセット色** | `presetColorButtons` (`Button[]`) | `SetColor(btnColor)` |
| **色セッタ** | — | `colorPicker.SetColor(Color color)` / `targetCanvas.CurrentColor` |

### 3. レイヤー機能（`LayerPanelUI.cs` / `RuntimeControlPanel.cs`）
| 操作内容 | UI側の変数 | 割り当てるセッタ / メソッド（`LayerManager` / `DrawingCanvas`） |
| :--- | :--- | :--- |
| **スクロール領域** | `layerListContent` (`Transform`) / `layerListContainer` (`RectTransform`) | `RefreshUI()` で行オブジェクトを動的生成・配置 |
| **レイヤー追加** | `addLayerButton` (`Button`) | `AddLayer()` → `canvas.Layers.AddLayer()` + `canvas.RefreshComposite()` |
| **レイヤー削除** | `removeLayerButton` (`Button`) | `RemoveActiveLayer()` → `lm.RemoveLayer(lm.ActiveLayerIndex)` + `canvas.RefreshComposite()` |
| **レイヤー選択** | 各行の `Button` (`selBtn`) | `lm.SetActiveLayer(capturedIndex)` + `canvas.RefreshComposite()` |
| **不透明度** | `layerOpacitySlider` (`Slider`) | `layerOpacitySlider.SetValueWithoutNotify(...)` / アクティブレイヤーの不透明度更新 |
| **ブレンドモード** | `blendModeButtons` (`Button[]`) | `UpdateBlendModeButtons(int activeIdx)` |

### 4. 保存・エクスポート機能（`DrawingUI.cs`）
| 操作内容 | UI側の変数 | 割り当てるメソッド |
| :--- | :--- | :--- |
| **PNG保存** | `exportPngButton` (`Button`) | `OnExportPNGClicked()` → `targetCanvas.ExportPNG(path)` |
| **JPG保存** | `exportJpgButton` (`Button`) | `OnExportJPGClicked()` → `targetCanvas.ExportJPG(path)` |
| **SVG保存** | `exportSvgButton` (`Button`) | `OnExportSVGClicked()` → `targetCanvas.ExportSVG(path)` |

### 5. 採点（スコアリング）機能（`DrawingUI.cs` / `RuntimeControlPanel.cs`）
| 操作内容 | UI側の変数 | 割り当てるセッタ / メソッド |
| :--- | :--- | :--- |
| **採点実行** | `evaluateButton` (`Button`) | `OnEvaluateClicked()` → `targetCanvas.EvaluateCurrentDrawing()` |
| **難易度** | `difficultySlider` (`Slider`) | `RuntimeControlPanel` 内で採点難易度を設定 |
| **リアルタイム採点** | `realtimeScoreToggle` (`Toggle`) | リアルタイム採点のON/OFF |
| **採点間隔** | `realtimeIntervalSlider` (`Slider`) | リアルタイム採点の更新間隔設定 |
| **結果受取イベント** | — | `targetCanvas.OnScoreEvaluated += HandleScoreEvaluated` |
