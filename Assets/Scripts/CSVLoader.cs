using System.Collections.Generic;
using System.Globalization;
using Sinbad;
using UnityEngine;

public struct ResourceAmount
{
    public string id;
    public int amount;

    public override string ToString()
    {
        return id + ":" + amount;
    }
}

public class BuildingInfo
{
    public string identifier;
    public string name;
    public string desc;
    public string type;
    public string requireResource;
    public List<string> provide;
    public float cd;
    public float scale;
    public float radius;
    public float hp;
    public float attack;
    public float attackCD;
    public float attackRange;
    public List<string> consume;
    public int cost;
    public List<string> special;

    public List<ResourceAmount> ProvideList = new List<ResourceAmount>();
    public List<ResourceAmount> ConsumeList = new List<ResourceAmount>();

    public Vector2 Size
    {
        get { return BuildingArt.WorldSize(this); }
    }

    public bool IsCore
    {
        get { return type == "core"; }
    }

    public bool IsResource
    {
        get { return type == "resource"; }
    }

    public bool IsPlayerBuildable
    {
        get { return !IsCore && !IsResource; }
    }

    public bool RequiresTop
    {
        get { return HasSpecial("requireTop"); }
    }

    public bool CanAttack
    {
        get { return attack > 0f; }
    }

    public bool HasProvideDisplay
    {
        get { return ProvideList != null && ProvideList.Count > 0; }
    }

    public bool HasStockDisplay
    {
        get
        {
            if (!HasProvideDisplay || ProvideList == null)
                return false;
            for (int i = 0; i < ProvideList.Count; i++)
            {
                if (ProvideList[i].id != "coin")
                    return true;
            }

            return false;
        }
    }

    public bool HasConsumeDisplay
    {
        get { return ConsumeList != null && ConsumeList.Count > 0; }
    }

    public bool ProvidesResource(string resource)
    {
        if (!HasProvideDisplay || ProvideList == null || string.IsNullOrEmpty(resource))
            return false;

        for (int i = 0; i < ProvideList.Count; i++)
        {
            if (ProvideList[i].id == resource)
                return true;
        }

        return false;
    }

    public bool HasSpecial(string flag)
    {
        if (special == null || string.IsNullOrEmpty(flag))
            return false;

        for (int i = 0; i < special.Count; i++)
        {
            if (special[i] == flag)
                return true;
        }

        return false;
    }

    public void ParseAmounts()
    {
        ProvideList = ParseResourceList(provide);
        ConsumeList = ParseResourceList(consume);
    }

    public static List<ResourceAmount> ParseResourceList(List<string> tokens)
    {
        var result = new List<ResourceAmount>();
        if (tokens == null)
            return result;

        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (string.IsNullOrEmpty(token))
                continue;

            int split = token.LastIndexOf('_');
            if (split <= 0 || split >= token.Length - 1)
            {
                Debug.LogError("资源格式应为 identifier_数量: " + token);
                continue;
            }

            string id = token.Substring(0, split);
            int amount;
            if (!int.TryParse(token.Substring(split + 1), out amount))
            {
                Debug.LogError("资源数量无法解析: " + token);
                continue;
            }

            result.Add(new ResourceAmount { id = id, amount = amount });
        }

        return result;
    }

    public static bool ResourceListsOverlap(List<ResourceAmount> a, List<ResourceAmount> b)
    {
        if (a == null || b == null || a.Count == 0 || b.Count == 0)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (string.IsNullOrEmpty(a[i].id))
                continue;
            for (int j = 0; j < b.Count; j++)
            {
                if (a[i].id == b[j].id)
                    return true;
            }
        }

        return false;
    }
}

public class EnemyInfo
{
    public string identifier;
    public float hp;
    public float attack;
    public float speed;
    public float attackCD;
    public float attackRange;
    public bool isMelee;
    public List<float> size;
    public List<string> special;

    public Vector2 Size
    {
        get
        {
            float w = size != null && size.Count > 0 ? size[0] : 0.5f;
            float h = size != null && size.Count > 1 ? size[1] : w;
            return new Vector2(w, h);
        }
    }

    public bool HasSpecial(string flag)
    {
        if (special == null || string.IsNullOrEmpty(flag))
            return false;

        for (int i = 0; i < special.Count; i++)
        {
            if (special[i] == flag)
                return true;
        }

        return false;
    }
}

public class EncounterInfo
{
    public string level;
    public float time;
    public List<string> enemies;
    public float interval;
    public List<float> pos;

    public Vector2 Position
    {
        get
        {
            float x = pos != null && pos.Count > 0 ? pos[0] : 0f;
            float y = pos != null && pos.Count > 1 ? pos[1] : 0f;
            return new Vector2(x, y);
        }
    }

    public List<EnemyInfo> ExpandEnemies()
    {
        var result = new List<EnemyInfo>();
        if (enemies == null)
            return result;

        var map = CSVLoader.Instance.enemyInfoMap;
        for (int i = 0; i < enemies.Count; i++)
        {
            string token = enemies[i];
            if (string.IsNullOrEmpty(token))
                continue;

            int split = token.LastIndexOf('_');
            if (split <= 0 || split >= token.Length - 1)
            {
                Debug.LogError("encounter enemies 格式应为 identifier_数量: " + token);
                continue;
            }

            string id = token.Substring(0, split);
            int count;
            if (!int.TryParse(token.Substring(split + 1), out count))
            {
                Debug.LogError("encounter enemies 数量无法解析: " + token);
                continue;
            }

            EnemyInfo info;
            if (!map.TryGetValue(id, out info))
            {
                Debug.LogError("未知敌人 identifier: " + id);
                continue;
            }

            for (int n = 0; n < count; n++)
                result.Add(info);
        }

        return result;
    }
}

public class TutorialInfo
{
    public string identifier;
    public string text;
    public bool isEnd;
    public List<string> startAction;
    public List<string> waitToFinish;

    public bool HasWait
    {
        get { return HasTokens(waitToFinish); }
    }

    public static bool HasTokens(List<string> tokens)
    {
        if (tokens == null)
            return false;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!string.IsNullOrEmpty(tokens[i]))
                return true;
        }

        return false;
    }
}

public class PlatformDef
{
    public string type;
    public string name;
    public float x;
    public float y;
    public float width;
    public float pivot = 0.5f;

    public bool IsBalance
    {
        get { return type == "balance"; }
    }

    public Vector2 Position
    {
        get { return new Vector2(x, y); }
    }
}

public class MineDef
{
    public string resourceId;
    public string platformName;
    public string side;
}

public class LevelInfo
{
    public string identifier;
    public string name;
    public List<string> buildings;
    public List<string> platform;
    public List<string> mines;
    public float coinIncrease = 1f;

    public List<BuildingInfo> buildingInfos = new List<BuildingInfo>();
    public List<BuildingInfo> playerBuildings = new List<BuildingInfo>();
    public List<PlatformDef> platformDefs = new List<PlatformDef>();
    public List<MineDef> mineDefs = new List<MineDef>();

    public void ResolveBuildings()
    {
        buildingInfos.Clear();
        playerBuildings.Clear();
        if (buildings == null)
            return;

        var map = CSVLoader.Instance.buildingInfoMap;
        for (int i = 0; i < buildings.Count; i++)
        {
            string id = buildings[i];
            if (string.IsNullOrEmpty(id))
                continue;

            BuildingInfo info;
            if (!map.TryGetValue(id, out info) || info == null)
            {
                Debug.LogError("level 未知建筑 identifier: " + id);
                continue;
            }

            buildingInfos.Add(info);
            if (info.IsPlayerBuildable)
                playerBuildings.Add(info);
        }
    }

    public void ResolveLayout()
    {
        platformDefs.Clear();
        mineDefs.Clear();

        var names = new HashSet<string>();
        if (platform != null)
        {
            for (int i = 0; i < platform.Count; i++)
            {
                PlatformDef def;
                if (!TryParsePlatform(platform[i], out def))
                    continue;
                if (!names.Add(def.name))
                {
                    Debug.LogError("level platform 名字重复: " + def.name + " (" + platform[i] + ")");
                    continue;
                }

                platformDefs.Add(def);
            }
        }

        if (mines == null)
            return;

        for (int i = 0; i < mines.Count; i++)
        {
            MineDef def;
            if (!TryParseMine(mines[i], out def))
                continue;

            if (!names.Contains(def.platformName))
            {
                Debug.LogError("mines 引用了未知 platform: " + def.platformName + " (" + mines[i] + ")");
                continue;
            }

            BuildingInfo info;
            if (!CSVLoader.Instance.buildingInfoMap.TryGetValue(def.resourceId, out info)
                || info == null || !info.IsResource)
            {
                Debug.LogError("mines 未知资源 identifier: " + def.resourceId);
                continue;
            }

            mineDefs.Add(def);
        }
    }

    static bool TryParsePlatform(string token, out PlatformDef def)
    {
        def = null;
        if (string.IsNullOrEmpty(token))
            return false;

        string[] parts = token.Split('_');
        if (parts.Length < 5 || parts.Length > 6)
        {
            Debug.LogError("platform 格式应为 type_name_x_y_width[_pivot]: " + token);
            return false;
        }

        string type = parts[0];
        if (type != "balance" && type != "platform")
        {
            Debug.LogError("platform 未知类型: " + type + " (" + token + ")");
            return false;
        }

        if (string.IsNullOrEmpty(parts[1]))
        {
            Debug.LogError("platform 缺少名字: " + token);
            return false;
        }

        float x;
        float y;
        float width;
        if (!TryParseFloat(parts[2], out x)
            || !TryParseFloat(parts[3], out y)
            || !TryParseFloat(parts[4], out width))
        {
            Debug.LogError("platform 数值无法解析: " + token);
            return false;
        }

        if (width <= 0f)
        {
            Debug.LogError("platform 宽度必须大于 0: " + token);
            return false;
        }

        float pivot = 0.5f;
        if (parts.Length == 6 && !TryParseFloat(parts[5], out pivot))
        {
            Debug.LogError("platform pivot 无法解析: " + token);
            return false;
        }

        def = new PlatformDef
        {
            type = type,
            name = parts[1],
            x = x,
            y = y,
            width = width,
            pivot = pivot
        };
        return true;
    }

    static bool TryParseMine(string token, out MineDef def)
    {
        def = null;
        if (string.IsNullOrEmpty(token))
            return false;

        string[] parts = token.Split('_');
        if (parts.Length != 3)
        {
            Debug.LogError("mines 格式应为 resource_platform_side: " + token);
            return false;
        }

        string side = parts[2];
        if (side != "left" && side != "right" && side != "full")
        {
            Debug.LogError("mines side 应为 left/right/full: " + token);
            return false;
        }

        def = new MineDef
        {
            resourceId = parts[0],
            platformName = parts[1],
            side = side
        };
        return true;
    }

    static bool TryParseFloat(string text, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

public class CSVLoader : Singleton<CSVLoader>
{
    public Dictionary<string, BuildingInfo> buildingInfoMap = new Dictionary<string, BuildingInfo>();
    public List<BuildingInfo> buildingList = new List<BuildingInfo>();
    public List<BuildingInfo> playerBuildingList = new List<BuildingInfo>();
    public Dictionary<string, EnemyInfo> enemyInfoMap = new Dictionary<string, EnemyInfo>();
    public List<EnemyInfo> enemyList = new List<EnemyInfo>();
    public List<EncounterInfo> encounterList = new List<EncounterInfo>();
    public List<LevelInfo> levelList = new List<LevelInfo>();
    public List<TutorialInfo> tutorialList = new List<TutorialInfo>();

    bool inited;

    public void Init()
    {
        if (inited)
            return;
        inited = true;

        buildingList = CsvUtil.LoadObjects<BuildingInfo>("building");
        buildingInfoMap.Clear();
        playerBuildingList.Clear();
        for (int i = 0; i < buildingList.Count; i++)
        {
            buildingList[i].ParseAmounts();
            buildingInfoMap[buildingList[i].identifier] = buildingList[i];
            if (buildingList[i].IsPlayerBuildable)
                playerBuildingList.Add(buildingList[i]);
        }

        enemyList = CsvUtil.LoadObjects<EnemyInfo>("enemy");
        enemyInfoMap.Clear();
        for (int i = 0; i < enemyList.Count; i++)
            enemyInfoMap[enemyList[i].identifier] = enemyList[i];

        encounterList = CsvUtil.LoadObjects<EncounterInfo>("encounter");

        levelList = CsvUtil.LoadObjects<LevelInfo>("level");
        var seenLevelIds = new HashSet<string>();
        for (int i = 0; i < levelList.Count; i++)
        {
            LevelInfo level = levelList[i];
            level.ResolveBuildings();
            level.ResolveLayout();
            string id = level.identifier;
            if (!string.IsNullOrEmpty(id) && seenLevelIds.Add(id))
                continue;

            string next = (i + 1).ToString();
            int n = i + 1;
            while (seenLevelIds.Contains(next))
            {
                n++;
                next = n.ToString();
            }
            if (!string.IsNullOrEmpty(id))
                Debug.LogError("level identifier 重复: " + id + " (" + level.name + ")，已改为 " + next);
            level.identifier = next;
            seenLevelIds.Add(next);
        }

        tutorialList = CsvUtil.LoadObjects<TutorialInfo>("tutorial");
    }

    public List<EncounterInfo> GetEncountersForLevel(string levelId)
    {
        var result = new List<EncounterInfo>();
        if (encounterList == null || string.IsNullOrEmpty(levelId))
            return result;

        for (int i = 0; i < encounterList.Count; i++)
        {
            EncounterInfo info = encounterList[i];
            if (info != null && info.level == levelId)
                result.Add(info);
        }

        return result;
    }

    public List<TutorialInfo> GetTutorialSteps(string id)
    {
        var result = new List<TutorialInfo>();
        if (tutorialList == null || string.IsNullOrEmpty(id))
            return result;

        bool inGroup = false;
        for (int i = 0; i < tutorialList.Count; i++)
        {
            TutorialInfo info = tutorialList[i];
            if (info == null)
                continue;

            if (!string.IsNullOrEmpty(info.identifier))
            {
                if (inGroup)
                    break;
                inGroup = info.identifier == id;
            }

            if (inGroup)
                result.Add(info);
        }

        return result;
    }
}
