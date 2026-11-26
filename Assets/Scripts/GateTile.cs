using System;
using System.Collections;
using UnityEngine;

// A gate/bridge tile. Closed by default: its renderers are hidden and its
// colliders are disabled, so the ground raycast sees no tile and the cube
// cannot enter. When opened (by a ButtonTile) it rises into place over
// about 0.5 s and the colliders are enabled; from then on it is walkable.
// Opening is permanent.
public class GateTile : MonoBehaviour
{
    [Header("Opening animation")]
    [Tooltip("Seconds the gate takes to rise into place.")]
    [SerializeField] private float openDuration = 0.5f;

    [Tooltip("How far below its normal position the gate sits while closed.")]
    [SerializeField] private float sinkHeight = 0.2f;

    [Tooltip("Hide the gate's renderers while closed (it still starts sunk by 'Sink Height').")]
    [SerializeField] private bool hideWhenClosed = true;

    // Fired when the gate has fully opened and is walkable.
    public static event Action<GateTile> GateOpened;

    // Whether the gate is open (or currently opening).
    public bool IsOpen { get; private set; }

    private Renderer[] renderers;
    private Collider[] colliders;
    private Vector3 openLocalPosition;
    private bool isOpening;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        openLocalPosition = transform.localPosition;

        // Closed by default.
        if (!IsOpen)
            ApplyClosedState();
    }

    // Opens the gate (permanent). Call this from e.g. a ButtonTile.
    public void Open()
    {
        if (IsOpen || isOpening) return;
        isOpening = true;
        StartCoroutine(OpenRoutine());
    }

    private void ApplyClosedState()
    {
        // Sunk below the normal position.
        transform.localPosition = openLocalPosition - Vector3.up * sinkHeight;

        // Hidden (optional) and un-walkable.
        if (hideWhenClosed)
            foreach (Renderer r in renderers)
                if (r != null) r.enabled = false;

        foreach (Collider c in colliders)
            if (c != null) c.enabled = false;
    }

    private IEnumerator OpenRoutine()
    {
        Vector3 closedPosition = openLocalPosition - Vector3.up * sinkHeight;
        transform.localPosition = closedPosition;

        // Rise into place (the renderers become visible while rising).
        foreach (Renderer r in renderers)
            if (r != null) r.enabled = true;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(openDuration, 0.0001f);
            transform.localPosition = Vector3.Lerp(closedPosition, openLocalPosition, Mathf.Clamp01(t));
            yield return null;
        }
        transform.localPosition = openLocalPosition;

        // From now on the tile is walkable.
        foreach (Collider c in colliders)
            if (c != null) c.enabled = true;

        IsOpen = true;
        GateOpened?.Invoke(this);
    }
}
