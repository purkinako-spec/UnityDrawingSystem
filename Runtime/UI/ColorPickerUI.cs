using System;
using UnityEngine;
using UnityEngine.UI;

namespace DrawingSystem.UI
{
    /// <summary>
    /// パレット色選択とRGBスライダーによる簡易カラーピッカー
    /// </summary>
    public class ColorPickerUI : MonoBehaviour
    {
        [Header("UI参照")]
        [SerializeField] private Slider redSlider;
        [SerializeField] private Slider greenSlider;
        [SerializeField] private Slider blueSlider;
        [SerializeField] private Image previewColorImage;
        [SerializeField] private Button[] presetColorButtons;

        public event Action<Color> OnColorChanged;

        private Color currentColor = Color.black;
        public Color CurrentColor => currentColor;

        private void Awake()
        {
            if (redSlider != null) redSlider.onValueChanged.AddListener(_ => UpdateColorFromSliders());
            if (greenSlider != null) greenSlider.onValueChanged.AddListener(_ => UpdateColorFromSliders());
            if (blueSlider != null) blueSlider.onValueChanged.AddListener(_ => UpdateColorFromSliders());

            if (presetColorButtons != null)
            {
                foreach (var btn in presetColorButtons)
                {
                    if (btn != null)
                    {
                        var btnImage = btn.GetComponent<Image>();
                        if (btnImage != null)
                        {
                            Color btnColor = btnImage.color;
                            btn.onClick.AddListener(() => SetColor(btnColor));
                        }
                    }
                }
            }

            SetColor(currentColor);
        }

        public void SetColor(Color color)
        {
            currentColor = color;

            if (redSlider != null) redSlider.SetValueWithoutNotify(color.r);
            if (greenSlider != null) greenSlider.SetValueWithoutNotify(color.g);
            if (blueSlider != null) blueSlider.SetValueWithoutNotify(color.b);

            UpdatePreview();
            OnColorChanged?.Invoke(currentColor);
        }

        private void UpdateColorFromSliders()
        {
            float r = redSlider != null ? redSlider.value : 0f;
            float g = greenSlider != null ? greenSlider.value : 0f;
            float b = blueSlider != null ? blueSlider.value : 0f;

            currentColor = new Color(r, g, b, 1f);
            UpdatePreview();
            OnColorChanged?.Invoke(currentColor);
        }

        private void UpdatePreview()
        {
            if (previewColorImage != null)
            {
                previewColorImage.color = currentColor;
            }
        }
    }
}
