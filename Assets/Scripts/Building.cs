using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class Building : MonoBehaviour
{
    public static readonly List<Building> All = new List<Building>();

    public BuildingInfo Info;
    public Health Health { get; private set; }

    readonly Dictionary<string, int> stock = new Dictionary<string, int>();
    readonly StringBuilder labelBuilder = new StringBuilder();

    TextMesh infoText;
    float cdRemain;
    BalanceWorld world;

    public bool IsCore
    {
        get { return Info != null && Info.IsCore; }
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

    public void Setup(BuildingInfo info)
    {
        Info = info;
        world = FindObjectOfType<BalanceWorld>();
        cdRemain = info.CanAttack ? 0f : info.cd;

        float halfH = info.Size.y * 0.5f;
        nameText = CreateLabel("Name", info.name, Color.white, 0f, 12);
        stockText = CreateLabel("Stock", "", Color.white, -halfH - 0.18f, 13);
        missingText = CreateLabel("Missing", "", Color.red, -halfH - 0.38f, 13);
        missingText.gameObject.SetActive(false);

        Health = gameObject.AddComponent<Health>();
        Health.Init(info.hp, new Vector3(0f, halfH + 0.28f, 0f));
        RefreshLabels();
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
        if (Info == null || Health == null || !Health.IsAlive)
            return;

        if (Info.CanAttack)
            TickAttack();
        else
            TickProduce();

        RefreshLabels();
    }

    void TickProduce()
    {
        if (Info.cd <= 0f)
            return;

        cdRemain -= Time.deltaTime;
        if (cdRemain > 0f)
            return;

        cdRemain += Info.cd;
        if (cdRemain < 0f)
            cdRemain = 0f;

        if (TryConsume())
            Produce();
    }

    void TickAttack()
    {
        cdRemain -= Time.deltaTime;
        if (cdRemain > 0f)
            return;

        Enemy target = FindEnemyInRange();
        if (target == null)
            return;
        if (!TryConsume())
            return;

        cdRemain = Info.cd;
        Projectile.Fire(transform.position, target.Health, Info.attack, new Color(0.35f, 0.9f, 1f));
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
            AddStock(list[i].id, list[i].amount);
    }

    Building FindRichestCovering(string resource, int need)
    {
        Building best = null;
        int bestAmount = 0;
        for (int i = 0; i < All.Count; i++)
        {
            Building other = All[i];
            if (other == null || other == this || other.Info == null)
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
        labelBuilder.Append("\nStock ").Append(FormatStock());
        labelBuilder.Append("\nConsume ").Append(FormatAmounts(Info != null ? Info.ConsumeList : null));
        labelBuilder.Append("\nProvide ").Append(FormatAmounts(Info != null ? Info.ProvideList : null));
        labelBuilder.Append("\nCD ").Append(Info != null && Info.cd > 0f ? Info.cd.ToString("0.##") : "-");
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
        RefreshStockLabel();
        RefreshMissingLabel();
    }

    void RefreshStockLabel()
    {
        if (stockText == null)
            return;

        if (stock.Count == 0)
        {
            stockText.gameObject.SetActive(false);
            return;
        }

        stockText.gameObject.SetActive(true);
        stockText.text = FormatStock();
    }

    void RefreshMissingLabel()
    {
        if (missingText == null || Info == null)
            return;

        labelBuilder.Length = 0;
        List<ResourceAmount> needs = Info.ConsumeList;
        if (needs != null)
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

        if (labelBuilder.Length == 0)
        {
            missingText.gameObject.SetActive(false);
            return;
        }

        missingText.gameObject.SetActive(true);
        missingText.text = labelBuilder.ToString();

        float halfH = Info.Size.y * 0.5f;
        float y = -halfH - 0.18f;
        if (stockText != null && stockText.gameObject.activeSelf)
            y -= 0.22f;
        missingText.transform.localPosition = new Vector3(0f, y, -0.1f);
    }

    void LateUpdate()
    {
        if (nameText != null)
            nameText.transform.rotation = Quaternion.identity;
        if (stockText != null)
            stockText.transform.rotation = Quaternion.identity;
        if (missingText != null)
            missingText.transform.rotation = Quaternion.identity;
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
}
