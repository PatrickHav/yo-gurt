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

    // Fired once a roll has completely finished (cube is at rest on a tile).
    public static event Action<RollingCube> Landed;

    private bool isRolling;
    private Vector3 startPosition;
    private CubeFaceTextures faceTextures;
    private Transform roundDough;
    private Renderer roundDoughRenderer;
    private bool doughFlattened;

    [SerializeField, Range(0.05f, 0.5f)] private float flattenedDoughHeight = 0.18f;

    private void Awake()
    {
        startPosition = transform.position;
        faceTextures = GetComponent<CubeFaceTextures>();

        GameObject doughObject = GameObject.Find("dough");
        if (doughObject != null)
        {
            roundDough = doughObject.transform;
            roundDoughRenderer = doughObject.GetComponentInChildren<Renderer>();
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
    // so every face stays exactly where it was.
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
        bool flattenDoughWhenMoveEnds = !doughFlattened
            && IsOnRoundDoughTile(startPos)
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
        if (flattenDoughWhenMoveEnds)
            FlattenRoundDough();
        isRolling = false;

        Landed?.Invoke(this);
    }

    private bool IsOnRoundDoughTile(Vector3 position)
    {
        if (roundDough == null) return false;

        float tolerance = stepSize * 0.25f;
        return Mathf.Abs(position.x - roundDough.position.x) <= tolerance
            && Mathf.Abs(position.z - roundDough.position.z) <= tolerance;
    }

    private void FlattenRoundDough()
    {
        if (roundDough == null || doughFlattened) return;

        float bottomBefore = roundDoughRenderer != null
            ? roundDoughRenderer.bounds.min.y
            : 0f;

        Vector3 scale = roundDough.localScale;
        scale.y *= flattenedDoughHeight;
        roundDough.localScale = scale;

        if (roundDoughRenderer != null)
        {
            float bottomAfter = roundDoughRenderer.bounds.min.y;
            roundDough.position += Vector3.up * (bottomBefore - bottomAfter);
        }

        doughFlattened = true;
    }
}
