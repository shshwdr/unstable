using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class Building : MonoBehaviour
{
    public static readonly List<Building> All = new List<Building>();

    public BuildingInfo Info;
    public Health Health { get; private set; }
    public Bounds PhysicsBounds { get; private set; }

    readonly Dictionary<string, int> stock = new Dictionary<string, int>();
    readonly StringBuilder labelBuilder = new StringBuilder();

    TextMesh infoText;
    Transform radiusVisual;
    SpriteRenderer cdFill;
    float cdRemain;
    bool cycling;
    BalanceWorld world;

    public bool IsCore
    {
        get { return Info != null && Info.IsCore; }
    }

    public bool IsResource
    {
        get { return Info != null && Info.IsResource; }
    }

    public bool CanBeAttacked
    {
        get { return !IsResource && Health != null && Health.IsAlive; }
    }

    public static Building FindCore()
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null && All[i].IsCore)
                return All[i];
        }

        return null;
    }

    public static bool CoreExists()
    {
        return FindCore() != null;
    }

    public static Building FindClosestAttackable(Vector3 position)
    {
        Building best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < All.Count; i++)
        {
            Building other = All[i];
            if (other == null || !other.CanBeAttacked)
                continue;

            float dist = (other.transform.position - position).sqrMagnitude;
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = other;
        }

        return best;
    }

    public static Building FindAttackableInRange(Component from, float range)
    {
        Building best = null;
        float bestDist = range;
        for (int i = 0; i < All.Count; i++)
        {
            Building other = All[i];
            if (other == null || !other.CanBeAttacked)
                continue;

            float dist = CombatUtil.Distance(from, other);
            if (dist > bestDist)
                continue;

            bestDist = dist;
            best = other;
        }

        return best;
    }

    public void Setup(BuildingInfo info)
    {
        Info = info;
        world = FindObjectOfType<BalanceWorld>();
        cycling = false;
        cdRemain = 0f;
        PhysicsBounds = BuildingArt.PhysicsLocalBounds(info);

        float top = PhysicsBounds.max.y;
        infoText = CreateLabel("Info", info.name, Color.white, 0.12f, 12);
        infoText.anchor = TextAnchor.UpperCenter;
        infoText.richText = true;

        if (!info.IsResource)
        {
            Health = gameObject.AddComponent<Health>();
            Health.Init(info.hp, new Vector3(0f, top + 0.28f, 0f));
        }

        if (info.IsResource && info.radius > 0f)
            CreateRadiusCircle(info.radius);

        if (!info.IsResource && info.cd > 0f)
            CreateCdFill();

        RefreshLabels();
        RefreshCdFill();
    }

    public int GetStock(string resource)
    {
        int amount;
        return stock.TryGetValue(resource, out amount) ? amount : 0;
    }

    public void AddStock(string resource, int amount)
    {
        if (string.IsNullOrEmpty(resource) || amount == 0)
            return;

        int next = GetStock(resource) + amount;
        if (next <= 0)
            stock.Remove(resource);
        else
            stock[resource] = next;
    }

    void OnEnable()
    {
        All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
        if (IsCore && world != null && !world.IsRestarting && !world.HasEnded)
            world.Fail();
    }

    void Update()
    {
        if (world != null && world.HasEnded)
            return;
        if (Info == null)
            return;
        if (!IsResource && (Health == null || !Health.IsAlive))
            return;

        if (!IsResource && Info.cd > 0f)
            TickWork();

        RefreshLabels();
        RefreshCdFill();
    }

    public bool HasWorkConditions()
    {
        return !IsBlockedByTop() && InRequiredResourceRange();
    }

    public bool InRequiredResourceRange()
    {
        if (Info == null || string.IsNullOrEmpty(Info.requireResource))
            return true;

        for (int i = 0; i < All.Count; i++)
        {
            Building resource = All[i];
            if (resource == null || resource.Info == null || !resource.IsResource)
                continue;
            if (resource.Info.identifier != Info.requireResource)
                continue;
            if (resource.Info.radius <= 0f)
                continue;
            if (Vector2.Distance(transform.position, resource.transform.position) <= resource.Info.radius)
                return true;
        }

        return false;
    }

    public bool IsBlockedByTop()
    {
        if (Info == null || !Info.RequiresTop)
            return false;

        var col = GetComponent<Collider2D>();
        if (col == null)
            return false;

        Vector2 size = PhysicsBounds.size;
        if (size.x < 0.01f || size.y < 0.01f)
            size = Info.Size;
        Vector2 center = transform.TransformPoint(new Vector3(PhysicsBounds.center.x, PhysicsBounds.max.y + 0.1f, 0f));
        Vector2 box = new Vector2(Mathf.Max(0.2f, size.x * 0.75f), 0.16f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, box, transform.eulerAngles.z);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null || hit == col)
                continue;

            Building other = hit.GetComponent<Building>();
            if (other != null && other != this)
                return true;

            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null && enemy.IsMelee)
                return true;
        }

        return false;
    }

    void TickWork()
    {
        if (cycling)
        {
            cdRemain -= Time.deltaTime;
            if (cdRemain > 0f)
                return;

            cycling = false;
            cdRemain = 0f;
            FinishWork();
        }

        TryStartWork();
    }

    void TryStartWork()
    {
        if (cycling)
            return;
        if (!HasWorkConditions())
            return;
        if (Info.CanAttack && FindEnemyInRange() == null)
            return;
        if (!TryConsume())
            return;

        cycling = true;
        cdRemain = Info.cd;
    }

    void FinishWork()
    {
        if (Info.CanAttack)
        {
            Enemy target = FindEnemyInRange();
            if (target != null)
                Projectile.Fire(transform.position, target.Health, Info.attack, new Color(0.35f, 0.9f, 1f));
            return;
        }

        Produce();
    }

    bool TryConsume()
    {
        List<ResourceAmount> needs = Info.ConsumeList;
        if (needs == null || needs.Count == 0)
            return true;

        var sources = new List<Building>(needs.Count);
        for (int i = 0; i < needs.Count; i++)
        {
            Building source = FindRichestCovering(needs[i].id, needs[i].amount);
            if (source == null)
                return false;
            sources.Add(source);
        }

        for (int i = 0; i < needs.Count; i++)
            sources[i].AddStock(needs[i].id, -needs[i].amount);

        return true;
    }

    void Produce()
    {
        List<ResourceAmount> list = Info.ProvideList;
        if (list == null)
            return;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].id == "coin")
            {
                if (world != null)
                    world.AddGold(list[i].amount);
                continue;
            }

            AddStock(list[i].id, list[i].amount);
        }
    }

    Building FindRichestCovering(string resource, int need)
    {
        Building best = null;
        int bestAmount = 0;
        for (int i = 0; i < All.Count; i++)
        {
            Building other = All[i];
            if (other == null || other == this || other.Info == null || other.IsResource)
                continue;
            if (other.Info.radius <= 0f)
                continue;

            float dist = Vector2.Distance(transform.position, other.transform.position);
            if (dist > other.Info.radius)
                continue;

            int amount = other.GetStock(resource);
            if (amount < need || amount <= bestAmount)
                continue;

            bestAmount = amount;
            best = other;
        }

        return best;
    }

    Enemy FindEnemyInRange()
    {
        Enemy best = null;
        float bestDist = Info.attackRange;
        for (int i = 0; i < Enemy.All.Count; i++)
        {
            Enemy enemy = Enemy.All[i];
            if (enemy == null || enemy.Health == null || !enemy.Health.IsAlive)
                continue;

            float dist = CombatUtil.Distance(this, enemy);
            if (dist > bestDist)
                continue;

            bestDist = dist;
            best = enemy;
        }

        return best;
    }

    public string FormatStock()
    {
        if (stock.Count == 0)
            return "none";

        var sb = new StringBuilder();
        bool first = true;
        foreach (var pair in stock)
        {
            if (!first)
                sb.Append(' ');
            first = false;
            sb.Append(pair.Key).Append(':').Append(pair.Value);
        }

        return sb.ToString();
    }

    public string HoverInfo()
    {
        labelBuilder.Length = 0;
        labelBuilder.Append(Info != null ? Info.name : "");
        if (IsResource)
        {
            labelBuilder.Append("\nResource node");
            if (Info != null && Info.radius > 0f)
                labelBuilder.Append("\nRadius ").Append(Info.radius.ToString("0.##"));
            return labelBuilder.ToString();
        }

        labelBuilder.Append("\nStock ").Append(FormatStock());
        labelBuilder.Append("\nConsume ").Append(FormatAmounts(Info != null ? Info.ConsumeList : null));
        labelBuilder.Append("\nProvide ").Append(FormatAmounts(Info != null ? Info.ProvideList : null));
        labelBuilder.Append("\nCD ").Append(Info != null && Info.cd > 0f ? Info.cd.ToString("0.##") : "-");
        if (Info != null && Info.RequiresTop)
            labelBuilder.Append(IsBlockedByTop() ? "\nBlocked: must be on top" : "\nSpecial: must be on top");
        if (Info != null && !string.IsNullOrEmpty(Info.requireResource))
            labelBuilder.Append(InRequiredResourceRange()
                ? "\nNeeds " + Info.requireResource
                : "\nBlocked: needs " + Info.requireResource);
        return labelBuilder.ToString();
    }

    static string FormatAmounts(List<ResourceAmount> list)
    {
        if (list == null || list.Count == 0)
            return "none";

        var sb = new StringBuilder();
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(list[i].id).Append(':').Append(list[i].amount);
        }

        return sb.ToString();
    }

    void RefreshLabels()
    {
        if (infoText == null || Info == null)
            return;

        labelBuilder.Length = 0;
        labelBuilder.Append(Info.name);

        if (IsBlockedByTop())
            labelBuilder.Append("\n<color=#ff3b3b>Must be on top to work</color>");
        else if (!InRequiredResourceRange())
            labelBuilder.Append("\n<color=#ff3b3b>Need ").Append(Info.requireResource).Append("</color>");

        if (stock.Count > 0)
            labelBuilder.Append('\n').Append(FormatStock());

        List<ResourceAmount> needs = Info.ConsumeList;
        if (needs != null && !cycling)
        {
            for (int i = 0; i < needs.Count; i++)
            {
                if (FindRichestCovering(needs[i].id, needs[i].amount) != null)
                    continue;
                labelBuilder.Append("\n<color=#ff3b3b>");
                labelBuilder.Append(needs[i].id);
                if (needs[i].amount > 1)
                    labelBuilder.Append(':').Append(needs[i].amount);
                labelBuilder.Append("</color>");
            }
        }

        infoText.text = labelBuilder.ToString();
    }

    void CreateCdFill()
    {
        var go = new GameObject("CdFill");
        go.transform.SetParent(transform);
        go.transform.localRotation = Quaternion.identity;
        cdFill = go.AddComponent<SpriteRenderer>();
        cdFill.sprite = ShapeUtil.Square(Color.white);
        cdFill.color = new Color(1f, 1f, 1f, 0.35f);
        cdFill.sortingOrder = 6;
    }

    void RefreshCdFill()
    {
        if (cdFill == null || Info == null || Info.cd <= 0f)
            return;

        float progress = cycling ? 1f - Mathf.Clamp01(cdRemain / Info.cd) : 0f;
        Vector2 size = PhysicsBounds.size;
        if (size.x < 0.01f || size.y < 0.01f)
            size = Info.Size;
        float h = size.y * progress;
        if (h < 0.001f)
        {
            cdFill.enabled = false;
            return;
        }

        cdFill.enabled = true;
        cdFill.transform.localScale = new Vector3(size.x, h, 1f);
        cdFill.transform.localPosition = new Vector3(
            PhysicsBounds.center.x,
            PhysicsBounds.min.y + h * 0.5f,
            -0.05f);
    }

    void LateUpdate()
    {
        if (infoText != null)
            infoText.transform.rotation = Quaternion.identity;
        if (radiusVisual != null)
            radiusVisual.rotation = Quaternion.identity;
    }

    TextMesh CreateLabel(string objectName, string text, Color color, float localY, int sortingOrder)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, localY, -0.1f);
        go.transform.localScale = Vector3.one;

        var mesh = go.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.fontSize = 48;
        mesh.characterSize = 0.05f;
        mesh.color = color;

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sortingOrder = sortingOrder;
        return mesh;
    }

    void CreateRadiusCircle(float radius)
    {
        var circleGo = new GameObject("Radius");
        circleGo.transform.SetParent(transform);
        circleGo.transform.localPosition = Vector3.zero;
        radiusVisual = circleGo.transform;
        var ring = circleGo.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.startWidth = 0.04f;
        ring.endWidth = 0.04f;
        ring.material = new Material(Shader.Find("Sprites/Default"));
        ring.startColor = new Color(0.72f, 0.55f, 0.32f, 0.55f);
        ring.endColor = new Color(0.72f, 0.55f, 0.32f, 0.55f);
        ring.sortingOrder = 4;
        const int segments = 48;
        ring.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
    }
}

public static class BuildingArt
{
    const string ResourceFolder = "building/";
    static readonly List<Vector2> shapePoints = new List<Vector2>(64);
    static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    static readonly HashSet<string> missingSprites = new HashSet<string>();

    public static Sprite LoadSprite(BuildingInfo info)
    {
        if (info == null || string.IsNullOrEmpty(info.identifier))
            return null;

        Sprite sprite;
        if (spriteCache.TryGetValue(info.identifier, out sprite))
            return sprite;
        if (missingSprites.Contains(info.identifier))
            return null;

        sprite = Resources.Load<Sprite>(ResourceFolder + info.identifier);
        if (sprite == null)
        {
            missingSprites.Add(info.identifier);
            return null;
        }

        spriteCache[info.identifier] = sprite;
        return sprite;
    }

    public static Sprite ResolveSprite(BuildingInfo info, Color fallbackColor, bool hexagon)
    {
        Sprite sprite = LoadSprite(info);
        if (sprite != null)
            return sprite;
        return hexagon ? ShapeUtil.Hexagon(fallbackColor) : ShapeUtil.Square(fallbackColor);
    }

    public static Vector2 VisualScale(BuildingInfo info, Sprite sprite)
    {
        Vector2 shape = info != null ? info.Size : Vector2.one;
        if (sprite == null)
            return shape;

        Vector2 spriteSize = sprite.bounds.size;
        return new Vector2(
            spriteSize.x > 0.0001f ? shape.x / spriteSize.x : shape.x,
            spriteSize.y > 0.0001f ? shape.y / spriteSize.y : shape.y);
    }

    public static Bounds PhysicsLocalBounds(BuildingInfo info)
    {
        bool hexagon = info != null && info.IsResource;
        Sprite sprite = ResolveSprite(info, Color.white, hexagon);
        return PhysicsLocalBounds(sprite, VisualScale(info, sprite), info);
    }

    public static Bounds PhysicsLocalBounds(Sprite sprite, Vector2 scale, BuildingInfo info)
    {
        Bounds shapeBounds;
        if (TryPhysicsShapeBounds(sprite, scale, out shapeBounds))
            return shapeBounds;

        Vector2 size = info != null ? info.Size : Vector2.one;
        return new Bounds(Vector3.zero, new Vector3(size.x, size.y, 0f));
    }

    public static Collider2D AddCollider(GameObject go, Sprite sprite, Vector2 scale, BuildingInfo info, PhysicsMaterial2D material)
    {
        if (sprite != null && sprite.GetPhysicsShapeCount() > 0)
        {
            var poly = go.AddComponent<PolygonCollider2D>();
            int count = sprite.GetPhysicsShapeCount();
            poly.pathCount = count;
            for (int i = 0; i < count; i++)
            {
                shapePoints.Clear();
                sprite.GetPhysicsShape(i, shapePoints);
                for (int p = 0; p < shapePoints.Count; p++)
                    shapePoints[p] = new Vector2(shapePoints[p].x * scale.x, shapePoints[p].y * scale.y);
                poly.SetPath(i, shapePoints);
            }

            poly.sharedMaterial = material;
            return poly;
        }

        Vector2 size = info != null ? info.Size : Vector2.one;
        var box = go.AddComponent<BoxCollider2D>();
        box.size = size;
        box.sharedMaterial = material;
        return box;
    }

    static bool TryPhysicsShapeBounds(Sprite sprite, Vector2 scale, out Bounds bounds)
    {
        bounds = new Bounds();
        if (sprite == null || sprite.GetPhysicsShapeCount() <= 0)
            return false;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        int count = sprite.GetPhysicsShapeCount();
        for (int i = 0; i < count; i++)
        {
            shapePoints.Clear();
            sprite.GetPhysicsShape(i, shapePoints);
            for (int p = 0; p < shapePoints.Count; p++)
            {
                float x = shapePoints[p].x * scale.x;
                float y = shapePoints[p].y * scale.y;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (minX > maxX)
            return false;

        bounds = new Bounds(
            new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f),
            new Vector3(maxX - minX, maxY - minY, 0f));
        return true;
    }
}
