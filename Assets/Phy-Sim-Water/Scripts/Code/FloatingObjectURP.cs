using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Buoyant floating object that BOBS and DRIFTS with the waves.
/// Reads back a small block of the height field to get both the local height and the
/// wave slope (gradient), then applies vertical buoyancy + horizontal drift + surface align.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FloatingObjectURP : MonoBehaviour
{
    public WaterSimulationURP water;

    [Header("Buoyancy (vertical)")]
    public float displacementScale = 0.10f;   // match the water shader/surface value
    public float buoyancy = 14f;
    public float verticalDamping = 2.5f;

    [Header("Drift (horizontal, from wave slope)")]
    [Tooltip("How strongly the object slides along the water surface slope.")]
    public float driftStrength = 6f;
    public float horizontalDamping = 1.2f;

    [Header("Orientation")]
    public bool  alignToSurface = true;
    public float alignSpeed = 4f;

    [Header("Footprint (object disturbs the water)")]
    public bool  writeFootprint = true;
    public float footprintRadius = 0.03f;
    public float footprintStrength = 0.10f;

    const int K = 5;                 // readback block size (odd)
    Rigidbody _rb;
    bool _pending;
    float _height;                   // world-space surface height at object (meters)
    Vector3 _surfaceNormal = Vector3.up;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        if (water == null) water = FindAnyObjectByType<WaterSimulationURP>();
    }

    void FixedUpdate()
    {
        if (water == null || water.HeightTexture == null) return;
        if (!water.WorldToUV(transform.position, out Vector2 uv)) return;

        RequestBlock(uv);

        // vertical buoyancy toward the surface
        float targetY = water.SurfaceY + _height;
        float diff = targetY - transform.position.y;
        float vForce = diff * buoyancy - _rb.linearVelocity.y * verticalDamping;
        _rb.AddForce(Vector3.up * vForce, ForceMode.Acceleration);

        // horizontal drift: pushed along the downhill direction of the surface
        Vector3 slope = new Vector3(_surfaceNormal.x, 0f, _surfaceNormal.z);
        Vector3 hVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        Vector3 dForce = slope * driftStrength - hVel * horizontalDamping;
        if (diff > -0.25f) _rb.AddForce(dForce, ForceMode.Acceleration);   // only when in water

        if (alignToSurface)
        {
            Quaternion target = Quaternion.FromToRotation(transform.up, _surfaceNormal) * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, target, alignSpeed * Time.fixedDeltaTime);
        }

        if (writeFootprint)
        {
            float s = -footprintStrength * Mathf.Clamp(_rb.linearVelocity.y, -3f, 3f);
            if (Mathf.Abs(s) > 5e-4f) water.AddRipple(uv, footprintRadius, s);
        }
    }

    void RequestBlock(Vector2 uv)
    {
        if (_pending) return;
        var rt = water.HeightTexture;
        int half = K / 2;
        int px = Mathf.Clamp(Mathf.RoundToInt(uv.x * rt.width),  0, rt.width  - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(uv.y * rt.height), 0, rt.height - 1);
        int sx = Mathf.Clamp(px - half, 0, rt.width  - K);
        int sy = Mathf.Clamp(py - half, 0, rt.height - K);
        int cx = px - sx, cy = py - sy;

        Vector2 size = water.Size;
        float worldPerTexelX = size.x / rt.width;
        float worldPerTexelZ = size.y / rt.height;
        float dispScale = displacementScale;

        _pending = true;
        AsyncGPUReadback.Request(rt, 0, sx, K, sy, K, 0, 1, TextureFormat.RGFloat, req =>
        {
            _pending = false;
            if (req.hasError) return;
            var data = req.GetData<Vector2>();
            if (data.Length < K * K) return;

            int xL = Mathf.Max(cx - 1, 0), xR = Mathf.Min(cx + 1, K - 1);
            int yD = Mathf.Max(cy - 1, 0), yU = Mathf.Min(cy + 1, K - 1);

            float hC = data[cy * K + cx].x;
            float hl = data[cy * K + xL].x, hr = data[cy * K + xR].x;
            float hd = data[yD * K + cx].x, hu = data[yU * K + cx].x;

            _height = hC * dispScale;

            float gx = (hr - hl) * dispScale / (Mathf.Max(xR - xL, 1) * worldPerTexelX);
            float gz = (hu - hd) * dispScale / (Mathf.Max(yU - yD, 1) * worldPerTexelZ);
            _surfaceNormal = Vector3.Normalize(new Vector3(-gx, 1f, -gz));
        });
    }
}
