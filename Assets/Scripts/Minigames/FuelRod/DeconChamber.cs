using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The purification chamber the Handler has to stand in to be decontaminated. Put this on an empty object in the
/// decon room and size the BoxCollider to fill the floor area - it's a trigger, so players walk straight through it.
///
/// The server checks the Handler's position against the box every frame (works for remote players, no Rigidbody
/// needed) and keeps FuelRodMinigame.HandlerInDecon up to date. The Purifier's DeconButtons only work while it's true.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DeconChamber : MonoBehaviour
{
    [Tooltip("Height above the player's feet (pivot) that has to be inside the box.")]
    [SerializeField] float checkHeight = 0.5f;

    BoxCollider box;

    void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true; // pass-through
    }

    void Update()
    {
        var game = FuelRodMinigame.Current;
        if (game == null || !game.IsServer)
            return;

        bool inside = IsInside(game.HandlerClient.Value);
        if (game.HandlerInDecon.Value != inside)
            game.SetHandlerInDecon(inside);
    }

    bool IsInside(ulong clientId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            return false;

        // Test in the box's local space so a rotated/scaled chamber still works
        var point = client.PlayerObject.transform.position + Vector3.up * checkHeight;
        var d = transform.InverseTransformPoint(point) - box.center;
        var half = box.size * 0.5f;
        return Mathf.Abs(d.x) <= half.x && Mathf.Abs(d.y) <= half.y && Mathf.Abs(d.z) <= half.z;
    }

    void OnDrawGizmos()
    {
        var b = GetComponent<BoxCollider>();
        if (b == null)
            return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.15f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.8f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
