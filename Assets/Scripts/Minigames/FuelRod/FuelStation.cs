using TMPro;
using UnityEngine;

/// <summary>
/// Where the Handler picks up a fuel: a glass column holding glowing rocks. Look at it and press E (see PlayerInteractor).
/// The visuals are child objects in the scene; this adds the floating name tag.
/// </summary>
public class FuelStation : MonoBehaviour
{
    public FuelType Fuel = FuelType.Uranium;
    [SerializeField] float labelHeight = 2.1f;

    void Start()
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        go.transform.position = transform.position + Vector3.up * labelHeight;
        go.transform.rotation = transform.rotation * Quaternion.Euler(0f, 180f, 0f); // readable from the grid/spawn side
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = Fuel.ToString();
        tmp.fontSize = 3f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = FuelRodGrid.FuelColor(Fuel);
        ((RectTransform)go.transform).sizeDelta = new Vector2(3f, 1f);
    }
}
