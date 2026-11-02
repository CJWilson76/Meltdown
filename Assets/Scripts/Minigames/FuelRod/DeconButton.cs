using System.Collections;
using UnityEngine;

/// <summary>
/// The Purifier's decon button. Drag the DeconButton prefab wherever it should go on the Purifier's side of the chamber.
/// Look at it while the Handler stands in the DeconChamber and press a number key to spray that chemical:
/// [1] = first entry in FuelRodMinigame's "Decon chemicals" list, [2] = second, and so on (see PlayerInteractor).
/// Needs a collider on this object or a child.
/// </summary>
public class DeconButton : MonoBehaviour
{
    [Tooltip("The part that sinks when pressed (optional).")]
    [SerializeField] Transform cap;
    [SerializeField] float pressDepth = 0.04f;
    [SerializeField] float pressSeconds = 0.15f;

    Vector3 capUp;
    Coroutine pressing;

    void Awake()
    {
        if (cap)
            capUp = cap.localPosition;
    }

    /// <summary>Local press animation only - the actual decon happens on the server.</summary>
    public void Press()
    {
        if (!cap)
            return;
        if (pressing != null)
            StopCoroutine(pressing);
        pressing = StartCoroutine(PressRoutine());
    }

    IEnumerator PressRoutine()
    {
        var down = capUp + Vector3.down * pressDepth;
        for (float t = 0f; t < 1f; t += Time.deltaTime / pressSeconds)
        {
            cap.localPosition = Vector3.Lerp(capUp, down, t);
            yield return null;
        }
        for (float t = 0f; t < 1f; t += Time.deltaTime / pressSeconds)
        {
            cap.localPosition = Vector3.Lerp(down, capUp, t);
            yield return null;
        }
        cap.localPosition = capUp;
        pressing = null;
    }
}
