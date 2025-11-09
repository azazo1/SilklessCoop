// Decompiled with JetBrains decompiler
// Type: SilksongCoop.SteamCoopPlugin
// Assembly: ClassLibrary1, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 901D39ED-0492-4306-A98E-FB496E06AC71
// Assembly location: D:\Temp\Temp\sk\silksongcoop\SilksongCoop.dll

using BepInEx;
using BepInEx.Logging;
using GlobalEnums;
using HarmonyLib;
using HutongGames.PlayMaker;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ReSharper disable InconsistentNaming
#nullable enable
namespace SilklessCoopVisual.Syncs.SilksongCoop;

[BepInPlugin("com.silksong.coop.steamcoop", "Silksong Coop Steam Plugin", "3.4.0")] // todo delete
public class SteamCoopPlugin : BaseUnityPlugin
{

    private Callback<LobbyCreated_t> lobbyCreated;
    private Callback<LobbyEnter_t> lobbyEnter;
    private Callback<GameLobbyJoinRequested_t> lobbyJoinRequest;
    private Callback<LobbyChatUpdate_t> lobbyChatUpdate;
    public static CSteamID CurrentLobby;
    private HeroController hero;
    private tk2dSpriteAnimator heroAnim;
    private GameObject partner;
    private tk2dSpriteAnimator partnerAnim;
    private PlayerState lastPartnerState;
    private float playerSyncTimer;
    private float enemyDeltaTimer;
    private float enemyFullTimer;
    private float presenceTimer;
    public const string MSG_PLAYER_DEATH = "PlayerDeath";
    public const string MSG_ENEMY_FSM = "EnemyFsm";
    public const string MSG_CHAT = "Chat";
    private const string MSG_PLAYER = "PlayerState";
    private const string MSG_ATTACK = "AttackRequest";
    private const string MSG_ENEMY_DELTA = "EnemyDelta";
    private const string MSG_ENEMY_FULL = "EnemyFull";
    private const string MSG_SCENE = "ScenePresence";
    private const string MSG_TELEPORT = "TeleportRequest";
    private GameObject partnerPointer;
    private Vector3? pendingTeleportPos;
    private string? pendingTeleportScene;
    public static bool suppressDeathBroadcast = false;
    public bool partnerLastFaceLeft = false;
    private static Dictionary<string, string> lastEnemyStates = new Dictionary<string, string>();
    internal static Dictionary<string, Queue<string>> TempFsmQueues = new Dictionary<string, Queue<string>>();
    internal static readonly Dictionary<ulong, string> PeerScene = new Dictionary<ulong, string>();
    internal static readonly Dictionary<string, ulong> SceneLastEntrant = new Dictionary<string, ulong>();
    private GameObject partnerNameTag;
    internal static readonly Dictionary<string, Vector3> lastEnemyPositions = new Dictionary<string, Vector3>();
    private int lobbyRetryCount = 0;
    private const int MaxLobbyRetries = 3;

    public static ManualLogSource s_Logger;

    private static string CurrentSceneName
    {
        get
        {
            var activeScene = SceneManager.GetActiveScene();
            return activeScene.name;
        }
    }

    private void Awake()
    {
        s_Logger = Logger;
        if (!SteamAPI.Init())
        {
            Logger.LogError("Steam initialization failed.");
        }
        else
        {
            Logger.LogInfo("Steam initialized successfully. User: " +
                           SteamFriends.GetPersonaName());
            // ISSUE: method pointer
            lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            // ISSUE: method pointer
            lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            // ISSUE: method pointer
            lobbyJoinRequest = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            // ISSUE: method pointer
            lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            new Harmony("com.silksong.coop.harmony").PatchAll();
            // ISSUE: method pointer
            SceneManager.sceneLoaded += OnSceneLoaded;
            SendScenePresence(true);
        }
    }

    private void OnDestroy()
    {
        // ISSUE: method pointer
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SteamAPI.Shutdown();
        Logger.LogInfo("Steam shutdown.");
    }

    private void Update()
    {
        SteamAPI.RunCallbacks();
        if (Input.GetKeyDown((KeyCode)288))
        {
            SteamMatchmaking.CreateLobby((ELobbyType)1, 20);
            Logger.LogInfo("Creating Lobby...");
        }

        if (Input.GetKeyDown((KeyCode)289))
            TryLeaveLobby();
        if (Input.GetKeyDown((KeyCode)290))
        {
            if (lastPartnerState == null)
                Logger.LogWarning("[Teleport] No partner state available, cannot teleport.");
            else if (lastPartnerState.scene == CurrentSceneName)
            {
                Logger.LogInfo("[Teleport] Teleporting self to partner in same scene");
                hero.transform.position = lastPartnerState.position;
            }
            else
            {
                Logger.LogInfo("[Teleport] Teleporting self across scene to partner");
                pendingTeleportPos = lastPartnerState.position;
                pendingTeleportScene = lastPartnerState.scene;
                GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo
                {
                    SceneName = lastPartnerState.scene,
                    EntryGateName = string.IsNullOrEmpty(lastPartnerState.entryGate)
                        ? "door_dreamReturn"
                        : lastPartnerState.entryGate,
                    PreventCameraFadeOut = false,
                    WaitForSceneTransitionCameraFade = true,
                    Visualization = 0,
                    IsFirstLevelForPlayer = false,
                    AlwaysUnloadUnusedAssets = true
                });
            }
        }

        if (Input.GetKeyDown((KeyCode)291) && hero != null)
        {
            Logger.LogInfo("[Teleport] Sending teleport request to bring partner here");
            var teleportRequest = new TeleportRequest
            {
                targetPos = hero.transform.position,
                scene = CurrentSceneName,
                entryGate = GameManager.instance.entryGateName ?? "door_dreamReturn"
            };
            SendToAll(
                new Packet<TeleportRequest>
                {
                    type = "TeleportRequest",
                    payload = teleportRequest
                }, (EP2PSend)2);
        }

        if (hero == null)
        {
            hero = FindObjectOfType<HeroController>();
            if (hero != null)
                heroAnim = hero.GetComponentInChildren<tk2dSpriteAnimator>();
        }

        if (IsLobbyValid())
        {
            presenceTimer += Time.deltaTime;
            if (presenceTimer >= 2.0)
            {
                presenceTimer = 0.0f;
                SendScenePresence(false);
            }
        }

        if (hero != null && heroAnim != null && IsLobbyValid())
        {
            playerSyncTimer += Time.deltaTime;
            if (playerSyncTimer >= 0.0099999997764825821)
            {
                playerSyncTimer = 0.0f;
                var playerState = new PlayerState
                {
                    position = hero.transform.position,
                    animation = heroAnim.CurrentClip != null ? heroAnim.CurrentClip.name : "Idle",
                    scene = CurrentSceneName,
                    entryGate = GameManager.instance.entryGateName ?? "door_dreamReturn"
                };
                var flag = partnerLastFaceLeft;
                if (Input.GetKey((KeyCode)97) && !Input.GetKey((KeyCode)100))
                {
                    flag = true;
                    partnerLastFaceLeft = true;
                }
                else if (Input.GetKey((KeyCode)100) && !Input.GetKey((KeyCode)97))
                {
                    flag = false;
                    partnerLastFaceLeft = false;
                }

                playerState.facingLeft = flag;
                SendToAll(
                    new Packet<PlayerState>
                    {
                        type = "PlayerState",
                        payload = playerState
                    }, (EP2PSend)2);
            }
        }

        uint length;
        while (SteamNetworking.IsP2PPacketAvailable(out length))
        {
            var bytes = new byte[(int)length];
            uint count;
            CSteamID remote;
            if (SteamNetworking.ReadP2PPacket(bytes, length, out count, out remote))
            {
                var str1 = Encoding.UTF8.GetString(bytes, 0, (int)count);
                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(str1);
                if (dictionary != null && dictionary.ContainsKey("type"))
                {
                    var str2 = dictionary["type"]?.ToString();
                    int num;
                    switch (str2)
                    {
                        case "PlayerState":
                            ApplyPartner(JsonConvert.DeserializeObject<Packet<PlayerState>>(str1).payload); // todo
                            goto label_68;
                        case "AttackRequest":
                            if (IsHost())
                            {
                                var packet = JsonConvert.DeserializeObject<Packet<AttackRequest>>(str1);
                                if (packet != null && packet.payload != null &&
                                    packet.payload.scene == CurrentSceneName)
                                    HandleAttackRequest(packet.payload, remote);
                            }

                            goto label_68;
                        case "EnemyDelta":
                            num = 1;
                            break;
                        default:
                            num = str2 == "EnemyFull" ? 1 : 0;
                            break;
                    }

                    if (num != 0)
                    {
                        var packet =
                            JsonConvert.DeserializeObject<Packet<EnemyDelta>>(str1);
                        if (packet != null && packet.payload != null &&
                            packet.payload.scene == CurrentSceneName)
                            EnemySync.ApplyEnemyDelta(packet.payload);
                    }
                    else
                    {
                        switch (str2)
                        {
                            case "ScenePresence":
                                var packet1 = JsonConvert.DeserializeObject<Packet<ScenePresence>>(str1);
                                if (packet1 != null && packet1.payload != null)
                                {
                                    PeerScene[remote.m_SteamID] = packet1.payload.scene;
                                }

                                break;
                            case "PlayerDeath":
                                var packet2 = JsonConvert.DeserializeObject<Packet<PlayerDeath>>(str1);
                                if (packet2 != null && packet2.payload != null)
                                {
                                    Logger.LogInfo(
                                        "[Sync] Received death event from teammate.");
                                    ForceLocalDeath();
                                }

                                break;
                            case "TeleportRequest":
                                var packet3 = JsonConvert.DeserializeObject<Packet<TeleportRequest>>(str1);
                                if (packet3 != null && packet3.payload != null)
                                {
                                    if (packet3.payload.scene == CurrentSceneName)
                                    {
                                        Logger.LogInfo(
                                            "[Teleport] Teleporting to position in current scene");
                                        if (hero != null)
                                            hero.transform.position = packet3.payload.targetPos;
                                    }
                                    else
                                    {
                                        Logger.LogInfo("[Teleport] Changing scene to " +
                                                       packet3.payload.scene);
                                        pendingTeleportPos = packet3.payload.targetPos;
                                        pendingTeleportScene = packet3.payload.scene;
                                        GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo
                                        {
                                            SceneName = packet3.payload.scene,
                                            EntryGateName = string.IsNullOrEmpty(packet3.payload.entryGate)
                                                ? "door_dreamReturn"
                                                : packet3.payload.entryGate,
                                            PreventCameraFadeOut = false,
                                            WaitForSceneTransitionCameraFade = true,
                                            Visualization = 0,
                                            IsFirstLevelForPlayer = false,
                                            AlwaysUnloadUnusedAssets = true
                                        });
                                    }
                                }

                                break;
                            case "EnemyFsm":
                                var packet4 = JsonConvert.DeserializeObject<Packet<EnemyFsmState>>(str1);
                                if (packet4?.payload != null &&
                                    packet4.payload.scene == CurrentSceneName)
                                {
                                    ApplyEnemyFsm(packet4.payload);
                                }

                                break;
                            case "EnemyFsmTransition":
                                var packet5 = JsonConvert.DeserializeObject<Packet<EnemyFsmTransition>>(str1);
                                if (packet5?.payload != null &&
                                    packet5.payload.scene == CurrentSceneName)
                                {
                                    var byId = EnemyRegistry.FindById(packet5.payload.id, packet5.payload.scene);
                                    if (byId != null)
                                    {
                                        var component = byId.GetComponent<PlayMakerFSM>();
                                        if (component != null)
                                        {
                                            if (!string.IsNullOrEmpty(packet5.payload.eventName))
                                                component.SendEvent(packet5.payload.eventName);
                                            if (component.Fsm.ActiveStateName != packet5.payload.targetState &&
                                                !string.IsNullOrEmpty(packet5.payload.targetState))
                                            {
                                                Logger.LogWarning(
                                                    $"[FSM Sync] Forcing {byId.name} to {packet5.payload.targetState}");
                                                component.SetState(packet5.payload.targetState);
                                            }
                                        }
                                    }
                                }

                                break;
                            case "SceneEnter":
                                var packet6 = JsonConvert.DeserializeObject<Packet<ScenePresence>>(str1);
                                if (packet6?.payload != null)
                                {
                                    var steamId = remote.m_SteamID;
                                    var scene = packet6.payload.scene;
                                    SceneLastEntrant[scene] = steamId;
                                    Logger.LogInfo(
                                        $"[SceneHost] Scene {scene} last entrant updated → {steamId}");
                                }

                                break;
                        }
                    }

                    label_68: ;
                }
            }
            else
                break;
        }

        if (!IsHost())
            return;
        enemyDeltaTimer += Time.deltaTime;
        enemyDeltaTimer = 0.0f;
        foreach (var enumerateActiveEnemy in HealthManager.EnumerateActiveEnemies())
        {
            if (enumerateActiveEnemy != null)
                BroadcastEnemyState(enumerateActiveEnemy.gameObject);
        }
    }

    public static void DumpFsmVariables(PlayMakerFSM fsm)
    {
        if (fsm == null)
        {
            Debug.LogWarning("[FSM Dump] fsm is null");
        }
        else
        {
            var timeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var str1 = DateTime.Now.ToString("HH:mm:ss.fff");
            Debug.Log($"[FSM Dump] ===== {fsm.gameObject.name}/{fsm.FsmName} =====");
            Debug.Log($"[FSM Dump] Timestamp(ms) = {timeMilliseconds}, LocalTime = {str1}");
            foreach (var allNamedVariable in fsm.FsmVariables.GetAllNamedVariables())
            {
                string str2;
                switch (allNamedVariable)
                {
                    case FsmFloat fsmFloat:
                        str2 = fsmFloat.Value.ToString("F3");
                        break;
                    case FsmInt fsmInt:
                        str2 = fsmInt.Value.ToString();
                        break;
                    case FsmBool fsmBool:
                        str2 = fsmBool.Value.ToString();
                        break;
                    case FsmString fsmString:
                        str2 = fsmString.Value;
                        break;
                    case FsmVector2 fsmVector2:
                        str2 = fsmVector2.Value.ToString();
                        break;
                    case FsmVector3 fsmVector3:
                        str2 = fsmVector3.Value.ToString();
                        break;
                    case FsmGameObject fsmGameObject:
                        str2 = fsmGameObject.Value?.name ?? "null";
                        break;
                    default:
                        str2 = "(unsupported type)";
                        break;
                }

                Debug.Log($"[FSM Var] {allNamedVariable.Name} = {str2}");
            }
        }
    }

    public static Dictionary<string, string> CollectImportantFsmVars(PlayMakerFSM fsm)
    {
        var dictionary = new Dictionary<string, string>();
        foreach (var allNamedVariable in fsm.FsmVariables.GetAllNamedVariables())
        {
            string str;
            switch (allNamedVariable)
            {
                case FsmFloat fsmFloat:
                    str = fsmFloat.Value.ToString("F3");
                    break;
                case FsmInt fsmInt:
                    str = fsmInt.Value.ToString();
                    break;
                case FsmBool fsmBool:
                    str = fsmBool.Value.ToString();
                    break;
                case FsmString fsmString:
                    str = fsmString.Value;
                    break;
                case FsmVector2 fsmVector2:
                    str = fsmVector2.Value.ToString();
                    break;
                case FsmVector3 fsmVector3:
                    str = fsmVector3.Value.ToString();
                    break;
                default:
                    continue;
            }

            if (allNamedVariable.Name == "Self X" || allNamedVariable.Name == "Centre X" ||
                allNamedVariable.Name == "Facing Right" || allNamedVariable.Name == "Hero Is Right" ||
                allNamedVariable.Name == "Lace Is Right" || allNamedVariable.Name == "Ct Charge" ||
                allNamedVariable.Name == "Ct Combo" || allNamedVariable.Name == "Ct CrossSlash" ||
                allNamedVariable.Name == "Ms Charge" || allNamedVariable.Name == "Ms Combo" ||
                allNamedVariable.Name == "Ms J Slash" || allNamedVariable.Name == "Not Above Hero" ||
                allNamedVariable.Name == "Below Ground" || allNamedVariable.Name == "Next Event" ||
                allNamedVariable.Name == "Stun Timer" || allNamedVariable.Name == "Rage HP" ||
                allNamedVariable.Name == "Counter Pause" || allNamedVariable.Name == "Distance" ||
                allNamedVariable.Name == "Facing Right" || allNamedVariable.Name == "Hero Is Right")
                dictionary[allNamedVariable.Name] = str;
        }

        return dictionary;
    }

    public static void ApplyEnemyFsm(EnemyFsmState state)
    {
        var byId = EnemyRegistry.FindById(state.id, state.scene);
        if (byId == null)
            return;
        var component = byId.GetComponent<PlayMakerFSM>();
        if (component == null)
            return;
        if (!IsHost())
            component.enabled = true;
        if (component.Fsm.ActiveStateName == state.stateName)
            return;
        component.SetState(state.stateName);
    }

    public static bool IsLastEntrantOfScene(string scene = null)
    {
        if (string.IsNullOrEmpty(scene))
        {
            var activeScene = SceneManager.GetActiveScene();
            scene = activeScene.name;
        }

        ulong num;
        return !SceneLastEntrant.TryGetValue(scene, out num) ||
               (long)num == (long)SteamUser.GetSteamID().m_SteamID;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnemyRegistry.RefreshAllIds();
        var steamId = SteamUser.GetSteamID().m_SteamID;
        SceneLastEntrant[scene.name] = steamId;
        Logger.LogInfo((object)$"[SceneHost] Player {steamId} entered {scene.name}");
        SendToAll(
            new Packet<ScenePresence>
            {
                type = "SceneEnter",
                payload = new ScenePresence
                {
                    scene = scene.name
                }
            }, (EP2PSend)2);
        SendScenePresence(true);
        ForceSendPlayerState();
        if (string.IsNullOrEmpty(pendingTeleportScene) || scene.name != pendingTeleportScene)
            return;
        StartCoroutine(ApplyPendingTeleportWhenReady());
    }

    private IEnumerator ApplyPendingTeleportWhenReady()
    {
        var gm = GameManager.instance;
        var safetyFrames = 300;
        while (gm != null &&
               (gm.IsInSceneTransition || !gm.HasFinishedEnteringScene) && safetyFrames-- > 0)
            yield return null;
        yield return new WaitForEndOfFrame();
        int num;
        if (hero != null && pendingTeleportPos.HasValue)
        {
            var activeScene = SceneManager.GetActiveScene();
            num = activeScene.name == pendingTeleportScene ? 1 : 0;
        }
        else
            num = 0;

        if (num != 0)
        {
            Logger.LogInfo(
                $"[Teleport] Applying pending teleport at {pendingTeleportPos}");
            if (pendingTeleportPos != null && hero != null)
            {
                hero.transform.position = pendingTeleportPos.Value;
            }
        }

        pendingTeleportScene = null;
        pendingTeleportPos = new Vector3?();
    }

    private void ForceLocalDeath()
    {
        Logger.LogInfo("[DeathSync] Forcing local death (via TakeDamage)");
        suppressDeathBroadcast = true;
        try
        {
            var instance = HeroController.instance;
            if (instance != null)
            {
                instance.TakeDamage(null, (CollisionSide)4, 9999, 0, 0);
                Logger.LogInfo("[DeathSync] TakeDamage invoked, should trigger death");
            }
            else
                Logger.LogError("[DeathSync] HeroController.instance is null");
        }
        catch (Exception ex)
        {
            Logger.LogError("[DeathSync] Exception in ForceLocalDeath: " + ex?.ToString());
        }
        finally
        {
            suppressDeathBroadcast = false;
        }
    }

    private void TryLeaveLobby()
    {
        if (!IsLobbyValid())
        {
            Logger.LogWarning("Not in any Lobby, ignoring leave request.");
        }
        else
        {
            try
            {
                var numLobbyMembers = SteamMatchmaking.GetNumLobbyMembers(CurrentLobby);
                for (var index = 0; index < numLobbyMembers; ++index)
                {
                    var lobbyMemberByIndex = SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobby, index);
                    if (lobbyMemberByIndex != SteamUser.GetSteamID())
                        SteamNetworking.CloseP2PSessionWithUser(lobbyMemberByIndex);
                }

                Logger.LogInfo($"Leaving Lobby: {CurrentLobby.m_SteamID}");
                SteamMatchmaking.LeaveLobby(CurrentLobby);
            }
            catch (Exception ex)
            {
                Logger.LogError("Exception while leaving Lobby: " + ex?.ToString());
            }

            CurrentLobby = new CSteamID(0UL);
            PeerScene.Clear();
            if (partner != null)
            {
                try
                {
                    Destroy(partner);
                }
                catch
                {
                    // ignored
                }

                partner = null;
                partnerAnim = null;
            }

            presenceTimer = 0.0f;
            playerSyncTimer = 0.0f;
            Logger.LogInfo("Left Lobby and cleaned up local state.");
        }
    }

    private void UpdatePartnerPointer(Vector3 partnerWorldPos, string partnerScene)
    {
        if (partnerPointer == null)
        {
            Debug.LogWarning("[PartnerPointer] partnerPointer is null");
        }
        else
        {
            var main = Camera.main;
            if (main == null)
            {
                Debug.LogWarning("[PartnerPointer] Camera.main is null");
                partnerPointer.SetActive(false);
            }
            else
            {
                var viewportPoint = main.WorldToViewportPoint(partnerWorldPos);
                var flag = viewportPoint.z > 0.0 && viewportPoint.x > 0.0 &&
                           viewportPoint.x < 1.0 && viewportPoint.y > 0.0 &&
                           viewportPoint.y < 1.0;
                partnerPointer.SetActive(!flag);
                if (flag)
                    return;
                var vector3_1 = partnerWorldPos - main.transform.position;
                var normalized = vector3_1.normalized;
                var num = Mathf.Atan2(normalized.y, normalized.x) * Mathf.Rad2Deg;
                var vector3_2 = new Vector3(
                    Mathf.Clamp(viewportPoint.x, 0.05f, 0.95f),
                    Mathf.Clamp(viewportPoint.y, 0.05f, 0.95f),
                    0.0f
                );
                var worldPoint = main.ViewportToWorldPoint(new Vector3(vector3_2.x, vector3_2.y, viewportPoint.z));

                Vector3 vector3_3 = RectTransformUtility.WorldToScreenPoint(main, worldPoint);
                var component = partnerPointer.GetComponent<RectTransform>();
                if (component != null)
                {
                    component.position = vector3_3;
                    component.rotation = Quaternion.Euler(0.0f, 0.0f, num);
                }
                else
                    Debug.LogWarning("[PartnerPointer] partnerPointer has no RectTransform");
            }
        }
    }

    private void SendScenePresence(bool immediate)
    {
        if (!IsLobbyValid())
            return;
        SendToAll(new Packet<ScenePresence>
        {
            type = "ScenePresence",
            payload = new ScenePresence
            {
                scene = CurrentSceneName
            }
        }, EP2PSend.k_EP2PSendUnreliable);
    }

    private void ApplyPartner(PlayerState s)
    {
        lastPartnerState = s;
        if (s == null || s.scene != CurrentSceneName)
        {
            if (partner != null && partner.activeSelf)
                partner.SetActive(false);
            if (partnerPointer == null)
                return;
            partnerPointer.SetActive(false);
        }
        else
        {
            if (partner == null)
                CreatePartner();
            if (partner == null)
                return;
            if (!partner.activeSelf)
                partner.SetActive(true);
            if (partnerAnim == null)
                return;
            partner.transform.position = s.position;
            var str = s.animation ?? "Idle";
            if (partnerAnim.CurrentClip == null || partnerAnim.CurrentClip.name != str)
                partnerAnim.Play(str);
            var component = partner.GetComponent<tk2dSprite>();
            if (component != null)
                component.FlipX = !s.facingLeft;
            UpdatePartnerPointer(s.position, s.scene);
        }
    }

    private void CreatePartner()
    {
        if (hero == null)
            return;
        partner = new GameObject("PartnerClone");
        var componentInChildren = hero.GetComponentInChildren<tk2dSprite>();
        if (componentInChildren != null)
        {
            var tk2dSprite = partner.AddComponent<tk2dSprite>();
            if (tk2dSprite != null)
                tk2dSprite.color = new Color(0.6f, 0.8f, 1f, 1f);
            var field =
                typeof(tk2dBaseSprite).GetField("collection", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                var obj = field.GetValue(componentInChildren);
                field.SetValue(tk2dSprite, obj);
            }

            tk2dSprite.spriteId = componentInChildren.spriteId;
        }

        if (heroAnim != null)
        {
            partnerAnim = partner.AddComponent<tk2dSpriteAnimator>();
            partnerAnim.Library = heroAnim.Library;
        }

        CreatePartnerPointer();
        Logger.LogInfo("Partner clone created (scene-aware).");
    }

    private void CreatePartnerPointer()
    {
        if (partnerPointer != null)
            return;
        var objectOfType = FindObjectOfType<Canvas>();
        if (objectOfType == null)
            return;
        partnerPointer = new GameObject("PartnerPointer");
        partnerPointer.transform.SetParent(objectOfType.transform);
        partnerPointer.AddComponent<RectTransform>().sizeDelta = new Vector2(40f, 40f);
        var image = partnerPointer.AddComponent<Image>();
        var sprite = LoadCustomSprite("TeammateIcon.png");
        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
            Debug.Log("[PartnerPointer] Successfully loaded custom icon.");
        }
        else
        {
            Debug.LogWarning("[PartnerPointer] Could not find TeammateIcon.png, using cyan square instead.");
            image.color = Color.cyan;
        }

        partnerPointer.SetActive(false);
    }

    private Sprite LoadCustomSprite(string fileName)
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(SteamCoopPlugin).Assembly.Location), fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning("[PartnerPointer] File not found: " + path);
                return null;
            }

            var numArray = File.ReadAllBytes(path);
            var texture2D = new Texture2D(2, 2, (TextureFormat)5, false);
            if (ImageConversion.LoadImage(texture2D, numArray))
                return Sprite.Create(texture2D,
                    new Rect(0.0f, 0.0f, texture2D.width, texture2D.height),
                    new Vector2(0.5f, 0.5f));
            Debug.LogError("[PartnerPointer] LoadImage failed.");
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError("[PartnerPointer] Failed to load custom icon: " + ex?.ToString());
            return null;
        }
    }

    private void HandleAttackRequest(AttackRequest req, CSteamID remote)
    {
        var byId = EnemyRegistry.FindById(req.enemyId, req.scene);
        if (byId == null)
            return;
        var component = byId.GetComponent<HealthManager>();
        if (component == null || !component.isActiveAndEnabled)
            return;
        component.Hit(new HitInstance
        {
            Source = hero != null
                ? hero.gameObject
                : null,
            AttackType = (AttackTypes)req.hit.attackType,
            NailElement = (NailElements)req.hit.nailElement,
            DamageDealt = req.hit.damageDealt,
            Direction = req.hit.direction,
            MagnitudeMultiplier = req.hit.magnitudeMult,
            NonLethal = req.hit.nonLethal,
            CriticalHit = req.hit.critical,
            CanWeakHit = req.hit.canWeakHit,
            Multiplier = req.hit.multiplier,
            DamageScalingLevel = req.hit.damageScalingLevel,
            SpecialType = (SpecialTypes)req.hit.specialType,
            IsHeroDamage = req.hit.isHeroDamage
        });
        BroadcastEnemyDelta(component);
    }

    private static bool IsLobbyValid() => SteamCoopPlugin.CurrentLobby.IsValid();

    public static bool IsHost() => !IsLastEntrantOfScene();

    internal static void SendToAll<T>(Packet<T> p, EP2PSend sendType)
    {
        if (!IsLobbyValid())
            return;
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(p));
        var numLobbyMembers = SteamMatchmaking.GetNumLobbyMembers(CurrentLobby);
        for (var index = 0; index < numLobbyMembers; ++index)
        {
            var lobbyMemberByIndex = SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobby, index);
            if (lobbyMemberByIndex != SteamUser.GetSteamID())
                SteamNetworking.SendP2PPacket(lobbyMemberByIndex, bytes, (uint)bytes.Length, sendType, 0);
        }
    }

    public static void SendToSceneMembers<T>(
        Packet<T> p,
        EP2PSend sendType,
        string scene)
    {
        if (!IsLobbyValid())
            return;
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(p));
        var numLobbyMembers = SteamMatchmaking.GetNumLobbyMembers(CurrentLobby);
        for (var index = 0; index < numLobbyMembers; ++index)
        {
            var lobbyMemberByIndex = SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobby, index);
            if (lobbyMemberByIndex != SteamUser.GetSteamID())
            {
                string str;
                if (PeerScene.TryGetValue(lobbyMemberByIndex.m_SteamID, out str))
                {
                    if (str == scene)
                        SteamNetworking.SendP2PPacket(lobbyMemberByIndex, bytes, (uint)bytes.Length, sendType, 0);
                }
                else
                    SteamNetworking.SendP2PPacket(lobbyMemberByIndex, bytes, (uint)bytes.Length, sendType, 0);
            }
        }
    }

    public static void BroadcastEnemyState(GameObject go)
    {
        if (go == null ||
            go.GetComponent<HealthManager>() == null)
            return;
        var orAssignId = EnemyRegistry.GetOrAssignId(go);
        var position = go.transform.position;
        var flag = false;
        Vector3 vector3;
        if (lastEnemyPositions.TryGetValue(orAssignId, out vector3))
        {
            var num = position.x - vector3.x;
            if (Mathf.Abs(num) > 0.0099999997764825821)
                flag = num < 0.0;
        }

        lastEnemyPositions[orAssignId] = position;
        var component = go.GetComponent<tk2dSpriteAnimator>();
        var enemyDelta = new EnemyDelta
        {
            id = EnemyRegistry.GetOrAssignId(go),
            pos = go.transform.position,
            anim = component?.CurrentClip?.name ?? "Idle",
            scene = CurrentSceneName,
            facingLeft = flag
        };
        SendToSceneMembers(
            new Packet<EnemyDelta>
            {
                type = "EnemyDelta",
                payload = enemyDelta
            }, EP2PSend.k_EP2PSendUnreliable, enemyDelta.scene);
    }

    private void BroadcastEnemyDelta(HealthManager hm)
    {
        var orAssignId = EnemyRegistry.GetOrAssignId(hm.gameObject);
        if (string.IsNullOrEmpty(orAssignId))
            return;
        var enemyDelta = new EnemyDelta();
        enemyDelta.id = orAssignId;
        enemyDelta.hp = hm.hp;
        enemyDelta.dead = hm.GetIsDead();
        enemyDelta.scene = CurrentSceneName;
        var p = new Packet<EnemyDelta>();
        p.type = "EnemyDelta";
        p.payload = enemyDelta;
        Logger.LogInfo(
            $"[BroadcastEnemyDelta] Enemy {orAssignId} hp={enemyDelta.hp} dead={enemyDelta.dead} sent to peers in scene {enemyDelta.scene}");
        SendToSceneMembers(p, EP2PSend.k_EP2PSendUnreliable, enemyDelta.scene);
    }

    private void OnLobbyCreated(LobbyCreated_t cb)
    {
        if (cb.m_eResult == EResult.k_EResultOK)
        {
            CurrentLobby = new CSteamID(cb.m_ulSteamIDLobby);
            Logger.LogInfo("Lobby created: " + CurrentLobby.m_SteamID);
            SendScenePresence(true);
            lobbyRetryCount = 0;
        }
        else
        {
            Logger.LogError("Lobby creation failed: " + cb.m_eResult);
            if (lobbyRetryCount < 3)
            {
                ++lobbyRetryCount;
                Logger.LogWarning($"[Lobby] Retry {lobbyRetryCount}/{3}...");
                StartCoroutine(RetryCreateLobbyAfterDelay(1f));
            }
            else
            {
                Logger.LogError("[Lobby] Failed to create lobby after multiple attempts.");
                lobbyRetryCount = 0;
            }
        }
    }

    private IEnumerator RetryCreateLobbyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SteamMatchmaking.CreateLobby((ELobbyType)1, 2);
    }

    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t cb)
    {
        Logger.LogInfo((object)$"[Lobby] Received join request for lobby {cb.m_steamIDLobby}");
        if (IsLobbyValid())
        {
            Logger.LogInfo(
                (object)$"[Lobby] Already in a lobby ({CurrentLobby}), leaving first...");
            TryLeaveLobby();
        }

        SteamMatchmaking.JoinLobby(cb.m_steamIDLobby);
    }

    private void OnLobbyEnter(LobbyEnter_t cb)
    {
        CurrentLobby = new CSteamID(cb.m_ulSteamIDLobby);
        Logger.LogInfo("Entered Lobby: " + CurrentLobby.m_SteamID.ToString());
        EnemyRegistry.RefreshAllIds();
        SendScenePresence(true);
        ForceSendPlayerState();
    }

    private void OnLobbyChatUpdate(LobbyChatUpdate_t cb)
    {
        Logger.LogInfo("Lobby member update: " + cb.m_ulSteamIDUserChanged.ToString());
    }

    private void ForceSendPlayerState()
    {
        if (hero == null ||
            heroAnim == null || !IsLobbyValid())
            return;
        var playerState = new PlayerState
        {
            position = hero.transform.position,
            animation = heroAnim.CurrentClip != null ? heroAnim.CurrentClip.name : "Idle",
            scene = CurrentSceneName,
            entryGate = GameManager.instance.entryGateName ?? "door_dreamReturn",
            facingLeft = partnerLastFaceLeft
        };
        SendToAll(new Packet<PlayerState>
        {
            type = "PlayerState",
            payload = playerState
        }, (EP2PSend)2);
        Logger.LogInfo("[Sync] Force-sent PlayerState");
    }

    [Serializable]
    public class Packet<T>
    {
        public string type;
        public T? payload;
    }

    [Serializable]
    public class PlayerState
    {
        public Vector3 position;
        public string animation;
        public string scene;
        public bool facingLeft;
        public string entryGate;
    }

    [Serializable]
    public class EnemyDelta
    {
        public string id;
        public Vector3 pos;
        public string scene;
        public string anim;
        public bool facingLeft;
        public bool inCombat;
        public int? hp;
        public bool? dead;
    }

    [Serializable]
    public class AttackRequest
    {
        public string enemyId;
        public SimpleHit hit;
        public string scene;
    }

    [Serializable]
    public class SimpleHit
    {
        public int damageDealt;
        public float direction;
        public float magnitudeMult;
        public int attackType;
        public int nailElement;
        public bool nonLethal;
        public bool critical;
        public bool canWeakHit;
        public float multiplier;
        public int damageScalingLevel;
        public int specialType;
        public bool isHeroDamage;
    }

    [Serializable]
    public class ScenePresence
    {
        public string scene;
    }

    [Serializable]
    public class PlayerDeath
    {
        public string scene;
    }

    [Serializable]
    public class TeleportRequest
    {
        public Vector3 targetPos;
        public string scene;
        public string entryGate;
    }

    [Serializable]
    public class EnemyFsmState
    {
        public string id;
        public string scene;
        public string stateName;
        public Dictionary<string, string> vars;
        public Vector3 heroPos;
        public bool heroFacingRight;
    }

    [Serializable]
    public class EnemyFsmSnapshot
    {
        public string id;
        public string scene;
        public string stateName;
        public Dictionary<string, string> vars;
        public long timestamp;
    }

    [Serializable]
    public class EnemyFsmTransition
    {
        public string id;
        public string scene;
        public string eventName;
        public string targetState;
    }

    public static class ParseUtils
    {
        public static bool TryParseVector2(string s, out Vector2 result)
        {
            result = Vector2.zero;
            if (string.IsNullOrEmpty(s))
                return false;
            s = s.Trim('(', ')');
            var strArray = s.Split(',');
            float result1;
            float result2;
            if (strArray.Length < 2 || !float.TryParse(strArray[0], out result1) ||
                !float.TryParse(strArray[1], out result2))
                return false;
            result = new Vector2(result1, result2);
            return true;
        }

        public static bool TryParseVector3(string s, out Vector3 result)
        {
            result = Vector3.zero;
            if (string.IsNullOrEmpty(s))
                return false;
            s = s.Trim('(', ')');
            var strArray = s.Split(',');
            float result1;
            float result2;
            float result3;
            if (strArray.Length < 3 || !float.TryParse(strArray[0], out result1) ||
                !float.TryParse(strArray[1], out result2) || !float.TryParse(strArray[2], out result3))
                return false;
            result = new Vector3(result1, result2, result3);
            return true;
        }
    }

    public class LookAtCamera : MonoBehaviour
    {
        private void Update()
        {
            if (Camera.main == null)
                return;
            transform.forward = Camera.main.transform.forward;
        }
    }
}
