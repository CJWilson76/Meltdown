using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AnimationSlider
{
    public class AnimationSliderController : MonoBehaviour
    {
        [SerializeField] private Animator animator; // Reference to the Animator
        [SerializeField] private Slider speedSlider; // Reference to the Slider
        [SerializeField] private TextMeshProUGUI speedText; // Reference to the Text UI

        private void Start()
        {
            speedSlider.onValueChanged.AddListener(UpdateAnimation);
            UpdateAnimation(speedSlider.value); // Initial update
        }

        private void UpdateAnimation(float value)
        {
            animator.SetFloat("Speed", value); // Set the animator parameter
            speedText.text = $"Speed: {value:F1}"; // Update the text with 1 decimal precision
        }

        private void OnDestroy()
        {
            speedSlider.onValueChanged.RemoveListener(UpdateAnimation);
        }
    }
}
