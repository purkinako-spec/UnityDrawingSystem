using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DrawingSystem.Layers;

namespace DrawingSystem.UI
{
    /// <summary>
    /// ゲームビュー上に表示するレイヤーパネルUI。
    /// DrawingCanvas と連携してレイヤーの追加・削除・選択・不透明度変更を行います。
    /// </summary>
    [AddComponentMenu("Drawing System/Layer Panel UI")]
    public class LayerPanelUI : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private DrawingCanvas canvas;

        [Header("UI 要素")]
        [SerializeField] private Transform layerListContent;    // ScrollView の Content
        [SerializeField] private GameObject layerItemPrefab;    // レイヤー行プレハブ
        [SerializeField] private Button addLayerButton;
        [SerializeField] private Button removeLayerButton;

        // 生成した行オブジェクト
        private readonly List<GameObject> rowObjects = new List<GameObject>();

        private void Awake()
        {
            if (canvas == null)
                canvas = FindFirstObjectByType<DrawingCanvas>();
        }

        private void Start()
        {
            if (canvas != null)
                canvas.Layers.OnLayersChanged += RefreshUI;

            if (addLayerButton != null)
                addLayerButton.onClick.AddListener(AddLayer);
            if (removeLayerButton != null)
                removeLayerButton.onClick.AddListener(RemoveActiveLayer);

            RefreshUI();
        }

        private void OnDestroy()
        {
            if (canvas != null && canvas.Layers != null)
                canvas.Layers.OnLayersChanged -= RefreshUI;
        }

        private void AddLayer()
        {
            canvas?.Layers?.AddLayer();
            canvas?.RefreshComposite();
        }

        private void RemoveActiveLayer()
        {
            if (canvas == null) return;
            var lm = canvas.Layers;
            if (lm == null || lm.Layers.Count <= 1) return;
            lm.RemoveLayer(lm.ActiveLayerIndex);
            canvas.RefreshComposite();
        }

        /// <summary>レイヤー一覧のUIをLayerManagerの状態に合わせて再構築します。</summary>
        public void RefreshUI()
        {
            if (canvas == null || canvas.Layers == null || layerListContent == null) return;

            var lm = canvas.Layers;
            var layers = lm.Layers;

            // 既存の行を削除
            foreach (var row in rowObjects)
            {
                if (row != null) Destroy(row);
            }
            rowObjects.Clear();

            // 上が最前面（リスト末尾から描画）
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                int capturedIndex = i;
                var layer = layers[i];
                bool isActive = (i == lm.ActiveLayerIndex);

                GameObject row = layerItemPrefab != null
                    ? Instantiate(layerItemPrefab, layerListContent)
                    : CreateDefaultRow(layerListContent);

                // --- テキスト（レイヤー名） ---
                var text = row.GetComponentInChildren<TMP_Text>();
                if (text == null)
                {
                    var legacyText = row.GetComponentInChildren<Text>();
                    if (legacyText != null) legacyText.text = layer.LayerName;
                }
                else
                {
                    text.text = layer.LayerName;
                    text.fontStyle = isActive ? FontStyles.Bold : FontStyles.Normal;
                }

                // --- 背景色でアクティブを強調 ---
                var bg = row.GetComponent<Image>();
                if (bg != null)
                    bg.color = isActive ? new Color(0.3f, 0.6f, 1f, 0.5f) : new Color(0.15f, 0.15f, 0.15f, 0.7f);

                // --- クリックで選択 ---
                var btn = row.GetComponent<Button>();
                if (btn == null) btn = row.AddComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    lm.SetActiveLayer(capturedIndex);
                    canvas.RefreshComposite();
                    RefreshUI();
                });

                // --- 表示/非表示トグル ---
                var toggles = row.GetComponentsInChildren<Toggle>(true);
                if (toggles.Length > 0)
                {
                    var vis = toggles[0];
                    vis.SetIsOnWithoutNotify(layer.IsVisible);
                    vis.onValueChanged.RemoveAllListeners();
                    vis.onValueChanged.AddListener(val =>
                    {
                        layer.IsVisible = val;
                        canvas.RefreshComposite();
                    });
                }

                // --- 不透明度スライダー ---
                var sliders = row.GetComponentsInChildren<Slider>(true);
                if (sliders.Length > 0)
                {
                    var opSlider = sliders[0];
                    opSlider.minValue = 0f;
                    opSlider.maxValue = 1f;
                    opSlider.SetValueWithoutNotify(layer.Opacity);
                    opSlider.onValueChanged.RemoveAllListeners();
                    opSlider.onValueChanged.AddListener(val =>
                    {
                        layer.Opacity = val;
                        canvas.RefreshComposite();
                    });
                }

                rowObjects.Add(row);
            }
        }

        // プレハブが未設定の場合に作るシンプルな行
        private static GameObject CreateDefaultRow(Transform parent)
        {
            var go = new GameObject("LayerRow", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 36);

            go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 0.7f);

            // テキスト
            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGO.transform.SetParent(go.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(1, 1);
            labelRT.offsetMin = new Vector2(8, 0);
            labelRT.offsetMax = new Vector2(-8, 0);
            var tmp = labelGO.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 14;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;

            return go;
        }
    }
}
