using System.Collections.Generic;
using UnityEngine;

public class Building : MonoBehaviour
{
    public static readonly List<Building> All = new List<Building>();

    public BuildingInfo Info;
    public bool IsSatisfied { get; private set; }

    readonly List<string> missing = new List<string>();
    TextMesh nameText;
    TextMesh requireText;

    static readonly Dictionary<string, HashSet<Building>> served = new Dictionary<string, HashSet<Building>>();
    static readonly Dictionary<Building, int> usedSlots = new Dictionary<Building, int>();
    static readonly List<AssignPair> pairs = new List<AssignPair>();
    static readonly List<string> resources = new List<string>();

    struct AssignPair
    {
        public Building provider;
        public Building consumer;
        public float dist;
    }

    public bool IsHome
    {
        get { return Info != null && Info.IsHome; }
    }

    public void Setup(BuildingInfo info)
    {
        Info = info;
        nameText = CreateLabel("Name", info.name, Color.white, 0f, 12);
        requireText = CreateLabel("Require", "", Color.red, -info.Size.y * 0.5f - 0.2f, 13);
        requireText.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    void LateUpdate()
    {
        if (nameText != null)
            nameText.transform.rotation = Quaternion.identity;
        if (requireText != null)
            requireText.transform.rotation = Quaternion.identity;
    }

    public bool UpdateSatisfaction()
    {
        bool ok = true;
        missing.Clear();

        if (Info != null && Info.require != null)
        {
            for (int i = 0; i < Info.require.Count; i++)
            {
                string resource = Info.require[i];
                if (string.IsNullOrEmpty(resource))
                    continue;
                if (IsServed(resource))
                    continue;

                ok = false;
                missing.Add(resource);
            }
        }

        bool changed = ok != IsSatisfied;
        IsSatisfied = ok;
        RefreshRequireLabel();
        return changed;
    }

    public static void RefreshAll()
    {
        for (int i = 0; i < All.Count; i++)
            All[i].IsSatisfied = All[i].Info != null && All[i].Info.HasNoRequires();

        int limit = All.Count + 1;
        for (int pass = 0; pass < limit; pass++)
        {
            BuildServed();
            bool changed = false;
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].UpdateSatisfaction())
                    changed = true;
            }

            if (!changed)
                break;
        }
    }

    public static int SatisfiedHomeCount()
    {
        int count = 0;
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i].IsHome && All[i].IsSatisfied)
                count++;
        }

        return count;
    }

    static void BuildServed()
    {
        foreach (var pair in served)
            pair.Value.Clear();

        resources.Clear();
        for (int i = 0; i < All.Count; i++)
        {
            Building provider = All[i];
            if (!provider.IsSatisfied || provider.Info == null || provider.Info.provide == null)
                continue;
            if (provider.Info.radius <= 0f || provider.Info.provideCount <= 0)
                continue;

            for (int p = 0; p < provider.Info.provide.Count; p++)
            {
                string resource = provider.Info.provide[p];
                if (string.IsNullOrEmpty(resource) || resources.Contains(resource))
                    continue;
                resources.Add(resource);
            }
        }

        for (int i = 0; i < resources.Count; i++)
            AssignResource(resources[i]);
    }

    static void AssignResource(string resource)
    {
        pairs.Clear();
        usedSlots.Clear();

        for (int i = 0; i < All.Count; i++)
        {
            Building provider = All[i];
            if (!provider.IsSatisfied || provider.Info == null)
                continue;
            if (!provider.Info.Provides(resource))
                continue;
            if (provider.Info.radius <= 0f || provider.Info.provideCount <= 0)
                continue;

            Vector2 pos = provider.transform.position;
            float radius = provider.Info.radius;
            for (int j = 0; j < All.Count; j++)
            {
                Building consumer = All[j];
                if (consumer == provider || consumer.Info == null)
                    continue;
                if (!consumer.Info.Requires(resource))
                    continue;

                float dist = Vector2.Distance(pos, consumer.transform.position);
                if (dist > radius)
                    continue;

                pairs.Add(new AssignPair { provider = provider, consumer = consumer, dist = dist });
            }
        }

        pairs.Sort(ComparePair);
        HashSet<Building> set = GetServed(resource);
        for (int i = 0; i < pairs.Count; i++)
        {
            Building consumer = pairs[i].consumer;
            if (set.Contains(consumer))
                continue;

            Building provider = pairs[i].provider;
            int used;
            usedSlots.TryGetValue(provider, out used);
            if (used >= provider.Info.provideCount)
                continue;

            set.Add(consumer);
            usedSlots[provider] = used + 1;
        }
    }

    static int ComparePair(AssignPair a, AssignPair b)
    {
        return a.dist.CompareTo(b.dist);
    }

    static HashSet<Building> GetServed(string resource)
    {
        HashSet<Building> set;
        if (!served.TryGetValue(resource, out set))
        {
            set = new HashSet<Building>();
            served[resource] = set;
        }

        return set;
    }

    bool IsServed(string resource)
    {
        HashSet<Building> set;
        return served.TryGetValue(resource, out set) && set.Contains(this);
    }

    void RefreshRequireLabel()
    {
        if (requireText == null)
            return;

        if (IsSatisfied || missing.Count == 0)
        {
            requireText.gameObject.SetActive(false);
            return;
        }

        requireText.gameObject.SetActive(true);
        requireText.text = string.Join("\n", missing.ToArray());
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
        mesh.characterSize = 0.06f;
        mesh.color = color;

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sortingOrder = sortingOrder;
        return mesh;
    }
}
