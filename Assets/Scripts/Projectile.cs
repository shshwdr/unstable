using System.Collections.Generic;
using UnityEngine;

public class Projectile : MonoBehaviour
{
    public static readonly List<Projectile> All = new List<Projectile>();
    public const float Speed = 12f;
    const float AoeHitRadius = 0.55f;
    const float AoeSpinSpeed = 720f;

    Health target;
    Transform source;
    Transform visual;
    Vector3 origin;
    Vector3 outboundDest;
    float damage;
    bool applySlow;
    bool aoe;
    bool returning;
    readonly HashSet<int> hitIds = new HashSet<int>();

    public static void Fire(Vector3 from, Health target, float damage, Color color)
    {
        Fire(from, target, damage, color, null, null);
    }

    public static void Fire(Vector3 from, Health target, float damage, Color color, BuildingInfo info, Transform source)
    {
        if (target == null || !target.IsAlive)
            return;

        bool aoe = info != null && info.HasSpecial("aoe");
        bool slow = info != null && info.HasSpecial("slow");

        var go = new GameObject("Projectile");
        go.transform.position = from;

        var visualGo = new GameObject("Visual");
        visualGo.transform.SetParent(go.transform);
        visualGo.transform.localPosition = Vector3.zero;
        var renderer = visualGo.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 12;

        Sprite itemSprite = ItemArt.Load(ItemArt.PrimaryItemId(info));
        float scaleMul = ProjectileScale();
        if (itemSprite != null)
        {
            renderer.sprite = itemSprite;
            float size = (aoe ? 0.5f : 0.38f) * scaleMul;
            float scale = ItemArt.FitScale(itemSprite, size);
            visualGo.transform.localScale = new Vector3(scale, scale, 1f);
        }
        else
        {
            visualGo.transform.localScale = aoe
                ? new Vector3(0.42f * scaleMul, 0.22f * scaleMul, 1f)
                : new Vector3(0.18f * scaleMul, 0.18f * scaleMul, 1f);
            renderer.sprite = ShapeUtil.Square(color);
        }

        var projectile = go.AddComponent<Projectile>();
        projectile.target = target;
        projectile.source = source;
        projectile.visual = visualGo.transform;
        projectile.origin = from;
        projectile.damage = damage;
        projectile.applySlow = slow;
        projectile.aoe = aoe;
        if (aoe)
        {
            Vector3 dir = target.transform.position - from;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.right;
            dir.Normalize();
            float range = info != null && info.attackRange > 0.1f ? info.attackRange : 5f;
            projectile.outboundDest = from + dir * range;
        }

        if (aoe)
        {
            FMODUnity.RuntimeManager.PlayOneShot("event:/SFX/sfx_croissant_shot", from);
        }
        else if (slow)
        {
            FMODUnity.RuntimeManager.PlayOneShot("event:/SFX/sfx_slow_chesse_shot", from);
        }
        else if (info != null)
        {
            FMODUnity.RuntimeManager.PlayOneShot("event:/SFX/sfx_bread_shot", from);
        }
        else
        {
            FMODUnity.RuntimeManager.PlayOneShot("event:/SFX/sfx_basic_shoot", from);
        }
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

    static float ProjectileScale()
    {
        var world = Object.FindObjectOfType<BalanceWorld>();
        if (world == null || world.settings == null || world.settings.projectileScale <= 0f)
            return 1.5f;
        return world.settings.projectileScale;
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
        if (aoe)
            TickBoomerang();
        else
            TickHoming();
    }

    void TickHoming()
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
            ApplyHit(target);
            Destroy(gameObject);
        }
    }

    void TickBoomerang()
    {
        if (visual != null)
            visual.Rotate(0f, 0f, AoeSpinSpeed * Time.deltaTime);

        HitEnemiesAlongPath();

        Vector3 dest = returning ? ReturnPoint() : outboundDest;
        transform.position = Vector3.MoveTowards(transform.position, dest, Speed * Time.deltaTime);
        if ((transform.position - dest).sqrMagnitude > 0.01f)
            return;

        if (!returning)
        {
            returning = true;
            hitIds.Clear();
            return;
        }

        Destroy(gameObject);
    }

    Vector3 ReturnPoint()
    {
        return source != null ? source.position : origin;
    }

    void HitEnemiesAlongPath()
    {
        for (int i = 0; i < Enemy.All.Count; i++)
        {
            Enemy enemy = Enemy.All[i];
            if (enemy == null || enemy.Health == null || !enemy.Health.IsAlive)
                continue;

            int id = enemy.GetInstanceID();
            if (hitIds.Contains(id))
                continue;
            if (CombatUtil.Distance(this, enemy) > AoeHitRadius)
                continue;

            hitIds.Add(id);
            ApplyHit(enemy.Health);
        }
    }

    void ApplyHit(Health health)
    {
        if (health == null || !health.IsAlive)
            return;

        health.TakeDamage(damage);
        if (!applySlow)
            return;

        var enemy = health.GetComponent<Enemy>();
        if (enemy != null)
            enemy.ApplySlow();
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
