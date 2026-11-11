using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class TeleportPortal : MonoBehaviour
{
    [Header("Behaviour")]
    [Tooltip("ON  -> the cube is sent back to its spawn tile.\nOFF -> the cube is sent to the Linked Portal's tile.")]
    [SerializeField] private bool returnToSpawn = true;

    [Tooltip("The portal tile the cube teleports to when 'Return To Spawn' is OFF.\nDrag the other portal in here. If left empty it falls back to spawn.")]
    [SerializeField] private TeleportPortal linkedPortal;

    private Renderer rend;

    private void Awake()
    {
        rend = GetComponent<Renderer>();
    }

    private void OnEnable() => RollingCube.Landed += OnCubeLanded;
    private void OnDisable() => RollingCube.Landed -= OnCubeLanded;

    private void OnCubeLanded(RollingCube cube)
    {
        // Only react if the cube's centre is inside this portal's footprint (top-down).
        if (rend == null) return;
        Bounds b = rend.bounds;
        Vector3 p = cube.transform.position;

        bool inside = p.x >= b.min.x && p.x <= b.max.x &&
                      p.z >= b.min.z && p.z <= b.max.z;

        if (!inside) return;

        // Decide where the cube goes now, but apply the move at the END of the
        // frame. This is important: Landed is delivered to EVERY portal, so if we
        // moved the cube here, a portal later in the event list would read the new
        // position and immediately teleport it straight back. Deferring means every
        // portal reads the cube's original landing position, so only the portal it
        // actually landed on reacts.
        if (returnToSpawn || linkedPortal == null)
        {
            // Safe fallback is always "back to spawn" (position only, rotation untouched).
            StartCoroutine(MoveAtEndOfFrame(() => cube.ResetToStart()));
            return;
        }

        // Teleport to the linked portal's tile.
        // Keep the cube's height *relative to the portal plane* so it lands sitting
        // correctly even if the two tiles are at different elevations.
        float restOffset = p.y - transform.position.y;
        Vector3 dest = linkedPortal.transform.position;
        Vector3 target = new Vector3(dest.x, dest.y + restOffset, dest.z);
        StartCoroutine(MoveAtEndOfFrame(() => cube.transform.position = target));
        // Rotation is deliberately untouched, same as ResetToStart().
    }

    private static IEnumerator MoveAtEndOfFrame(Action action)
    {
        yield return new WaitForEndOfFrame();
        action();
    }
}
