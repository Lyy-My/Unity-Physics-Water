using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GPU浅水模拟驱动程序。  
/// 将此文件放在资源池根目录下。WaterSurfaceURP（可视化）和FloatingObjectURP会从中读取数据。
/// </summary>
[DisallowMultipleComponent]
public class WaterSimulationURP : MonoBehaviour
{
    [Header("Compute")]
    public ComputeShader compute;          // 分配 WaterSimCompute
    public int resolution = 512;           // 网格（PC可支持512）

    [Header("Size (world meters)")]
    public Vector2 size = new Vector2(6f, 6f);
    public bool    circularPool = false;          // OFF = 整个矩形的波纹（修复“仅中心”问题）

    [Header("Simulation")]
    [Range(0.05f, 0.49f)] public float propagation = 0.45f; // 波速（限制稳定）
    [Range(0f, 0.05f)]    public float friction    = 0.004f;
    [Range(0.99f, 1f)]    public float damping      = 0.999f;
    [Range(1, 8)]         public int   substeps     = 4;

    [Header("Brush")]
    public bool   mouseInteraction = true;
    public float  brushRadius   = 0.025f;
    public float  brushStrength = 0.25f;
    public Camera interactionCamera;

    [Header("Ambient (idle liveliness)")]
    public float ambientStrength = 0.05f;
    public float ambientRate     = 18f;

    [Header("Debug")]
    public bool debugShowHeightTex = false;

    RenderTexture _a, _b;
    int _kSim, _kSplat;
    float _ambientAcc;

    struct Splat { public Vector2 uv; public float radius; public float strength; }
    readonly List<Splat> _queue = new List<Splat>();

    public RenderTexture HeightTexture => _a;
    public Vector2 Size => size;

    void OnEnable()
    {
        _kSim   = compute.FindKernel("Simulate");
        _kSplat = compute.FindKernel("Splat");
        _a = NewRT(); _b = NewRT();
        if (interactionCamera == null) interactionCamera = Camera.main;
    }

    void OnDisable()
    {
        if (_a) _a.Release();
        if (_b) _b.Release();
    }

    RenderTexture NewRT()
    {
        var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGFloat)
        {
            enableRandomWrite = true,
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        rt.Create();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
        return rt;
    }

    void Swap() {
        (_b, _a) = (_a, _b);
    }

    void Update()
    {
        if (mouseInteraction) HandleMouse();
        QueueAmbient();
    }

    void LateUpdate()
    {
        int groups = Mathf.CeilToInt(resolution / 8f);
        float aspect = Mathf.Approximately(size.y, 0f) ? 1f : size.x / size.y;

        // 作用于 _a 上
        compute.SetInt("_Res", resolution);
        compute.SetFloat("_BrushAspect", aspect);
        foreach (var s in _queue)
        {
            compute.SetVector("_BrushUV", s.uv);
            compute.SetFloat("_BrushRadius", s.radius);
            compute.SetFloat("_BrushStrength", s.strength);
            compute.SetTexture(_kSplat, "StateIn", _a);
            compute.Dispatch(_kSplat, groups, groups, 1);
        }
        _queue.Clear();

        compute.SetFloat("_Propagation", propagation);
        compute.SetFloat("_Friction", friction);
        compute.SetFloat("_Damping", damping);
        compute.SetFloat("_UseMask", circularPool ? 1f : 0f);
        compute.SetFloat("_MaskRadius", 0.5f);
        for (int i = 0; i < substeps; i++)
        {
            compute.SetTexture(_kSim, "StateIn", _a);
            compute.SetTexture(_kSim, "StateOut", _b);
            compute.Dispatch(_kSim, groups, groups, 1);
            Swap();
        }
    }

    public void AddRipple(Vector2 uv, float radius, float strength)
        => _queue.Add(new Splat { uv = uv, radius = radius, strength = strength });

    /// <summary>世界坐标系 -> sim uv。以该变换为中心的平面，足迹 = 尺寸（X，Z）。</summary>
    public bool WorldToUV(Vector3 world, out Vector2 uv)
    {
        Vector3 local = world - transform.position;
        uv = new Vector2(local.x / size.x + 0.5f, local.z / size.y + 0.5f);
        return uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;
    }

    void QueueAmbient()
    {
        if (ambientStrength <= 0f || ambientRate <= 0f) return;
        _ambientAcc += ambientRate * Time.deltaTime;
        while (_ambientAcc >= 1f)
        {
            _ambientAcc -= 1f;
            Vector2 uv = new(Random.value, Random.value);
            if (circularPool && Vector2.Distance(uv, new Vector2(0.5f, 0.5f)) > 0.5f) continue;
            AddRipple(uv, brushRadius * Random.Range(0.3f, 0.8f),
                      ambientStrength * Random.Range(-1f, 1f));
        }
    }

    void HandleMouse()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.isPressed || interactionCamera == null) return;

        var plane = new Plane(Vector3.up, transform.position);
        Ray ray = interactionCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (plane.Raycast(ray, out float d))
        {
            Vector3 hit = ray.GetPoint(d);
            if (WorldToUV(hit, out Vector2 uv))
                AddRipple(uv, brushRadius, brushStrength * Time.deltaTime * 60f);
        }
    }

    void OnGUI()
    {
        if (debugShowHeightTex && HeightTexture != null)
            GUI.DrawTexture(new Rect(10, 10, 256, 256), HeightTexture, ScaleMode.ScaleToFit, false);
    }
}
