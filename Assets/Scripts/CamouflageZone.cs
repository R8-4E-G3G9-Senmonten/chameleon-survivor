using UnityEngine;

// Marker placed on background patches (leaf/trunk/ground colored areas).
// The player samples this color to blend into when standing still on it.
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class CamouflageZone : MonoBehaviour
{
    public Color zoneColor = Color.green;

    void Awake()
    {
        var col = GetComponent<BoxCollider2D>();
        col.isTrigger = true;

        var sr = GetComponent<SpriteRenderer>();
        sr.sprite = SpriteFactory.CreateSquareSprite(Color.white, 8);
        sr.color = zoneColor;
        sr.sortingOrder = -1;
    }
}
