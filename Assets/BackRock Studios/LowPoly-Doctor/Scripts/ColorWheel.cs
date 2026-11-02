using UnityEngine;
using UnityEngine.UI;

namespace BackRock
{
    public class ColorWheel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Material targetMaterial; // Material to change the color of
        [SerializeField] private Slider hueSlider;       // Slider for Hue
        [SerializeField] private Slider saturationSlider; // Slider for Saturation
        [SerializeField] private Slider brightnessSlider; // Slider for Brightness
        [SerializeField] private Image previewImage;     // Preview image to show the current color

        [Header("Slider Visual Elements")]
        [SerializeField] private Image hueSliderImage;   // Image of Hue Slider
        [SerializeField] private Image saturationSliderImage; // Image of Saturation Slider
        [SerializeField] private Image brightnessSliderImage; // Image of Brightness Slider

        private Color currentColor;

        private void Start()
        {
            if (targetMaterial == null || hueSlider == null || saturationSlider == null || brightnessSlider == null)
            {
                Debug.LogError("Please assign all required references in the inspector.");
                return;
            }

            // Initialize sliders
            hueSlider.onValueChanged.AddListener(UpdateColor);
            saturationSlider.onValueChanged.AddListener(UpdateColor);
            brightnessSlider.onValueChanged.AddListener(UpdateColor);

            // Set default slider values
            hueSlider.value = 0f; // Start with red
            saturationSlider.value = 1f; // Full saturation
            brightnessSlider.value = 1f; // Full brightness
            UpdateColor(0); // Initialize color
        }

        private void UpdateColor(float value)
        {
            float hue = hueSlider.value; // Get hue from slider
            float saturation = saturationSlider.value; // Get saturation from slider
            float brightness = brightnessSlider.value; // Get brightness from slider

            // Convert HSB to RGB
            currentColor = Color.HSVToRGB(hue, saturation, brightness);

            // Apply color to material
            if (targetMaterial != null)
            {
                targetMaterial.color = currentColor;
            }

            // Update preview image
            if (previewImage != null)
            {
                previewImage.color = currentColor;
            }

            // Update slider colors
            UpdateSliderColors(hue, saturation, brightness);
        }

        private void UpdateSliderColors(float hue, float saturation, float brightness)
        {
            // Update hue slider color
            if (hueSliderImage != null)
            {
                hueSliderImage.color = Color.HSVToRGB(hue, 1f, 1f); // Full saturation and brightness for hue
            }

            // Update saturation slider color
            if (saturationSliderImage != null)
            {
                saturationSliderImage.color = Color.HSVToRGB(hue, saturation, 1f); // Adjust saturation
            }

            // Update brightness slider color
            if (brightnessSliderImage != null)
            {
                brightnessSliderImage.color = Color.HSVToRGB(hue, 1f, brightness); // Adjust brightness
            }
        }
    }

}