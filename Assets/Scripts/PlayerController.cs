using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 3.5f;
    public float stillTimeToHide = 0.6f;
    public float colorLerpSpeed = 4f;
    public Color visibleColor = new Color(0.2f, 0.85f, 0.6f);

    Rigidbody2D rb;
    SpriteRenderer sr;
    Vector2 input;
    float stillTimer;
    Color targetColor;

    public bool IsMoving { get; private set; }
    public bool IsHidden { get; private set; } // fully camouflaged and still long enough

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        sr.sprite = SpriteFactory.CreateCircleSprite(Color.white, 32);
        sr.sortingOrder = 5;
        targetColor = visibleColor;
        sr.color = visibleColor;

        var col = GetComponent<CircleCollider2D>();
        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
        col.radius = 0.45f;
        col.isTrigger = false;
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing)
        {
            input = Vector2.zero;
        }
        else
        {
            input.x = Input.GetAxisRaw("Horizontal");
            input.y = Input.GetAxisRaw("Vertical");
            if (input.sqrMagnitude > 1f) input.Normalize();
        }

        IsMoving = input.sqrMagnitude > 0.01f;

        if (IsMoving)
        {
            stillTimer = 0f;
            IsHidden = false;
            targetColor = visibleColor;
        }
        else
        {
            stillTimer += Time.deltaTime;
            if (stillTimer >= stillTimeToHide)
            {
                CamouflageZone zone = FindZoneAtPosition();
                if (zone != null)
                {
                    targetColor = zone.zoneColor;
                    IsHidden = true;
                }
            }
        }

        sr.color = Color.Lerp(sr.color, targetColor, colorLerpSpeed * Time.deltaTime);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = input * moveSpeed;
    }

    CamouflageZone FindZoneAtPosition()
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(transform.position);
        foreach (var h in hits)
        {
            var zone = h.GetComponent<CamouflageZone>();
            if (zone != null) return zone;
        }
        return null;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<Predator>() != null)
        {
            GameManager.Instance?.Fail();
        }
        else if (other.GetComponent<GoalZone>() != null)
        {
            GameManager.Instance?.Success();
        }
    }
}
