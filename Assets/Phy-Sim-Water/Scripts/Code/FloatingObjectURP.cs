using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 用于URP水体的漂浮物体  
/// 使用AsyncGPUReadback 取物体下方的水位，应用弹簧阻尼浮力，并将足迹波纹写回。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FloatingObjectURP : MonoBehaviour
{
    public WaterSimulationURP water;

    [Header("Buoyancy")]
    public float displacementScale = 0.12f;  // 匹配表面/着色器值
    public float waterBaseY = float.NaN;      // 静水 Y；auto = water.transform.y
    public float buoyancy   = 14f;
    public float damping    = 2.5f;

    [Header("Footprint")]
    public bool  writeFootprint    = true;
    public float footprintRadius   = 0.03f;
    public float footprintStrength = 0.10f;

    Rigidbody _rb;
    float _height;            // 最新采样水位（米）
    bool  _pending;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        if (water == null) water = FindAnyObjectByType<WaterSimulationURP>();
        if (float.IsNaN(waterBaseY) && water != null) waterBaseY = water.transform.position.y;
    }

    void FixedUpdate()
    {
        if (water == null || water.HeightTexture == null) return;
        if (!water.WorldToUV(transform.position, out Vector2 uv)) return;

        RequestHeight(uv);

        float targetY = waterBaseY + _height;
        float diff    = targetY - transform.position.y;
        float force   = diff * buoyancy - _rb.linearVelocity.y * damping;
        _rb.AddForce(Vector3.up * force, ForceMode.Acceleration);

        if (writeFootprint)
        {
            float s = -footprintStrength * Mathf.Clamp(_rb.linearVelocity.y, -3f, 3f);
            if (Mathf.Abs(s) > 5e-4f) water.AddRipple(uv, footprintRadius, s);
        }
    }

    void RequestHeight(Vector2 uv)
    {
        if (_pending) return;
        var rt = water.HeightTexture;
        int px = Mathf.Clamp(Mathf.RoundToInt(uv.x * rt.width),  0, rt.width  - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(uv.y * rt.height), 0, rt.height - 1);

        _pending = true;
        AsyncGPUReadback.Request(rt, 0, px, 1, py, 1, 0, 1, TextureFormat.RGFloat, req =>
        {
            _pending = false;
            if (req.hasError) return;
            var data = req.GetData<Vector2>();
            if (data.Length > 0) _height = data[0].x * displacementScale;
        });
    }
}
