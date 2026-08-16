using System.Collections.Generic;
using UnityEngine;

public class Enemy : MonoBehaviour
{
    public static readonly List<Enemy> All = new List<Enemy>();

    public EnemyInfo Info;
    public Health Health { get; private set; }

    Transform visual;
    float attackTimer;
    BalanceWorld world;

    public static Enemy Spawn(EnemyInfo info, Vector3 position)
    {
        var go = new GameObject(info.identifier);
        go.transform.position = position;
        var enemy = go.AddComponent<Enemy>();
        enemy.Setup(info);
        return enemy;
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

    public void Setup(EnemyInfo info)
    {
        Info = info;
        world = FindObjectOfType<BalanceWorld>();

        visual = new GameObject("Visual").transform;
        visual.SetParent(transform);
        visual.localPosition = Vector3.zero;
        visual.localScale = new Vector3(0.45f, 0.45f, 1f);
        var renderer = visual.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = ShapeUtil.Triangle(ColorFor(info.identifier));
        renderer.sortingOrder = 10;

        Health = gameObject.AddComponent<Health>();
        Health.Init(info.hp, new Vector3(0f, 0.45f, 0f));
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
        if (world != null && world.IsGameOver)
            return;
        if (Info == null || Health == null || !Health.IsAlive)
            return;

        Building target = Utils.findClosestItem(transform.position, Building.All);
        if (target == null || target.Health == null || !target.Health.IsAlive)
            return;

        Vector3 dest = target.transform.position;
        Vector3 delta = dest - transform.position;
        float dist = CombatUtil.Distance(this, target);

        if (delta.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;
            visual.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        if (dist > Info.attackRange)
        {
            transform.position += delta.normalized * Info.speed * Time.deltaTime;
            return;
        }

        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f)
        {
            attackTimer = Info.attackCD;
            Projectile.Fire(transform.position, target.Health, Info.attack, new Color(1f, 0.35f, 0.2f));
        }
    }

    static Color ColorFor(string identifier)
    {
        switch (identifier)
        {
            case "normal": return new Color(0.78f, 0.18f, 0.2f);
            case "speed": return new Color(0.95f, 0.55f, 0.12f);
            default: return new Color(0.65f, 0.2f, 0.45f);
        }
    }
}
