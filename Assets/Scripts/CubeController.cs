using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
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

    [Tooltip("The flat dough. Scene object or imported model/prefab. If empty, the object named 'dough_flat' is used.")]
    [SerializeField] private GameObject flatDough;

    [Tooltip("The dough with the cookie shape cut into it (cookie still in place). If empty, the object named 'dough_cut' is used.")]
    [FormerlySerializedAs("cutoutDough")]
    [SerializeField] private GameObject cutDough;

    [Tooltip("The dough with the cookie taken out (hole). If empty, the object named 'dough_cutout' is used.")]
    [SerializeField] private GameObject doughWithHole;

    [Tooltip("The gingerbread man that sticks to the baking sheet side. If empty, the object named 'cookie' is used.")]
    [SerializeField] private GameObject cookie;

    [Tooltip("Optional. Transform at the centre of the dough tile. If empty, the round dough's position is used.")]
    [SerializeField] private Transform doughTile;

    [Tooltip("When swapping, move the new dough/cookie to the centre of the dough tile and sit it on the same ground level as the round dough. Turn off if you positioned the objects yourself.")]
    [SerializeField] private bool alignDough = true;

    [Tooltip("Extra height added to the flat dough, cut dough, dough with hole and cookie after aligning. Raise this if they sink into the tile.")]
    [SerializeField] private float doughHeightOffset = 0f;

    // Fired once a roll has completely finished (cube is at rest on a tile).
    public static event Action<RollingCube> Landed;

    private enum DoughState { Round, Flat, Cut, CookieTaken }

    private bool isRolling;
    private Vector3 startPosition;
    private CubeFaceTextures faceTextures;
    private DoughState doughState = DoughState.Round;

    // Ground level of the round dough, measured once at the start.
    private bool hasGroundY;
    private float doughGroundY;

    private void Awake()
    {
        startPosition = transform.position;
        faceTextures = GetComponent<CubeFaceTextures>();

        // Fall back to finding the objects by name (also finds inactive ones).
        if (roundDough == null) roundDough = FindSceneObject("dough");
        if (flatDough == null) flatDough = FindSceneObject("dough_flat");
        if (cutDough == null) cutDough = FindSceneObject("dough_cut");
        if (doughWithHole == null) doughWithHole = FindSceneObject("dough_cutout");
        if (cookie == null) cookie = FindSceneObject("cookie");

        if (roundDough == null) Debug.LogWarning("RollingCube: could not find 'dough'.", this);
        if (flatDough == null) Debug.LogWarning("RollingCube: could not find 'dough_flat'.", this);
        if (cutDough == null) Debug.LogWarning("RollingCube: could not find 'dough_cut'.", this);
        if (doughWithHole == null) Debug.LogWarning("RollingCube: could not find 'dough_cutout'.", this);
        if (cookie == null) Debug.LogWarning("RollingCube: could not find 'cookie'.", this);

        // If a model/prefab from the Project window was assigned (not a scene object),
        // create a copy in the scene to use.
        flatDough = InstantiateIfAsset(flatDough);
        cutDough = InstantiateIfAsset(cutDough);
        doughWithHole = InstantiateIfAsset(doughWithHole);
        cookie = InstantiateIfAsset(cookie);

        if (roundDough != null) roundDough.SetActive(true);
        if (flatDough != null) flatDough.SetActive(false);
        if (cutDough != null) cutDough.SetActive(false);
        if (doughWithHole != null) doughWithHole.SetActive(false);
        if (cookie != null) cookie.SetActive(false);

        // Remember where the round dough's bottom is, to place the other versions at the same level.
        if (TryGetBounds(roundDough, out Bounds roundBounds))
        {
            hasGroundY = true;
            doughGroundY = roundBounds.min.y;
        }
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

        // Checked BEFORE the move: the dough changes as we leave the tile.
        bool onDough = faceTextures != null && IsOnDoughTile(startPos);

        // Round dough + rolling pin face down  ->  flat dough.
        bool flattenWhenMoveEnds = onDough
            && doughState == DoughState.Round
            && faceTextures.IsRollingPinFacingDown(startRot);

        // Flat dough + cookie cutter face down  ->  cut dough.
        bool cutWhenMoveEnds = onDough
            && doughState == DoughState.Flat
            && faceTextures.IsCookieCutterFacingDown(startRot);

        // Cut dough + baking sheet face down  ->  the cookie sticks to the baking sheet side.
        // This happens right as the roll starts, so the cookie travels with the cube.
        if (onDough
            && doughState == DoughState.Cut
            && faceTextures.IsBakingSheetFacingDown(startRot))
        {
            TakeCookie();
        }

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
            ChangeDough(roundDough, flatDough, DoughState.Flat);
        else if (cutWhenMoveEnds)
            ChangeDough(flatDough, cutDough, DoughState.Cut);

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

    // Hides the old dough, shows the new one and remembers the new state.
    private void ChangeDough(GameObject oldDough, GameObject newDough, DoughState newState)
    {
        if (newDough != null)
        {
            newDough.SetActive(true);
            if (alignDough) AlignToTile(newDough);
        }

        if (oldDough != null) oldDough.SetActive(false);

        doughState = newState;
    }

    // The cookie leaves the dough (hole appears) and sticks to the cube's baking sheet side.
    private void TakeCookie()
    {
        if (cookie != null)
        {
            cookie.SetActive(true);
            if (alignDough) AlignToTile(cookie);

            // The cookie rides along with the cube, so it must not block the ground raycasts.
            foreach (Collider c in cookie.GetComponentsInChildren<Collider>())
                c.enabled = false;

            // Keep its current world position/rotation, but follow the cube from now on.
            cookie.transform.SetParent(transform, true);
        }

        ChangeDough(cutDough, doughWithHole, DoughState.CookieTaken);
    }

    // Centres the object on the dough tile (X/Z) and sits its bottom on the round dough's ground level.
    private void AlignToTile(GameObject obj)
    {
        if (!TryGetBounds(obj, out Bounds b)) return;

        Vector3 tile = DoughTilePosition();
        float groundY = (hasGroundY ? doughGroundY : b.min.y) + doughHeightOffset;

        Vector3 delta = new Vector3(
            tile.x - b.center.x,
            groundY - b.min.y,
            tile.z - b.center.z);
        obj.transform.position += delta;
    }

    private static GameObject InstantiateIfAsset(GameObject obj)
    {
        return (obj != null && !obj.scene.IsValid()) ? Instantiate(obj) : obj;
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