using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    public static readonly List<Projectile> All = new List<Projectile>();
    public const float Speed = 12f;

    Health target;
    float damage;

    public static void Fire(Vector3 from, Health target, float damage, Color color)
    {
        if (target == null || !target.IsAlive)
            return;

        var go = new GameObject("Projectile");
        go.transform.position = from;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = new Vector3(0.18f, 0.18f, 1f);
        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = ShapeUtil.Square(color);
        renderer.sortingOrder = 12;

        var projectile = go.AddComponent<Projectile>();
        projectile.target = target;
        projectile.damage = damage;
    }

    public static void ClearAll()
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            if (All[i] != null)
                Destroy(All[i].gameObject);
        }

        All.Clear();
    }

    void OnEnable()
    {
        All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    void Update()
    {
        if (target == null || !target.IsAlive)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 dest = target.transform.position;
        transform.position = Vector3.MoveTowards(transform.position, dest, Speed * Time.deltaTime);
        if ((transform.position - dest).sqrMagnitude <= 0.01f)
        {
            target.TakeDamage(damage);
            Destroy(gameObject);
        }
    }
}

public static class CombatUtil
{
    public static float Distance(Component a, Component b)
    {
        if (a == null || b == null)
            return float.MaxValue;

        Vector2 from = a.transform.position;
        Vector2 to = b.transform.position;
        var fromCol = a.GetComponent<Collider2D>();
        var toCol = b.GetComponent<Collider2D>();
        if (fromCol != null)
            from = fromCol.ClosestPoint(to);
        if (toCol != null)
            to = toCol.ClosestPoint(from);
        return Vector2.Distance(from, to);
    }
}
