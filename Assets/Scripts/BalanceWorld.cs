using System.Collections.Generic;
using UnityEngine;

public class BalanceWorld : MonoBehaviour
{
    public GameBalanceSettings settings;

    public List<WorldPlatform> Platforms { get { return platforms; } }
    public PhysicsMaterial2D SharedMaterial { get; private set; }
    public Collider2D BoardCollider { get; private set; }
    public Rigidbody2D BoardBody { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool IsVictory { get; private set; }
    public bool IsRestarting { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsTutorialPaused { get; private set; }
    public bool HasEnded { get { return IsGameOver || IsVictory; } }
    public bool IsAllClearPopup { get { return showAllClearPopup; } }
    public int LevelIndex { get { return levelIndex; } }
    public float Gold { get; private set; }
    public int DisplayGold { get { return Mathf.FloorToInt(Gold); } }

    public const float GoldStart = 100f;
    public float BoardTilt { get; private set; }

    float tiltTimer;
    TextMesh tiltHint;
    const float TiltHintAngle = 20f;
    const float FulcrumScale = 1.1f;
    static readonly Color FulcrumColor = new Color(0.18f, 0.18f, 0.2f);
    float appliedFulcrumTopWidth = float.NaN;
    int levelIndex;
    bool showAllClearPopup;
    readonly List<WorldPlatform> platforms = new List<WorldPlatform>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindObjectOfType<BalanceWorld>() != null)
            return;

        var go = new GameObject("BalanceGame");
        go.AddComponent<BalanceWorld>();
        go.AddComponent<BuildingPlacer>();
        go.AddComponent<EncounterManager>();
        go.AddComponent<CheatManager>();
        go.AddComponent<TutorialManager>();
    }

    void Awake()
    {
        CSVLoader.Instance.Init();

        if (settings == null)
            settings = Resources.Load<GameBalanceSettings>("GameBalanceSettings");
        if (settings == null)
            settings = ScriptableObject.CreateInstance<GameBalanceSettings>();

        SharedMaterial = new PhysicsMaterial2D("Shared")
        {
            friction = settings.friction,
            bounciness = settings.bounciness
        };

        CreateTiltHint();
        RebuildPlatforms();
        FitCamera();
        ApplyPhysics();
        Gold = GoldStart;
    }

    void Start()
    {
        var placer = GetComponent<BuildingPlacer>();
        if (placer != null)
            placer.SpawnStartingResources();
    }

    void Update()
    {
        if (IsRestarting)
            IsRestarting = false;

        HandleCameraZoom();

        var tutorial = GetComponent<TutorialManager>();
        bool blocked = tutorial != null && tutorial.BlocksInput;

        if (!blocked && !IsGameOver && Input.GetKeyDown(KeyCode.Space))
            SetPaused(!IsPaused);

        if (IsGameOver && Input.GetKeyDown(KeyCode.R))
            Restart();

        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (!blocked && enter)
        {
            if (IsVictory && HasNextLevel)
            {
                GoToNextLevel();
                return;
            }

            bool tutorialFinish = !showAllClearPopup && !IsGameOver
                && tutorial != null && tutorial.AllowEnterToFinish;
            if (tutorialFinish && HasNextLevel)
            {
                GoToNextLevel();
                return;
            }
        }

        if (IsPaused || IsTutorialPaused)
            return;

        ApplyPhysics();
        TickGold();
        CheckFail();
        CheckVictory();
        RefreshTiltHint();
    }

    void FixedUpdate()
    {
        if (settings == null)
            return;

        for (int i = 0; i < platforms.Count; i++)
        {
            WorldPlatform platform = platforms[i];
            if (platform == null || !platform.IsBalance || platform.Body == null)
                continue;

            float angle = Mathf.DeltaAngle(0f, platform.Body.rotation);
            if (Mathf.Abs(angle) >= settings.restoreAngle)
                continue;

            float torque = -angle * settings.restoreStrength
                           - platform.Body.angularVelocity * settings.restoreDamping;
            platform.Body.AddTorque(torque);
        }
    }

    public void ApplyPhysics()
    {
        if (settings == null)
            return;

        Physics2D.gravity = new Vector2(0f, settings.gravity);
        SharedMaterial.friction = settings.friction;
        SharedMaterial.bounciness = settings.bounciness;

        for (int i = 0; i < platforms.Count; i++)
        {
            WorldPlatform platform = platforms[i];
            if (platform == null || !platform.IsBalance || platform.Body == null)
                continue;
            platform.Body.mass = settings.boardMass;
            platform.Body.angularDrag = settings.boardAngularDrag;
            platform.Body.centerOfMass = Vector2.zero;
        }

        float buildingAngularDrag = settings.buildingAngularDrag;
        for (int i = 0; i < Building.All.Count; i++)
        {
            Building building = Building.All[i];
            if (building == null)
                continue;
            Rigidbody2D body = building.GetComponent<Rigidbody2D>();
            if (body != null)
                body.angularDrag = buildingAngularDrag;
        }

        RefreshFulcrumVisuals();
    }

    void RefreshFulcrumVisuals()
    {
        float topWidth = settings.fulcrumTopWidth;
        if (Mathf.Approximately(appliedFulcrumTopWidth, topWidth))
            return;

        appliedFulcrumTopWidth = topWidth;
        Sprite sprite = ShapeUtil.Trapezoid(FulcrumColor, topWidth);
        for (int i = 0; i < platforms.Count; i++)
        {
            WorldPlatform platform = platforms[i];
            if (platform == null || platform.Fulcrum == null)
                continue;
            var renderer = platform.Fulcrum.GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.sprite = sprite;
        }
    }

    public bool SpendGold(int amount)
    {
        if (amount <= 0)
            return true;
        if (DisplayGold < amount)
            return false;
        Gold -= amount;
        return true;
    }

    public void AddGold(float amount)
    {
        if (amount <= 0f)
            return;
        Gold += amount;
    }

    public LevelInfo CurrentLevel
    {
        get
        {
            var list = CSVLoader.Instance.levelList;
            if (list == null || list.Count == 0 || levelIndex < 0 || levelIndex >= list.Count)
                return null;
            return list[levelIndex];
        }
    }

    public bool HasNextLevel
    {
        get
        {
            var list = CSVLoader.Instance.levelList;
            return list != null && levelIndex + 1 < list.Count;
        }
    }

    public List<BuildingInfo> CurrentPlayerBuildings()
    {
        var tutorial = GetComponent<TutorialManager>();
        if (tutorial != null)
            return tutorial.BuildableBuildings;
        LevelInfo level = CurrentLevel;
        if (level != null)
            return level.playerBuildings;
        return CSVLoader.Instance.playerBuildingList;
    }

    public bool IsNewThisLevel(BuildingInfo info)
    {
        if (info == null || string.IsNullOrEmpty(info.identifier))
            return false;

        var tutorial = GetComponent<TutorialManager>();
        if (tutorial != null && tutorial.IsNewlyAdded(info.identifier))
            return true;

        var list = CSVLoader.Instance.levelList;
        if (list == null || levelIndex <= 0 || levelIndex >= list.Count)
            return false;

        LevelInfo prev = list[levelIndex - 1];
        if (prev == null || prev.playerBuildings == null)
            return true;

        for (int i = 0; i < prev.playerBuildings.Count; i++)
        {
            BuildingInfo building = prev.playerBuildings[i];
            if (building != null && building.identifier == info.identifier)
                return false;
        }

        return true;
    }

    public void GoToNextLevel()
    {
        if (!HasNextLevel)
            return;
        levelIndex++;
        Restart();
    }

    public void JumpToLevel(int index)
    {
        var list = CSVLoader.Instance.levelList;
        if (list == null || index < 0 || index >= list.Count)
            return;
        levelIndex = index;
        Restart();
    }

    public void SetPaused(bool paused)
    {
        if (IsGameOver)
            paused = false;
        IsPaused = paused;
        ApplyTimeScale();
    }

    public void SetTutorialPaused(bool paused)
    {
        if (IsGameOver)
            paused = false;
        IsTutorialPaused = paused;
        ApplyTimeScale();
    }

    void ApplyTimeScale()
    {
        Time.timeScale = !IsGameOver && (IsPaused || IsTutorialPaused) ? 0f : 1f;
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
    }

    public void Fail()
    {
        if (HasEnded)
            return;
        IsGameOver = true;
        var tutorial = GetComponent<TutorialManager>();
        if (tutorial != null)
            tutorial.StopForGameEnd();
        SetTutorialPaused(false);
        SetPaused(false);
        Projectile.ClearAll();
        ItemFx.ClearAll();
    }

    void TickGold()
    {
        if (IsGameOver)
            return;
        LevelInfo level = CurrentLevel;
        float rate = level != null ? level.coinIncrease : 1f;
        Gold += rate * Time.deltaTime;
    }

    void CheckVictory()
    {
        if (HasEnded)
            return;

        var tutorial = GetComponent<TutorialManager>();
        if (tutorial != null && tutorial.IsPlaying)
            return;

        var encounters = GetComponent<EncounterManager>();
        if (encounters == null || !encounters.IsComplete)
            return;
        if (Enemy.All.Count > 0)
            return;

        IsVictory = true;
        showAllClearPopup = true;
        SetTutorialPaused(false);
        SetPaused(false);
    }

    public void Restart()
    {
        SetTutorialPaused(false);
        SetPaused(false);
        showAllClearPopup = false;
        IsRestarting = true;
        Projectile.ClearAll();
        ItemFx.ClearAll();
        Enemy.ClearAll();

        Building[] buildings = FindObjectsOfType<Building>();
        for (int i = 0; i < buildings.Length; i++)
        {
            buildings[i].gameObject.SetActive(false);
            Destroy(buildings[i].gameObject);
        }

        RebuildPlatforms();

        IsGameOver = false;
        IsVictory = false;
        tiltTimer = 0f;
        Gold = GoldStart;

        var encounters = GetComponent<EncounterManager>();
        if (encounters != null)
            encounters.Restart();

        var placer = GetComponent<BuildingPlacer>();
        if (placer != null)
        {
            placer.ResetPlacement();
            placer.SpawnStartingResources();
        }

        var tutorial = GetComponent<TutorialManager>();
        if (tutorial != null)
            tutorial.OnLevelRestart();
    }

    void CheckFail()
    {
        if (HasEnded)
            return;

        BoardTilt = MaxBalanceTilt();
        if (BoardBody == null && platforms.Count == 0)
            return;

        if (settings.failOnTilt)
        {
            if (BoardTilt >= settings.failAngle)
                tiltTimer += Time.deltaTime;
            else
                tiltTimer = 0f;

            if (tiltTimer >= settings.failHoldSeconds)
                Fail();
        }

        CheckCoreOffScreen();
    }

    void CheckCoreOffScreen()
    {
        if (HasEnded)
            return;

        Building core = Building.FindCore();
        if (core == null)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        var col = core.GetComponent<Collider2D>();
        Bounds bounds = col != null
            ? col.bounds
            : new Bounds(core.transform.position, core.Info != null ? (Vector3)core.Info.Size : Vector3.one);

        Vector3 min = cam.WorldToViewportPoint(bounds.min);
        Vector3 max = cam.WorldToViewportPoint(bounds.max);
        if (max.x < 0f || min.x > 1f || max.y < 0f || min.y > 1f)
            Fail();
    }

    public WorldPlatform GetPlatform(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        for (int i = 0; i < platforms.Count; i++)
        {
            WorldPlatform platform = platforms[i];
            if (platform != null && platform.Name == name)
                return platform;
        }

        return null;
    }

    float MaxBalanceTilt()
    {
        WorldPlatform tilting = FindMostTiltedBalance();
        if (tilting == null || tilting.Body == null)
            return 0f;
        return Mathf.Abs(Mathf.DeltaAngle(0f, tilting.Body.rotation));
    }

    WorldPlatform FindMostTiltedBalance()
    {
        WorldPlatform worst = null;
        float worstTilt = -1f;
        for (int i = 0; i < platforms.Count; i++)
        {
            WorldPlatform platform = platforms[i];
            if (platform == null || !platform.IsBalance || platform.Body == null)
                continue;
            float tilt = Mathf.Abs(Mathf.DeltaAngle(0f, platform.Body.rotation));
            if (tilt <= worstTilt)
                continue;
            worstTilt = tilt;
            worst = platform;
        }

        return worst;
    }

    public bool IsPlatformCollider(Collider2D col)
    {
        if (col == null)
            return false;
        for (int i = 0; i < platforms.Count; i++)
        {
            if (platforms[i] != null && platforms[i].Collider == col)
                return true;
        }

        return false;
    }

    public bool IsPlatformBody(Rigidbody2D body)
    {
        if (body == null)
            return false;
        for (int i = 0; i < platforms.Count; i++)
        {
            if (platforms[i] != null && platforms[i].Body == body)
                return true;
        }

        return false;
    }

    public WorldPlatform FindStandingPlatform(Collider2D col)
    {
        if (col == null)
            return null;
        for (int i = 0; i < platforms.Count; i++)
        {
            WorldPlatform platform = platforms[i];
            if (platform == null || platform.Collider == null)
                continue;
            ColliderDistance2D dist = Physics2D.Distance(col, platform.Collider);
            if (dist.isOverlapped || dist.distance < 0.08f)
                return platform;
        }

        return null;
    }

    void RebuildPlatforms()
    {
        for (int i = 0; i < platforms.Count; i++)
        {
            WorldPlatform platform = platforms[i];
            if (platform == null)
                continue;
            if (platform.Root != null)
            {
                platform.Root.SetActive(false);
                Destroy(platform.Root);
            }
            if (platform.Fulcrum != null)
            {
                platform.Fulcrum.SetActive(false);
                Destroy(platform.Fulcrum);
            }
        }

        platforms.Clear();
        BoardBody = null;
        BoardCollider = null;

        LevelInfo level = CurrentLevel;
        if (level == null || level.platformDefs == null || level.platformDefs.Count == 0)
        {
            Debug.LogError("level 没有 platform");
            return;
        }

        for (int i = 0; i < level.platformDefs.Count; i++)
            CreatePlatform(level.platformDefs[i]);

        if (BoardBody == null && platforms.Count > 0)
        {
            BoardBody = platforms[0].Body;
            BoardCollider = platforms[0].Collider;
        }

        Physics2D.SyncTransforms();
    }

    void CreatePlatform(PlatformDef def)
    {
        if (def == null)
            return;

        float height = settings != null ? settings.boardHeight : 0.35f;
        float offsetX = (0.5f - def.pivot) * def.width;
        Vector3 pos = new Vector3(def.x, def.y, 0f);

        var board = new GameObject(def.IsBalance ? "Balance_" + def.name : "Platform_" + def.name);
        board.transform.SetParent(transform);
        board.transform.position = pos;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(board.transform);
        visual.transform.localPosition = new Vector3(offsetX, 0f, 0f);
        visual.transform.localScale = new Vector3(def.width, height, 1f);
        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = ShapeUtil.Square(def.IsBalance
            ? new Color(0.78f, 0.62f, 0.42f)
            : new Color(0.58f, 0.52f, 0.46f));
        renderer.sortingOrder = 0;

        var body = board.AddComponent<Rigidbody2D>();
        var box = board.AddComponent<BoxCollider2D>();
        box.size = new Vector2(def.width, height);
        box.offset = new Vector2(offsetX, 0f);
        box.sharedMaterial = SharedMaterial;

        GameObject fulcrumGo = null;
        if (def.IsBalance)
        {
            body.bodyType = RigidbodyType2D.Dynamic;
            body.constraints = RigidbodyConstraints2D.FreezePosition;
            body.mass = settings != null ? settings.boardMass : 8f;
            body.angularDrag = settings != null ? settings.boardAngularDrag : 2f;
            body.centerOfMass = Vector2.zero;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var hinge = board.AddComponent<HingeJoint2D>();
            hinge.autoConfigureConnectedAnchor = false;
            hinge.anchor = Vector2.zero;
            hinge.connectedAnchor = new Vector2(def.x, def.y);
            hinge.enableCollision = false;

            fulcrumGo = CreateFulcrum(pos);
        }
        else
        {
            body.bodyType = RigidbodyType2D.Kinematic;
            body.constraints = RigidbodyConstraints2D.FreezeAll;
        }

        var created = new WorldPlatform
        {
            Def = def,
            Root = board,
            Fulcrum = fulcrumGo,
            Body = body,
            Collider = box
        };
        platforms.Add(created);

        if (BoardBody == null && def.IsBalance)
        {
            BoardBody = body;
            BoardCollider = box;
        }
    }

    GameObject CreateFulcrum(Vector3 boardPos)
    {
        float boardHalf = (settings != null ? settings.boardHeight : 0.35f) * 0.5f;
        var fulcrum = new GameObject("Fulcrum");
        fulcrum.transform.SetParent(transform);
        fulcrum.transform.position = new Vector3(boardPos.x, boardPos.y - boardHalf, 0f);
        fulcrum.transform.localScale = new Vector3(FulcrumScale, FulcrumScale, 1f);

        var renderer = fulcrum.AddComponent<SpriteRenderer>();
        float topWidth = settings != null ? settings.fulcrumTopWidth : 0.1f;
        renderer.sprite = ShapeUtil.Trapezoid(FulcrumColor, topWidth);
        renderer.sortingOrder = -1;
        appliedFulcrumTopWidth = topWidth;
        return fulcrum;
    }

    void CreateTiltHint()
    {
        var go = new GameObject("TiltHint");
        go.transform.SetParent(transform);
        tiltHint = go.AddComponent<TextMesh>();
        tiltHint.text = "press 0 to destroy a building";
        tiltHint.anchor = TextAnchor.UpperCenter;
        tiltHint.alignment = TextAlignment.Center;
        tiltHint.fontSize = 48;
        tiltHint.characterSize = 0.12f;
        tiltHint.color = new Color(0.85f, 0.15f, 0.15f);
        go.GetComponent<MeshRenderer>().sortingOrder = 16;
        go.SetActive(false);
    }

    void RefreshTiltHint()
    {
        if (tiltHint == null)
            return;

        WorldPlatform tilting = FindMostTiltedBalance();
        bool show = !HasEnded && tilting != null && tilting.Body != null && BoardTilt >= TiltHintAngle;
        tiltHint.gameObject.SetActive(show);
        if (!show)
            return;

        float below = settings != null ? settings.boardHeight * 0.5f + 0.7f : 0.9f;
        tiltHint.transform.position = tilting.Body.transform.TransformPoint(new Vector3(0f, -below, -0.1f));
        tiltHint.transform.rotation = Quaternion.identity;
        float pulse = 1f + 0.16f * Mathf.Sin(Time.time * 5.5f);
        tiltHint.transform.localScale = new Vector3(pulse, pulse, 1f);
    }

    void HandleCameraZoom()
    {
        Camera cam = Camera.main;
        if (cam == null || settings == null)
            return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.01f)
            return;

        float min;
        float max;
        GetCameraZoomRange(out min, out max);
        cam.orthographicSize = Mathf.Clamp(
            cam.orthographicSize - scroll * settings.cameraZoomSpeed,
            min,
            max);
    }

    void GetCameraZoomRange(out float min, out float max)
    {
        min = settings.cameraZoomMin;
        max = settings.cameraZoomMax;
        if (min > max)
        {
            float swap = min;
            min = max;
            max = swap;
        }
        min = Mathf.Max(0.1f, min);
        max = Mathf.Max(min, max);
    }

    void FitCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        cam.orthographic = true;
        float size = settings != null ? settings.cameraSize : 8f;
        if (settings != null)
        {
            float min;
            float max;
            GetCameraZoomRange(out min, out max);
            size = Mathf.Clamp(size, min, max);
        }
        cam.orthographicSize = size;
        cam.transform.position = new Vector3(0f, 0.4f, -10f);
        cam.backgroundColor = new Color(0.55f, 0.74f, 0.86f);
    }

    void OnGUI()
    {
        var hintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 18,
            fontStyle = FontStyle.Bold
        };
        hintStyle.normal.textColor = Color.black;
        hintStyle.hover.textColor = Color.black;
        GUI.Label(new Rect(8f, 8f, 520f, 78f),
            "Ctrl: show/hide resources\nScroll: zoom in/out\nSpace: pause", hintStyle);

        LevelInfo level = CurrentLevel;
        if (level != null && !string.IsNullOrEmpty(level.name))
        {
            var nameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 32,
                fontStyle = FontStyle.Bold
            };
            nameStyle.normal.textColor = Color.black;
            nameStyle.hover.textColor = Color.black;
            GUI.Label(new Rect(0f, 8f, Screen.width, 44f), level.name, nameStyle);
        }

        if (IsPaused && !IsTutorialPaused)
        {
            var pauseStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 48,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            pauseStyle.normal.textColor = Color.black;
            pauseStyle.hover.textColor = Color.black;
            GUI.Label(new Rect(0f, 52f, Screen.width, 70f), "space to unpause", pauseStyle);
        }

        if (showAllClearPopup)
        {
            DrawAllClearPopup();
            return;
        }

        if (!IsGameOver)
            return;

        float boxW = 360f;
        float boxH = 140f;
        var box = new Rect((Screen.width - boxW) * 0.5f, Screen.height * 0.32f, boxW, boxH);
        GUI.Box(box, "");

        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 36,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(box.x, box.y + 18f, box.width, 50f), "Defeat", style);

        var sub = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18
        };
        sub.normal.textColor = Color.white;
        GUI.Label(new Rect(box.x, box.y + 78f, box.width, 30f), "Press R to restart", sub);
    }

    void DrawAllClearPopup()
    {
        float boxW = 560f;
        float boxH = 200f;
        var box = new Rect((Screen.width - boxW) * 0.5f, Screen.height * 0.28f, boxW, boxH);
        GUI.Box(box, "");

        var title = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 36,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        title.normal.textColor = Color.white;
        GUI.Label(new Rect(box.x + 16f, box.y + 28f, box.width - 32f, 54f),
            "All enemy cleared", title);

        var btn = new GUIStyle(GUI.skin.button)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold
        };
        float btnW = 230f;
        float btnH = 48f;
        float btnY = box.y + boxH - 70f;
        if (GUI.Button(new Rect(box.x + 30f, btnY, btnW, btnH), "Stay in this level", btn))
            showAllClearPopup = false;

        bool wasEnabled = GUI.enabled;
        GUI.enabled = HasNextLevel;
        if (GUI.Button(new Rect(box.x + box.width - 30f - btnW, btnY, btnW, btnH), "Next level", btn)
            && HasNextLevel)
            GoToNextLevel();
        GUI.enabled = wasEnabled;
    }
}

public class WorldPlatform
{
    public PlatformDef Def;
    public GameObject Root;
    public GameObject Fulcrum;
    public Rigidbody2D Body;
    public Collider2D Collider;

    public string Name
    {
        get { return Def != null ? Def.name : null; }
    }

    public bool IsBalance
    {
        get { return Def != null && Def.IsBalance; }
    }

    public float Width
    {
        get { return Def != null ? Def.width : 0f; }
    }

    public float Pivot
    {
        get { return Def != null ? Def.pivot : 0.5f; }
    }
}

public static class ShapeUtil
{
    static Sprite whiteSprite;

    public static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
            whiteSprite = Square(Color.white);
        return whiteSprite;
    }

    public static Sprite Square(Color color)
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f, 0, SpriteMeshType.FullRect);
    }

    public static Sprite Triangle(Color color)
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < size; y++)
        {
            float t = (y + 0.5f) / size;
            float half = t * 0.5f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size;
                bool inside = nx >= 0.5f - half && nx <= 0.5f + half;
                tex.SetPixel(x, y, inside ? color : Color.clear);
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    public static Sprite Trapezoid(Color color, float topWidth = 0.1f)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float topHalf = Mathf.Clamp01(topWidth) * 0.5f;
        const float bottomHalf = 0.5f;
        for (int y = 0; y < size; y++)
        {
            float t = (y + 0.5f) / size;
            float half = Mathf.Lerp(bottomHalf, topHalf, t);
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size;
                bool inside = nx >= 0.5f - half && nx <= 0.5f + half;
                tex.SetPixel(x, y, inside ? color : Color.clear);
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 1f), size);
    }

    public static Sprite Hexagon(Color color)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float cx = 0.5f;
        float cy = 0.5f;
        float r = 0.48f;
        for (int y = 0; y < size; y++)
        {
            float ny = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size;
                float dx = Mathf.Abs(nx - cx);
                float dy = Mathf.Abs(ny - cy);
                bool inside = dx <= r && r * dy + 0.5f * r * dx <= r * r;
                tex.SetPixel(x, y, inside ? color : Color.clear);
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
