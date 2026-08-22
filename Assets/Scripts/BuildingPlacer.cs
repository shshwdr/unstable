using System.Collections.Generic;
using UnityEngine;

public class BuildingPlacer : MonoBehaviour
{
    static readonly Color GiveArrowColor = new Color(1f, 0.78f, 0.12f, 0.95f);
    static readonly Color TakeArrowColor = new Color(0.25f, 0.78f, 1f, 0.95f);

    BalanceWorld world;
    int selected = -1;
    bool demolishMode;
    bool buttonHold;
    bool dragging;
    Vector2 holdStartGui;
    Rect[] buttonRects;
    Rect barRect;
    const float DragThreshold = 12f;
    Transform ghost;
    Transform ghostVisual;
    SpriteRenderer ghostRenderer;
    LineRenderer radiusCircle;
    LineRenderer hoverRadius;
    TextMesh ghostName;
    TextMesh ghostWarning;
    SpriteRenderer ghostWarningIcon;
    Building hovered;
    Building demolishShake;
    Material lineMaterial;
    readonly List<Building> highlighted = new List<Building>();
    readonly List<LineRenderer> arrows = new List<LineRenderer>();
    int usedArrows;
    Sprite demolishIcon;
    Sprite goldIcon;
    Sprite cardSprite;
    int newBadgeLevel = -1;
    readonly HashSet<string> seenNewBuildings = new HashSet<string>();

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
        ghostName.characterSize = 0.12f;
        ghostName.color = Color.white;
        nameGo.GetComponent<MeshRenderer>().sortingOrder = 21;

        CreateGhostWarning();

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

        var hoverGo = new GameObject("HoverRadius");
        hoverGo.transform.SetParent(transform);
        hoverRadius = hoverGo.AddComponent<LineRenderer>();
        hoverRadius.useWorldSpace = true;
        hoverRadius.loop = true;
        hoverRadius.startWidth = 0.05f;
        hoverRadius.endWidth = 0.05f;
        hoverRadius.material = LineMaterial();
        hoverRadius.startColor = new Color(0.2f, 0.85f, 1f, 0.7f);
        hoverRadius.endColor = new Color(0.2f, 0.85f, 1f, 0.7f);
        hoverRadius.sortingOrder = 19;
        hoverRadius.enabled = false;

        RefreshGhost();
    }

    void OnDisable()
    {
        ClearLinkPreview();
        StopDemolishShake();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl))
            Building.ToggleResourceStock();

        if (world.IsGameOver || Camera.main == null)
        {
            ghost.gameObject.SetActive(false);
            hovered = null;
            ClearLinkPreview();
            StopDemolishShake();
            return;
        }

        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0f;

        var encounters = GetComponent<EncounterManager>();
        if (Input.GetMouseButtonDown(0) && encounters != null && encounters.IsPointerOnGate(worldPos))
        {
            encounters.TryRushAt(worldPos);
            ghost.gameObject.SetActive(false);
            hovered = null;
            ClearLinkPreview();
            StopDemolishShake();
            return;
        }

        var tutorial = TutorialManager.Instance;
        if ((tutorial != null && tutorial.BlocksInput) || world.IsAllClearPopup)
        {
            ghost.gameObject.SetActive(false);
            hovered = null;
            ClearLinkPreview();
            StopDemolishShake();
            return;
        }

        var list = BuildableList();
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!shift && Building.CoreExists() && list != null)
        {
            int hotCount = list.Count < 9 ? list.Count : 9;
            for (int i = 0; i < hotCount; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                    SelectClick(i);
            }

            if (DestroyVisible() && (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)))
                SelectDemolish();
        }

        hovered = FindBuildingAt(worldPos);
        RefreshDemolishShake();
        bool overBar = OverBuildBar();
        int hitButton = HitBuildButton();

        if (Input.GetMouseButtonDown(1) && (dragging || buttonHold || selected >= 0 || demolishMode))
        {
            ResetPlacement();
            return;
        }

        if (Building.CoreExists() && Input.GetMouseButtonDown(0) && hitButton >= 0)
            BeginHold(hitButton);

        if (buttonHold)
        {
            if (!dragging && Vector2.Distance(GuiMouse(), holdStartGui) >= DragThreshold)
                dragging = true;

            if (Input.GetMouseButtonUp(0))
            {
                if (dragging)
                {
                    if (demolishMode)
                    {
                        if (!overBar && CanDemolish(hovered))
                            Demolish(hovered);
                    }
                    else
                    {
                        BuildingInfo dragInfo = CurrentInfo();
                        if (!overBar && hovered == null && CanPlace(worldPos, dragInfo))
                            Place(worldPos, dragInfo);
                    }

                    ResetPlacement();
                }

                buttonHold = false;
            }
        }

        if (hovered != null && (!dragging || demolishMode))
        {
            ghost.gameObject.SetActive(false);
            UpdateLinkPreview(hovered.transform.position, hovered.Info, hovered);
            if (!dragging && demolishMode && Input.GetMouseButtonDown(0) && CanDemolish(hovered))
                Demolish(hovered);
            if (!dragging || demolishMode)
                return;
        }

        if (overBar && !dragging)
        {
            ghost.gameObject.SetActive(false);
            ClearLinkPreview();
            return;
        }

        if (demolishMode)
        {
            ghost.gameObject.SetActive(false);
            ClearLinkPreview();
            return;
        }

        BuildingInfo info = CurrentInfo();
        if (info == null)
        {
            ghost.gameObject.SetActive(false);
            ClearLinkPreview();
            return;
        }

        ghost.gameObject.SetActive(true);
        ghost.position = worldPos;
        UpdateLinkPreview(worldPos, info, null);
        UpdateGhostWarning(worldPos, info);

        bool canPlace = !overBar && CanPlace(worldPos, info);
        ghostRenderer.color = canPlace
            ? new Color(0.25f, 0.9f, 0.3f, 0.45f)
            : new Color(0.95f, 0.2f, 0.2f, 0.45f);

        if (!buttonHold && !dragging && Input.GetMouseButtonDown(0) && canPlace)
            Place(worldPos, info);
    }

    void BeginHold(int index)
    {
        var list = BuildableList();
        if (list != null && index == list.Count)
            SelectDemolish();
        else
        {
            selected = index;
            demolishMode = false;
        }

        buttonHold = true;
        dragging = false;
        holdStartGui = GuiMouse();
        RefreshGhost();
    }

    void SelectClick(int index)
    {
        selected = index;
        demolishMode = false;
        buttonHold = false;
        dragging = false;
        StopDemolishShake();
        RefreshGhost();
    }

    void SelectDemolish()
    {
        selected = -1;
        demolishMode = true;
        buttonHold = false;
        dragging = false;
        if (ghost != null)
            ghost.gameObject.SetActive(false);
        RefreshGhost();
    }

    public void ResetPlacement()
    {
        selected = -1;
        demolishMode = false;
        buttonHold = false;
        dragging = false;
        StopDemolishShake();
        if (ghost != null)
            ghost.gameObject.SetActive(false);
        RefreshGhost();
    }

    void RefreshDemolishShake()
    {
        Building want = demolishMode && CanDemolish(hovered) ? hovered : null;
        if (demolishShake == want)
            return;
        if (demolishShake != null)
            demolishShake.SetDeleteShake(false);
        demolishShake = want;
        if (demolishShake != null)
            demolishShake.SetDeleteShake(true);
    }

    void StopDemolishShake()
    {
        if (demolishShake == null)
            return;
        demolishShake.SetDeleteShake(false);
        demolishShake = null;
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

        var list = BuildableList();
        if (list == null || selected < 0 || selected >= list.Count)
            return null;
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
        Vector2 scale = BuildingArt.VisualScale(info);
        ghostVisual.localScale = new Vector3(scale.x, scale.y, 1f);
        ghostName.text = info.name;
        SetCircle(info.radius);
        LayoutGhostWarning(info);
    }

    void CreateGhostWarning()
    {
        Color warningRed = new Color(1f, 0.23f, 0.23f, 1f);
        var textGo = new GameObject("Warning");
        textGo.transform.SetParent(ghost);
        textGo.transform.localPosition = new Vector3(0f, 0.5f, -0.1f);
        ghostWarning = textGo.AddComponent<TextMesh>();
        ghostWarning.anchor = TextAnchor.LowerCenter;
        ghostWarning.alignment = TextAlignment.Center;
        ghostWarning.fontSize = 48;
        ghostWarning.characterSize = 0.1f;
        ghostWarning.color = warningRed;
        ghostWarning.richText = false;
        textGo.GetComponent<MeshRenderer>().sortingOrder = 22;
        textGo.SetActive(false);

        var iconGo = new GameObject("WarningIcon");
        iconGo.transform.SetParent(ghost);
        iconGo.transform.localPosition = new Vector3(0f, 0.5f, -0.1f);
        ghostWarningIcon = iconGo.AddComponent<SpriteRenderer>();
        ghostWarningIcon.sortingOrder = 23;
        ghostWarningIcon.color = warningRed;
        Sprite sprite = Resources.Load<Sprite>("warning");
        ghostWarningIcon.sprite = sprite;
        if (sprite != null)
        {
            float spriteSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            float scale = spriteSize > 0.0001f ? 0.42f / spriteSize : 1f;
            iconGo.transform.localScale = new Vector3(scale, scale, 1f);
        }

        iconGo.SetActive(false);
    }

    void LayoutGhostWarning(BuildingInfo info)
    {
        if (ghostWarning == null || info == null)
            return;

        float top = BuildingArt.PhysicsLocalBounds(info).max.y;
        ghostWarning.transform.localPosition = new Vector3(0f, top + 0.08f, -0.1f);
    }

    void UpdateGhostWarning(Vector2 position, BuildingInfo info)
    {
        if (ghostWarning == null || info == null)
            return;

        var sb = new System.Text.StringBuilder();
        if (world != null && info.cost > 0 && world.DisplayGold < info.cost)
            sb.Append("Need ").Append(info.cost).Append(" gold");
        if (!InRequiredResourceRange(position, info))
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append("Need ").Append(BuildingInfo.DisplayName(info.requireResource));
        }
        if (Building.IsTopBlockedAt(position, info))
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append("Keep the top clear to work");
        }

        List<ResourceAmount> needs = info.ConsumeList;
        if (needs != null)
        {
            for (int i = 0; i < needs.Count; i++)
            {
                if (Building.HasProviderCovering(position, needs[i].id))
                    continue;
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append("Need ").Append(needs[i].id);
                if (needs[i].amount > 1)
                    sb.Append(':').Append(needs[i].amount);
            }
        }

        bool show = sb.Length > 0;
        ghostWarning.text = show ? sb.ToString() : "";
        ghostWarning.gameObject.SetActive(show);
        if (ghostWarningIcon != null)
            ghostWarningIcon.gameObject.SetActive(show);
        AlignGhostWarningIcon();
    }

    void AlignGhostWarningIcon()
    {
        if (ghostWarningIcon == null || ghostWarning == null || !ghostWarningIcon.gameObject.activeSelf)
            return;

        ghostWarningIcon.transform.rotation = Quaternion.identity;
        ghostWarning.transform.rotation = Quaternion.identity;
        var textRenderer = ghostWarning.GetComponent<Renderer>();
        if (textRenderer == null)
            return;

        Bounds textBounds = textRenderer.bounds;
        float iconW = ghostWarningIcon.bounds.size.x;
        if (iconW < 0.01f)
            iconW = 0.4f;
        ghostWarningIcon.transform.position = new Vector3(
            textBounds.min.x - iconW * 0.5f - 0.06f,
            textBounds.center.y,
            textBounds.center.z);
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

    void UpdateLinkPreview(Vector3 origin, BuildingInfo info, Building source)
    {
        ClearHighlights();
        usedArrows = 0;

        bool hovering = source != null;
        if (hoverRadius != null)
            hoverRadius.enabled = false;

        RefreshRadiusVisibility(hovering ? null : info, hovering ? source : hovered);

        if (info == null)
        {
            HideUnusedArrows();
            return;
        }

        bool canGive = info.radius > 0f && info.HasProvideDisplay;
        bool canTake = info.HasConsumeDisplay;
        bool canTakeNode = !string.IsNullOrEmpty(info.requireResource);
        bool canTakeAttackStock = info.IsCoinMachine && info.radius > 0f;
        bool canGiveAttackStock = info.IsAttack;
        if (!canGive && !canTake && !canTakeNode && !canTakeAttackStock && !canGiveAttackStock)
        {
            HideUnusedArrows();
            return;
        }

        for (int i = 0; i < Building.All.Count; i++)
        {
            Building other = Building.All[i];
            if (other == null || other == source || other.Info == null)
                continue;
            if (other.Health != null && !other.Health.IsAlive)
                continue;

            float dist = Vector2.Distance(origin, other.transform.position);
            bool give = canGive && dist <= info.radius &&
                BuildingInfo.ResourceListsOverlap(info.ProvideList, other.Info.ConsumeList);
            bool take = canTake && other.Info.radius > 0f && dist <= other.Info.radius &&
                BuildingInfo.ResourceListsOverlap(other.Info.ProvideList, info.ConsumeList);
            bool takeNode = canTakeNode && other.IsResource && other.Info.identifier == info.requireResource &&
                other.Info.radius > 0f && dist <= other.Info.radius;
            bool takeAttack = canTakeAttackStock && other.Info.IsAttack && dist <= info.radius;
            bool giveAttack = canGiveAttackStock && other.Info.IsCoinMachine && other.Info.radius > 0f &&
                dist <= other.Info.radius;
            if (!give && !take && !takeNode && !takeAttack && !giveAttack)
                continue;

            other.SetLinkPulse(true);
            highlighted.Add(other);

            if (give || giveAttack)
                AddArrow(origin, other.transform.position, GiveArrowColor, 0.08f);
            if (take || takeNode || takeAttack)
                AddArrow(other.transform.position, origin, TakeArrowColor, -0.08f);
        }

        HideUnusedArrows();
    }

    void SetHoverRadius(Vector3 center, float radius)
    {
        if (hoverRadius == null)
            return;
        if (radius <= 0f)
        {
            hoverRadius.enabled = false;
            return;
        }

        hoverRadius.enabled = true;
        const int segments = 48;
        hoverRadius.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            hoverRadius.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
    }

    void AddArrow(Vector3 from, Vector3 to, Color color, float side)
    {
        Vector3 delta = to - from;
        float mag = delta.magnitude;
        if (mag < 0.15f)
            return;

        Vector3 dir = delta / mag;
        Vector3 n = new Vector3(-dir.y, dir.x, 0f);
        from += n * side;
        to += n * side;
        delta = to - from;
        mag = delta.magnitude;
        dir = delta / mag;

        float inset = Mathf.Min(0.45f, mag * 0.22f);
        Vector3 start = from + dir * inset;
        Vector3 end = to - dir * inset;
        float head = Mathf.Min(0.28f, mag * 0.18f);
        Vector3 left = end - dir * head + n * (head * 0.55f);
        Vector3 right = end - dir * head - n * (head * 0.55f);

        LineRenderer lr = NextArrow();
        lr.enabled = true;
        lr.startColor = color;
        lr.endColor = color;
        lr.positionCount = 6;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.SetPosition(2, left);
        lr.SetPosition(3, end);
        lr.SetPosition(4, right);
        lr.SetPosition(5, end);
    }

    LineRenderer NextArrow()
    {
        if (usedArrows < arrows.Count)
            return arrows[usedArrows++];

        var go = new GameObject("LinkArrow");
        go.transform.SetParent(transform);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.startWidth = 0.055f;
        lr.endWidth = 0.055f;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.material = LineMaterial();
        lr.sortingOrder = 18;
        arrows.Add(lr);
        usedArrows++;
        return lr;
    }

    void HideUnusedArrows()
    {
        for (int i = usedArrows; i < arrows.Count; i++)
        {
            if (arrows[i] != null)
                arrows[i].enabled = false;
        }
    }

    void ClearHighlights()
    {
        for (int i = 0; i < highlighted.Count; i++)
        {
            if (highlighted[i] != null)
                highlighted[i].SetLinkPulse(false);
        }
        highlighted.Clear();
    }

    void ClearLinkPreview()
    {
        ClearHighlights();
        usedArrows = 0;
        HideUnusedArrows();
        if (hoverRadius != null)
            hoverRadius.enabled = false;
        RefreshRadiusVisibility(null, null);
    }

    void RefreshRadiusVisibility(BuildingInfo placing, Building hoveredBuilding)
    {
        for (int i = 0; i < Building.All.Count; i++)
        {
            Building building = Building.All[i];
            if (building == null || building.Info == null)
                continue;

            bool show = hoveredBuilding == building && building.Info.radius > 0f;
            if (!show && placing != null)
                show = ProvidesForPlacement(building, placing);
            building.SetRadiusVisible(show);
        }
    }

    static bool ProvidesForPlacement(Building building, BuildingInfo placing)
    {
        if (building == null || building.Info == null || placing == null || building.Info.radius <= 0f)
            return false;

        if (building.IsResource && !string.IsNullOrEmpty(placing.requireResource) &&
            building.Info.identifier == placing.requireResource)
            return true;

        return BuildingInfo.ResourceListsOverlap(building.Info.ProvideList, placing.ConsumeList);
    }

    Material LineMaterial()
    {
        if (lineMaterial == null)
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
        return lineMaterial;
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

    static bool CanDemolish(Building building)
    {
        return building != null && building.Info != null && !building.IsResource && !building.IsCore;
    }

    static int DemolishRefund(Building building)
    {
        if (building == null || building.Info == null)
            return 0;
        return building.Info.cost / 2;
    }

    void Demolish(Building building)
    {
        if (!CanDemolish(building))
            return;

        world.AddGold(DemolishRefund(building));
        if (demolishShake == building)
            StopDemolishShake();
        Destroy(building.gameObject);
        hovered = null;
        if (TutorialManager.Instance != null)
            TutorialManager.Instance.NotifyBuildingDestroyed();
    }

    bool CanPlace(Vector2 position, BuildingInfo info)
    {
        if (info == null || info.IsResource)
            return false;
        if (info.IsCore && Building.CoreExists())
            return false;
        if (!info.IsCore && !Building.CoreExists())
            return false;
        if (!info.IsCore)
        {
            var list = BuildableList();
            if (list == null || !list.Contains(info))
                return false;
        }
        if (world.DisplayGold < info.cost)
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
        if (!info.IsCore)
            ResetPlacement();
        else
            RefreshGhost();
    }

    public void SpawnStartingResources()
    {
        LevelInfo level = world.CurrentLevel;
        if (level == null || level.mineDefs == null)
            return;

        for (int i = 0; i < level.mineDefs.Count; i++)
        {
            MineDef mine = level.mineDefs[i];
            WorldPlatform platform = world.GetPlatform(mine.platformName);
            if (platform == null || platform.Body == null)
            {
                Debug.LogError("未找到 mines 对应的 platform: " + mine.platformName);
                continue;
            }

            BuildingInfo info;
            if (!CSVLoader.Instance.buildingInfoMap.TryGetValue(mine.resourceId, out info) || info == null)
            {
                Debug.LogError("未找到 identifier 为 " + mine.resourceId + " 的资源建筑");
                continue;
            }

            Bounds bounds = BuildingArt.PhysicsLocalBounds(info);
            SpawnResourceLocal(platform, RandomPlatformTopLocalX(platform, bounds.size, mine.side), info);
        }

        RefreshGhost();
    }

    float RandomPlatformTopLocalX(WorldPlatform platform, Vector2 size, string side)
    {
        float left = -platform.Pivot * platform.Width;
        float right = (1f - platform.Pivot) * platform.Width;
        float center = 0f;
        float margin = size.x * 0.5f + 0.2f;
        float min;
        float max;
        if (side == "left")
        {
            min = left + margin;
            max = center - margin;
        }
        else if (side == "right")
        {
            min = center + margin;
            max = right - margin;
        }
        else
        {
            min = left + margin;
            max = right - margin;
        }

        if (min > max)
        {
            if (side == "left")
                return (left + center) * 0.5f;
            if (side == "right")
                return (center + right) * 0.5f;
            return center;
        }
        return Random.Range(min, max);
    }

    public Building Spawn(Vector3 position, BuildingInfo info)
    {
        if (info.IsResource)
            return null;

        var go = new GameObject(info.name);
        go.transform.position = position;

        Sprite sprite = BuildingArt.ResolveSprite(info, ColorFor(info.type), false);
        Vector2 scale = BuildingArt.VisualScale(info);
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
        body.angularDrag = world.settings.buildingAngularDrag;
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

    Building SpawnResourceLocal(WorldPlatform platform, float localX, BuildingInfo info)
    {
        if (platform == null || platform.Body == null)
            return null;

        Sprite sprite = BuildingArt.ResolveSprite(info, ColorFor(info.type), true);
        Vector2 scale = BuildingArt.VisualScale(info);
        Bounds phys = BuildingArt.PhysicsLocalBounds(sprite, scale, info);

        float height = world.settings != null ? world.settings.boardHeight : 0.35f;
        var go = new GameObject(info.name);
        go.transform.SetParent(platform.Body.transform, false);
        go.transform.localPosition = new Vector3(
            localX,
            height * 0.5f - phys.min.y + 0.02f,
            0f);
        go.transform.localRotation = Quaternion.identity;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 1;

        var building = go.AddComponent<Building>();
        building.Setup(info);
        return building;
    }

    void SeparateFromBoard(Collider2D placed)
    {
        if (world.Platforms == null)
            return;

        float extra = world.settings.overlapPush;
        for (int i = 0; i < world.Platforms.Count; i++)
        {
            WorldPlatform platform = world.Platforms[i];
            if (platform == null || platform.Collider == null || platform.Body == null)
                continue;

            ColliderDistance2D dist = Physics2D.Distance(placed, platform.Collider);
            if (!dist.isOverlapped)
                continue;

            Vector2 push = -dist.normal * (-dist.distance + extra);
            Vector2 sky = platform.Body.transform.up;
            if (Vector2.Dot(sky, Vector2.up) < 0f)
                sky = -sky;
            if (Vector2.Dot(push, sky) < 0f)
                push = sky * (-dist.distance + extra);

            placed.attachedRigidbody.position += push;
        }
    }

    void PushOverlappingBuildings(Collider2D placed)
    {
        Collider2D[] hits = Physics2D.OverlapBoxAll(placed.bounds.center, placed.bounds.size, 0f);
        float extra = world.settings.overlapPush;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null || hit == placed || world.IsPlatformCollider(hit))
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
            if (other == null || world.IsPlatformBody(other))
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

    List<BuildingInfo> BuildableList()
    {
        if (world != null)
            return world.CurrentPlayerBuildings();
        return CSVLoader.Instance.playerBuildingList;
    }

    void SyncNewBadgeLevel()
    {
        int idx = world != null ? world.LevelIndex : -1;
        if (idx == newBadgeLevel)
            return;
        newBadgeLevel = idx;
        seenNewBuildings.Clear();
    }

    bool ShouldShowNewBadge(BuildingInfo info, int buttonIndex)
    {
        if (info == null || world == null || !world.IsNewThisLevel(info))
            return false;

        SyncNewBadgeLevel();
        if (HitBuildButton() == buttonIndex)
        {
            bool blocked = TutorialManager.Instance != null && TutorialManager.Instance.BlocksInput;
            if (!blocked)
            {
                seenNewBuildings.Add(info.identifier);
                return false;
            }
        }

        return !seenNewBuildings.Contains(info.identifier);
    }

    static void DrawNewBadge(Rect button)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperRight
        };
        style.normal.textColor = new Color(0.12f, 0.55f, 0.18f);
        style.hover.textColor = style.normal.textColor;
        GUI.Label(new Rect(button.x, button.y + 4f, button.width - 8f, 28f), "NEW", style);
    }

    static bool DestroyVisible()
    {
        return TutorialManager.Instance == null || TutorialManager.Instance.DestroyButtonVisible;
    }

    static Vector2 GuiMouse()
    {
        return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
    }

    bool OverBuildBar()
    {
        return barRect.width > 0f && barRect.Contains(GuiMouse());
    }

    int HitBuildButton()
    {
        if (buttonRects == null)
            return -1;

        Vector2 mouse = GuiMouse();
        for (int i = 0; i < buttonRects.Length; i++)
        {
            if (buttonRects[i].Contains(mouse))
                return i;
        }

        return -1;
    }

    void OnGUI()
    {
        bool blocked = TutorialManager.Instance != null && TutorialManager.Instance.BlocksInput;
        DrawBuildBar();

        string panelText = ButtonHoverPanelText();
        if (!blocked)
        {
            DrawDemolishHoverPrompt();
            if (string.IsNullOrEmpty(panelText))
            {
                if (hovered != null && hovered.Info != null && (!dragging || demolishMode))
                    panelText = hovered.HoverInfo();
                else if (ghost != null && ghost.gameObject.activeSelf)
                {
                    BuildingInfo dragInfo = CurrentInfo();
                    if (dragInfo != null)
                        panelText = Building.FormatPanelInfo(dragInfo, null);
                }
            }
        }

        if (string.IsNullOrEmpty(panelText))
            return;

        float panelW = 460f;
        float panelH = 280f;
        float panelY = barRect.height > 0f
            ? Mathf.Max(8f, barRect.y - panelH - 8f)
            : Screen.height - panelH;
        var panel = new Rect(Screen.width - panelW, panelY, panelW, panelH);
        GUI.Box(panel, "");

        var panelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            wordWrap = true
        };
        panelStyle.normal.textColor = Color.black;
        panelStyle.hover.textColor = Color.black;
        GUI.Label(new Rect(panel.x + 12f, panel.y + 10f, panel.width - 20f, panel.height - 16f),
            panelText, panelStyle);
    }

    string ButtonHoverPanelText()
    {
        int index = HitBuildButton();
        if (index < 0)
            return null;

        var list = BuildableList();
        if (list == null || index < 0 || index >= list.Count)
            return null;
        return Building.FormatPanelInfo(list[index], null);
    }

    void DrawDemolishHoverPrompt()
    {
        if (!demolishMode || hovered == null || !CanDemolish(hovered) || Camera.main == null)
            return;

        int refund = DemolishRefund(hovered);
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 44,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
        style.normal.textColor = new Color(0.85f, 0.1f, 0.1f);
        style.hover.textColor = style.normal.textColor;

        Vector3 sp = Camera.main.WorldToScreenPoint(hovered.transform.position);
        float x = sp.x;
        float y = Screen.height - sp.y;
        GUI.Label(new Rect(x - 420f, y - 110f, 840f, 90f),
            "Left click to destroy (get " + refund + " coin)", style);
    }

    void DrawBuildBar()
    {
        float btnH = 230f;
        float barY = Screen.height - 16f - btnH;
        float goldH = 56f;
        float goldY = barY - goldH - 8f;
        var goldStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            fontStyle = FontStyle.Bold
        };
        goldStyle.normal.textColor = Color.black;
        goldStyle.hover.textColor = Color.black;
        DrawGoldAmount(new Rect(16f, goldY, 280f, goldH), world.DisplayGold, goldStyle, 40f);

        var list = BuildableList();
        bool coreExists = Building.CoreExists();
        int buildCount = list != null ? list.Count : 0;
        if (!coreExists && buildCount <= 0)
        {
            barRect = new Rect(0f, goldY - 8f, Screen.width, Screen.height - (goldY - 8f));
            buttonRects = null;
            var hotStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 36,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            hotStyle.normal.textColor = Color.black;
            hotStyle.hover.textColor = Color.black;
            GUI.Label(new Rect(0f, barY + 20f, Screen.width, 72f), "Place Core", hotStyle);
            return;
        }

        bool showDestroy = coreExists && DestroyVisible();
        int n = buildCount + (showDestroy ? 1 : 0);
        if (n <= 0)
        {
            barRect = new Rect(0f, goldY - 8f, Screen.width, Screen.height - (goldY - 8f));
            buttonRects = null;
            return;
        }
        float gap = 8f;
        float sidePad = 24f;
        float areaW = Mathf.Max(200f, Screen.width - sidePad * 2f);
        float btnW = Mathf.Clamp((areaW - gap * (n - 1)) / n, 160f, 280f) * 0.9f;
        float total = n * btnW + (n - 1) * gap;
        float startX = (Screen.width - total) * 0.5f;

        barRect = new Rect(0f, goldY - 8f, Screen.width, Screen.height - (goldY - 8f));
        if (buttonRects == null || buttonRects.Length != n)
            buttonRects = new Rect[n];

        var captionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
        captionStyle.normal.textColor = new Color(0.22f, 0.14f, 0.1f);
        captionStyle.hover.textColor = captionStyle.normal.textColor;
        var hotkeyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft
        };
        hotkeyStyle.normal.textColor = new Color(0.22f, 0.14f, 0.1f);
        hotkeyStyle.hover.textColor = hotkeyStyle.normal.textColor;

        for (int i = 0; i < n; i++)
        {
            var r = new Rect(startX + i * (btnW + gap), barY, btnW, btnH);
            buttonRects[i] = r;

            bool isDemolish = showDestroy && i == buildCount;
            Color oldGui = GUI.color;
            if (isDemolish)
                GUI.color = demolishMode
                    ? new Color(1f, 0.55f, 0.45f)
                    : new Color(1f, 0.88f, 0.88f);
            else if (selected == i)
                GUI.color = new Color(1f, 0.95f, 0.72f);
            else if (list[i].cost > world.DisplayGold)
                GUI.color = new Color(0.78f, 0.78f, 0.78f);
            else
                GUI.color = Color.white;

            DrawCard(r);
            GUI.color = oldGui;

            bool wasEnabled = GUI.enabled;
            GUI.enabled = coreExists;

            GUI.Label(new Rect(r.x + 10f, r.y + 6f, 36f, 24f),
                isDemolish ? "0" : (i + 1).ToString(), hotkeyStyle);

            float footerH = r.height * 0.22f;
            float nameH = 36f;
            float imgPad = 14f;
            var imgRect = new Rect(
                r.x + imgPad,
                r.y + 32f,
                r.width - imgPad * 2f,
                r.height - footerH - nameH - 36f);
            var nameRect = new Rect(r.x + 8f, r.y + r.height - footerH - nameH, r.width - 16f, nameH);
            var costRect = new Rect(r.x + 8f, r.y + r.height - footerH, r.width - 16f, footerH);

            if (isDemolish)
            {
                DrawSprite(imgRect, DemolishIcon());
                captionStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(nameRect, "Destroy", captionStyle);
            }
            else
            {
                DrawBuildingIcon(imgRect, list[i]);
                captionStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(nameRect, list[i].name, captionStyle);
                DrawCost(costRect, list[i].cost, captionStyle);
                if (ShouldShowNewBadge(list[i], i))
                    DrawNewBadge(r);
            }

            GUI.enabled = wasEnabled;
        }

        if (!coreExists)
        {
            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 32,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            hintStyle.normal.textColor = Color.black;
            hintStyle.hover.textColor = Color.black;
            GUI.Label(new Rect(0f, goldY - 40f, Screen.width, 36f), "Place Core", hintStyle);
            barRect = new Rect(0f, goldY - 48f, Screen.width, Screen.height - (goldY - 48f));
        }
    }

    void DrawCost(Rect rect, int cost, GUIStyle nameStyle)
    {
        var costStyle = new GUIStyle(nameStyle)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleLeft
        };
        float goldSize = 22f;
        string costText = cost.ToString();
        float costW = costStyle.CalcSize(new GUIContent(costText)).x;
        float totalW = goldSize + 4f + costW;
        float x = rect.x + Mathf.Max(0f, (rect.width - totalW) * 0.5f);
        float y = rect.y;
        float rowH = rect.height;
        DrawGoldIcon(new Rect(x, y + (rowH - goldSize) * 0.5f, goldSize, goldSize));
        GUI.Label(new Rect(x + goldSize + 4f, y, costW + 8f, rowH), costText, costStyle);
    }

    void DrawGoldAmount(Rect rect, int amount, GUIStyle style, float iconSize)
    {
        string text = amount.ToString();
        float textW = style.CalcSize(new GUIContent(text)).x;
        DrawGoldIcon(new Rect(rect.x, rect.y + (rect.height - iconSize) * 0.5f, iconSize, iconSize));
        GUI.Label(new Rect(rect.x + iconSize + 6f, rect.y, textW + 20f, rect.height), text, style);
    }

    Sprite CardSprite()
    {
        if (cardSprite == null)
            cardSprite = WorldArt.Load("card");
        return cardSprite;
    }

    void DrawCard(Rect rect)
    {
        Sprite sprite = CardSprite();
        if (sprite != null)
        {
            DrawSpriteFill(rect, sprite);
            return;
        }

        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
    }

    static void DrawSpriteFill(Rect position, Sprite sprite)
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

    Sprite GoldIcon()
    {
        if (goldIcon == null)
            goldIcon = ItemArt.Load("coin");
        return goldIcon;
    }

    void DrawGoldIcon(Rect rect)
    {
        Sprite sprite = GoldIcon();
        if (sprite != null)
            DrawSprite(rect, sprite);
    }

    Sprite DemolishIcon()
    {
        if (demolishIcon == null)
            demolishIcon = Resources.Load<Sprite>("warning");
        return demolishIcon;
    }

    void DrawBuildingIcon(Rect rect, BuildingInfo info)
    {
        Sprite sprite = BuildingArt.LoadSprite(info);
        if (sprite != null)
        {
            DrawSprite(rect, sprite);
            return;
        }

        Color old = GUI.color;
        GUI.color = ColorFor(info != null ? info.type : "");
        GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.ScaleToFit, false);
        GUI.color = old;
    }

    static void DrawSprite(Rect position, Sprite sprite)
    {
        if (sprite == null || sprite.texture == null || position.width <= 1f || position.height <= 1f)
            return;

        Texture2D tex = sprite.texture;
        Rect sr = sprite.rect;
        if (sr.width < 1f || sr.height < 1f)
            return;

        float aspect = sr.width / sr.height;
        float w = position.width;
        float h = position.height;
        if (w / h > aspect)
        {
            w = h * aspect;
            position.x += (position.width - w) * 0.5f;
            position.width = w;
        }
        else
        {
            h = w / aspect;
            position.y += (position.height - h) * 0.5f;
            position.height = h;
        }

        var uv = new Rect(sr.x / tex.width, sr.y / tex.height, sr.width / tex.width, sr.height / tex.height);
        Color old = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTextureWithTexCoords(position, tex, uv);
        GUI.color = old;
    }
}
