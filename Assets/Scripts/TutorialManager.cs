using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(50)]
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    BalanceWorld world;
    EncounterManager encounters;
    readonly List<BuildingInfo> buildable = new List<BuildingInfo>();
    readonly HashSet<string> newlyAdded = new HashSet<string>();
    List<TutorialInfo> steps;
    string playingId;
    int stepIndex = -1;
    bool overlay;
    bool destroyButtonVisible = true;
    bool demolishedThisStep;
    bool killedThisStep;
    int blockUntilFrame = -1;
    bool finishedStart;

    public bool IsPlaying
    {
        get { return steps != null && stepIndex >= 0 && stepIndex < steps.Count; }
    }

    public bool BlocksInput
    {
        get { return overlay || Time.frameCount <= blockUntilFrame; }
    }

    public bool DestroyButtonVisible
    {
        get { return destroyButtonVisible; }
    }

    public bool AllowEnterToFinish
    {
        get { return finishedStart && !IsPlaying && !BlocksInput; }
    }

    public List<BuildingInfo> BuildableBuildings
    {
        get { return buildable; }
    }

    public bool IsNewlyAdded(string identifier)
    {
        return !string.IsNullOrEmpty(identifier) && newlyAdded.Contains(identifier);
    }

    void Awake()
    {
        Instance = this;
        world = GetComponent<BalanceWorld>();
        encounters = GetComponent<EncounterManager>();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        ResetForLevel();
    }

    public void OnLevelRestart()
    {
        ResetForLevel();
    }

    void ResetForLevel()
    {
        StopTutorial(false);
        destroyButtonVisible = true;
        finishedStart = false;
        CopyLevelBuildings();
        if (world == null)
            return;
        LevelInfo level = world.CurrentLevel;
        if (level == null)
            return;
        if (level.identifier == "1")
            Play("start");
        else if (level.identifier == "2")
            Play("level2");
        else if (level.identifier == "4")
            Play("level4");
    }

    void CopyLevelBuildings()
    {
        buildable.Clear();
        newlyAdded.Clear();
        LevelInfo level = world != null ? world.CurrentLevel : null;
        List<BuildingInfo> source = level != null
            ? level.playerBuildings
            : CSVLoader.Instance.playerBuildingList;
        if (source == null)
            return;
        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] != null)
                buildable.Add(source[i]);
        }
    }

    public void Play(string id)
    {
        playingId = id;
        steps = CSVLoader.Instance.GetTutorialSteps(id);
        stepIndex = -1;
        if (steps == null || steps.Count == 0)
            return;
        if (id == "start" && encounters != null)
            encounters.SetHidden(true);
        BeginStep(0);
    }

    public void StopForGameEnd()
    {
        StopTutorial(false);
    }

    void StopTutorial(bool completed)
    {
        steps = null;
        stepIndex = -1;
        overlay = false;
        if (completed && playingId == "start")
            finishedStart = true;
        playingId = null;
        if (world != null)
            world.SetTutorialPaused(false);
    }

    void BeginStep(int index)
    {
        if (steps == null || index < 0 || index >= steps.Count)
        {
            StopTutorial(true);
            return;
        }

        stepIndex = index;
        demolishedThisStep = false;
        killedThisStep = false;
        TutorialInfo info = steps[stepIndex];
        RunStartActions(info);
        overlay = !info.HasWait;
        if (world != null)
            world.SetTutorialPaused(overlay);
    }

    void Advance()
    {
        if (!IsPlaying)
            return;

        TutorialInfo current = steps[stepIndex];
        if (overlay)
            blockUntilFrame = Time.frameCount;
        if (current != null && current.isEnd)
        {
            StopTutorial(true);
            return;
        }

        BeginStep(stepIndex + 1);
    }

    void Update()
    {
        if (world != null && world.IsGameOver)
        {
            StopTutorial(false);
            return;
        }

        if (!IsPlaying)
            return;

        TutorialInfo info = steps[stepIndex];
        if (overlay)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                Advance();
            return;
        }

        if (WaitsSatisfied(info))
            Advance();
    }

    void RunStartActions(TutorialInfo info)
    {
        if (info == null || !TutorialInfo.HasTokens(info.startAction))
            return;

        for (int i = 0; i < info.startAction.Count; i++)
        {
            string action = info.startAction[i];
            if (string.IsNullOrEmpty(action))
                continue;
            if (action == "hideDestroyButton")
                destroyButtonVisible = false;
            else if (action == "addDestoryButton")
                destroyButtonVisible = true;
            else if (action == "addEncounter")
            {
                if (encounters != null)
                    encounters.TriggerFirst();
            }
            else if (action == "hideEncounter")
            {
                if (encounters != null)
                    encounters.SetHidden(true);
            }
            else if (action.StartsWith("addBuilding_"))
                AddBuilding(action.Substring("addBuilding_".Length));
            else
                Debug.LogError("未知 tutorial startAction: " + action);
        }
    }

    void AddBuilding(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return;

        for (int i = 0; i < buildable.Count; i++)
        {
            if (buildable[i] != null && buildable[i].identifier == identifier)
                return;
        }

        BuildingInfo info;
        if (!CSVLoader.Instance.buildingInfoMap.TryGetValue(identifier, out info) || info == null)
        {
            Debug.LogError("tutorial 未知建筑 identifier: " + identifier);
            return;
        }

        buildable.Add(info);
        newlyAdded.Add(identifier);
    }

    bool WaitsSatisfied(TutorialInfo info)
    {
        if (info == null || !info.HasWait)
            return true;

        for (int i = 0; i < info.waitToFinish.Count; i++)
        {
            string wait = info.waitToFinish[i];
            if (string.IsNullOrEmpty(wait))
                continue;
            if (!WaitDone(wait))
                return false;
        }

        return true;
    }

    bool WaitDone(string wait)
    {
        if (wait == "destoryBuilding")
            return demolishedThisStep;
        if (wait == "killEnemy")
            return killedThisStep;
        if (wait.StartsWith("build_"))
            return HasWorkingBuilding(wait.Substring("build_".Length));
        Debug.LogError("未知 tutorial waitToFinish: " + wait);
        return true;
    }

    static bool HasWorkingBuilding(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return false;

        for (int i = 0; i < Building.All.Count; i++)
        {
            Building building = Building.All[i];
            if (building == null || building.Info == null)
                continue;
            if (building.Info.identifier != identifier)
                continue;
            if (!building.IsResource && building.Health != null && !building.Health.IsAlive)
                continue;
            if (building.HasNoProblems())
                return true;
        }

        return false;
    }

    public void NotifyBuildingDestroyed()
    {
        demolishedThisStep = true;
    }

    public void NotifyEnemyKilled()
    {
        killedThisStep = true;
    }

    void OnGUI()
    {
        if (!IsPlaying)
            return;

        TutorialInfo info = steps[stepIndex];
        if (info == null || string.IsNullOrEmpty(info.text))
            return;

        GUI.depth = -100;
        if (overlay)
        {
            var overlayRect = new Rect(0f, 0f, Screen.width, Screen.height);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(overlayRect, Texture2D.whiteTexture);
            GUI.color = old;
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.KeyDown)
                Event.current.Use();
        }

        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 36,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        style.normal.textColor = overlay ? Color.white : Color.black;
        style.hover.textColor = style.normal.textColor;

        float width = Mathf.Min(900f, Screen.width - 80f);
        float y = Screen.height * 0.22f;
        string text = overlay
            ? info.text + "\n(Click Anywhere To Continue)"
            : info.text;
        GUI.Label(new Rect((Screen.width - width) * 0.5f, y, width, 260f), text, style);
    }
}
