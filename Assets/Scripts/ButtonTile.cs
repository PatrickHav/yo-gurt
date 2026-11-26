using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A button tile. Landing on it with ANY face pressed it (one time, permanent).
// It lowers a little as visual feedback and opens every linked gate.
public class ButtonTile : MonoBehaviour
{
    [Header("Linked targets")]
    [Tooltip("The gates/bridges that open when this button is pressed.")]
    [SerializeField] private List<GateTile> linkedGates = new List<GateTile>();

    [Header("Press animation")]
    [Tooltip("How far the button lowers when pressed.")]
    [SerializeField] private float pressDistance = 0.08f;

    [Tooltip("Seconds the press animation takes.")]
    [SerializeField] private float pressDuration = 0.15f;

    [Tooltip("Optionally tint the button when it is pressed.")]
    [SerializeField] private bool changeColorOnPress;

    [Tooltip("Colour used when 'Change Color On Press' is on.")]
    [SerializeField] private Color pressedColor = new Color(0.55f, 0.15f, 0.15f, 1f);

    // Fired once, when the button was pressed.
    public static event Action<ButtonTile> ButtonPressed;

    // Whether the button was already pressed (permanent).
    public bool IsPressed { get; private set; }

    private Vector3 restPosition;
    private Renderer[] tintRenderers;

    private void Awake()
    {
        restPosition = transform.position;
        tintRenderers = GetComponentsInChildren<Renderer>();
    }

    private void OnEnable() => CubeController.Landed += OnCubeLanded;
    private void OnDisable() => CubeController.Landed -= OnCubeLanded;

    private void OnCubeLanded(CubeController cube)
    {
        if (IsPressed) return;
        if (!CubeOnButton(cube.transform.position)) return;

        IsPressed = true;

        // Any face counts: the button is pressed.
        ButtonPressed?.Invoke(this);
        StartCoroutine(PressAnimation());

        foreach (GateTile gate in linkedGates)
        {
            if (gate != null)
                gate.Open();
        }
    }

    // Top-down footprint check, same principle as TeleportPortal (renderer bounds).
    private bool CubeOnButton(Vector3 p)
    {
        if (!TryGetBounds(gameObject, out Bounds b)) return false;
        return p.x >= b.min.x && p.x <= b.max.x
            && p.z >= b.min.z && p.z <= b.max.z;
    }

    // Lowers the button a little and optionally changes its colour.
    private IEnumerator PressAnimation()
    {
        Vector3 pressedPosition = restPosition - Vector3.up * pressDistance;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(pressDuration, 0.0001f);
            transform.position = Vector3.Lerp(restPosition, pressedPosition, Mathf.Clamp01(t));
            yield return null;
        }
        transform.position = pressedPosition;

        if (changeColorOnPress)
        {
            foreach (Renderer r in tintRenderers)
            {
                if (r == null) continue;
                // r.materials (not sharedMaterials) so the shared tile material is not tinted.
                foreach (Material m in r.materials)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", pressedColor);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", pressedColor);
                }
            }
        }
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
