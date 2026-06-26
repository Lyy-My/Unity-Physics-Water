using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Character swimming controller (surface + underwater) for the URP water.
/// Activates only while the body is in the water; out of water it does nothing,
/// so it coexists with a normal land controller.
///
/// Controls (new Input System):
///   WASD  - swim horizontally (relative to cameraTransform if set)
///   Space - swim up / surface
///   LeftCtrl - dive down
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SwimmingController : MonoBehaviour
{
    public WaterSimulationURP water;
    public Transform cameraTransform;     // movement direction reference (optional)

    [Header("Body")]
    public float bodyHalfHeight = 0.9f;   // half the capsule height
    public float floatLine = 0.35f;       // how deep the body sits when idle-floating

    [Header("Swim forces")]
    public float swimForce = 16f;
    public float verticalSwimForce = 14f;
    public float buoyancy = 30f;
    public float waterDrag = 3.5f;
    public float maxSwimSpeed = 4f;

    [Header("Ripples")]
    public bool  makeRipples = true;
    public float rippleStrength = 0.12f;
    public float rippleRadius = 0.04f;

    Rigidbody _rb;
    bool _pending;
    float _waterY;
    public bool InWater { get; private set; }
    public bool Submerged { get; private set; }

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        if (water == null) water = FindAnyObjectByType<WaterSimulationURP>();
        if (cameraTransform == null && Camera.main) cameraTransform = Camera.main.transform;
    }

    void FixedUpdate()
    {
        if (water == null || water.HeightTexture == null) return;

        bool inBounds = water.WorldToUV(transform.position, out Vector2 uv);
        if (inBounds) SampleWaterY(uv);

        float pos = transform.position.y;
        float bottom = pos - bodyHalfHeight;
        float head   = pos + bodyHalfHeight;
        InWater   = inBounds && _waterY > bottom;
        Submerged = inBounds && _waterY > head;

        if (!InWater) return;   // let gravity / land controller handle out-of-water

        var kb = Keyboard.current;
        float ix = 0, iz = 0, iy = 0;
        if (kb != null)
        {
            if (kb.aKey.isPressed) ix -= 1;
            if (kb.dKey.isPressed) ix += 1;
            if (kb.sKey.isPressed) iz -= 1;
            if (kb.wKey.isPressed) iz += 1;
            if (kb.spaceKey.isPressed)    iy += 1;
            if (kb.leftCtrlKey.isPressed) iy -= 1;
        }

        // horizontal direction relative to the camera
        Vector3 fwd   = cameraTransform ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 right = cameraTransform ? Vector3.ProjectOnPlane(cameraTransform.right,   Vector3.up).normalized : Vector3.right;
        Vector3 move  = (fwd * iz + right * ix);
        if (move.sqrMagnitude > 1f) move.Normalize();

        // cancel gravity in water; buoyancy + drag + input dominate
        _rb.AddForce(-Physics.gravity, ForceMode.Acceleration);

        // water drag
        _rb.AddForce(-_rb.linearVelocity * waterDrag, ForceMode.Acceleration);

        // swim input
        _rb.AddForce(move * swimForce, ForceMode.Acceleration);
        if (iy != 0) _rb.AddForce(Vector3.up * iy * verticalSwimForce, ForceMode.Acceleration);

        // buoyancy toward the float line (suppressed while actively diving)
        float targetY = _waterY - floatLine;
        float b = (targetY - pos) * buoyancy;
        if (iy < 0) b = Mathf.Min(b, 0f);          // allow diving below the surface
        _rb.AddForce(Vector3.up * b, ForceMode.Acceleration);

        // clamp horizontal swim speed
        Vector3 hVel = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z);
        if (hVel.magnitude > maxSwimSpeed)
        {
            hVel = hVel.normalized * maxSwimSpeed;
            _rb.linearVelocity = new Vector3(hVel.x, _rb.linearVelocity.y, hVel.z);
        }

        // surface ripples while swimming near the top
        if (makeRipples && !Submerged && inBounds)
        {
            float speed = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.z).magnitude;
            if (speed > 0.2f)
                water.AddRipple(uv, rippleRadius, -rippleStrength * Mathf.Clamp01(speed / maxSwimSpeed));
        }
    }

    void SampleWaterY(Vector2 uv)
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
            if (data.Length > 0)
                _waterY = water.SurfaceY + data[0].x * 0.10f;   // 0.10 = displacementScale
        });
    }
}
