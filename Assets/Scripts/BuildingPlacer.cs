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
        var list = CSVLoader.Instance.playerBuildingList;
        if (Building.CoreExists())
        {
            for (int i = 0; i < list.Count && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                    Select(i);
            }
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

        var encounters = GetComponent<EncounterManager>();
        if (Input.GetMouseButtonDown(0) && encounters != null && encounters.TryRushAt(worldPos))
        {
            ghost.gameObject.SetActive(false);
            return;
        }

        BuildingInfo info = CurrentInfo();
        if (info == null)
        {
            ghost.gameObject.SetActive(false);
            return;
        }

        ghost.gameObject.SetActive(true);
        ghost.position = worldPos;

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

    BuildingInfo CurrentInfo()
    {
        if (!Building.CoreExists())
        {
            BuildingInfo core;
            if (CSVLoader.Instance.buildingInfoMap.TryGetValue("core", out core))
                return core;
            return null;
        }

        var list = CSVLoader.Instance.playerBuildingList;
        if (list.Count == 0)
            return null;
        if (selected < 0 || selected >= list.Count)
            selected = 0;
        return list[selected];
    }

    void RefreshGhost()
    {
        if (ghost == null)
            return;

        BuildingInfo info = CurrentInfo();
        if (info == null)
            return;

        Sprite sprite = BuildingArt.ResolveSprite(info, Color.white, false);
        ghostRenderer.sprite = sprite;
        Vector2 scale = BuildingArt.VisualScale(info, sprite);
        ghostVisual.localScale = new Vector3(scale.x, scale.y, 1f);
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

        for (int i = 0; i < Building.All.Count; i++)
        {
            Building resource = Building.All[i];
            if (resource == null || !resource.IsResource)
                continue;

            Vector2 localPoint = resource.transform.InverseTransformPoint(worldPos);
            Bounds local = resource.PhysicsBounds;
            if (localPoint.x >= local.min.x && localPoint.x <= local.max.x &&
                localPoint.y >= local.min.y && localPoint.y <= local.max.y)
                return resource;
        }

        return null;
    }

    void Demolish(Building building)
    {
        if (building == null || building.Info == null || building.IsResource)
            return;

        world.AddGold(building.Info.cost);
        Destroy(building.gameObject);
        hovered = null;
    }

    bool CanPlace(Vector2 position, BuildingInfo info)
    {
        if (info == null || info.IsResource)
            return false;
        if (info.IsCore && Building.CoreExists())
            return false;
        if (!info.IsCore && !Building.CoreExists())
            return false;
        if (world.Gold < info.cost)
            return false;
        if (OverlapsBuilding(position, info))
            return false;
        if (!InRequiredResourceRange(position, info))
            return false;
        return true;
    }

    bool InRequiredResourceRange(Vector2 position, BuildingInfo info)
    {
        if (info == null || string.IsNullOrEmpty(info.requireResource))
            return true;

        for (int i = 0; i < Building.All.Count; i++)
        {
            Building resource = Building.All[i];
            if (resource == null || resource.Info == null || !resource.IsResource)
                continue;
            if (resource.Info.identifier != info.requireResource)
                continue;
            if (resource.Info.radius <= 0f)
                continue;
            if (Vector2.Distance(position, resource.transform.position) <= resource.Info.radius)
                return true;
        }

        return false;
    }

    bool OverlapsBuilding(Vector2 position, BuildingInfo info)
    {
        Bounds local = BuildingArt.PhysicsLocalBounds(info);
        Vector2 center = position + (Vector2)local.center;
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, local.size, 0f);
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

        Spawn(position, info);
        RefreshGhost();
    }

    public void SpawnStartingResources()
    {
        if (world.BoardBody == null)
            return;

        BuildingInfo rock;
        if (!CSVLoader.Instance.buildingInfoMap.TryGetValue("rock", out rock) || rock == null)
        {
            Debug.LogError("未找到 identifier 为 rock 的资源建筑");
            return;
        }

        Bounds rockBounds = BuildingArt.PhysicsLocalBounds(rock);
        SpawnResourceLocal(RandomBoardTopLocalX(rockBounds.size, -1), rock);
        SpawnResourceLocal(RandomBoardTopLocalX(rockBounds.size, 1), rock);

        RefreshGhost();
    }

    float RandomBoardTopLocalX(Vector2 size, int side)
    {
        float halfW = world.settings.boardWidth * 0.5f;
        float margin = size.x * 0.5f + 0.2f;
        float min = side < 0 ? -halfW + margin : margin;
        float max = side < 0 ? -margin : halfW - margin;
        if (min > max)
            return side < 0 ? -margin : margin;
        return Random.Range(min, max);
    }

    public Building Spawn(Vector3 position, BuildingInfo info)
    {
        if (info.IsResource)
            return null;

        var go = new GameObject(info.name);
        go.transform.position = position;

        Sprite sprite = BuildingArt.ResolveSprite(info, ColorFor(info.type), false);
        Vector2 scale = BuildingArt.VisualScale(info, sprite);
        Bounds phys = BuildingArt.PhysicsLocalBounds(sprite, scale, info);

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 5;

        var body = go.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.mass = Mathf.Max(0.05f, world.settings.buildingDensity * Mathf.Max(0.01f, phys.size.x * phys.size.y));
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        var col = BuildingArt.AddCollider(go, sprite, scale, info, world.SharedMaterial);

        var building = go.AddComponent<Building>();
        building.Setup(info);

        Physics2D.SyncTransforms();
        SeparateFromBoard(col);
        Physics2D.SyncTransforms();
        PushOverlappingBuildings(col);
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
        return building;
    }

    Building SpawnResourceLocal(float localX, BuildingInfo info)
    {
        if (world.BoardBody == null)
            return null;

        Sprite sprite = BuildingArt.ResolveSprite(info, ColorFor(info.type), true);
        Vector2 scale = BuildingArt.VisualScale(info, sprite);
        Bounds phys = BuildingArt.PhysicsLocalBounds(sprite, scale, info);

        var go = new GameObject(info.name);
        go.transform.SetParent(world.BoardBody.transform, false);
        go.transform.localPosition = new Vector3(
            localX,
            world.settings.boardHeight * 0.5f - phys.min.y + 0.02f,
            0f);
        go.transform.localRotation = Quaternion.identity;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 5;

        var building = go.AddComponent<Building>();
        building.Setup(info);
        return building;
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
            Enemy enemy = hit.GetComponent<Enemy>();
            if (building != null && building.IsResource)
                continue;
            if (building == null && (enemy == null || !enemy.IsMelee))
                continue;

            ColliderDistance2D dist = Physics2D.Distance(placed, hit);
            if (!dist.isOverlapped)
                continue;

            Vector2 push = dist.normal * (-dist.distance + extra);
            Rigidbody2D other = hit.attachedRigidbody;
            if (other == null || other == world.BoardBody)
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
            case "sunElectricity": return new Color(1f, 0.88f, 0.28f);
            case "furnace": return new Color(0.82f, 0.38f, 0.18f);
            case "wall": return new Color(0.55f, 0.55f, 0.58f);
            case "attack": return new Color(0.28f, 0.58f, 0.72f);
            case "coinMachine": return new Color(0.92f, 0.72f, 0.18f);
            case "resource": return new Color(0.58f, 0.5f, 0.42f);
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

        var list = CSVLoader.Instance.playerBuildingList;
        if (!Building.CoreExists() || list.Count > 0)
        {
            var hotStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter
            };
            hotStyle.normal.textColor = Color.black;
            hotStyle.hover.textColor = Color.black;

            string hotkeys;
            if (!Building.CoreExists())
            {
                hotkeys = "Place Core";
            }
            else
            {
                hotkeys = "";
                int count = list.Count < 9 ? list.Count : 9;
                for (int i = 0; i < count; i++)
                {
                    if (i > 0)
                        hotkeys += "    ";
                    hotkeys += (i + 1) + " " + list[i].name;
                }
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
