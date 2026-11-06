using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class TeleportPortal : MonoBehaviour
{
    private Renderer rend;

    private void Awake()
    {
        rend = GetComponent<Renderer>();
    }

    private void OnEnable() => RollingCube.Landed += OnCubeLanded;
    private void OnDisable() => RollingCube.Landed -= OnCubeLanded;

    private void OnCubeLanded(RollingCube cube)
    {
        // Is the cube's centre inside this portal's footprint (top-down)?
        Bounds b = rend.bounds;
        Vector3 p = cube.transform.position;

        bool inside = p.x >= b.min.x && p.x <= b.max.x &&
                      p.z >= b.min.z && p.z <= b.max.z;

        if (inside)
            cube.ResetToStart(); // position only, rotation untouched
    }
}
