using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class CubeController : MonoBehaviour
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

    [Header("Oven")]
    [Tooltip("The oven tile. Land on it with the baking sheet side down to drop the cookie there. If empty, the object named 'oven' is used.")]
    [SerializeField] private Transform ovenTile;

    [Tooltip("Extra height for the cookie when it is put on the oven tile.")]
    [SerializeField] private float ovenCookieHeightOffset = 0f;

    [Header("Cookie on the baking sheet")]
    [Tooltip("Tiny gap between the baking sheet side and the cookie (prevents flickering).")]
    [SerializeField] private float cookieGap = 0.005f;

    [Tooltip("Rotation (degrees, in world space) of the cookie while it lies flat against the baking sheet. " +
             "If the gingerbread man stands up instead of lying flat, try X = 90 or -90. Use Y to turn it around.")]
    [SerializeField] private Vector3 cookieRotation = Vector3.zero;

    [Header("Alignment")]
    [Tooltip("When swapping, move the new dough/cookie to the centre of the dough tile and sit it on the same ground level as the round dough. Turn off if you positioned the objects yourself.")]
    [SerializeField] private bool alignDough = true;

    [Tooltip("Extra height added to the flat dough, cut dough, dough with hole and cookie after aligning. Raise this if they sink into the tile.")]
    [SerializeField] private float doughHeightOffset = 0f;

    [Header("Debug")]
    [Tooltip("Writes messages to the Console that explain why the cookie was (not) picked up or dropped.")]
    [SerializeField] private bool debugLogs = true;

    // Fired once a roll has completely finished (cube is at rest on a tile).
    public static event Action<CubeController> Landed;

    // Fired when the round dough is rolled flat with the rolling pin.
    public static event Action<CubeController> DoughFlattened;

    // Fired when the cookie shape is cut into the flat dough.
    public static event Action<CubeController> DoughCut;

    // Fired when the cookie is picked up with the baking sheet side.
    public static event Action<CubeController> CookieTaken;

    // Fired when the carried cookie is dropped on the oven tile.
    public static event Action<CubeController> CookieDroppedOnOven;

    // Whether the dough objects exist in this scene (resolved in Awake).
    // Used to build the default task list.
    public bool HasDough => roundDough != null;

    // Whether an oven tile is present in this scene (resolved in Awake).
    public bool HasOvenTile => ovenTile != null;

    private enum DoughState { Round, Flat, Cut, CookieTaken }

    private bool isRolling;
    private Vector3 startPosition;
    private CubeFaceTextures faceTextures;
    private DoughState doughState = DoughState.Round;
    private bool carryingCookie;

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
        if (ovenTile == null)
        {
            GameObject oven = FindSceneObject("oven");
            if (oven != null) ovenTile = oven.transform;
        }

        if (roundDough == null) Debug.LogWarning("CubeController: could not find 'dough'.", this);
        if (flatDough == null) Debug.LogWarning("CubeController: could not find 'dough_flat'.", this);
        if (cutDough == null) Debug.LogWarning("CubeController: could not find 'dough_cut'.", this);
        if (doughWithHole == null) Debug.LogWarning("CubeController: could not find 'dough_cutout'.", this);
        if (cookie == null) Debug.LogWarning("CubeController: could not find 'cookie'.", this);
        if (ovenTile == null) Debug.LogWarning("CubeController: no oven tile assigned (or object named 'oven' found).", this);

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

        // Show this level's task list in the top-right corner. The default list
        // only contains tasks this level can actually fulfil. If the scene has its
        // own TaskListUI object, that one is used instead of the default list.
        if (FindAnyObjectByType<TaskListUI>() == null)
            TaskListUI.CreateDefaultList(this);
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

        Vector3 endPos = startPos + dir * stepSize;
        Quaternion finalRot = Quaternion.Euler(e);
        transform.SetPositionAndRotation(endPos, finalRot);

        // Landed with the baking sheet side down?
        if (faceTextures != null && faceTextures.IsBakingSheetFacingDown(finalRot))
        {
            bool onCutDough = IsOnTile(endPos, DoughTilePosition());

            if (doughState == DoughState.Cut && onCutDough)
                TakeCookie();            // on the cut dough: the cookie sticks to the baking sheet
            else if (carryingCookie && ovenTile != null && IsOnTile(endPos, TileCenter(ovenTile)))
                DropCookieOnOven();      // on the oven tile: the cookie is put down
            else if (debugLogs)
                Debug.Log($"CubeController: baking sheet is down, but nothing happened. " +
                          $"Dough state = {doughState} (needs Cut), on dough tile = {onCutDough}, " +
                          $"carrying cookie = {carryingCookie}.", this);
        }
        else if (debugLogs && faceTextures != null && doughState == DoughState.Cut
                 && IsOnTile(endPos, DoughTilePosition()))
        {
            Debug.Log("CubeController: landed on the cut dough, but the baking sheet side is not facing down " +
                      "(is 'Is Baking Sheet' ticked on the right face in CubeFaceTextures?).", this);
        }

        if (flattenWhenMoveEnds)
        {
            ChangeDough(roundDough, flatDough, DoughState.Flat);
            DoughFlattened?.Invoke(this);
        }
        else if (cutWhenMoveEnds)
        {
            ChangeDough(flatDough, cutDough, DoughState.Cut);
            DoughCut?.Invoke(this);
        }

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
        return IsOnTile(position, DoughTilePosition());
    }

    private bool IsOnTile(Vector3 position, Vector3 tileCenter)
    {
        float tolerance = stepSize * 0.25f;
        return Mathf.Abs(position.x - tileCenter.x) <= tolerance
            && Mathf.Abs(position.z - tileCenter.z) <= tolerance;
    }

    // Centre of a tile object (uses its visible bounds when it has any, else its pivot).
    private static Vector3 TileCenter(Transform tile)
    {
        return TryGetBounds(tile.gameObject, out Bounds b) ? b.center : tile.position;
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
    // Called when the cube has just landed on the cut dough with the baking sheet side down.
    private void TakeCookie()
    {
        if (cookie == null)
        {
            Debug.LogWarning("CubeController: no cookie object assigned or found (name it 'cookie' or " +
                             "drag it into the Cookie slot).", this);
        }
        else
        {
            // Attach to the cube first (keeping its world position), THEN show it. This way it also
            // appears if it was a child of an object that is hidden right now.
            cookie.transform.SetParent(transform, true);
            cookie.SetActive(true);

            // Lay the cookie flat. (The baking sheet side is facing down right now, so a flat
            // cookie in world space is also flat against the sheet.)
            cookie.transform.rotation = Quaternion.Euler(cookieRotation);

            // The cookie rides along with the cube, so it must not block the ground raycasts.
            foreach (Collider c in cookie.GetComponentsInChildren<Collider>())
                c.enabled = false;

            // Put the cookie directly UNDER the baking sheet side (outside the cube),
            // centred below the cube, with its top touching the sheet.
            if (TryGetBounds(cookie, out Bounds b))
            {
                float sheetY = transform.position.y - stepSize * 0.5f;
                Vector3 delta = new Vector3(
                    transform.position.x - b.center.x,
                    (sheetY - cookieGap) - b.max.y,
                    transform.position.z - b.center.z);
                cookie.transform.position += delta;
            }
            else
            {
                Debug.LogWarning("CubeController: the cookie has no renderer, so it can't be positioned.", this);
            }

            carryingCookie = true;
            if (debugLogs) Debug.Log("CubeController: cookie picked up.", this);
        }

        ChangeDough(cutDough, doughWithHole, DoughState.CookieTaken);
        CookieTaken?.Invoke(this);
    }

    // Puts the carried cookie down on the oven tile.
    private void DropCookieOnOven()
    {
        if (cookie == null || ovenTile == null) return;

        cookie.transform.SetParent(null, true);
        carryingCookie = false;

        Vector3 center = TileCenter(ovenTile);
        float surfaceY = TryGetBounds(ovenTile.gameObject, out Bounds ovenBounds)
            ? ovenBounds.max.y
            : ovenTile.position.y;

        if (TryGetBounds(cookie, out Bounds b))
        {
            Vector3 delta = new Vector3(
                center.x - b.center.x,
                (surfaceY + ovenCookieHeightOffset) - b.min.y,
                center.z - b.center.z);
            cookie.transform.position += delta;
        }

        CookieDroppedOnOven?.Invoke(this);
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