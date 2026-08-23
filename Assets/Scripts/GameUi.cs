using UnityEngine;
using UnityEngine.UI;

public static class GameUi
{
    static Sprite cardSprite;
    static Font builtinFont;

    public static Font Font()
    {
        GameBalanceSettings settings = GameBalanceSettings.Loaded();
        if (settings != null && settings.font != null)
            return settings.font;

        if (builtinFont == null)
        {
            builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtinFont == null)
                builtinFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return builtinFont;
    }

    public static void BeginGui()
    {
        Font font = Font();
        if (font != null)
            GUI.skin.font = font;
    }

    public static void ApplyFont(GUIStyle style)
    {
        if (style == null)
            return;
        Font font = Font();
        if (font != null)
            style.font = font;
    }

    public static void ApplyFont(TextMesh mesh)
    {
        if (mesh == null)
            return;
        Font font = Font();
        if (font == null)
            return;
        mesh.font = font;
        MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
        if (renderer != null && font.material != null)
            renderer.sharedMaterial = font.material;
    }

    public static void ApplyFont(Text text)
    {
        if (text == null)
            return;
        Font font = Font();
        if (font != null)
            text.font = font;
    }

    public static Sprite Card()
    {
        if (cardSprite != null)
            return cardSprite;

        cardSprite = Resources.Load<Sprite>("card");
        if (cardSprite != null)
            return cardSprite;

        Texture2D tex = Resources.Load<Texture2D>("card");
        if (tex != null)
            cardSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
        return cardSprite;
    }

    public static void DrawCard(Rect rect)
    {
        Sprite sprite = Card();
        if (sprite != null)
        {
            DrawSpriteFill(rect, sprite);
            return;
        }

        GUI.Box(rect, "");
    }

    public static bool CardButton(Rect rect, string text, GUIStyle textStyle)
    {
        Color old = GUI.color;
        if (!GUI.enabled)
            GUI.color = old * new Color(1f, 1f, 1f, 0.55f);
        DrawCard(rect);
        GUI.color = old;

        var style = new GUIStyle(GUI.skin.label);
        ApplyFont(style);
        style.fontSize = textStyle != null ? textStyle.fontSize : 20;
        style.fontStyle = textStyle != null ? textStyle.fontStyle : FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.wordWrap = true;
        Color color = textStyle != null ? textStyle.normal.textColor : new Color(0.22f, 0.14f, 0.1f);
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        GUI.Label(rect, text, style);

        Event e = Event.current;
        if (!GUI.enabled || e == null)
            return false;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            e.Use();
            return true;
        }

        return false;
    }

    public static void DrawSpriteFill(Rect position, Sprite sprite)
    {
        if (sprite == null || sprite.texture == null || position.width <= 1f || position.height <= 1f)
            return;

        Texture2D tex = sprite.texture;
        Rect sr = sprite.rect;
        if (sr.width < 1f || sr.height < 1f)
            return;

        var uv = new Rect(sr.x / tex.width, sr.y / tex.height, sr.width / tex.width, sr.height / tex.height);
        GUI.DrawTextureWithTexCoords(position, tex, uv);
    }
}
