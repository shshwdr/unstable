using System.Collections.Generic;
using UnityEngine;

public class Enemy : MonoBehaviour
{
    public static readonly List<Enemy> All = new List<Enemy>();
    static int nextSpawnOrder;

    const float AttackDropExtra = 1f;

    public EnemyInfo Info;
    public Health Health { get; private set; }

    Transform visual;
    Rigidbody2D body;
    float attackTimer;
    BalanceWorld world;
    int spawnOrder;
    Building attackTarget;

    public bool IsMelee
    {
        get { return Info != null && Info.isMelee; }
    }

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
        nextSpawnOrder = 0;
    }

    public void Setup(EnemyInfo info)
    {
        Info = info;
        world = FindObjectOfType<BalanceWorld>();

        visual = new GameObject("Visual").transform;
        visual.SetParent(transform);
        visual.localPosition = Vector3.zero;

        var renderer = visual.gameObject.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = 10;

        if (info.isMelee)
        {
            Vector2 size = info.Size;
            visual.localScale = new Vector3(size.x, size.y, 1f);
            renderer.sprite = ShapeUtil.Square(ColorFor(info.identifier));
            SetupMeleePhysics(size);
            IgnoreResourceColliders();
            IgnoreOtherMeleeColliders();
            Health = gameObject.AddComponent<Health>();
            Health.Init(info.hp, new Vector3(0f, size.y * 0.5f + 0.28f, 0f));
            return;
        }

        visual.localScale = new Vector3(0.45f, 0.45f, 1f);
        renderer.sprite = ShapeUtil.Triangle(ColorFor(info.identifier));
        Health = gameObject.AddComponent<Health>();
        Health.Init(info.hp, new Vector3(0f, 0.45f, 0f));
    }

    void SetupMeleePhysics(Vector2 size)
    {
        body = gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.mass = Mathf.Max(0.05f, world.settings.buildingDensity * size.x * size.y);
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;

        var box = gameObject.AddComponent<BoxCollider2D>();
        box.size = size;
        box.sharedMaterial = world.SharedMaterial;
    }

    void IgnoreResourceColliders()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
            return;

        for (int i = 0; i < Building.All.Count; i++)
        {
            Building resource = Building.All[i];
            if (resource == null || !resource.IsResource)
                continue;

            var other = resource.GetComponent<Collider2D>();
            if (other != null)
                Physics2D.IgnoreCollision(col, other);
        }
    }

    void IgnoreOtherMeleeColliders()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
            return;

        for (int i = 0; i < All.Count; i++)
        {
            Enemy other = All[i];
            if (other == null || other == this || !other.IsMelee)
                continue;

            var otherCol = other.GetComponent<Collider2D>();
            if (otherCol != null)
                Physics2D.IgnoreCollision(col, otherCol);
        }
    }

    void OnEnable()
    {
        spawnOrder = nextSpawnOrder++;
        All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
        if (world != null && !world.IsRestarting && !world.HasEnded && TutorialManager.Instance != null)
            TutorialManager.Instance.NotifyEnemyKilled();
    }

    void Update()
    {
        if (ShouldIdle())
            return;

        if (Info.isMelee)
            TickMeleeCombat();
        else
            TickRanged();

        if (Info.isMelee)
            CheckFellOff();
    }

    void FixedUpdate()
    {
        if (ShouldIdle() || !Info.isMelee || body == null)
            return;

        AlignToBoard();
        RefreshAttackTarget();

        if (attackTarget != null)
        {
            if (IsGrounded())
                SeparateAlongBoard();
            else
                StopAlongBoard();
            return;
        }

        if (!IsGrounded())
            return;

        Building dest = Building.FindCore();
        if (dest == null || !dest.CanBeAttacked)
            dest = Building.FindClosestAttackable(transform.position);
        if (dest == null)
        {
            StopAlongBoard();
            return;
        }

        WalkAlongBoard(dest);
    }

    bool ShouldIdle()
    {
        return (world != null && world.IsGameOver)
               || Info == null
               || Health == null
               || !Health.IsAlive;
    }

    void TickRanged()
    {
        RefreshAttackTarget();
        if (attackTarget != null)
        {
            Face(attackTarget.transform.position - transform.position);
            TryShoot(attackTarget);
            SeparateRanged();
            return;
        }

        Building target = Building.FindClosestAttackable(transform.position);
        if (target == null)
            return;

        Vector3 delta = target.transform.position - transform.position;
        Face(delta);
        transform.position += delta.normalized * Info.speed * Time.deltaTime;
    }

    void TickMeleeCombat()
    {
        RefreshAttackTarget();
        if (attackTarget == null)
            return;

        TryShoot(attackTarget);
    }

    void RefreshAttackTarget()
    {
        if (Info == null)
        {
            attackTarget = null;
            return;
        }

        if (attackTarget != null)
        {
            if (!attackTarget.CanBeAttacked || CombatUtil.Distance(this, attackTarget) > Info.attackRange + AttackDropExtra)
                attackTarget = null;
        }

        if (attackTarget == null)
            attackTarget = Building.FindAttackableInRange(this, Info.attackRange);
    }

    void TryShoot(Building target)
    {
        attackTimer -= Time.deltaTime;
        if (attackTimer > 0f)
            return;

        attackTimer = Info.attackCD;
        Projectile.Fire(transform.position, target.Health, Info.attack, new Color(1f, 0.35f, 0.2f));
    }

    void Face(Vector3 delta)
    {
        if (visual == null || delta.sqrMagnitude <= 0.0001f)
            return;
        if (Info.isMelee)
            return;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;
        visual.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    void AlignToBoard()
    {
        if (world == null || world.BoardBody == null)
            return;
        body.rotation = world.BoardBody.rotation;
    }

    void WalkAlongBoard(Building target)
    {
        Vector2 along = world.BoardBody.transform.right;
        Vector2 toTarget = (Vector2)(target.transform.position - transform.position);
        float alongDelta = Vector2.Dot(toTarget, along);
        if (Mathf.Abs(alongDelta) < 0.04f)
        {
            StopAlongBoard();
            return;
        }

        WalkAlongSign(Mathf.Sign(alongDelta));
    }

    void WalkAlongSign(float sign)
    {
        Vector2 along = world.BoardBody.transform.right;
        Vector2 up = world.BoardBody.transform.up;
        float upSpeed = Vector2.Dot(body.velocity, up);
        body.velocity = along * (sign * Info.speed) + up * upSpeed;
        body.WakeUp();
    }

    void SeparateRanged()
    {
        Vector2 push = SeparationFromEarlier();
        if (push.sqrMagnitude < 0.0001f)
            return;
        transform.position += (Vector3)(push.normalized * Info.speed * Time.deltaTime);
    }

    void SeparateAlongBoard()
    {
        Vector2 push = SeparationFromEarlier();
        if (push.sqrMagnitude < 0.0001f || world == null || world.BoardBody == null)
        {
            StopAlongBoard();
            return;
        }

        float alongDelta = Vector2.Dot(push, world.BoardBody.transform.right);
        if (Mathf.Abs(alongDelta) < 0.01f)
        {
            StopAlongBoard();
            return;
        }

        WalkAlongSign(Mathf.Sign(alongDelta));
    }

    Vector2 SeparationFromEarlier()
    {
        float spacing = 0.3f;
        if (world != null && world.settings != null)
            spacing = world.settings.enemyAttackSpacing;

        Vector2 push = Vector2.zero;
        for (int i = 0; i < All.Count; i++)
        {
            Enemy other = All[i];
            if (other == null || other == this || other.spawnOrder >= spawnOrder)
                continue;
            if (other.Health == null || !other.Health.IsAlive)
                continue;

            float gap = CombatUtil.Distance(this, other);
            if (gap >= spacing)
                continue;

            Vector2 away = (Vector2)(transform.position - other.transform.position);
            if (away.sqrMagnitude < 0.0001f)
            {
                if (world != null && world.BoardBody != null)
                    away = world.BoardBody.transform.right;
                else
                    away = Vector2.right;
            }
            else
                away.Normalize();

            push += away * (spacing - gap);
        }

        return push;
    }

    void StopAlongBoard()
    {
        if (body == null || world == null || world.BoardBody == null)
            return;

        Vector2 up = world.BoardBody.transform.up;
        float upSpeed = Vector2.Dot(body.velocity, up);
        body.velocity = up * upSpeed;
    }

    bool IsGrounded()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
            return false;

        if (NearSurface(col, world != null ? world.BoardCollider : null))
            return true;

        for (int i = 0; i < Building.All.Count; i++)
        {
            Building building = Building.All[i];
            if (building == null || building.IsResource)
                continue;
            if (NearSurface(col, building.GetComponent<Collider2D>()))
                return true;
        }

        return false;
    }

    static bool NearSurface(Collider2D self, Collider2D other)
    {
        if (self == null || other == null)
            return false;

        ColliderDistance2D dist = Physics2D.Distance(self, other);
        return dist.isOverlapped || dist.distance < 0.08f;
    }

    void CheckFellOff()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        if (vp.y < -0.2f || vp.x < -0.35f || vp.x > 1.35f)
            Destroy(gameObject);
    }

    static Color ColorFor(string identifier)
    {
        switch (identifier)
        {
            case "normal": return new Color(0.78f, 0.18f, 0.2f);
            case "speed": return new Color(0.95f, 0.55f, 0.12f);
            case "melee": return new Color(0.42f, 0.08f, 0.12f);
            default: return new Color(0.65f, 0.2f, 0.45f);
        }
    }
}
