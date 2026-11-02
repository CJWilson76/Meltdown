using UnityEngine;

/// <summary>
/// Sci-fi sliding door. The panels slide into the wall when a player walks into the trigger,
/// and close again once everyone has left. Runs locally on every client, so no networking is needed.
///
/// Put this on the door root alongside a trigger collider covering the area in front of and behind the door.
/// For a single door, assign only the panel on the side it should slide towards and leave the other empty.
/// </summary>
public class SlidingDoor : MonoBehaviour
{
    [Tooltip("Panel that slides towards local -Z. Optional.")]
    [SerializeField] Transform leftPanel;
    [Tooltip("Panel that slides towards local +Z. Optional.")]
    [SerializeField] Transform rightPanel;

    [Tooltip("How far each panel slides when fully open.")]
    [SerializeField] float openDistance = 0.78f;
    [Tooltip("Metres per second the panels move.")]
    [SerializeField] float speed = 2.5f;

    Vector3 leftClosed, rightClosed;
    int playersInside;
    float openAmount; // 0 = closed, 1 = open

    void Start()
    {
        if (leftPanel) leftClosed = leftPanel.localPosition;
        if (rightPanel) rightClosed = rightPanel.localPosition;
    }

    void Update()
    {
        float target = playersInside > 0 ? 1f : 0f;
        openAmount = Mathf.MoveTowards(openAmount, target, speed / openDistance * Time.deltaTime);

        float offset = Mathf.SmoothStep(0f, 1f, openAmount) * openDistance;
        if (leftPanel) leftPanel.localPosition = leftClosed + Vector3.back * offset;
        if (rightPanel) rightPanel.localPosition = rightClosed + Vector3.forward * offset;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<Movement>()) playersInside++;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<Movement>()) playersInside = Mathf.Max(0, playersInside - 1);
    }
}
