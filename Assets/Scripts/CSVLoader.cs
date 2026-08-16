using System.Collections.Generic;
using Sinbad;
using UnityEngine;

public class BuildingInfo
{
    public string identifier;
    public string name;
    public string type;
    public List<string> provide;
    public int provideCount;
    public List<float> shape;
    public float radius;
    public List<string> require;
    public float hp;
    public float attack;
    public float attackRange;
    public float attackCD;

    public Vector2 Size
    {
        get
        {
            float w = shape != null && shape.Count > 0 ? shape[0] : 1f;
            float h = shape != null && shape.Count > 1 ? shape[1] : w;
            return new Vector2(w, h);
        }
    }

    public bool IsHome
    {
        get { return type == "home"; }
    }

    public bool Provides(string resource)
    {
        if (provide == null || string.IsNullOrEmpty(resource))
            return false;

        for (int i = 0; i < provide.Count; i++)
        {
            if (provide[i] == resource)
                return true;
        }

        return false;
    }

    public bool Requires(string resource)
    {
        if (require == null || string.IsNullOrEmpty(resource))
            return false;

        for (int i = 0; i < require.Count; i++)
        {
            if (require[i] == resource)
                return true;
        }

        return false;
    }

    public bool HasNoRequires()
    {
        if (require == null)
            return true;

        for (int i = 0; i < require.Count; i++)
        {
            if (!string.IsNullOrEmpty(require[i]))
                return false;
        }

        return true;
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
        for (int i = 0; i < buildingList.Count; i++)
            buildingInfoMap[buildingList[i].identifier] = buildingList[i];

        enemyList = CsvUtil.LoadObjects<EnemyInfo>("enemy");
        enemyInfoMap.Clear();
        for (int i = 0; i < enemyList.Count; i++)
            enemyInfoMap[enemyList[i].identifier] = enemyList[i];

        encounterList = CsvUtil.LoadObjects<EncounterInfo>("encounter");
        encounterList.Sort(CompareEncounterTime);
    }

    static int CompareEncounterTime(EncounterInfo a, EncounterInfo b)
    {
        return a.time.CompareTo(b.time);
    }
}
