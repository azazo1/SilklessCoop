// Decompiled with JetBrains decompiler
// Type: SilksongCoop.EnemyRegistry
// Assembly: ClassLibrary1, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 901D39ED-0492-4306-A98E-FB496E06AC71
// Assembly location: D:\Temp\Temp\sk\silksongcoop\SilksongCoop.dll

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#nullable enable
namespace SilklessCoopVisual.Syncs.SilksongCoop;

public static class EnemyRegistry
{
    private static readonly Dictionary<string, GameObject> id2go = new Dictionary<string, GameObject>();
    private static readonly Dictionary<GameObject, string> go2id = new Dictionary<GameObject, string>();
    private static float lastRescanTime = -10f;

    public static void RefreshAllIds()
    {
        id2go.Clear();
        go2id.Clear();
        var healthManagerList = new List<HealthManager>(HealthManager.EnumerateActiveEnemies());
        foreach (var healthManager in healthManagerList)
        {
            if (healthManager != null)
                GetOrAssignId(healthManager.gameObject);
        }

        foreach (var healthManager in Object.FindObjectsByType<HealthManager>(FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (healthManager != null)
                GetOrAssignId(healthManager.gameObject);
        }
    }

    public static string GetOrAssignId(GameObject go)
    {
        if (go == null)
            return null;
        string orAssignId;
        if (go2id.TryGetValue(go, out orAssignId))
            return orAssignId;
        var key = BuildStableId(go);
        id2go[key] = go;
        go2id[go] = key;
        return key;
    }

    public static GameObject FindById(string? id, string expectedScene)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        GameObject byId;
        if (id2go.TryGetValue(id, out byId) && byId != null)
            return byId;
        var str = expectedScene;
        var activeScene = SceneManager.GetActiveScene();
        var name = activeScene.name;
        if (str != name)
            return null;
        var realtimeSinceStartup = Time.realtimeSinceStartup;
        if (realtimeSinceStartup - (double)lastRescanTime < 0.5)
            return null;
        lastRescanTime = realtimeSinceStartup;
        RefreshAllIds();
#pragma warning disable CS8603 // Possible null reference return.
        return id2go.TryGetValue(id, out byId) && byId != null
            ? byId
            : null;
#pragma warning restore CS8603 // Possible null reference return.
    }

    private static string BuildStableId(GameObject go)
    {
        var scene1 = go.scene;
        string str;
        if (!scene1.IsValid())
        {
            str = "noscn";
        }
        else
        {
            var scene2 = go.scene;
            str = scene2.name;
        }

        return $"{str}:{GetPath(go.transform)}";
    }

    private static string GetPath(Transform t)
    {
        var stringList = new List<string>();
        for (var transform = t; transform != null; transform = transform.parent)
        {
            stringList.Add($"{transform.name}#{transform.GetSiblingIndex().ToString()}");
        }

        stringList.Reverse();
        return string.Join("/", stringList.ToArray());
    }
}
