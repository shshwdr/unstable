using UnityEngine;

public class BalanceWorld : MonoBehaviour
{
    public GameBalanceSettings settings;

    public PhysicsMaterial2D SharedMaterial { get; private set; }
    public Collider2D BoardCollider { get; private set; }
    public Rigidbody2D BoardBody { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool IsVictory { get; private set; }
    public bool IsRestarting { get; private set; }
    public bool HasEnded { get { return IsGameOver || IsVictory; } }
    public float Gold { get; private set; }

    public const float GoldStart = 100f;
    public const float GoldPerSecond = 1f;
    public float BoardTilt { get; private set; }

    float tiltTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindObjectOfType<BalanceWorld>() != null)
            return;

        var go = new GameObject("BalanceGame");
        go.AddComponent<BalanceWorld>();
        go.AddComponent<BuildingPlacer>();
        go.AddComponent<EncounterManager>();
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

        CreateFulcrum();
        CreateBoard();
        FitCamera();
        ApplyPhysics();
        Gold = GoldStart;
    }

    void Update()
    {
        if (IsRestarting)
            IsRestarting = false;

        ApplyPhysics();
        TickGold();
        CheckFail();
        CheckVictory();

        if (HasEnded && Input.GetKeyDown(KeyCode.R))
            Restart();
    }

    void FixedUpdate()
    {
        if (BoardBody == null || settings == null)
            return;

        float angle = Mathf.DeltaAngle(0f, BoardBody.rotation);
        if (Mathf.Abs(angle) >= settings.restoreAngle)
            return;

        float torque = -angle * settings.restoreStrength
                       - BoardBody.angularVelocity * settings.restoreDamping;
        BoardBody.AddTorque(torque);
    }

    public void ApplyPhysics()
    {
        if (settings == null)
            return;

        Physics2D.gravity = new Vector2(0f, settings.gravity);
        SharedMaterial.friction = settings.friction;
        SharedMaterial.bounciness = settings.bounciness;

        if (BoardBody != null)
        {
            BoardBody.mass = settings.boardMass;
            BoardBody.angularDrag = settings.boardAngularDrag;
        }
    }

    public bool SpendGold(int amount)
    {
        if (amount <= 0)
            return true;
        if (Gold < amount)
            return false;
        Gold -= amount;
        return true;
    }

    public void AddGold(int amount)
    {
        if (amount <= 0)
            return;
        Gold += amount;
    }

    public void Fail()
    {
        if (HasEnded)
            return;
        IsGameOver = true;
        Projectile.ClearAll();
    }

    void TickGold()
    {
        if (HasEnded)
            return;
        Gold += GoldPerSecond * Time.deltaTime;
    }

    void CheckVictory()
    {
        if (HasEnded)
            return;

        var encounters = GetComponent<EncounterManager>();
        if (encounters == null || !encounters.IsComplete)
            return;
        if (Enemy.All.Count > 0)
            return;

        IsVictory = true;
        Projectile.ClearAll();
    }

    public void Restart()
    {
        IsRestarting = true;
        Projectile.ClearAll();
        Enemy.ClearAll();

        Building[] buildings = FindObjectsOfType<Building>();
        for (int i = 0; i < buildings.Length; i++)
            Destroy(buildings[i].gameObject);

        BoardBody.velocity = Vector2.zero;
        BoardBody.angularVelocity = 0f;
        BoardBody.rotation = 0f;
        BoardBody.position = settings.pivotPosition;

        IsGameOver = false;
        IsVictory = false;
        tiltTimer = 0f;
        Gold = GoldStart;

        var encounters = GetComponent<EncounterManager>();
        if (encounters != null)
            encounters.Restart();
    }

    void CheckFail()
    {
        if (HasEnded || BoardBody == null)
            return;

        BoardTilt = Mathf.Abs(Mathf.DeltaAngle(0f, BoardBody.rotation));

        if (settings.failOnTilt)
        {
            if (BoardTilt >= settings.failAngle)
                tiltTimer += Time.deltaTime;
            else
                tiltTimer = 0f;

            if (tiltTimer >= settings.failHoldSeconds)
                Fail();
        }
    }

    void CreateFulcrum()
    {
        var fulcrum = new GameObject("Fulcrum").transform;
        fulcrum.SetParent(transform);
        fulcrum.position = new Vector3(settings.pivotPosition.x, settings.pivotPosition.y - 0.55f, 0f);
        fulcrum.localScale = new Vector3(1.1f, 1.1f, 1f);

        var renderer = fulcrum.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = ShapeUtil.Triangle(new Color(0.18f, 0.18f, 0.2f));
        renderer.sortingOrder = -1;
    }

    void CreateBoard()
    {
        var board = new GameObject("Board");
        board.transform.SetParent(transform);
        board.transform.position = settings.pivotPosition;

        var visual = new GameObject("Visual");
        visual.transform.SetParent(board.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = new Vector3(settings.boardWidth, settings.boardHeight, 1f);
        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = ShapeUtil.Square(new Color(0.78f, 0.62f, 0.42f));
        renderer.sortingOrder = 0;

        var body = board.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.constraints = RigidbodyConstraints2D.FreezePosition;
        body.mass = settings.boardMass;
        body.angularDrag = settings.boardAngularDrag;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        BoardBody = body;

        var box = board.AddComponent<BoxCollider2D>();
        box.size = new Vector2(settings.boardWidth, settings.boardHeight);
        box.sharedMaterial = SharedMaterial;
        BoardCollider = box;

        var hinge = board.AddComponent<HingeJoint2D>();
        hinge.autoConfigureConnectedAnchor = false;
        hinge.anchor = Vector2.zero;
        hinge.connectedAnchor = settings.pivotPosition;
        hinge.enableCollision = false;
    }

    void FitCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        cam.orthographic = true;
        cam.orthographicSize = 8f;
        cam.transform.position = new Vector3(0f, 0.4f, -10f);
        cam.backgroundColor = new Color(0.55f, 0.74f, 0.86f);
    }

    void OnGUI()
    {
        if (!HasEnded)
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
        GUI.Label(new Rect(box.x, box.y + 18f, box.width, 50f), IsVictory ? "Victory" : "Defeat", style);

        var sub = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18
        };
        sub.normal.textColor = Color.white;
        GUI.Label(new Rect(box.x, box.y + 78f, box.width, 30f), "Press R to restart", sub);
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
}
