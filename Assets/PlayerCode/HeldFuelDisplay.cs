using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shows the fuel the Handler is carrying as a glowing rock held in front of them.
/// Runs on every copy of the player, so everyone can see what the Handler is holding.
/// </summary>
public class HeldFuelDisplay : NetworkBehaviour
{
    [Tooltip("Rock material per fuel, indexed by FuelType (element 0 = None, unused).")]
    [SerializeField] Material[] fuelMaterials;
    [SerializeField] Vector3 holdOffset = new Vector3(0.3f, -0.25f, 0.55f);
    [SerializeField] float rockSize = 0.14f;
    [SerializeField] float spinSpeed = 60f;

    GameObject rock;
    Renderer rockRenderer;
    Light glow;
    FuelType shown;

    void Start()
    {
        // Hang it off the head camera so it sits in the owner's view (camera is disabled, not missing, on other copies)
        var cam = GetComponentInChildren<Camera>(true);

        rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        rock.name = "HeldFuel";
        Destroy(rock.GetComponent<Collider>());
        var holder = cam != null ? cam.transform : transform;
        rock.transform.SetParent(holder, false);
        // The player prefab is scaled up, so convert our metre values into the holder's local space
        var s = holder.lossyScale;
        rock.transform.localPosition = new Vector3(holdOffset.x / s.x, holdOffset.y / s.y, holdOffset.z / s.z);
        rock.transform.localScale = new Vector3(rockSize / s.x, rockSize * 0.8f / s.y, rockSize * 0.9f / s.z); // lumpy, not a perfect ball
        rockRenderer = rock.GetComponent<Renderer>();
        rockRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(rock.transform, false);
        glow.type = LightType.Point;
        glow.range = 1.5f;
        glow.intensity = 1.5f;
        glow.shadows = LightShadows.None;

        rock.SetActive(false);
    }

    void Update()
    {
        var game = FuelRodMinigame.Current;
        var fuel = FuelType.None;
        if (game != null && game.IsSpawned && game.HandlerClient.Value == OwnerClientId)
            fuel = game.CarriedFuel.Value;

        if (fuel != shown)
        {
            shown = fuel;
            rock.SetActive(fuel != FuelType.None);
            if (fuel != FuelType.None)
            {
                if (fuelMaterials != null && (int)fuel < fuelMaterials.Length && fuelMaterials[(int)fuel] != null)
                    rockRenderer.sharedMaterial = fuelMaterials[(int)fuel];
                glow.color = FuelRodGrid.FuelColor(fuel);
            }
        }

        if (rock.activeSelf)
            rock.transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
    }
}
