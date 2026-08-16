using UnityEngine;

public class BuildingPlacer : MonoBehaviour
{
    BalanceWorld world;
    int selected;
    Transform ghost;
    Transform ghostVisual;
    SpriteRenderer ghostRenderer;
    LineRenderer radiusCircle;
    TextMesh ghostName;
    Building hovered;

    void Awake()
    {
        world = GetComponent<BalanceWorld>();
    }

    void Start()
    {
        ghost = new GameObject("Ghost").transform;
        ghost.SetParent(transform);

        ghostVisual = new GameObject("Visual").transform;
        ghostVisual.SetParent(ghost);
        ghostVisual.localPosition = Vector3.zero;
        ghostRenderer = ghostVisual.gameObject.AddComponent<SpriteRenderer>();
        ghostRenderer.sprite = ShapeUtil.Square(Color.white);
        ghostRenderer.sortingOrder = 20;

        var nameGo = new GameObject("Name");
        nameGo.transform.SetParent(ghost);
        nameGo.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        ghostName = nameGo.AddComponent<TextMesh>();
        ghostName.anchor = TextAnchor.MiddleCenter;
        ghostName.alignment = TextAlignment.Center;
        ghostName.fontSize = 48;
        ghostName.characterSize = 0.06f;
        ghostName.color = Color.white;
        nameGo.GetComponent<MeshRenderer>().sortingOrder = 21;

        var circleGo = new GameObject("Radius");
        circleGo.transform.SetParent(ghost);
        circleGo.transform.localPosition = Vector3.zero;
        radiusCircle = circleGo.AddComponent<LineRenderer>();
        radiusCircle.useWorldSpace = false;
        radiusCircle.loop = true;
        radiusCircle.startWidth = 0.05f;
        radiusCircle.endWidth = 0.05f;
        radiusCircle.material = new Material(Shader.Find("Sprites/Default"));
        radiusCircle.startColor = new Color(0.2f, 0.85f, 1f, 0.7f);
        radiusCircle.endColor = new Color(0.2f, 0.85f, 1f, 0.7f);
        radiusCircle.sortingOrder = 19;

        RefreshGhost();
    }

    void Update()
    {
        var list = CSVLoader.Instance.buildingList;
        if (list.Count == 0)
            return;

        for (int i = 0; i < list.Count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                Select(i);
        }

        if (world.HasEnded || Camera.main == null)
        {
            ghost.gameObject.SetActive(false);
            hovered = null;
            return;
        }

        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0f;
        hovered = FindBuildingAt(worldPos);

        if (hovered != null)
        {
            ghost.gameObject.SetActive(false);
            if (Input.GetMouseButtonDown(1))
                Demolish(hovered);
            return;
        }

        ghost.gameObject.SetActive(true);
        ghost.position = worldPos;

        BuildingInfo info = list[selected];
        bool canPlace = CanPlace(worldPos, info);
        ghostRenderer.color = canPlace
            ? new Color(0.25f, 0.9f, 0.3f, 0.45f)
            : new Color(0.95f, 0.2f, 0.2f, 0.45f);

        if (Input.GetMouseButtonDown(0) && canPlace)
            Place(worldPos, info);
    }

    void Select(int index)
    {
        selected = index;
        RefreshGhost();
    }

    void RefreshGhost()
    {
        if (ghost == null)
            return;

        var list = CSVLoader.Instance.buildingList;
        if (list.Count == 0)
            return;

        if (selected >= list.Count)
            selected = 0;

        BuildingInfo info = list[selected];
        Vector2 size = info.Size;
        ghostVisual.localScale = new Vector3(size.x, size.y, 1f);
        ghostName.text = info.name;
        SetCircle(info.radius);
    }

    void SetCircle(float radius)
    {
        if (radius <= 0f)
        {
            radiusCircle.enabled = false;
            return;
        }

        radiusCircle.enabled = true;
        const int segments = 48;
        radiusCircle.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            radiusCircle.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
    }

    Building FindBuildingAt(Vector2 worldPos)
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(worldPos);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null)
                continue;
            Building building = hits[i].GetComponent<Building>();
            if (building != null)
                return building;
        }

        return null;
    }

    void Demolish(Building building)
    {
        if (building == null || building.Info == null)
            return;

        world.AddGold(building.Info.cost);
        Destroy(building.gameObject);
        hovered = null;
    }

    bool CanPlace(Vector2 position, BuildingInfo info)
    {
        if (info.IsCore && Building.CoreExists())
            return false;
        if (!info.IsCore && !Building.CoreExists())
            return false;
        if (world.Gold < info.cost)
            return false;
        if (OverlapsBuilding(position, info.Size))
            return false;
        return true;
    }

    bool OverlapsBuilding(Vector2 position, Vector2 size)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(position, size, 0f);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] != null && hits[i].GetComponent<Building>() != null)
                return true;
        }

        return false;
    }

    void Place(Vector3 position, BuildingInfo info)
    {
        if (!world.SpendGold(info.cost))
            return;

        Vector2 size = info.Size;

        var go = new GameObject(info.name);
        go.transform.position = position;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = new Vector3(size.x, size.y, 1f);
        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = ShapeUtil.Square(ColorFor(info.type));
        renderer.sortingOrder = 5;

        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.mass = Mathf.Max(0.05f, world.settings.buildingDensity * size.x * size.y);
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        var box = go.AddComponent<BoxCollider2D>();
        box.size = size;
        box.sharedMaterial = world.SharedMaterial;

        var building = go.AddComponent<Building>();
        building.Setup(info);

        Physics2D.SyncTransforms();
        SeparateFromBoard(box);
        Physics2D.SyncTransforms();
        PushOverlappingBuildings(box);
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
    }

    void SeparateFromBoard(Collider2D placed)
    {
        Collider2D board = world.BoardCollider;
        if (board == null)
            return;

        ColliderDistance2D dist = Physics2D.Distance(placed, board);
        if (!dist.isOverlapped)
            return;

        float extra = world.settings.overlapPush;
        Vector2 push = -dist.normal * (-dist.distance + extra);

        Vector2 sky = world.BoardBody.transform.up;
        if (Vector2.Dot(sky, Vector2.up) < 0f)
            sky = -sky;
        if (Vector2.Dot(push, sky) < 0f)
            push = sky * (-dist.distance + extra);

        placed.attachedRigidbody.position += push;
    }

    void PushOverlappingBuildings(Collider2D placed)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(placed.bounds.center, placed.bounds.size, 0f);
        float extra = world.settings.overlapPush;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null || hit == placed || hit == world.BoardCollider)
                continue;

            Building building = hit.GetComponent<Building>();
            if (building == null)
                continue;

            ColliderDistance2D dist = Physics2D.Distance(placed, hit);
            if (!dist.isOverlapped)
                continue;

            Vector2 push = dist.normal * (-dist.distance + extra);
            Rigidbody2D other = hit.attachedRigidbody;
            if (other == null)
                continue;

            other.position += push;
            other.WakeUp();
        }
    }

    static Color ColorFor(string type)
    {
        switch (type)
        {
            case "core": return new Color(0.95f, 0.78f, 0.28f);
            case "mine": return new Color(0.62f, 0.45f, 0.28f);
            case "electricity": return new Color(0.95f, 0.82f, 0.25f);
            case "furnace": return new Color(0.82f, 0.38f, 0.18f);
            case "wall": return new Color(0.55f, 0.55f, 0.58f);
            case "attack": return new Color(0.78f, 0.28f, 0.32f);
            default: return new Color(0.7f, 0.7f, 0.7f);
        }
    }

    void OnGUI()
    {
        var goldStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            fontStyle = FontStyle.Bold
        };
        goldStyle.normal.textColor = Color.black;
        goldStyle.hover.textColor = Color.black;
        GUI.Label(new Rect(16f, Screen.height - 86f, 240f, 50f),
            "Gold " + Mathf.FloorToInt(world.Gold), goldStyle);

        var list = CSVLoader.Instance.buildingList;
        if (list.Count > 0)
        {
            var hotStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            };
            hotStyle.normal.textColor = Color.black;
            hotStyle.hover.textColor = Color.black;

            string hotkeys = "";
            int count = list.Count < 9 ? list.Count : 9;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    hotkeys += "    ";
                hotkeys += (i + 1) + " " + list[i].name;
            }

            GUI.Label(new Rect(0f, Screen.height - 34f, Screen.width, 28f), hotkeys, hotStyle);
        }

        if (hovered == null || hovered.Info == null)
            return;

        float panelW = 230f;
        float panelH = 148f;
        var panel = new Rect(Screen.width - panelW - 16f, Screen.height - panelH - 48f, panelW, panelH);
        GUI.Box(panel, "");

        var panelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16
        };
        panelStyle.normal.textColor = Color.black;
        panelStyle.hover.textColor = Color.black;
        GUI.Label(new Rect(panel.x + 12f, panel.y + 10f, panel.width - 20f, panel.height - 16f),
            hovered.HoverInfo(), panelStyle);
    }
}
