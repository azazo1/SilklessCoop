// Decompiled with JetBrains decompiler
// Type: SilksongCoop.Patch_HealthManager_Hit
// Assembly: ClassLibrary1, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 901D39ED-0492-4306-A98E-FB496E06AC71
// Assembly location: D:\Temp\Temp\sk\silksongcoop\SilksongCoop.dll

using HarmonyLib;
using Newtonsoft.Json;
using System.Text;
using Steamworks;
using UnityEngine.SceneManagement;

#nullable enable
namespace SilklessCoopVisual.Syncs.SilksongCoop;

[HarmonyPatch(typeof (HealthManager), "Hit")]
public static class Patch_HealthManager_Hit
{
  private static bool Prefix(HealthManager instance, ref HitInstance hitInstance)
  {
    if (!SteamCoopPlugin.CurrentLobby.IsValid())
      return true;
    if (SteamCoopPlugin.IsHost())
    {
      SteamCoopPlugin.Logger.LogInfo("You are Host");
      return true;
    }
    SteamCoopPlugin.Logger.LogInfo("Client Attack");
    var orAssignId = EnemyRegistry.GetOrAssignId(instance.gameObject);
    if (string.IsNullOrEmpty(orAssignId))
      return true;
    var attackRequest1 = new SteamCoopPlugin.AttackRequest();
    attackRequest1.enemyId = orAssignId;
    var attackRequest2 = attackRequest1;
    var activeScene = SceneManager.GetActiveScene();
    var name = activeScene.name;
    attackRequest2.scene = name;
    attackRequest1.hit = new SteamCoopPlugin.SimpleHit()
    {
      damageDealt = hitInstance.DamageDealt,
      direction = hitInstance.Direction,
      magnitudeMult = hitInstance.MagnitudeMultiplier,
      attackType = (int) hitInstance.AttackType,
      nailElement = (int) hitInstance.NailElement,
      nonLethal = false,
      critical = hitInstance.CriticalHit,
      canWeakHit = hitInstance.CanWeakHit,
      multiplier = hitInstance.Multiplier,
      damageScalingLevel = hitInstance.DamageScalingLevel,
      specialType = (int) hitInstance.SpecialType,
      isHeroDamage = true
    };
    var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new SteamCoopPlugin.Packet<SteamCoopPlugin.AttackRequest>()
    {
      type = "AttackRequest",
      payload = attackRequest1
    }));
    var numLobbyMembers = SteamMatchmaking.GetNumLobbyMembers(SteamCoopPlugin.CurrentLobby);
    for (var index = 0; index < numLobbyMembers; ++index)
    {
      var lobbyMemberByIndex = SteamMatchmaking.GetLobbyMemberByIndex(SteamCoopPlugin.CurrentLobby, index);
      if (lobbyMemberByIndex != SteamUser.GetSteamID())
        SteamNetworking.SendP2PPacket(lobbyMemberByIndex, bytes, (uint) bytes.Length, (EP2PSend) 2, 0);
    }
    return true;
  }

  private static void Postfix(
    HealthManager instance,
    HitInstance hitInstance,
    IHitResponder.HitResponse result)
  {
    if (!SteamCoopPlugin.CurrentLobby.IsValid() || !SteamCoopPlugin.IsHost())
      return;
    var orAssignId = EnemyRegistry.GetOrAssignId(instance.gameObject);
    if (string.IsNullOrEmpty(orAssignId))
      return;
    var enemyDelta1 = new SteamCoopPlugin.EnemyDelta();
    enemyDelta1.id = orAssignId;
    enemyDelta1.hp = instance.hp;
    enemyDelta1.dead = instance.GetIsDead();
    var enemyDelta2 = enemyDelta1;
    var activeScene = SceneManager.GetActiveScene();
    var name = activeScene.name;
    enemyDelta2.scene = name;
    var packet = new SteamCoopPlugin.Packet<SteamCoopPlugin.EnemyDelta>();
    packet.type = "EnemyDelta";
    packet.payload = enemyDelta1;
    SteamCoopPlugin.Logger.LogInfo($"[Hit] Enemy {orAssignId} hp={enemyDelta1.hp} dead={enemyDelta1.dead} sent to peers in scene {enemyDelta1.scene}");
    var scene = enemyDelta1.scene;
    var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(packet));
    var numLobbyMembers = SteamMatchmaking.GetNumLobbyMembers(SteamCoopPlugin.CurrentLobby);
    for (var index = 0; index < numLobbyMembers; ++index)
    {
      var lobbyMemberByIndex = SteamMatchmaking.GetLobbyMemberByIndex(SteamCoopPlugin.CurrentLobby, index);
      if (lobbyMemberByIndex == SteamUser.GetSteamID()) continue;
      if (SteamCoopPlugin.PeerScene.TryGetValue(lobbyMemberByIndex.m_SteamID, out var str))
      {
        if (str == scene)
          SteamNetworking.SendP2PPacket(lobbyMemberByIndex, bytes, (uint) bytes.Length, 0, 0);
      }
      else
        SteamNetworking.SendP2PPacket(lobbyMemberByIndex, bytes, (uint) bytes.Length, 0, 0);
    }
  }
}
