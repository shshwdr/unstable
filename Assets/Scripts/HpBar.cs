using UnityEngine;
using UnityEngine.UI;

public class HpBar : MonoBehaviour
{
    ProgressBar progressBar;
    Text hpText;

    public static HpBar Create(Transform owner, Vector3 localOffset, Color fillColor)
    {
        var root = new GameObject("HpBar");
        root.transform.SetParent(owner, false);
        root.transform.localPosition = localOffset;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 50;
        canvas.worldCamera = Camera.main;

        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(110f, 28f);
        rt.localScale = new Vector3(0.01f, 0.01f, 0.01f);

        var bar = root.AddComponent<HpBar>();
        bar.Build(fillColor);
        return bar;
    }

    void Build(Color fillColor)
    {
        var bg = CreateUiChild("Background", transform);
        var bgImage = bg.AddComponent<Image>();
        bgImage.sprite = ShapeUtil.WhiteSprite();
        bgImage.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);
        bgImage.raycastTarget = false;
        Stretch(bg.GetComponent<RectTransform>(), 0f);

        var fill = CreateUiChild("Fill", transform);
        var fillImage = fill.AddComponent<Image>();
        fillImage.sprite = ShapeUtil.WhiteSprite();
        fillImage.color = fillColor;
        fillImage.raycastTarget = false;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 1f;
        Stretch(fill.GetComponent<RectTransform>(), 2f);

        progressBar = fill.AddComponent<ProgressBar>();
        progressBar.image = fillImage;

        var label = CreateUiChild("Value", transform);
        hpText = label.AddComponent<Text>();
        hpText.font = UiFont();
        hpText.fontSize = 14;
        hpText.alignment = TextAnchor.MiddleCenter;
        hpText.color = Color.white;
        hpText.raycastTarget = false;
        Stretch(label.GetComponent<RectTransform>(), 0f);
    }

    public void Set(float current, float max)
    {
        if (progressBar != null)
            progressBar.amount = max <= 0f ? 0f : current / max;

        if (hpText != null)
            hpText.text = Mathf.CeilToInt(Mathf.Max(0f, current)) + "/" + Mathf.CeilToInt(Mathf.Max(0f, max));
    }

    void LateUpdate()
    {
        transform.rotation = Quaternion.identity;
    }

    static GameObject CreateUiChild(string objectName, Transform parent)
    {
        var go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(RectTransform rt, float padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    static Font UiFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }
}
