using System.Collections.Generic;
using UnityEngine;

public static class ItemArt
{
    const string ResourceFolder = "item/";
    const float DefaultWorldSize = 0.42f;

    static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    static readonly HashSet<string> missingSprites = new HashSet<string>();

    public static float IconWorldSize
    {
        get { return 0.42f; }
    }

    public static Sprite Load(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return null;
        if (itemId == "chess")
            itemId = "cheese";

        Sprite sprite;
        if (spriteCache.TryGetValue(itemId, out sprite))
            return sprite;
        if (missingSprites.Contains(itemId))
            return null;

        sprite = Resources.Load<Sprite>(ResourceFolder + itemId);
        if (sprite == null)
        {
            missingSprites.Add(itemId);
            return null;
        }

        spriteCache[itemId] = sprite;
        return sprite;
    }

    public static float FitScale(Sprite sprite, float worldSize)
    {
        if (sprite == null)
            return 1f;

        Vector2 size = sprite.bounds.size;
        float max = Mathf.Max(size.x, size.y);
        if (max < 0.0001f)
            return 1f;
        return (worldSize > 0f ? worldSize : DefaultWorldSize) / max;
    }

    public static string PrimaryItemId(BuildingInfo info)
    {
        if (info == null || info.ProvideList == null)
            return null;

        for (int i = 0; i < info.ProvideList.Count; i++)
        {
            string id = info.ProvideList[i].id;
            if (!string.IsNullOrEmpty(id) && id != "coin")
                return id;
        }

        return null;
    }
}

public class ItemFx : MonoBehaviour
{
    public static readonly List<ItemFx> All = new List<ItemFx>();

    const float RiseDuration = 0.55f;
    const float RiseHeight = 0.62f;
    const float FlyDuration = 0.42f;
    const float FxIconSize = 0.4f;
    const int Sorting = 28;

    SpriteRenderer sr;
    Transform follow;
    Vector3 start;
    Vector3 riseStart;
    float arcHeight;
    float delay;
    float duration;
    float age;
    bool rising;
    Color baseColor = Color.white;

    public static void Rise(string itemId, Vector3 from, int count)
    {
        Sprite sprite = ItemArt.Load(itemId);
        if (sprite == null)
            return;

        int n = Mathf.Clamp(count, 1, 4);
        for (int i = 0; i < n; i++)
        {
            Vector3 pos = from;
            if (n > 1)
                pos += new Vector3(Random.Range(-0.12f, 0.12f), Random.Range(-0.04f, 0.06f), 0f);
            Spawn(sprite, pos, true, null, i * 0.05f, FxIconSize);
        }
    }

    public static void Fly(string itemId, Vector3 from, Transform to, int amount)
    {
        Sprite sprite = ItemArt.Load(itemId);
        if (sprite == null || to == null)
            return;

        int n = Mathf.Clamp(amount, 1, 6);
        for (int i = 0; i < n; i++)
        {
            Vector3 pos = from + (Vector3)(Random.insideUnitCircle * 0.14f);
            Spawn(sprite, pos, false, to, i * 0.04f, FxIconSize);
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

    static ItemFx Spawn(Sprite sprite, Vector3 position, bool rise, Transform to, float startDelay, float worldSize)
    {
        var go = new GameObject(rise ? "ItemRise" : "ItemFly");
        go.transform.position = position;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        float scale = ItemArt.FitScale(sprite, worldSize);
        visual.transform.localScale = new Vector3(scale, scale, 1f);
        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = Sorting;

        var fx = go.AddComponent<ItemFx>();
        fx.sr = renderer;
        fx.rising = rise;
        fx.follow = to;
        fx.start = position;
        fx.riseStart = position;
        fx.delay = startDelay;
        fx.duration = rise ? RiseDuration : FlyDuration + Random.Range(0f, 0.08f);
        fx.arcHeight = rise ? 0f : Random.Range(0.45f, 0.9f);
        if (startDelay > 0f)
        {
            Color c = renderer.color;
            c.a = 0f;
            renderer.color = c;
        }

        return fx;
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
        age += Time.deltaTime;
        if (age < delay)
        {
            SetAlpha(0f);
            return;
        }

        float t = duration > 0.0001f ? Mathf.Clamp01((age - delay) / duration) : 1f;
        if (rising)
            TickRise(t);
        else
            TickFly(t);

        if (t >= 1f)
            Destroy(gameObject);
    }

    void TickRise(float t)
    {
        float eased = 1f - (1f - t) * (1f - t);
        transform.position = riseStart + Vector3.up * (RiseHeight * eased);
        float pop = 1f + 0.18f * Mathf.Sin(t * Mathf.PI);
        transform.localScale = new Vector3(pop, pop, 1f);
        SetAlpha(t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f);
    }

    void TickFly(float t)
    {
        Vector3 dest = follow != null ? follow.position : start;
        Vector3 control = (start + dest) * 0.5f + Vector3.up * arcHeight;
        float u = 1f - t;
        transform.position = u * u * start + 2f * u * t * control + t * t * dest;
        SetAlpha(t < 0.82f ? 1f : 1f - (t - 0.82f) / 0.18f);
    }

    void SetAlpha(float alpha)
    {
        if (sr == null)
            return;
        Color c = baseColor;
        c.a = Mathf.Clamp01(alpha);
        sr.color = c;
    }
}
