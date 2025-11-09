// Decompiled with JetBrains decompiler
// Type: SilksongCoop.Patch_Fsm_DoTransition
// Assembly: ClassLibrary1, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 901D39ED-0492-4306-A98E-FB496E06AC71
// Assembly location: D:\Temp\Temp\sk\silksongcoop\SilksongCoop.dll

using HarmonyLib;
using HutongGames.PlayMaker;
using Steamworks;
using UnityEngine.SceneManagement;

#nullable enable
namespace SilklessCoopVisual.Syncs.SilksongCoop;

[HarmonyPatch(typeof(Fsm), "DoTransition")]
public static class Patch_Fsm_DoTransition
{
    private static void Postfix(Fsm instance, FsmTransition? transition)
    {
        if (!SteamCoopPlugin.IsHost() || transition == null)
            return;
        var gameObject = instance.GameObject;
        if (gameObject == null || gameObject.GetComponent<HealthManager>() == null)
            return;
        PlayMakerFSM? playMakerFsm = null;
        foreach (var component in gameObject.GetComponents<PlayMakerFSM>())
        {
            if (component == null || component.Fsm != instance) continue;
            playMakerFsm = component;
            break;
        }

        if (playMakerFsm == null)
            return;
        var orAssignId = EnemyRegistry.GetOrAssignId(gameObject);
        if (string.IsNullOrEmpty(orAssignId))
            return;
        var activeScene = SceneManager.GetActiveScene();
        var name = activeScene.name;
        var component1 = gameObject.GetComponent<tk2dSprite>();
        var flag = false;
        flag = component1 == null
            ? gameObject.transform.localScale.x < 0.0
            : component1.FlipX;
        var enemyFsmState = new SteamCoopPlugin.EnemyFsmState
        {
            id = orAssignId,
            scene = name,
            stateName = transition.ToState ?? instance.ActiveStateName
        };
        SteamCoopPlugin.SendToSceneMembers(
            new SteamCoopPlugin.Packet<SteamCoopPlugin.EnemyFsmState>()
            {
                type = "EnemyFsm",
                payload = enemyFsmState
            }, EP2PSend.k_EP2PSendReliable, name);
    }
}
