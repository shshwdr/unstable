using System.Collections.Generic;
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
    public string type;
    public string requireResource;
    public List<string> provide;
    public float cd;
    public List<float> shape;
    public float scale;
    public float radius;
    public float hp;
    public float attack;
    public float attackRange;
    public List<string> consume;
    public int cost;
    public List<string> special;

    public List<ResourceAmount> ProvideList = new List<ResourceAmount>();
    public List<ResourceAmount> ConsumeList = new List<ResourceAmount>();

    public Vector2 Size
    {
        get
        {
            float w = shape != null && shape.Count > 0 ? shape[0] : 1f;
            float h = shape != null && shape.Count > 1 ? shape[1] : w;
            float s = scale > 0f ? scale : 1f;
            return new Vector2(w * s, h * s);
        }
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

public class CSVLoader : Singleton<CSVLoader>
{
    public Dictionary<string, BuildingInfo> buildingInfoMap = new Dictionary<string, BuildingInfo>();
    public List<BuildingInfo> buildingList = new List<BuildingInfo>();
    public List<BuildingInfo> playerBuildingList = new List<BuildingInfo>();
    public Dictionary<string, EnemyInfo> enemyInfoMap = new Dictionary<string, EnemyInfo>();
    public List<EnemyInfo> enemyList = new List<EnemyInfo>();
    public List<EncounterInfo> encounterList = new List<EncounterInfo>();

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
    }
}
