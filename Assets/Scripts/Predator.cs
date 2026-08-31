using UnityEngine;

public class Predator : MonoBehaviour
{
    public Transform pointA;
    public Transform pointB;
    public float patrolSpeed = 1.2f;
    public float chaseSpeed = 2.4f;

    [Header("Detection")]
    public float movingDetectRadius = 4f;   // sees a moving chameleon from far away
    public float hiddenDetectRadius = 0.9f; // must be very close to notice a still, camouflaged one

    PlayerController player;
    Transform currentTarget;
    bool chasing;

    void Awake()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateCircleSprite(Color.white, 32);
        sr.color = new Color(0.85f, 0.15f, 0.15f);
        sr.sortingOrder = 4;

        var col = GetComponent<CircleCollider2D>();
        if (col == null) col = gameObject.AddComponent<CircleCollider2D>();
        col.radius = 0.4f;
        col.isTrigger = true;
    }

    void Start()
    {
        player = FindAnyObjectByType<PlayerController>();
        currentTarget = pointB;
    }

    void Update()
    {
        if (player == null) return;
        if (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing) return;

        float dist = Vector2.Distance(transform.position, player.transform.position);
        float detectRadius = player.IsHidden ? hiddenDetectRadius : movingDetectRadius;
        chasing = dist <= detectRadius;

        Vector3 dest;
        float speed;
        if (chasing)
        {
            dest = player.transform.position;
            speed = chaseSpeed;
        }
        else
        {
            dest = currentTarget.position;
            speed = patrolSpeed;
            if (Vector2.Distance(transform.position, currentTarget.position) < 0.1f)
            {
                currentTarget = currentTarget == pointA ? pointB : pointA;
            }
        }

        transform.position = Vector3.MoveTowards(transform.position, dest, speed * Time.deltaTime);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, movingDetectRadius);
        Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, hiddenDetectRadius);
    }
}
