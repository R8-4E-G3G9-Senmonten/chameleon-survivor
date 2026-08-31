using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class GoalZone : MonoBehaviour
{
    void Awake()
    {
        var col = GetComponent<BoxCollider2D>();
        col.isTrigger = true;

        var sr = GetComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateSquareSprite(Color.white, 8);
        sr.color = new Color(1f, 0.9f, 0.3f, 0.9f);
        sr.sortingOrder = -1;
    }
}
