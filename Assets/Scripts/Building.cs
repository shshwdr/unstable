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
    Transform stockRoot;
    readonly List<BuildingStockRow> stockRows = new List<BuildingStockRow>();
    readonly List<ResourceAmount> drainBuffer = new List<ResourceAmount>();
    TextMesh warningText;
    SpriteRenderer warningIcon;
    SpriteRenderer visualRenderer;
    Transform radiusVisual;
    SpriteRenderer cdFill;
    Material cdFillMaterial;
    Vector3 visualBaseScale = Vector3.one;
    float cdRemain;
    float workCdDuration;
    float attackRemain;
    float pendingCoinSale;
    bool cycling;
    bool linkPulse;
    bool deleteShake;
    float deleteShakePhase;
    BalanceWorld world;

    public static bool ShowResourceStock = true;

    static readonly Color StarvedTint = new Color(0.2f, 0.2f, 0.22f, 1f);
    static readonly Color WarningRed = new Color(1f, 0.23f, 0.23f, 1f);
    static Sprite warningSprite;

    public static void ToggleResourceStock()
    {
        ShowResourceStock = !ShowResourceStock;
    }

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
        workCdDuration = 0f;
        attackRemain = 0f;
        pendingCoinSale = 0f;
        PhysicsBounds = BuildingArt.PhysicsLocalBounds(info);

        float top = PhysicsBounds.max.y;
        visualRenderer = FindVisualRenderer();
        if (visualRenderer != null)
            visualBaseScale = visualRenderer.transform.localScale;
        if (info.HasStockDisplay)
            CreateStockDisplay(info);

        float hpBarY = top + 0.28f;
        float warningY = info.IsResource ? top + 0.08f : hpBarY + 0.22f;
        CreateWarningLabel(warningY);

        if (!info.IsResource)
        {
            Health = gameObject.AddComponent<Health>();
            Health.Init(info.hp, new Vector3(0f, hpBarY, 0f));
        }

        if (info.radius > 0f)
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

    public int TotalStock()
    {
        int total = 0;
        foreach (var pair in stock)
            total += pair.Value;
        return total;
    }

    public int TakeAllStock()
    {
        return DrainStock(null);
    }

    public int DrainStock(List<ResourceAmount> into)
    {
        int total = 0;
        if (into != null)
            into.Clear();

        foreach (var pair in stock)
        {
            total += pair.Value;
            if (into != null)
                into.Add(new ResourceAmount { id = pair.Key, amount = pair.Value });
        }

        stock.Clear();
        return total;
    }

    public Vector3 ItemWorldPos()
    {
        Vector3 local = new Vector3(PhysicsBounds.center.x, PhysicsBounds.center.y, 0f);
        return transform.TransformPoint(local);
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

    void OnDestroy()
    {
        if (cdFillMaterial != null)
        {
            Destroy(cdFillMaterial);
            cdFillMaterial = null;
        }
    }

    void Update()
    {
        if (world != null && world.IsGameOver)
            return;
        if (Info == null)
            return;
        if (!IsResource && (Health == null || !Health.IsAlive))
            return;

        if (!IsResource && Info.cd > 0f)
            TickWork();
        if (Info.CanAttack)
            TickAttack();

        RefreshLabels();
        RefreshCdFill();
    }

    public bool HasWorkConditions()
    {
        return !IsBlockedByTop() && InRequiredResourceRange();
    }

    public bool HasNoProblems()
    {
        if (Info == null)
            return false;
        if (!HasWorkConditions())
            return false;

        List<ResourceAmount> needs = Info.ConsumeList;
        if (needs == null)
            return true;

        for (int i = 0; i < needs.Count; i++)
        {
            if (!HasProviderCovering(transform.position, needs[i].id))
                return false;
        }

        return true;
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

    public static bool HasProviderCovering(Vector2 position, string resource)
    {
        if (string.IsNullOrEmpty(resource))
            return true;

        for (int i = 0; i < All.Count; i++)
        {
            Building other = All[i];
            if (other == null || other.Info == null || other.IsResource)
                continue;
            if (other.Info.radius <= 0f)
                continue;
            if (!other.Info.ProvidesResource(resource))
                continue;
            if (Vector2.Distance(position, other.transform.position) <= other.Info.radius)
                return true;
        }

        return false;
    }

    public static bool IsTopBlockedAt(Vector2 position, BuildingInfo info)
    {
        if (info == null || !info.RequiresTop)
            return false;

        Bounds local = BuildingArt.PhysicsLocalBounds(info);
        Vector2 size = local.size;
        if (size.x < 0.01f || size.y < 0.01f)
            size = info.Size;
        Vector2 center = position + new Vector2(local.center.x, local.max.y + 0.1f);
        Vector2 box = new Vector2(Mathf.Max(0.2f, size.x * 0.75f), 0.16f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, box, 0f);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null)
                continue;

            Building building = hit.GetComponent<Building>();
            if (building != null)
                return true;

            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null && enemy.IsMelee)
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
        if (!TryConsume())
            return;

        cycling = true;
        workCdDuration = Info.cd * GetCdMultiplier();
        cdRemain = workCdDuration;
    }

    public float GetCdMultiplier()
    {
        float multiplier = 1f;
        Vector2 pos = transform.position;
        for (int i = 0; i < All.Count; i++)
        {
            Building other = All[i];
            if (other == null || other == this || other.Info == null || !other.Info.IsBuff)
                continue;
            if (other.Health != null && !other.Health.IsAlive)
                continue;
            if (other.Info.radius <= 0f)
                continue;

            float dist = Vector2.Distance(pos, other.transform.position);
            if (dist > other.Info.radius)
                continue;

            float percent = other.Info.CdReducePercent();
            if (percent <= 0f)
                continue;

            multiplier *= Mathf.Max(0f, 1f - percent * 0.01f);
        }

        return multiplier;
    }

    void FinishWork()
    {
        Produce();
    }

    void TickAttack()
    {
        float interval = Info.attackCD > 0f ? Info.attackCD : Info.cd;
        if (interval <= 0f)
            return;

        interval *= GetCdMultiplier();
        if (attackRemain > 0f)
            attackRemain -= Time.deltaTime;
        if (attackRemain > 0f)
            return;

        Enemy target = FindEnemyInRange();
        if (target == null)
            return;

        attackRemain = interval;
        Projectile.Fire(ItemWorldPos(), target.Health, Info.attack, AttackProjectileColor(), Info, transform);
    }

    Color AttackProjectileColor()
    {
        if (Info.HasSpecial("slow"))
            return new Color(0.45f, 0.78f, 1f);
        if (Info.HasSpecial("aoe"))
            return new Color(0.92f, 0.68f, 0.28f);
        return new Color(0.35f, 0.9f, 1f);
    }

    bool TryConsume()
    {
        if (Info.IsCoinMachine)
            return TryTakeAttackStockForSale();

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
        if (pendingCoinSale > 0f)
        {
            if (world != null)
                world.AddGold(pendingCoinSale);
            int coins = pendingCoinSale >= 8f ? 3 : (pendingCoinSale >= 3f ? 2 : 1);
            ItemFx.Rise("coin", ItemWorldPos(), coins);
            pendingCoinSale = 0f;
        }

        List<ResourceAmount> list = Info.ProvideList;
        if (list == null)
            return;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].id == "coin")
            {
                if (world != null)
                    world.AddGold(list[i].amount);
                ItemFx.Rise("coin", ItemWorldPos(), Mathf.Clamp(list[i].amount, 1, 3));
                continue;
            }

            AddStock(list[i].id, list[i].amount);
            ItemFx.Rise(list[i].id, ItemWorldPos(), Mathf.Clamp(list[i].amount, 1, 3));
        }
    }

    bool TryTakeAttackStockForSale()
    {
        pendingCoinSale = 0f;
        if (Info.radius <= 0f)
            return false;

        bool took = false;
        Vector2 pos = transform.position;
        for (int i = 0; i < All.Count; i++)
        {
            Building other = All[i];
            if (other == null || other == this || other.Info == null || !other.Info.IsAttack)
                continue;
            if (other.Health != null && !other.Health.IsAlive)
                continue;

            float dist = Vector2.Distance(pos, other.transform.position);
            if (dist > Info.radius)
                continue;

            int items = other.DrainStock(drainBuffer);
            if (items <= 0)
                continue;

            pendingCoinSale += items * other.Info.StockSellPrice;
            took = true;
            Vector3 from = other.ItemWorldPos();
            for (int s = 0; s < drainBuffer.Count; s++)
                ItemFx.Fly(drainBuffer[s].id, from, transform, drainBuffer[s].amount);
        }

        return took;
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

    public string FormatStoredAmounts()
    {
        if (Info == null || Info.ProvideList == null)
            return "";

        labelBuilder.Length = 0;
        for (int i = 0; i < Info.ProvideList.Count; i++)
        {
            string id = Info.ProvideList[i].id;
            if (string.IsNullOrEmpty(id) || id == "coin")
                continue;
            if (labelBuilder.Length > 0)
                labelBuilder.Append('\n');
            labelBuilder.Append(id).Append(':').Append(GetStock(id));
        }

        return labelBuilder.ToString();
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
        return FormatPanelInfo(Info, this);
    }

    public static string FormatPanelInfo(BuildingInfo info, Building runtime)
    {
        var sb = new StringBuilder();
        sb.Append(info != null ? info.name : "");
        if (info == null)
            return sb.ToString();

        if (!string.IsNullOrEmpty(info.desc))
            sb.Append('\n').Append(TrimCsvQuotes(info.desc));

        if (info.IsResource)
        {
            sb.Append("\nResource node");
            if (info.radius > 0f)
                sb.Append("\nRadius ").Append(info.radius.ToString("0.##"));
            return sb.ToString();
        }

        if (runtime != null && info.HasStockDisplay)
            sb.Append("\nStock ").Append(runtime.FormatStock());
        if (info.HasConsumeDisplay)
            sb.Append("\nConsume ").Append(FormatAmounts(info.ConsumeList));
        if (info.HasProvideDisplay)
            sb.Append("\nProvide ").Append(FormatAmounts(info.ProvideList));
        float cdMul = runtime != null ? runtime.GetCdMultiplier() : 1f;
        if (info.cd > 0f)
            sb.Append("\nCD ").Append((info.cd * cdMul).ToString("0.##"));
        if (info.CanAttack)
        {
            float attackCd = info.attackCD > 0f ? info.attackCD : info.cd;
            if (attackCd > 0f)
                sb.Append("\nAttack CD ").Append((attackCd * cdMul).ToString("0.##"));
        }
        if (info.RequiresTop)
            sb.Append(runtime != null && runtime.IsBlockedByTop()
                ? "\nBlocked: keep the top clear"
                : "\nSpecial: keep the top clear");
        if (!string.IsNullOrEmpty(info.requireResource))
            sb.Append(runtime != null && !runtime.InRequiredResourceRange()
                ? "\nBlocked: needs " + info.requireResource
                : "\nNeeds " + info.requireResource);
        return sb.ToString();
    }

    static string TrimCsvQuotes(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 2)
            return text;
        if (text[0] == '"' && text[text.Length - 1] == '"')
            return text.Substring(1, text.Length - 2).Replace("\"\"", "\"");
        return text;
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

    public void SetLinkPulse(bool pulse)
    {
        linkPulse = pulse;
        if (!pulse && visualRenderer != null)
            visualRenderer.transform.localScale = visualBaseScale;
    }

    public void SetDeleteShake(bool shake)
    {
        deleteShake = shake;
        if (shake)
            deleteShakePhase = Random.Range(0f, Mathf.PI * 2f);
        else if (visualRenderer != null)
        {
            visualRenderer.transform.localRotation = Quaternion.identity;
            visualRenderer.transform.localPosition = Vector3.zero;
            if (!linkPulse)
                visualRenderer.transform.localScale = visualBaseScale;
        }
    }

    void RefreshLabels()
    {
        if (Info == null)
            return;

        if (stockRoot != null)
        {
            bool show = ShowResourceStock && Info.HasStockDisplay;
            stockRoot.gameObject.SetActive(show);
            if (show)
                RefreshStockRows();
        }
        else if (infoText != null)
        {
            bool show = ShowResourceStock && Info.HasStockDisplay;
            infoText.gameObject.SetActive(show);
            if (show)
                infoText.text = FormatStoredAmounts();
        }

        SetStarvedVisual(RefreshShortage());
    }

    bool RefreshShortage()
    {
        if (warningText == null || Info == null)
            return false;

        labelBuilder.Length = 0;
        if (IsBlockedByTop())
            labelBuilder.Append("Keep the top clear to work");
        else if (!InRequiredResourceRange())
            labelBuilder.Append("Need ").Append(Info.requireResource);

        List<ResourceAmount> needs = Info.ConsumeList;
        if (needs != null && !cycling)
        {
            for (int i = 0; i < needs.Count; i++)
            {
                if (FindRichestCovering(needs[i].id, needs[i].amount) != null)
                    continue;
                if (labelBuilder.Length > 0)
                    labelBuilder.Append('\n');
                labelBuilder.Append(needs[i].id);
                if (needs[i].amount > 1)
                    labelBuilder.Append(':').Append(needs[i].amount);
            }
        }

        bool starved = labelBuilder.Length > 0;
        warningText.text = starved ? labelBuilder.ToString() : "";
        warningText.gameObject.SetActive(starved);
        if (warningIcon != null)
            warningIcon.gameObject.SetActive(starved);
        return starved;
    }

    void SetStarvedVisual(bool starved)
    {
        if (visualRenderer != null)
            visualRenderer.color = starved ? StarvedTint : Color.white;
        if (cdFill != null)
            cdFill.color = starved
                ? new Color(0.35f, 0.35f, 0.38f, 0.35f)
                : new Color(1f, 1f, 1f, 0.35f);
    }

    void CreateCdFill()
    {
        Transform visual = visualRenderer != null ? visualRenderer.transform : transform;
        Sprite sprite = visualRenderer != null ? visualRenderer.sprite : null;
        if (sprite == null)
            return;

        Shader shader = Resources.Load<Shader>("shaders/SpriteSolidFill");
        if (shader == null)
            shader = Shader.Find("Unstable/SpriteSolidFill");
        if (shader == null)
            return;

        cdFillMaterial = new Material(shader);
        cdFillMaterial.SetTexture("_MainTex", sprite.texture);
        cdFillMaterial.SetFloat("_MinY", sprite.bounds.min.y);
        cdFillMaterial.SetFloat("_MaxY", sprite.bounds.max.y);
        cdFillMaterial.SetFloat("_FillAmount", 0f);

        var go = new GameObject("CdFill");
        go.transform.SetParent(visual, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localScale = Vector3.one;
        cdFill = go.AddComponent<SpriteRenderer>();
        cdFill.sprite = sprite;
        cdFill.sharedMaterial = cdFillMaterial;
        cdFill.color = new Color(1f, 1f, 1f, 0.35f);
        cdFill.sortingOrder = 6;
        cdFill.enabled = false;
    }

    void RefreshCdFill()
    {
        if (cdFill == null || cdFillMaterial == null || Info == null || Info.cd <= 0f)
            return;

        float duration = workCdDuration > 0.0001f ? workCdDuration : Info.cd;
        float progress = cycling ? 1f - Mathf.Clamp01(cdRemain / duration) : 0f;
        if (progress < 0.001f)
        {
            cdFill.enabled = false;
            return;
        }

        cdFill.enabled = true;
        cdFillMaterial.SetFloat("_FillAmount", progress);
    }

    void LateUpdate()
    {
        if (stockRoot != null)
            stockRoot.rotation = Quaternion.identity;
        if (infoText != null)
            infoText.transform.rotation = Quaternion.identity;
        if (warningText != null)
            warningText.transform.rotation = Quaternion.identity;
        if (radiusVisual != null)
            radiusVisual.rotation = Quaternion.identity;
        AlignWarningIcon();
        RefreshLinkPulse();
        RefreshDeleteShake();
    }

    void RefreshLinkPulse()
    {
        if (!linkPulse || visualRenderer == null || deleteShake)
            return;

        float pulse = 1f + 0.14f * Mathf.Sin(Time.time * 5.5f);
        visualRenderer.transform.localScale = visualBaseScale * pulse;
    }

    void RefreshDeleteShake()
    {
        if (!deleteShake || visualRenderer == null)
            return;

        float t = Time.unscaledTime * 44f + deleteShakePhase;
        visualRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t) * 11f);
        visualRenderer.transform.localPosition = new Vector3(Mathf.Sin(t * 1.13f) * 0.04f, Mathf.Cos(t * 0.97f) * 0.03f, 0f);
        visualRenderer.transform.localScale = visualBaseScale * (1f + 0.04f * Mathf.Sin(t * 0.5f));
    }

    void AlignWarningIcon()
    {
        if (warningIcon == null || warningText == null || !warningIcon.gameObject.activeSelf)
            return;

        warningIcon.transform.rotation = Quaternion.identity;
        var textRenderer = warningText.GetComponent<Renderer>();
        if (textRenderer == null)
            return;

        Bounds textBounds = textRenderer.bounds;
        float iconW = warningIcon.bounds.size.x;
        if (iconW < 0.01f)
            iconW = 0.4f;
        warningIcon.transform.position = new Vector3(
            textBounds.min.x - iconW * 0.5f - 0.06f,
            textBounds.center.y,
            textBounds.center.z);
    }

    TextMesh CreateLabel(string objectName, string text, Color color, float localY, int sortingOrder)
    {
        return CreateLabel(objectName, text, color, localY, sortingOrder, transform);
    }

    TextMesh CreateLabel(string objectName, string text, Color color, float localY, int sortingOrder, Transform parent)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(parent);
        go.transform.localPosition = new Vector3(0f, localY, -0.1f);
        go.transform.localScale = Vector3.one;

        var mesh = go.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.fontSize = 48;
        mesh.characterSize = 0.1f;
        mesh.color = color;

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sortingOrder = sortingOrder;
        return mesh;
    }

    void CreateStockDisplay(BuildingInfo info)
    {
        stockRoot = new GameObject("Stock").transform;
        stockRoot.SetParent(transform, false);
        stockRoot.localPosition = new Vector3(0f, 0f, -0.1f);

        int rows = 0;
        for (int i = 0; i < info.ProvideList.Count; i++)
        {
            if (!string.IsNullOrEmpty(info.ProvideList[i].id) && info.ProvideList[i].id != "coin")
                rows++;
        }

        float startY = (rows - 1) * 0.22f;
        int row = 0;
        for (int i = 0; i < info.ProvideList.Count; i++)
        {
            string id = info.ProvideList[i].id;
            if (string.IsNullOrEmpty(id) || id == "coin")
                continue;

            float y = startY - row * 0.44f;
            var rowGo = new GameObject("Stock_" + id);
            rowGo.transform.SetParent(stockRoot, false);
            rowGo.transform.localPosition = new Vector3(0f, y, 0f);

            var amount = CreateLabel("Amount", "0", Color.white, 0f, 51, rowGo.transform);
            amount.anchor = TextAnchor.MiddleRight;
            amount.alignment = TextAlignment.Right;
            amount.characterSize = 0.11f;
            amount.transform.localPosition = new Vector3(-0.08f, 0f, 0f);

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(rowGo.transform, false);
            iconGo.transform.localPosition = new Vector3(0.16f, 0f, 0f);
            var icon = iconGo.AddComponent<SpriteRenderer>();
            icon.sortingOrder = 52;
            Sprite sprite = ItemArt.Load(id);
            icon.sprite = sprite;
            if (sprite != null)
            {
                float scale = ItemArt.FitScale(sprite, ItemArt.IconWorldSize);
                iconGo.transform.localScale = new Vector3(scale, scale, 1f);
            }

            stockRows.Add(new BuildingStockRow
            {
                id = id,
                amount = amount,
                icon = icon
            });
            row++;
        }
    }

    void RefreshStockRows()
    {
        for (int i = 0; i < stockRows.Count; i++)
        {
            BuildingStockRow row = stockRows[i];
            int amount = GetStock(row.id);
            if (row.icon != null && row.icon.sprite != null)
                row.amount.text = amount.ToString();
            else
                row.amount.text = row.id + ":" + amount;
        }
    }

    void CreateWarningLabel(float localY)
    {
        warningText = CreateLabel("Warning", "", WarningRed, localY, 51);
        warningText.anchor = TextAnchor.LowerCenter;
        warningText.characterSize = 0.1f;
        warningText.richText = false;
        warningText.gameObject.SetActive(false);

        var iconGo = new GameObject("WarningIcon");
        iconGo.transform.SetParent(transform);
        iconGo.transform.localPosition = new Vector3(0f, localY, -0.1f);
        warningIcon = iconGo.AddComponent<SpriteRenderer>();
        warningIcon.sortingOrder = 52;
        warningIcon.color = WarningRed;
        if (warningSprite == null)
            warningSprite = Resources.Load<Sprite>("warning");
        warningIcon.sprite = warningSprite;
        if (warningSprite != null)
        {
            float spriteSize = Mathf.Max(warningSprite.bounds.size.x, warningSprite.bounds.size.y);
            float scale = spriteSize > 0.0001f ? 0.42f / spriteSize : 1f;
            iconGo.transform.localScale = new Vector3(scale, scale, 1f);
        }

        iconGo.SetActive(false);
    }

    SpriteRenderer FindVisualRenderer()
    {
        Transform visual = transform.Find("Visual");
        return visual != null ? visual.GetComponent<SpriteRenderer>() : null;
    }

    public void SetRadiusVisible(bool visible)
    {
        if (radiusVisual != null)
            radiusVisual.gameObject.SetActive(visible);
    }

    void CreateRadiusCircle(float radius)
    {
        var circleGo = new GameObject("Radius");
        circleGo.transform.SetParent(transform, false);
        circleGo.transform.localPosition = Vector3.zero;
        circleGo.transform.localRotation = Quaternion.identity;
        radiusVisual = circleGo.transform;
        var ring = circleGo.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.startWidth = 0.04f;
        ring.endWidth = 0.04f;
        ring.material = new Material(Shader.Find("Sprites/Default"));
        Color color = IsResource
            ? new Color(0.72f, 0.55f, 0.32f, 0.55f)
            : new Color(0.2f, 0.85f, 1f, 0.55f);
        ring.startColor = color;
        ring.endColor = color;
        ring.sortingOrder = 2;
        const int segments = 48;
        ring.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }

        circleGo.SetActive(false);
    }
}

class BuildingStockRow
{
    public string id;
    public TextMesh amount;
    public SpriteRenderer icon;
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

    public static float UniformScale(BuildingInfo info)
    {
        return info != null && info.scale > 0f ? info.scale : 1f;
    }

    public static Vector2 WorldSize(BuildingInfo info)
    {
        float s = UniformScale(info);
        Sprite sprite = LoadSprite(info);
        if (sprite == null)
            return new Vector2(s, s);

        Vector2 spriteSize = sprite.bounds.size;
        return new Vector2(spriteSize.x * s, spriteSize.y * s);
    }

    public static Vector2 VisualScale(BuildingInfo info)
    {
        float s = UniformScale(info);
        return new Vector2(s, s);
    }

    public static Bounds PhysicsLocalBounds(BuildingInfo info)
    {
        bool hexagon = info != null && info.IsResource;
        Sprite sprite = ResolveSprite(info, Color.white, hexagon);
        return PhysicsLocalBounds(sprite, VisualScale(info), info);
    }

    public static Bounds PhysicsLocalBounds(Sprite sprite, Vector2 scale, BuildingInfo info)
    {
        Bounds shapeBounds;
        if (TryPhysicsShapeBounds(sprite, scale, out shapeBounds))
            return shapeBounds;

        if (sprite != null)
        {
            Vector3 size = sprite.bounds.size;
            size.x *= scale.x;
            size.y *= scale.y;
            Vector3 center = sprite.bounds.center;
            center.x *= scale.x;
            center.y *= scale.y;
            return new Bounds(center, size);
        }

        Vector2 sizeFallback = info != null ? WorldSize(info) : Vector2.one;
        return new Bounds(Vector3.zero, new Vector3(sizeFallback.x, sizeFallback.y, 0f));
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

        var box = go.AddComponent<BoxCollider2D>();
        if (sprite != null)
        {
            Vector2 size = sprite.bounds.size;
            box.size = new Vector2(size.x * scale.x, size.y * scale.y);
            box.offset = new Vector2(sprite.bounds.center.x * scale.x, sprite.bounds.center.y * scale.y);
        }
        else
        {
            box.size = info != null ? WorldSize(info) : Vector2.one;
        }
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
