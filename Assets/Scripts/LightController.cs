using System.Collections.Generic;
using UnityEngine;

// Fakes 2D lighting cheaply (no URP needed): a big semi-opaque black
// overlay follows the camera, and a soft additive "glow" sprite is placed
// wherever each active pointer (phone touch/gyro, or mouse-drag fallback)
// is aimed. Because the glow is drawn on top of the overlay with additive
// blending, it visually lifts the darkness there without any masking.
//
// Supports multiple simultaneous participants: each gets their own glow
// sprite, and the radius/brightness of every light shrinks in inverse
// proportion to how many people are currently shining a light, so the
// total illumination budget stays roughly constant as more people join.
public class LightController : MonoBehaviour
{
    [Header("Solo baseline (radius/brightness at 1 participant)")]
    public float baseLightRadius = 4.5f;
    public float baseIntensity = 1.6f;

    [Header("Floors so many participants don't go fully dark")]
    public float minLightRadius = 1.2f;
    public float minIntensity = 0.5f;

    public float followLerp = 12f;
    public float overlayAlpha = 0.88f;

    Camera cam;
    SpriteRenderer overlay;
    readonly List<SpriteRenderer> glowPool = new List<SpriteRenderer>();
    static readonly List<Vector2> mouseFallbackBuffer = new List<Vector2>(1);

    void Awake()
    {
        cam = Camera.main;

        var overlayGO = new GameObject("DarknessOverlay");
        overlay = overlayGO.AddComponent<SpriteRenderer>();
        overlay.sprite = SpriteFactory.CreateSquareSprite(Color.white, 8);
        overlay.color = new Color(0f, 0f, 0f, overlayAlpha);
        overlay.sortingOrder = 10;
    }

    SpriteRenderer CreateGlow()
    {
        var glowGO = new GameObject("LightGlow");
        var sr = glowGO.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateGlowSprite(128);
        sr.sortingOrder = 11;
        sr.material = SpriteFactory.CreateAdditiveMaterial();
        sr.color = new Color(1f, 0.92f, 0.7f, 0f);
        return sr;
    }

    void LateUpdate()
    {
        if (cam == null) return;

        // Keep the darkness overlay covering the full camera view.
        float h = cam.orthographicSize * 2f + 1f;
        float w = h * cam.aspect + 1f;
        overlay.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 1f);
        overlay.transform.localScale = new Vector3(w, h, 1f);

        List<Vector2> points = PointerServer.Instance != null
            ? PointerServer.Instance.GetActivePointers()
            : new List<Vector2>();

        // Mouse-drag fallback so lighting can be tested without a phone
        // (e.g. in the Editor Game view, or a WebGL build).
        if (points.Count == 0 && Input.GetMouseButton(0))
        {
            mouseFallbackBuffer.Clear();
            mouseFallbackBuffer.Add(new Vector2(
                Input.mousePosition.x / Screen.width,
                1f - (Input.mousePosition.y / Screen.height)));
            points = mouseFallbackBuffer;
        }

        int participants = Mathf.Max(1, points.Count);
        float radius = Mathf.Max(minLightRadius, baseLightRadius / participants);
        float intensity = Mathf.Max(minIntensity, baseIntensity / participants);

        while (glowPool.Count < points.Count) glowPool.Add(CreateGlow());

        for (int i = 0; i < glowPool.Count; i++)
        {
            SpriteRenderer g = glowPool[i];
            bool isActive = i < points.Count;

            float targetAlpha = isActive ? 1f : 0f;
            Color targetColor = new Color(intensity, intensity * 0.92f, intensity * 0.7f, targetAlpha);
            g.color = Color.Lerp(g.color, targetColor, followLerp * Time.deltaTime);
            g.transform.localScale = Vector3.Lerp(g.transform.localScale, Vector3.one * radius * 2f, followLerp * Time.deltaTime);

            if (isActive)
            {
                Vector2 p = points[i];
                Vector3 viewportPoint = new Vector3(p.x, 1f - p.y, Mathf.Abs(cam.transform.position.z));
                Vector3 targetWorld = cam.ViewportToWorldPoint(viewportPoint);
                targetWorld.z = 0f;
                g.transform.position = Vector3.Lerp(g.transform.position, targetWorld, followLerp * Time.deltaTime);
            }
        }
    }
}
