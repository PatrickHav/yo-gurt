using System.IO;
using UnityEngine;

/// <summary>
/// Puts a different texture on each of the 6 sides of a cube.
/// Each face is a thin child quad that sits just above the cube surface,
/// so the textures rotate with the cube and stay glued to their sides.
///
/// For every face you can either drag a PNG (Texture2D) into the slot,
/// or type a file path (absolute, or relative to StreamingAssets).
/// If a file path is set and the file exists, it wins over the dragged texture.
/// </summary>
public class CubeFaceTextures : MonoBehaviour
{
    [System.Serializable]
    public class Face
{
    [Tooltip("Drag a PNG here.")]
    public Texture2D texture;

    [Tooltip("Optional. Absolute path, or relative to Assets/StreamingAssets. Overrides the texture slot.")]
    public string filePath;

    [Tooltip("Rotate the image on this face in 90 degree steps.")]
    [Range(0, 3)] public int rotate90;
}

[Header("Faces (local to the cube)")]
public Face top;     // +Y
public Face bottom;  // -Y
public Face front;   // +Z
public Face back;    // -Z
public Face left;    // -X
public Face right;   // +X

[Header("Settings")]
[Tooltip("Material to copy for the faces. Leave empty to copy the cube's own material.")]
[SerializeField] private Material baseMaterial;

[Tooltip("Transparent pixels in the PNG show the cube's base colour underneath (good for cutouts).")]
[SerializeField] private bool alphaCutout = true;

[Tooltip("How far the face quads float above the cube surface (prevents z-fighting).")]
[SerializeField] private float surfaceOffset = 0.001f;

private const string FacePrefix = "Face_";

private void Awake()
{
    Rebuild();
}

/// <summary>Rebuilds all face quads. Also available from the component's context menu.</summary>
[ContextMenu("Rebuild Faces")]
public void Rebuild()
{
    ClearFaces();

    Material source = baseMaterial != null ? baseMaterial : GetBaseMaterialFromRenderer();
    if (source == null)
    {
        Debug.LogWarning("CubeFaceTextures: no base material found. Assign one in the inspector.", this);
        return;
    }

    // name, face data, outward direction, "up" hint for the quad
    BuildFace("Top", top, Vector3.up, Vector3.forward, source);
    BuildFace("Bottom", bottom, Vector3.down, Vector3.back, source);
    BuildFace("Front", front, Vector3.forward, Vector3.up, source);
    BuildFace("Back", back, Vector3.back, Vector3.up, source);
    BuildFace("Left", left, Vector3.left, Vector3.up, source);
    BuildFace("Right", right, Vector3.right, Vector3.up, source);
}

private Material GetBaseMaterialFromRenderer()
{
    var r = GetComponent<Renderer>();
    return r != null ? r.sharedMaterial : null;
}

private void BuildFace(string faceName, Face face, Vector3 outward, Vector3 upHint, Material source)
{
    if (face == null) return;

    Texture2D tex = ResolveTexture(face, faceName);
    if (tex == null) return; // nothing assigned for this side

    GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
    quad.name = FacePrefix + faceName;

    // Faces don't need their own collider.
    Collider col = quad.GetComponent<Collider>();
    if (col != null) SafeDestroy(col);

    Transform t = quad.transform;
    t.SetParent(transform, false);
    t.localPosition = outward * (0.5f + surfaceOffset);

    // A Unity quad's visible side points along -Z, so look "inward" to make it face outward.
    t.localRotation = Quaternion.LookRotation(-outward, upHint)
                      * Quaternion.Euler(0f, 0f, 90f * face.rotate90);
    t.localScale = Vector3.one;

    var rend = quad.GetComponent<MeshRenderer>();
    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    rend.sharedMaterial = CreateMaterial(source, tex);
}

private Material CreateMaterial(Material source, Texture2D tex)
{
    var mat = new Material(source);

    // URP Lit/Unlit use _BaseMap/_BaseColor, built-in uses _MainTex/_Color.
    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
    if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

    if (alphaCutout && mat.HasProperty("_AlphaClip"))
    {
        mat.SetFloat("_AlphaClip", 1f);
        if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.renderQueue = 2450;
    }

    return mat;
}

private Texture2D ResolveTexture(Face face, string faceName)
{
    if (!string.IsNullOrWhiteSpace(face.filePath))
    {
        string path = Path.IsPathRooted(face.filePath)
            ? face.filePath
            : Path.Combine(Application.streamingAssetsPath, face.filePath);

        if (File.Exists(path))
        {
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (loaded.LoadImage(File.ReadAllBytes(path)))
            {
                loaded.name = Path.GetFileNameWithoutExtension(path);
                return loaded;
            }
            Debug.LogWarning($"CubeFaceTextures: could not decode image for {faceName}: {path}", this);
        }
        else
        {
            Debug.LogWarning($"CubeFaceTextures: file not found for {faceName}: {path}", this);
        }
    }

    return face.texture;
}

private void ClearFaces()
{
    for (int i = transform.childCount - 1; i >= 0; i--)
    {
        Transform child = transform.GetChild(i);
        if (child.name.StartsWith(FacePrefix))
            SafeDestroy(child.gameObject);
    }
}

private static void SafeDestroy(Object obj)
{
    if (Application.isPlaying) Destroy(obj);
    else DestroyImmediate(obj);
}
}
