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

public class CSVLoader : Singleton<CSVLoader>
{
    public Dictionary<string, BuildingInfo> buildingInfoMap = new Dictionary<string, BuildingInfo>();
    public List<BuildingInfo> buildingList = new List<BuildingInfo>();

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
    }
}
