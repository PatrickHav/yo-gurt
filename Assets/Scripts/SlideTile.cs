using System;
using System.Collections.Generic;
using UnityEngine;

// A long tile the cube slides across. The cube can only roll ONTO it along its
// direction; it is then carried one tile further without any input (it can never
// stop on the slide). Entering from the side or against the direction is blocked,
// as is entering if there is no ground on the tile after the slide.
//
// No tool, portal, button or pot effects trigger on the slide tile itself -
// only the FINAL landing counts (handled in CubeController).
public class SlideTile : MonoBehaviour
{
    [Header("Direction")]
    [Tooltip("The way the cube is carried. Leave at (0, 0, 0) to take this object's\n" +
             "forward axis, snapped to the nearest world axis.")]
    [SerializeField] private Vector3 direction = Vector3.zero;

    [Header("Chained roll")]
    [Tooltip("Seconds the second (automatic) roll takes. 0 = same duration as the normal roll.\n" +
             "Set e.g. 0.18 for a slightly faster slide-out.")]
    [SerializeField] private float chainedRollDuration = 0f;

    // Fired when the cube was carried across this slide (at the final landing).
    public static event Action<SlideTile> SlideUsed;

    // Called by the CubeController when the cube has been carried across this slide.
    public void NotifySlideUsed()
    {
        SlideUsed?.Invoke(this);
    }

    // The carry direction, snapped to a world axis.
    public Vector3 Direction => direction != Vector3.zero
        ? SnapToAxis(direction)
        : SnapToAxis(transform.forward);

    // Duration of the second leg of the chained roll (0 = use the cube's rollDuration).
    public float ChainedRollDuration => chainedRollDuration;

    // Registered slides, so the cube can ask "is the target tile a slide?"
    // without per-frame FindObjects calls.
    private static readonly List<SlideTile> registered = new List<SlideTile>();

    private void OnEnable()
    {
        if (!registered.Contains(this))
            registered.Add(this);
    }

    private void OnDisable()
    {
        registered.Remove(this);
    }

    // The slide whose top-down footprint contains the given (world) position, or null.
    public static SlideTile At(Vector3 position)
    {
        for (int i = 0; i < registered.Count; i++)
        {
            SlideTile s = registered[i];
            if (s != null && s.Covers(position))
                return s;
        }
        return null;
    }

    // Top-down footprint check, same principle as TeleportPortal (renderer bounds).
    private bool Covers(Vector3 p)
    {
        if (!TryGetBounds(gameObject, out Bounds b)) return false;
        return p.x >= b.min.x && p.x <= b.max.x
            && p.z >= b.min.z && p.z <= b.max.z;
    }

    // Snaps a direction to the nearest world axis (a slide always carries along one axis).
    private static Vector3 SnapToAxis(Vector3 v)
    {
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.z))
            return v.x < 0f ? Vector3.left : Vector3.right;
        return v.z < 0f ? Vector3.back : Vector3.forward;
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
