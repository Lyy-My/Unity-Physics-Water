using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 构建水网格网格（尺寸由 WaterSimulationURP.size 决定），并将其输入实时高度纹理。需要使用 Water/URP 材质的 MeshRenderer。
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterSurfaceURP : MonoBehaviour
{
    public WaterSimulationURP water;
    [Tooltip("Grid subdivisions per side. 256 = 66k verts (fine on PC).")]
    public int meshResolution = 256;
    public Material material;

    [Header("Displacement (kept in sync with the shader)")]
    public float displacementScale = 0.12f;
    public float displacementClamp = 0.35f;
    public float normalScale       = 2.0f;

    MeshRenderer _mr;
    Vector2 _builtSize;

    static readonly int HeightTexID   = Shader.PropertyToID("_HeightTex");
    static readonly int DispScaleID    = Shader.PropertyToID("_DisplacementScale");
    static readonly int DispClampID    = Shader.PropertyToID("_DisplacementClamp");
    static readonly int NormalScaleID  = Shader.PropertyToID("_NormalScale");

    void Start()
    {
        if (water == null) water = FindAnyObjectByType<WaterSimulationURP>();
        _mr = GetComponent<MeshRenderer>();
        if (material != null) _mr.sharedMaterial = material;

        _builtSize = water ? water.Size : new Vector2(6, 6);
        BuildMesh(meshResolution, _builtSize);
        PushParams();
    }

    void Update()
    {
        if (water == null) return;

        // 如果在运行时检查器中尺寸发生变化，则重新构建
        if (water.Size != _builtSize)
        {
            _builtSize = water.Size;
            BuildMesh(meshResolution, _builtSize);
        }

        if (water.HeightTexture != null)
            _mr.sharedMaterial.SetTexture(HeightTexID, water.HeightTexture);
        PushParams();
    }

    void PushParams()
    {
        var m = _mr.sharedMaterial;
        m.SetFloat(DispScaleID, displacementScale);
        m.SetFloat(DispClampID, displacementClamp);
        m.SetFloat(NormalScaleID, normalScale);
    }

    void BuildMesh(int n, Vector2 size)
    {
        var mesh = new Mesh { name = "WaterGrid", indexFormat = IndexFormat.UInt32 };
        int v = n + 1;
        var verts = new Vector3[v * v];
        var uvs   = new Vector2[v * v];

        for (int z = 0; z < v; z++)
        for (int x = 0; x < v; x++)
        {
            float fx = (float)x / n, fz = (float)z / n;
            verts[z * v + x] = new Vector3((fx - 0.5f) * size.x, 0f, (fz - 0.5f) * size.y);
            uvs  [z * v + x] = new Vector2(fx, fz);
        }

        var tris = new int[n * n * 6];
        int ti = 0;
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * v + x;
            tris[ti++] = i;     tris[ti++] = i + v; tris[ti++] = i + 1;
            tris[ti++] = i + 1; tris[ti++] = i + v; tris[ti++] = i + v + 1;
        }

        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        var b = mesh.bounds;
        b.Expand(new Vector3(0f, displacementClamp * 4f, 0f));
        mesh.bounds = b;

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
