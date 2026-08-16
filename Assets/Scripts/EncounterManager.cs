using System.Collections.Generic;
using UnityEngine;

public class EncounterManager : MonoBehaviour
{
    BalanceWorld world;
    SpawnGate gate;
    readonly Queue<EnemyInfo> spawnQueue = new Queue<EnemyInfo>();

    float gameTime;
    int waveIndex;
    bool spawning;
    float interval;
    float nextSpawnAt;
    float nextWaveAt;

    public bool IsComplete
    {
        get
        {
            List<EncounterInfo> list = CSVLoader.Instance.encounterList;
            if (list == null || list.Count == 0)
                return false;
            return !spawning && waveIndex >= list.Count;
        }
    }

    void Start()
    {
        world = GetComponent<BalanceWorld>();
        gate = SpawnGate.Create(transform);
        ResetState();
    }

    public void Restart()
    {
        gameTime = 0f;
        ResetState();
    }

    void ResetState()
    {
        spawnQueue.Clear();
        spawning = false;
        waveIndex = 0;
        interval = 0f;
        nextSpawnAt = 0f;
        List<EncounterInfo> list = CSVLoader.Instance.encounterList;
        nextWaveAt = list != null && list.Count > 0 ? list[0].time : 0f;
        PlaceGate();
    }

    void Update()
    {
        if (world != null && world.HasEnded)
            return;

        gameTime += Time.deltaTime;

        if (spawning)
            TickSpawn();
        else
            TryStartWave();

        UpdateCountdown();
    }

    void TryStartWave()
    {
        List<EncounterInfo> list = CSVLoader.Instance.encounterList;
        if (list == null || waveIndex >= list.Count)
            return;
        if (gameTime < nextWaveAt)
            return;

        EncounterInfo encounter = list[waveIndex];
        interval = encounter.interval;
        List<EnemyInfo> enemies = encounter.ExpandEnemies();
        spawnQueue.Clear();
        for (int i = 0; i < enemies.Count; i++)
            spawnQueue.Enqueue(enemies[i]);

        spawning = true;
        nextSpawnAt = gameTime;
        TickSpawn();
    }

    void TickSpawn()
    {
        while (spawnQueue.Count > 0 && gameTime >= nextSpawnAt)
        {
            Vector3 pos = gate != null ? gate.transform.position : Vector3.zero;
            pos += (Vector3)(Random.insideUnitCircle * 0.12f);
            Enemy.Spawn(spawnQueue.Dequeue(), pos);
            nextSpawnAt = interval <= 0f ? gameTime : gameTime + interval;
        }

        if (spawnQueue.Count == 0)
        {
            spawning = false;
            waveIndex++;
            PlaceGate();
            List<EncounterInfo> list = CSVLoader.Instance.encounterList;
            if (list != null && waveIndex < list.Count)
                nextWaveAt = gameTime + list[waveIndex].time;
        }
    }

    void PlaceGate()
    {
        if (gate == null)
            return;

        List<EncounterInfo> list = CSVLoader.Instance.encounterList;
        if (list == null || waveIndex >= list.Count)
        {
            gate.gameObject.SetActive(false);
            return;
        }

        gate.gameObject.SetActive(true);
        gate.transform.position = list[waveIndex].Position;
    }

    void UpdateCountdown()
    {
        if (gate == null || !gate.gameObject.activeSelf)
            return;

        List<EncounterInfo> list = CSVLoader.Instance.encounterList;
        float remain = 0f;
        if (spawning)
            remain = nextSpawnAt - gameTime;
        else if (list != null && waveIndex < list.Count)
            remain = nextWaveAt - gameTime;

        gate.SetCountdown(remain);
    }
}

public class SpawnGate : MonoBehaviour
{
    TextMesh countdown;

    public static SpawnGate Create(Transform parent)
    {
        var go = new GameObject("SpawnGate");
        go.transform.SetParent(parent);
        var gate = go.AddComponent<SpawnGate>();
        gate.Build();
        return gate;
    }

    void Build()
    {
        var visual = new GameObject("Visual");
        visual.transform.SetParent(transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localScale = new Vector3(1.1f, 1.1f, 1f);
        var renderer = visual.AddComponent<SpriteRenderer>();
        renderer.sprite = ShapeUtil.Square(new Color(0.52f, 0.18f, 0.78f, 0.45f));
        renderer.sortingOrder = 6;

        var ringGo = new GameObject("Ring");
        ringGo.transform.SetParent(transform);
        ringGo.transform.localPosition = Vector3.zero;
        var ring = ringGo.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.startWidth = 0.07f;
        ring.endWidth = 0.07f;
        ring.material = new Material(Shader.Find("Sprites/Default"));
        ring.startColor = new Color(0.85f, 0.35f, 1f, 0.9f);
        ring.endColor = new Color(0.85f, 0.35f, 1f, 0.9f);
        ring.sortingOrder = 7;
        const int segments = 36;
        const float radius = 0.75f;
        ring.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }

        var textGo = new GameObject("Countdown");
        textGo.transform.SetParent(transform);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.1f);
        countdown = textGo.AddComponent<TextMesh>();
        countdown.anchor = TextAnchor.MiddleCenter;
        countdown.alignment = TextAlignment.Center;
        countdown.fontSize = 72;
        countdown.characterSize = 0.07f;
        countdown.color = Color.white;
        textGo.GetComponent<MeshRenderer>().sortingOrder = 8;
    }

    public void SetCountdown(float seconds)
    {
        if (countdown == null)
            return;
        countdown.text = Mathf.Max(0, Mathf.CeilToInt(seconds)).ToString();
    }

    void LateUpdate()
    {
        if (countdown != null)
            countdown.transform.rotation = Quaternion.identity;
    }
}
