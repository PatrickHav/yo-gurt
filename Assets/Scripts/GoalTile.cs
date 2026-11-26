using System;
using UnityEngine;

// The plate / goal tile. It only counts if the pot sequence is already complete:
// landing on it afterwards fires PlateReached. While the pot is not complete the
// cube may walk over it, nothing happens.
public class GoalTile : MonoBehaviour
{
    [Tooltip("The pot that must be complete before this plate counts.\n" +
             "If empty, the first CookingPot in the scene is used.")]
    [SerializeField] private CookingPot requiredPot;

    // Fired when the pot is already complete and the cube lands on the plate.
    public static event Action<GoalTile> PlateReached;

    private void Awake()
    {
        if (requiredPot == null)
            requiredPot = FindAnyObjectByType<CookingPot>();
    }

    private void OnEnable() => CubeController.Landed += OnCubeLanded;
    private void OnDisable() => CubeController.Landed -= OnCubeLanded;

    private void OnCubeLanded(CubeController cube)
    {
        // Only counts if the pot sequence is already complete.
        if (requiredPot == null || !requiredPot.IsComplete) return;
        if (!CubeOnPlate(cube.transform.position)) return;

        PlateReached?.Invoke(this);
    }

    // Top-down footprint check, same principle as TeleportPortal (renderer bounds).
    private bool CubeOnPlate(Vector3 p)
    {
        if (!TryGetBounds(gameObject, out Bounds b)) return false;
        return p.x >= b.min.x && p.x <= b.max.x
            && p.z >= b.min.z && p.z <= b.max.z;
    }

    // Combined world-space bounds of every renderer on this object and its children.
    private static bool TryGetBounds(GameObject obj, out Bounds bounds)
    {
        bounds = default;
        if (obj == null) return false;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }
}
