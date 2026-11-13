using System;
using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class RollingCube : MonoBehaviour
{
    [Tooltip("Distance of one roll. Should match your tile size (cube size = 1).")]
    [SerializeField] private float stepSize = 1f;

    [Tooltip("Seconds one roll takes.")]
    [SerializeField] private float rollDuration = 0.25f;

    [Tooltip("Layers that count as valid ground. Leave on Everything if unsure.")]
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Dough")]
    [Tooltip("The normal (round) dough object. If empty, the object named 'dough' is used.")]
    [SerializeField] private GameObject roundDough;

    [Tooltip("The flat dough. Can be an object in the scene or the imported model/prefab from the Project window. If empty, the object named 'dough_flat' is used.")]
    [SerializeField] private GameObject flatDough;

    [Tooltip("Optional. Transform at the centre of the dough tile. If empty, the round dough's position is used.")]
    [SerializeField] private Transform doughTile;

    [Tooltip("When flattening, move the flat dough to the centre of the dough tile and sit it on the same ground level as the round dough. Turn off if you positioned it yourself.")]
    [SerializeField] private bool alignFlatDough = true;
    [Tooltip("Extra height added to the flat dough after aligning. Raise this if it sinks into the tile.")]
    [SerializeField] private float flatDoughHeightOffset = 0f;

    // Fired once a roll has completely finished (cube is at rest on a tile).
    public static event Action<RollingCube> Landed;

    private bool isRolling;
    private Vector3 startPosition;
    private CubeFaceTextures faceTextures;
    private bool doughFlattened;

    private void Awake()
    {
        startPosition = transform.position;
        faceTextures = GetComponent<CubeFaceTextures>();

        // Fall back to finding the objects by name (also finds inactive ones).
        if (roundDough == null) roundDough = FindSceneObject("dough");
        if (flatDough == null) flatDough = FindSceneObject("dough_flat");

        if (roundDough == null) Debug.LogWarning("RollingCube: could not find 'dough'.", this);
        if (flatDough == null) Debug.LogWarning("RollingCube: could not find 'dough_flat'.", this);

        // If a model/prefab from the Project window was assigned (not a scene object),
        // create a copy in the scene to use.
        if (flatDough != null && !flatDough.scene.IsValid())
            flatDough = Instantiate(flatDough);

        if (flatDough != null) flatDough.SetActive(false);
        if (roundDough != null) roundDough.SetActive(true);
    }

    private void Update()
    {
        if (isRolling) return;

        Vector3 dir = ReadDirection();
        if (dir == Vector3.zero) return;

        if (!HasGroundAt(transform.position + dir * stepSize)) return; // blocked

        StartCoroutine(Roll(dir));
    }

    // Moves the cube back to its start tile. Rotation is deliberately NOT touched,
    // so every face stays exactly where it was. The dough keeps its current state.
    public void ResetToStart()
    {
        StopAllCoroutines();
        isRolling = false;
        transform.position = startPosition;
    }

    private Vector3 ReadDirection()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return Vector3.zero;
        if (kb.wKey.isPressed) return Vector3.forward;
        if (kb.sKey.isPressed) return Vector3.back;
        if (kb.aKey.isPressed) return Vector3.left;
        if (kb.dKey.isPressed) return Vector3.right;
#else
        if (Input.GetKey(KeyCode.W)) return Vector3.forward;
        if (Input.GetKey(KeyCode.S)) return Vector3.back;
        if (Input.GetKey(KeyCode.A)) return Vector3.left;
        if (Input.GetKey(KeyCode.D)) return Vector3.right;
#endif
        return Vector3.zero;
    }

    private bool HasGroundAt(Vector3 targetCenter)
    {
        // Cast straight down from the target cell's centre.
        Vector3 origin = targetCenter + Vector3.up * 0.1f;
        float distance = stepSize * 0.5f + 0.3f;
        return Physics.Raycast(origin, Vector3.down, distance, groundMask,
                               QueryTriggerInteraction.Ignore);
    }

    private IEnumerator Roll(Vector3 dir)
    {
        isRolling = true;

        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        // Are we standing on the dough tile with the rolling pin face pointing down?
        // Checked BEFORE the move: the dough flattens as we leave the tile.
        bool flattenWhenMoveEnds = !doughFlattened
            && IsOnDoughTile(startPos)
            && faceTextures != null
            && faceTextures.IsRollingPinFacingDown(startRot);

        // Pivot = the bottom edge we're tipping over.
        Vector3 pivot = startPos + dir * (stepSize * 0.5f) + Vector3.down * (stepSize * 0.5f);
        Vector3 axis = Vector3.Cross(Vector3.up, dir);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / rollDuration;
            float angle = Mathf.Lerp(0f, 90f, Mathf.Clamp01(t));

            // Re-apply from the start each frame so no drift accumulates.
            transform.SetPositionAndRotation(startPos, startRot);
            transform.RotateAround(pivot, axis, angle);
            yield return null;
        }

        // Snap to exact values.
        Quaternion endRot = Quaternion.AngleAxis(90f, axis) * startRot;
        Vector3 e = endRot.eulerAngles;
        e.x = Mathf.Round(e.x / 90f) * 90f;
        e.y = Mathf.Round(e.y / 90f) * 90f;
        e.z = Mathf.Round(e.z / 90f) * 90f;

        transform.SetPositionAndRotation(startPos + dir * stepSize, Quaternion.Euler(e));

        if (flattenWhenMoveEnds)
            FlattenDough();

        isRolling = false;

        Landed?.Invoke(this);
    }

    private Vector3 DoughTilePosition()
    {
        if (doughTile != null) return doughTile.position;
        return roundDough != null ? roundDough.transform.position : Vector3.zero;
    }

    private bool IsOnDoughTile(Vector3 position)
    {
        if (doughTile == null && roundDough == null) return false;

        float tolerance = stepSize * 0.25f;
        Vector3 d = DoughTilePosition();
        return Mathf.Abs(position.x - d.x) <= tolerance
            && Mathf.Abs(position.z - d.z) <= tolerance;
    }

    private void FlattenDough()
    {
        if (doughFlattened) return;

        // Measure the round dough while it's still visible.
        bool hasRoundBounds = TryGetBounds(roundDough, out Bounds roundBounds);

        if (flatDough != null)
        {
            flatDough.SetActive(true);

            if (alignFlatDough && TryGetBounds(flatDough, out Bounds flatBounds))
            {
                Vector3 tile = DoughTilePosition();
               float groundY = (hasRoundBounds ? roundBounds.min.y : flatBounds.min.y) + flatDoughHeightOffset;

                // Centre it on the tile (X/Z) and put its bottom where the round dough's bottom was.
                Vector3 delta = new Vector3(
                    tile.x - flatBounds.center.x,
                    groundY - flatBounds.min.y,
                    tile.z - flatBounds.center.z);
                flatDough.transform.position += delta;
            }
        }

        if (roundDough != null) roundDough.SetActive(false);

        doughFlattened = true;
    }

    // Combined world-space bounds of every renderer on an object and its children.
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

    // Finds an object in the open scene by name, including inactive ones.
    private static GameObject FindSceneObject(string objectName)
    {
        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t.name == objectName
                && t.gameObject.scene.IsValid()
                && t.hideFlags == HideFlags.None)
                return t.gameObject;
        }
        return null;
    }
}