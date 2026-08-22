using System;
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
    SpriteRenderer visualRenderer;
    Sprite[] animFrames;
    int animFrame;
    float animTimer;
    Rigidbody2D body;
    float attackTimer;
    BalanceWorld world;
    int spawnOrder;
    Building attackTarget;
    bool slowed;

    const float SlowMul = 0.5f;
    static readonly Color SlowColor = new Color(0.25f, 0.55f, 1f);
    static readonly Color EnemyHpColor = new Color(0.9f, 0.22f, 0.22f, 1f);

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

        FMODUnity.RuntimeManager.PlayOneShot("event:/SFX/sfx_enemies_unlocked", position);

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

        visualRenderer = visual.gameObject.AddComponent<SpriteRenderer>();
        visualRenderer.sortingOrder = 10;
        float scaleMul = EnemyScale();
        ApplyVisual(info, scaleMul);

        if (info.isMelee)
        {
            Vector2 size = info.Size * scaleMul;
            SetupMeleePhysics(size);
            IgnoreOtherMeleeColliders();
            Health = gameObject.AddComponent<Health>();
            Health.Init(info.hp, new Vector3(0f, size.y * 0.5f + 0.28f, 0f), EnemyHpColor);
            return;
        }

        Health = gameObject.AddComponent<Health>();
        Health.Init(info.hp, new Vector3(0f, 0.45f * scaleMul, 0f), EnemyHpColor);
    }

    float EnemyScale()
    {
        if (world != null && world.settings != null && world.settings.enemyScale > 0f)
            return world.settings.enemyScale;
        return 2f;
    }

    void ApplyVisual(EnemyInfo info, float scaleMul)
    {
        animFrames = EnemyArt.LoadFrames(info.identifier);
        Sprite sprite = animFrames != null && animFrames.Length > 0 ? animFrames[0] : null;
        if (sprite != null)
        {
            visualRenderer.sprite = sprite;
            float worldSize = (info.isMelee ? Mathf.Max(info.Size.x, info.Size.y) : 0.45f) * scaleMul;
            float scale = ItemArt.FitScale(sprite, worldSize);
            visual.localScale = new Vector3(scale, scale, 1f);
            return;
        }

        if (info.isMelee)
        {
            Vector2 size = info.Size * scaleMul;
            visual.localScale = new Vector3(size.x, size.y, 1f);
            visualRenderer.sprite = ShapeUtil.Square(ColorFor(info.identifier));
            return;
        }

        float fallback = 0.45f * scaleMul;
        visual.localScale = new Vector3(fallback, fallback, 1f);
        visualRenderer.sprite = ShapeUtil.Triangle(ColorFor(info.identifier));
    }

    void TickAnim()
    {
        if (visualRenderer == null || animFrames == null || animFrames.Length <= 1)
            return;

        float interval = 0.3f;
        if (world != null && world.settings != null && world.settings.enemyAnimInterval > 0f)
            interval = world.settings.enemyAnimInterval;

        animTimer += Time.deltaTime;
        if (animTimer < interval)
            return;

        animTimer -= interval;
        animFrame = (animFrame + 1) % animFrames.Length;
        if (animFrames[animFrame] != null)
            visualRenderer.sprite = animFrames[animFrame];
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

            FMODUnity.RuntimeManager.PlayOneShot("event:/SFX/sfx_enemy_die", transform.position);
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

        UpdateFacing();
        TickAnim();

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
            TryShoot(attackTarget);
            SeparateRanged();
            return;
        }

        Building target = Building.FindClosestAttackable(transform.position);
        if (target == null)
            return;

        Vector3 delta = target.transform.position - transform.position;
        transform.position += delta.normalized * CurrentSpeed() * Time.deltaTime;
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

        attackTimer = CurrentAttackCd();
        Projectile.Fire(transform.position, target.Health, Info.attack, new Color(1f, 0.35f, 0.2f));
    }

    void UpdateFacing()
    {
        Building target = attackTarget;
        if (target == null)
        {
            if (Info.isMelee)
            {
                target = Building.FindCore();
                if (target == null || !target.CanBeAttacked)
                    target = Building.FindClosestAttackable(transform.position);
            }
            else
                target = Building.FindClosestAttackable(transform.position);
        }

        if (target == null)
            return;

        Face(target.transform.position - transform.position);
    }

    void Face(Vector3 delta)
    {
        if (visual == null || visualRenderer == null || delta.sqrMagnitude <= 0.0001f)
            return;

        visual.localRotation = Quaternion.identity;
        visualRenderer.flipX = delta.x < 0f;
    }

    void AlignToBoard()
    {
        Rigidbody2D board = WalkBoard();
        if (board == null)
            return;
        body.rotation = board.rotation;
    }

    void WalkAlongBoard(Building target)
    {
        Rigidbody2D board = WalkBoard();
        if (board == null)
            return;
        Vector2 along = board.transform.right;
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
        Rigidbody2D board = WalkBoard();
        if (board == null)
            return;
        Vector2 along = board.transform.right;
        Vector2 up = board.transform.up;
        float upSpeed = Vector2.Dot(body.velocity, up);
        body.velocity = along * (sign * CurrentSpeed()) + up * upSpeed;
        body.WakeUp();
    }

    void SeparateRanged()
    {
        Vector2 push = SeparationFromEarlier();
        if (push.sqrMagnitude < 0.0001f)
            return;
        transform.position += (Vector3)(push.normalized * CurrentSpeed() * Time.deltaTime);
    }

    void SeparateAlongBoard()
    {
        Vector2 push = SeparationFromEarlier();
        if (push.sqrMagnitude < 0.0001f)
        {
            StopAlongBoard();
            return;
        }

        Rigidbody2D board = WalkBoard();
        if (board == null)
        {
            StopAlongBoard();
            return;
        }

        float alongDelta = Vector2.Dot(push, board.transform.right);
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
                Rigidbody2D board = WalkBoard();
                if (board != null)
                    away = board.transform.right;
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
        Rigidbody2D board = WalkBoard();
        if (body == null || board == null)
            return;

        Vector2 up = board.transform.up;
        float upSpeed = Vector2.Dot(body.velocity, up);
        body.velocity = up * upSpeed;
    }

    Rigidbody2D WalkBoard()
    {
        var col = GetComponent<Collider2D>();
        WorldPlatform standing = world != null ? world.FindStandingPlatform(col) : null;
        if (standing != null && standing.Body != null)
            return standing.Body;
        if (world != null)
            return world.BoardBody;
        return null;
    }

    bool IsGrounded()
    {
        var col = GetComponent<Collider2D>();
        if (col == null)
            return false;

        if (world != null && world.FindStandingPlatform(col) != null)
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

    public void ApplySlow()
    {
        slowed = true;
        if (visualRenderer != null)
            visualRenderer.color = SlowColor;
    }

    float CurrentSpeed()
    {
        return Info.speed * (slowed ? SlowMul : 1f);
    }

    float CurrentAttackCd()
    {
        return Info.attackCD * (slowed ? 1f / SlowMul : 1f);
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

public static class EnemyArt
{
    const string ResourceFolder = "enemy/";
    static readonly Dictionary<string, Sprite[]> frameCache = new Dictionary<string, Sprite[]>();
    static readonly HashSet<string> missingAnims = new HashSet<string>();

    public static Sprite[] LoadFrames(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return null;

        Sprite[] frames;
        if (frameCache.TryGetValue(identifier, out frames))
            return frames;
        if (missingAnims.Contains(identifier))
            return null;

        frames = LoadFolder(identifier);
        if (frames == null || frames.Length == 0)
        {
            missingAnims.Add(identifier);
            return null;
        }

        frameCache[identifier] = frames;
        return frames;
    }

    static Sprite[] LoadFolder(string identifier)
    {
        string path = ResourceFolder + identifier;
        var list = new List<Sprite>();

        Sprite[] sprites = Resources.LoadAll<Sprite>(path);
        if (sprites != null)
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null)
                    list.Add(sprites[i]);
            }
        }

        if (list.Count == 0)
        {
            Texture2D[] textures = Resources.LoadAll<Texture2D>(path);
            if (textures != null)
            {
                for (int i = 0; i < textures.Length; i++)
                {
                    Texture2D tex = textures[i];
                    if (tex == null)
                        continue;
                    Sprite created = Sprite.Create(
                        tex,
                        new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    created.name = tex.name;
                    list.Add(created);
                }
            }
        }

        if (list.Count == 0)
            return null;

        list.Sort(CompareFrame);
        return list.ToArray();
    }

    static int CompareFrame(Sprite a, Sprite b)
    {
        int ia = 0;
        int ib = 0;
        bool na = a != null && int.TryParse(a.name, out ia);
        bool nb = b != null && int.TryParse(b.name, out ib);
        if (na && nb)
            return ia.CompareTo(ib);
        if (na)
            return -1;
        if (nb)
            return 1;
        string an = a != null ? a.name : "";
        string bn = b != null ? b.name : "";
        return string.CompareOrdinal(an, bn);
    }
}
