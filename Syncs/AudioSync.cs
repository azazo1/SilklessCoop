// Made by @p11necone

using GlobalEnums;
using HarmonyLib;
using SilklessLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SilklessCoopVisual.Syncs;
public class AudioPlaySoundPacket : SilklessPacket
{
    public int HeroSound;
    public bool PlayVibration;
}
public class AudioStopSoundPacket : SilklessPacket
{
    public int HeroSound;
    public bool ResetStarts;
}
public class AudioStopAllSoundsPacket : SilklessPacket;
public class AudioPauseAllSoundsPacket : SilklessPacket;
public class AudioUnPauseAllSoundsPacket : SilklessPacket;
public class AudioSetFootstepsTablePacket : SilklessPacket
{
    public string TableName;
}

public class AudioAllowFootstepsGracePacket : SilklessPacket;
public class AudioResetPitchPacket : SilklessPacket;
public class AudioFadeInVolumePacket : SilklessPacket;
public class AudioSoftLandingPacket : SilklessPacket
{
    public float PositionX;
    public float PositionY;
    public int EnvironmentType;
}
public class AudioPlaySoundFromTablePacket : SilklessPacket
{
    public float Volume;
    public float Pitch;
    public float PositionX;
    public float PositionY;
    public string TableName;
}
public class AudioMantlePacket : SilklessPacket;

public class AudioUmbrellaInflatePacket : SilklessPacket;
public class AudioDoubleJumpPacket : SilklessPacket;

public class AudioFootstepsTableRequestPacket : SilklessPacket
{
    public string RequestedPlayer;
}

internal class AudioSync : Sync
{
    /*
     * Method used:
     * I just decided to completely clone the HeroAudioController class on the player object, then I can just use its preexisting functions instead of sending extra information
     */
    /*

     * The HeroAudioController uses a RandomAudioClipTable for its footstep sounds
     * The RandomAudioClipTable is a list of sounds, when it's used it plays a random sound from that table with a random pitch and sometimes a random volume
     *
     * In order to play footstep sounds, team cherry used the built-in event system with TK2D, since you already synced animation, we can just see when an animation frame has the footstep sound event
     * If we combine this with syncing the current footstep table (RandomAudioClipTable controlling footsteps), then we only need to send the current footstep table whenever it changes and use the animation data for the rest
     */

    /*
     * Syncing grunt sounds (and maybe any other sounds using RandomAudioClipTable excluding footsteps as they are already synced:
     *      We can just send the name of the table, the volume, pitch, and position and just play our own copy of it at that position
     *      Hopefully I can sync the exact clip instead of it being separately, but I haven't found a way yet
     */

    // Data
    private HornetVisualSync _visualSync;

    // The audio controller for each player (Excluding own clients)
    private readonly Dictionary<string, HeroAudioController> _playerAudioControllers = new();
    // Stores the data for while players are currently dashing, used in PreFunc() and PostFunc()
    private readonly Dictionary<string, bool> _playersDashing = new();
    // This is the previous animation frame for each players animator, used to make sure frame events aren't called twice
    private readonly Dictionary<string, int> _previousFrame = new();

    //Store key and audioSource in list
    private class AdditionSoundPlaying
    {
        public AudioSource Source;
        public string Key;
    }

    // A list of every extra sound that's playing, used to adjust volume and panning based on distance
    private static readonly List<AdditionSoundPlaying> AdditionalSoundsPlaying = new();

    // Memory for PreFunc() and PostFunc()
    private bool _previouslyDashing;

    // Similar to the _previousFrame list but for yourself
    private bool _previouslyMantle;
    private bool _previouslyInflate;

    // Current cooldown for sending soft landing sounds (not sure why, but it used to send like 8 without a cooldown)
    public static float LastSoftLandingPacketTime;

    private bool _tryHookDoubleJump;
    private bool _tryUnhookDoubleJump;
    private bool _tryRunOnConnect;
    protected override void OnEnable()
    {
        // Add all handlers
        SilklessAPI.AddHandler<AudioPlaySoundPacket>(OnAudioPlaySoundPacket);
        SilklessAPI.AddHandler<AudioStopSoundPacket>(OnAudioStopSoundPacket);
        SilklessAPI.AddHandler<AudioStopAllSoundsPacket>(OnAudioStopAllSoundsPacket);
        SilklessAPI.AddHandler<AudioPauseAllSoundsPacket>(OnAudioPauseAllSoundsPacket);
        SilklessAPI.AddHandler<AudioUnPauseAllSoundsPacket>(OnAudioUnPauseAllSoundsPacket);
        SilklessAPI.AddHandler<AudioSetFootstepsTablePacket>(OnAudioSetFootstepsTablePacket);
        SilklessAPI.AddHandler<AudioAllowFootstepsGracePacket>(OnAudioAllowFootstepsGracePacket);
        SilklessAPI.AddHandler<AudioResetPitchPacket>(OnAudioResetPitchPacket);
        SilklessAPI.AddHandler<AudioFadeInVolumePacket>(OnAudioFadeInVolumePacket);
        SilklessAPI.AddHandler<AudioSoftLandingPacket>(OnAudioSoftLandingPacket);
        SilklessAPI.AddHandler<AudioPlaySoundFromTablePacket>(OnAudioPlaySoundFromTablePacket);
        SilklessAPI.AddHandler<AudioMantlePacket>(OnAudioMantlePacket);
        SilklessAPI.AddHandler<AudioUmbrellaInflatePacket>(OnAudioUmbrellaInflatePacket);
        SilklessAPI.AddHandler<AudioDoubleJumpPacket>(OnAudioDoubleJumpPacket);
        SilklessAPI.AddHandler<AudioFootstepsTableRequestPacket>(OnAudioFootstepsTableRequestPacket);

        _tryHookDoubleJump = true;
    }
    protected override void OnDisable()
    {

        // Remove all handlers
        SilklessAPI.RemoveHandler<AudioPlaySoundPacket>(OnAudioPlaySoundPacket);
        SilklessAPI.RemoveHandler<AudioStopSoundPacket>(OnAudioStopSoundPacket);
        SilklessAPI.RemoveHandler<AudioStopAllSoundsPacket>(OnAudioStopAllSoundsPacket);
        SilklessAPI.RemoveHandler<AudioPauseAllSoundsPacket>(OnAudioPauseAllSoundsPacket);
        SilklessAPI.RemoveHandler<AudioUnPauseAllSoundsPacket>(OnAudioUnPauseAllSoundsPacket);
        SilklessAPI.RemoveHandler<AudioSetFootstepsTablePacket>(OnAudioSetFootstepsTablePacket);
        SilklessAPI.RemoveHandler<AudioAllowFootstepsGracePacket>(OnAudioAllowFootstepsGracePacket);
        SilklessAPI.RemoveHandler<AudioResetPitchPacket>(OnAudioResetPitchPacket);
        SilklessAPI.RemoveHandler<AudioFadeInVolumePacket>(OnAudioFadeInVolumePacket);
        SilklessAPI.RemoveHandler<AudioSoftLandingPacket>(OnAudioSoftLandingPacket);
        SilklessAPI.RemoveHandler<AudioMantlePacket>(OnAudioMantlePacket);
        SilklessAPI.RemoveHandler<AudioUmbrellaInflatePacket>(OnAudioUmbrellaInflatePacket);
        SilklessAPI.RemoveHandler<AudioDoubleJumpPacket>(OnAudioDoubleJumpPacket);
        SilklessAPI.RemoveHandler<AudioFootstepsTableRequestPacket>(OnAudioFootstepsTableRequestPacket);

        _tryUnhookDoubleJump = false;
    }

    private void OnConnect()
    {
        // When a player joins (or if you are the one joining), make sure the other clients know which footstep table to use, use reflection to pull the current one and set it again (triggers hook)
        var type = HeroController.instance.AudioCtrl.GetType();

        FieldInfo footstepsTableField = type.GetField("footstepsTable",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (footstepsTableField != null)
        {
            RandomAudioClipTable tb = (RandomAudioClipTable)footstepsTableField.GetValue(HeroController.instance.AudioCtrl);

            HeroController.instance.AudioCtrl.SetFootstepsTable(tb);
        }
    }


    protected override void Update()
    {
        try
        {
            base.Update();
            // Make sure the visual sync is there
            if (!_visualSync)
            {
                _visualSync = GetComponent<HornetVisualSync>();
            }
            if (!_visualSync) return;
            if (_visualSync.cachedHornetObject == null)
            {
                return;
            }
            // Delay any functions called before the game is loaded
            if (HeroController.instance == null) return;
            if (_tryHookDoubleJump)
            {
                HeroController.instance.OnDoubleJumped += SendAudioDoubleJumpPacket;
                _tryHookDoubleJump = false;
            }
            if (_tryUnhookDoubleJump)
            {
                HeroController.instance.OnDoubleJumped -= SendAudioDoubleJumpPacket;
                _tryUnhookDoubleJump = false;
            }
            if (_tryRunOnConnect)
            {
                OnConnect();
                _tryRunOnConnect = false;
            }
            bool isMantle = HeroController.instance.mantleFSM.ActiveStateName == "Cling";
            if (isMantle && !_previouslyMantle)
            {
                SilklessAPI.SendPacket(new AudioMantlePacket());
            }
            _previouslyMantle = isMantle;

            bool isInflating = HeroController.instance.umbrellaFSM.FsmStates[5].Name == HeroController.instance.umbrellaFSM.ActiveStateName;
            if (isInflating && !_previouslyInflate)
            {
                SilklessAPI.SendPacket(new AudioUmbrellaInflatePacket());
            }
            _previouslyInflate = isInflating;
            foreach (string key in _playerAudioControllers.Keys)
            {
                /*

                List of all animations that were used to find which footsteps sound to play

                Run sounds:

                Run
                Dash To Run
                Land To Run
                Idle To Run
                Dash To Run
                Run To Idle


                Sprint sounds:

                Sprint
                Sprint Turn

                Walk sounds:

                Walk
                TurnWalk
                Walk To Idle
                Land To Walk


                Dash animation (used to set "_playersDashing" variable)

                Dash

                 */
                if (!PreFunc(key)) return;
                // ReSharper disable once InconsistentNaming
                HeroAudioController HAC = _playerAudioControllers[key];

                tk2dSpriteAnimator animator = HAC.transform.parent.GetComponent<tk2dSpriteAnimator>();

                // Self-explanatory (this is how team cherry did it in their code, so I just copied)
                if (animator.CurrentClip.name.Contains("Run"))
                {
                    HAC.StopSound(HeroSounds.FOOTSTEPS_WALK);
                    HAC.StopSound(HeroSounds.FOOTSTEPS_SPRINT);
                    HAC.PlaySound(HeroSounds.FOOTSTEPS_RUN, playVibration: false);
                }
                if (animator.CurrentClip.name.Contains("Sprint"))
                {
                    HAC.PlaySound(HeroSounds.FOOTSTEPS_SPRINT, playVibration: false);
                    HAC.StopSound(HeroSounds.FOOTSTEPS_WALK);
                    HAC.StopSound(HeroSounds.FOOTSTEPS_RUN);
                }
                if (animator.CurrentClip.name.Contains("Walk"))
                {
                    HAC.StopSound(HeroSounds.FOOTSTEPS_RUN);
                    HAC.StopSound(HeroSounds.FOOTSTEPS_SPRINT);
                    HAC.PlaySound(HeroSounds.FOOTSTEPS_WALK, playVibration: false);
                }
                if (animator.CurrentClip.name == "Dash")
                {
                    _playersDashing[key] = true;
                }
                else
                {
                    _playersDashing[key] = false;
                }

                // If the current frame event is for "footstep" then play footsteps, don't play if the previous frame is same as this one (prevents it playing multiple times)
                if (animator.CurrentClip.frames.Length > animator.CurrentFrame) if (animator.CurrentClip.frames[animator.CurrentFrame].eventInfo.ToLower() == "footstep" && _previousFrame[key] != animator.CurrentFrame)
                {
                    // When a player joins (or if you are the one joining), make sure the other clients know which footstep table to use, use reflection to pull the current one and set it again (triggers hook)
                    var type = HeroController.instance.AudioCtrl.GetType();

                    FieldInfo footstepsTableField = type.GetField("footstepsTable",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (footstepsTableField != null)
                    {
                        RandomAudioClipTable tb = (RandomAudioClipTable)footstepsTableField.GetValue(HAC);

                        if (tb == null) SilklessAPI.SendPacket(new AudioFootstepsTableRequestPacket() { RequestedPlayer = key });
                    }
                    HAC.PlayFootstep();
                }
                _previousFrame[key] = animator.CurrentFrame;
                PostFunc(key);
            }
            SetVolumes();
        }
        catch (Exception e) { LogUtil.LogError(e); }

    }

    protected override void Tick()
    {

    }

    protected override void OnPlayerJoin(string id)
    {
        _tryRunOnConnect = true;
    }

    protected override void OnPlayerLeave(string id)
    {
        // Remove all variables from dictionary
        // ReSharper disable once InconsistentNaming
        if (_playerAudioControllers.Remove(id, out HeroAudioController HAC) && HAC) Destroy(HAC);
        _playersDashing.Remove(id);
        _previousFrame.Remove(id);
        AdditionalSoundsPlaying.RemoveAll(s => s.Key == id);
    }

    protected override void Reset()
    {
        try
        {
            // Clear all
            _playerAudioControllers.Clear();
            _playersDashing.Clear();
            _previousFrame.Clear();
            AdditionalSoundsPlaying.Clear();
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }

    private void OnAudioPlaySoundPacket(AudioPlaySoundPacket packet)
    {
        try
        {
            if (!_visualSync.PlayerObjects[packet.ID].activeSelf) return;
            if (!PreFunc(packet.ID)) return;
            _playerAudioControllers[packet.ID].PlaySound((HeroSounds)packet.HeroSound, packet.PlayVibration);
            PostFunc(packet.ID);
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }

    private void OnAudioStopSoundPacket(AudioStopSoundPacket packet)
    {
        try
        {
            if (!_visualSync.PlayerObjects[packet.ID].activeSelf) return;
            if ((HeroSounds)packet.HeroSound == HeroSounds.FALLING) return;
            if (!PreFunc(packet.ID)) return;
            _playerAudioControllers[packet.ID].StopSound((HeroSounds)packet.HeroSound, packet.ResetStarts);
            PostFunc(packet.ID);
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }

    private void OnAudioStopAllSoundsPacket(AudioStopAllSoundsPacket packet)
    {
        try
        {
            if (!PreFunc(packet.ID)) return;
            _playerAudioControllers[packet.ID].StopAllSounds();
            PostFunc(packet.ID);
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }

    private void OnAudioPauseAllSoundsPacket(AudioPauseAllSoundsPacket packet)
    {
        try
        {
            if (!PreFunc(packet.ID)) return;
            _playerAudioControllers[packet.ID].PauseAllSounds();
            PostFunc(packet.ID);
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }

    private void OnAudioUnPauseAllSoundsPacket(AudioUnPauseAllSoundsPacket packet)
    {
        try
        {
            if (!PreFunc(packet.ID)) return;
            _playerAudioControllers[packet.ID].UnPauseAllSounds();
            PostFunc(packet.ID);
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }

    private void OnAudioSetFootstepsTablePacket(AudioSetFootstepsTablePacket packet)
    {
        try
        {
            if (!PreFunc(packet.ID)) return;

            // Use reflection to find a list of every footstep table (stored in HeroController class)
            var type = HeroController.instance.GetType();

            FieldInfo tablesField = type.GetField("footStepTables",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (tablesField != null)
            {
                RandomAudioClipTable[] tables = (RandomAudioClipTable[])tablesField.GetValue(HeroController.instance);

                // Go through every table, if the table names match, update that players footstep table
                foreach (var table in tables)
                {
                    if (table.name == packet.TableName) _playerAudioControllers[packet.ID].SetFootstepsTable(table);
                }
            }
            PostFunc(packet.ID);
        }
        catch (Exception e) { LogUtil.LogError(e); }

    }

    private void OnAudioAllowFootstepsGracePacket(AudioAllowFootstepsGracePacket packet)
    {
        try
        {
            // Whenever a player dashes, it disables footstep noises for a little bit
            if (!PreFunc(packet.ID)) return;
            _playerAudioControllers[packet.ID].AllowFootstepsGrace();
            PostFunc(packet.ID);
        } catch(Exception e) { LogUtil.LogError(e);}
    }

    private void OnAudioResetPitchPacket(AudioResetPitchPacket packet)
    {
        if (!PreFunc(packet.ID)) return;
        // Useless imo - maybe add later support?
        PostFunc(packet.ID);
    }

    private void OnAudioFadeInVolumePacket(AudioFadeInVolumePacket packet)
    {
        if (!PreFunc(packet.ID)) return;
        PostFunc(packet.ID);
        // Useless imo - maybe add later support? Also, untested (cut out) (used for falling sound)
    }

    private void OnAudioSoftLandingPacket(AudioSoftLandingPacket packet)
    {
        try
        {
            // The environment type in the PlayerData.instance is used to decide which SoftLanding
            if (!PreFunc(packet.ID)) return;
            // Store the old "environment type" so we can set it back
            EnvironmentTypes oldType = PlayerData.instance.environmentType;
            PlayerData.instance.environmentType = (EnvironmentTypes)packet.EnvironmentType;
            Vector3 pos = new Vector3(packet.PositionX, packet.PositionY, 0.03f);
            // Spawn the prefab (stored in HeroController class) at the right position
            GameObject softL = HeroController.instance.softLandingEffectPrefab.Spawn(pos);

            // Sometimes bugs out volume, disable it
            softL.GetComponent<AudioSourcePriority>().enabled = false;
            // Destroy particles
            if (!ModConfig.SyncParticles)
            {
                foreach (ParticleSystem t in softL.GetComponentsInChildren<ParticleSystem>())
                {
                    t.Stop();
                }
                foreach (SpriteRenderer t in softL.GetComponentsInChildren<SpriteRenderer>())
                {
                    t.enabled = false;
                }
            }
            // Add to additionSounds list and set back the PlayerData environment type
            AdditionalSoundsPlaying.Add(new AdditionSoundPlaying { Source = softL.GetComponent<AudioSource>(), Key = packet.ID});
            PlayerData.instance.environmentType = oldType;
            PostFunc(packet.ID);
        }
        catch (Exception e) { LogUtil.LogError(e); }
    }

    private void OnAudioPlaySoundFromTablePacket(AudioPlaySoundFromTablePacket packet)
    {
        if (packet.TableName == null) return;
        if (packet.ID == null) return;
        if (!PreFunc(packet.ID)) return;
        try
        {
            // Use the correct table based on the name
            RandomAudioClipTable table;
            switch (packet.TableName)
            {
                case "Attack Normal Hornet Voice":
                    if(HeroController.instance.attackAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.attackAudioTable;
                    break;

                case "warrior_rage_attack":
                    if (HeroController.instance.warriorRageAttackAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.warriorRageAttackAudioTable;
                    break;

                case "hornet_projectile_twang Quick Sling":
                    if (HeroController.instance.quickSlingAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.quickSlingAudioTable;
                    break;

                case "Wound Hornet Voice":
                    if (HeroController.instance.woundAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.woundAudioTable;
                    break;

                case "hornet_wound_heavy":
                    if (HeroController.instance.woundHeavyAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.woundHeavyAudioTable;
                    break;

                case "Frost Damage Hornet Voice":
                    if (HeroController.instance.woundFrostAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.woundFrostAudioTable;
                    break;

                case "Hazard Damage Hornet Voice":
                    if (HeroController.instance.hazardDamageAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.hazardDamageAudioTable;
                    break;

                case "Pit Fall":
                    if (HeroController.instance.pitFallAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.pitFallAudioTable;
                    break;

                case "Death Hornet Voice":
                    if (HeroController.instance.deathAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.deathAudioTable;
                    break;

                case "Grunt Hornet Voice":
                    if (HeroController.instance.gruntAudioTable == null) { PostFunc(packet.ID); return; }
                    table = HeroController.instance.gruntAudioTable;
                    break;
                // case "Taunt Hornet Voice":
                // table = "\\Assets\\Audio\\Voices\\Hornet_Silksong";
                default:
                    // Exit the function if no table was found (Call PostFunc() always)
                    PostFunc(packet.ID);
                    return;
            }
            if (table == null)
            {
                // Exit the function if no table was found (Call PostFunc() always)
                LogUtil.LogError($"Audio table for {packet.TableName} is null");
                PostFunc(packet.ID);
                return;
            }
            // Spawn table with already made function, then set pitch and volume to match other one
            AudioSource output = table.SpawnAndPlayOneShot(new Vector3(packet.PositionX, packet.PositionY, 0.0405f), true);
            if (output == null)
            {
                PostFunc(packet.ID);
                return;
            }
            AdditionalSoundsPlaying.Add(new AdditionSoundPlaying { Source = output, Key = packet.ID });
            output.volume = packet.Volume;
            output.pitch = packet.Pitch;
            // Add to additional sounds list
        }
        catch (Exception e) { LogUtil.LogError(e); }
        PostFunc(packet.ID);
    }

    private void OnAudioMantlePacket(AudioMantlePacket packet)
    {
        try
        {
            if (!PreFunc(packet.ID)) return;
            // Mantle sounds are stored in a weird place
            var fsm = HeroController.instance.mantleFSM;

            var state = fsm.FsmStates[5];

            var actionData = state.ActionData;

            // Use reflection on MantleFSM
            FieldInfo field = actionData.GetType().GetField("unityObjectParams", BindingFlags.NonPublic | BindingFlags.Instance);

            if (field != null)
            {
                List<UnityEngine.Object> unityObjects = (List<UnityEngine.Object>)field.GetValue(actionData);
                if (unityObjects != null)
                {
                    // Choose a random clip out of 4
                    AudioClip clip = (AudioClip)unityObjects[UnityEngine.Random.Range(0, 3)];
                    // Instantiate and play clip (position at pos of player who sent it)
                    AudioSource audioSource = new GameObject().AddComponent<AudioSource>();
                    audioSource.volume = 1;
                    audioSource.pitch = 1;
                    audioSource.transform.position = _visualSync.PlayerObjects[packet.ID].transform.position;
                    audioSource.PlayOneShot(clip);
                    AdditionalSoundsPlaying.Add(new AdditionSoundPlaying { Source = audioSource, Key = packet.ID});
                    Destroy(audioSource.gameObject, 3);
                }
            }
            PostFunc(packet.ID);
        }
        catch (Exception e) { LogUtil.LogError(e); }
    }

    private void OnAudioUmbrellaInflatePacket(AudioUmbrellaInflatePacket packet)
    {
        try
        {
            if (!PreFunc(packet.ID)) return;
            // Umbrella sounds are stored in a weird place
            // ReSharper disable once InconsistentNaming
            PlayAudioEvent PAE = (PlayAudioEvent)HeroController.instance.umbrellaFSM.FsmStates[5].Actions[9];
            AudioClip clip = PAE.audioClip.Value as AudioClip;
            // Instantiate and play clip (position at pos of player who sent it)
            AudioSource audioSource = new GameObject().AddComponent<AudioSource>();
            audioSource.volume = 1;
            audioSource.pitch = 1;
            audioSource.transform.position = _visualSync.PlayerObjects[packet.ID].transform.position;
            audioSource.PlayOneShot(clip);
            AdditionalSoundsPlaying.Add(new AdditionSoundPlaying { Source = audioSource, Key = packet.ID });
            Destroy(audioSource.gameObject, 3);
            PostFunc(packet.ID);
        }
        catch (Exception e) { LogUtil.LogError(e); }
    }

    private void OnAudioDoubleJumpPacket(AudioDoubleJumpPacket packet)
    {
        try
        {
            if (!PreFunc(packet.ID)) return;
            AudioClip clip = HeroController.instance.doubleJumpClip;
            // Instantiate and play clip (position at pos of player who sent it)
            AudioSource audioSource = new GameObject().AddComponent<AudioSource>();
            audioSource.volume = 1;
            audioSource.pitch = 1;
            audioSource.transform.position = _visualSync.PlayerObjects[packet.ID].transform.position;
            audioSource.PlayOneShot(clip);
            AdditionalSoundsPlaying.Add(new AdditionSoundPlaying { Source = audioSource, Key = packet.ID });
            Destroy(audioSource.gameObject, 3);
            PostFunc(packet.ID);
        }
        catch(Exception e) { LogUtil.LogError(e); }
    }

    private void SendAudioDoubleJumpPacket() => SilklessAPI.SendPacket(new AudioDoubleJumpPacket());

    // Notes to self:

    // Hook umbrella updraft sound
    // Find and hook grunts?
    // Backflipped audio - Hero Animation Controller
    // seems to be already hooked? unsure  ^
    // Check out other sounds like wakeups ^ there
    // Long falling sound (fades in, sync that hook?, maybe not unnecessary)

    /* In HeroController
    public AudioClip nailArtChargeComplete;

    public AudioClip doubleJumpClip - complete;

    public AudioClip mantisClawClip - a tiny tick sound, probably unnecessary;

    public AudioClip downDashCancelClip;

    public AudioClip deathImpactClip;

    frostedAudioLoop
     *
     */

    // ReSharper disable once MemberCanBePrivate.Global
    public bool PreFunc(string player)
    {
        if (!_visualSync) return false;
        if (_visualSync.cachedHornetObject == null)
        {
            return false;
        }
        if (HeroController.instance == null) return false;
        // Set up audio gameObject if It's not there already
        if (!_playerAudioControllers.ContainsKey(player)) SetUpAudioControllerForPlayer(player);
        if (!_playerAudioControllers.ContainsKey(player)) return false;
        if (!DoubleCheckSetUpCorrect(_playerAudioControllers[player]))
        {
            if (!PostSetUpFix(_playerAudioControllers[player])) return false;
        }
        // Bunch of variables that affect sounds played, temporarily set them to the other clients (reverted in post func)
        _previouslyDashing = HeroController.instance.cState.dashing;
        if (_playersDashing.TryGetValue(player, out bool isDashing)) HeroController.instance.cState.dashing = isDashing;
        else
        {
            _playersDashing.Add(player, false);
            HeroController.instance.cState.dashing = false;
        }
        return true;
    }
    public void PostFunc(string player)
    {
        // Undo anything from the PreFunc
        HeroController.instance.cState.dashing = _previouslyDashing;
    }

    private bool DoubleCheckSetUpCorrect(HeroAudioController newAudioCtrl)
    {
        if (!HeroController.instance.AudioCtrl) return false;
        // Declare the reflection fields first to make sure original copy isn't null
        var type = HeroController.instance.AudioCtrl.GetType();

        FieldInfo audioPrefabField = type.GetField("audioSourcePrefab",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo runStartClipsField = type.GetField("runStartClips",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo runStartClipsCloaklessField = type.GetField("runStartClipsCloakless",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo heroCtrlField = type.GetField("heroCtrl",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        if (audioPrefabField.GetValue(newAudioCtrl) == null) return false;
        if (runStartClipsField.GetValue(newAudioCtrl) == null) return false;
        if (runStartClipsCloaklessField.GetValue(newAudioCtrl) == null) return false;
        if (heroCtrlField.GetValue(newAudioCtrl) == null) return false;

        return true;
    }

    private bool PostSetUpFix(HeroAudioController newAudioCtrl)
    {
        // Fix any errors of null that happen

        if (!HeroController.instance.AudioCtrl) return false;
        // Declare the reflection fields first to make sure original copy isn't null
        var type = HeroController.instance.AudioCtrl.GetType();

        FieldInfo audioPrefabField = type.GetField("audioSourcePrefab",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo runStartClipsField = type.GetField("runStartClips",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo runStartClipsCloaklessField = type.GetField("runStartClipsCloakless",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo heroCtrlField = type.GetField("heroCtrl",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        if (audioPrefabField.GetValue(HeroController.instance.AudioCtrl) == null) return false;
        if (runStartClipsField.GetValue(HeroController.instance.AudioCtrl) == null) return false;
        if (runStartClipsCloaklessField.GetValue(HeroController.instance.AudioCtrl) == null) return false;
        if (heroCtrlField.GetValue(HeroController.instance.AudioCtrl) == null) return false;

        audioPrefabField.SetValue(newAudioCtrl, audioPrefabField.GetValue(HeroController.instance.AudioCtrl));
        runStartClipsField.SetValue(newAudioCtrl, runStartClipsField.GetValue(HeroController.instance.AudioCtrl));
        runStartClipsCloaklessField.SetValue(newAudioCtrl, runStartClipsCloaklessField.GetValue(HeroController.instance.AudioCtrl));
        heroCtrlField.SetValue(newAudioCtrl, HeroController.instance);

        if (audioPrefabField.GetValue(newAudioCtrl) == null) return false;
        if (runStartClipsField.GetValue(newAudioCtrl) == null) return false;
        if (runStartClipsCloaklessField.GetValue(newAudioCtrl) == null) return false;
        if (heroCtrlField.GetValue(newAudioCtrl) == null) return false;

        return true;
    }
    public void SetUpAudioControllerForPlayer(string player)
    {
        try
        {
            if (!_visualSync.cachedHornetObject && !SilklessAPI.Initialized) { return; }
            if (!_visualSync.cachedHornetObject.transform.Find("Sounds")) return;
            if (!HeroController.instance.AudioCtrl) return;
            // Declare the reflection fields first to make sure original copy isn't null
            var type = HeroController.instance.AudioCtrl.GetType();

            FieldInfo audioPrefabField = type.GetField("audioSourcePrefab",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            FieldInfo runStartClipsField = type.GetField("runStartClips",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            FieldInfo runStartClipsCloaklessField = type.GetField("runStartClipsCloakless",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            FieldInfo heroCtrlField = type.GetField("heroCtrl",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            if (audioPrefabField.GetValue(HeroController.instance.AudioCtrl) == null) return;
            if (runStartClipsField.GetValue(HeroController.instance.AudioCtrl) == null) return;
            if (runStartClipsCloaklessField.GetValue(HeroController.instance.AudioCtrl) == null) return;
            if (heroCtrlField.GetValue(HeroController.instance.AudioCtrl) == null) return;
            // Find object to clone
            // ReSharper disable once InconsistentNaming
            GameObject audiosOG = _visualSync.cachedHornetObject.transform.Find("Sounds").gameObject;
            // Find player object to set parent
            if (!_visualSync.PlayerObjects.TryGetValue(player, out GameObject playerObject)) return;

            // Destroy any old copies
            if (playerObject.transform.Find("SoundsHolder") != null)
            {
                DestroyImmediate(playerObject.transform.Find("SoundsHolder"));
            }
            // Duplicate original copy into playerObject
            GameObject audioClone = Instantiate(audiosOG, playerObject.transform);
            audioClone.name = "SoundsHolder";
            // Add an audio controller
            HeroAudioController newAudioCtrl = audioClone.AddComponent<HeroAudioController>();
            _playerAudioControllers.Add(player, newAudioCtrl);

            // Set all variables (one time uses):

            // Set private variables with reflection (most)

            audioPrefabField.SetValue(newAudioCtrl, audioPrefabField.GetValue(HeroController.instance.AudioCtrl));
            runStartClipsField.SetValue(newAudioCtrl, runStartClipsField.GetValue(HeroController.instance.AudioCtrl));
            runStartClipsCloaklessField.SetValue(newAudioCtrl, runStartClipsCloaklessField.GetValue(HeroController.instance.AudioCtrl));
            heroCtrlField.SetValue(newAudioCtrl, HeroController.instance);

            // Set all public variables
            newAudioCtrl.softLanding = audioClone.transform.Find("Landing").GetComponent<AudioSource>();

            newAudioCtrl.hardLanding = audioClone.transform.Find("HardLanding").GetComponent<AudioSource>();

            newAudioCtrl.jump = audioClone.transform.Find("Jump").GetComponent<AudioSource>();

            newAudioCtrl.takeHit = audioClone.transform.Find("TakeHit").GetComponent<AudioSource>();

            newAudioCtrl.backDash = audioClone.transform.Find("BackDash").GetComponent<AudioSource>();

            newAudioCtrl.dash = audioClone.transform.Find("Dash").GetComponent<AudioSource>();

            newAudioCtrl.dashSilk = audioClone.transform.Find("Dash Silk").GetComponent<AudioSource>();

            newAudioCtrl.updraftIdle = audioClone.transform.Find("Updraft Idle").GetComponent<AudioSource>();

            newAudioCtrl.windyIdle = audioClone.transform.Find("Windy Idle").GetComponent<AudioSource>();

            newAudioCtrl.footSteps = audioClone.transform.Find("Footsteps").GetComponent<AudioSource>();

            newAudioCtrl.wallslide = audioClone.transform.Find("Wallslide").GetComponent<AudioSource>();

            newAudioCtrl.nailArtCharge = audioClone.transform.Find("Nail Art Charge").GetComponent<AudioSource>();

            newAudioCtrl.nailArtReady = audioClone.transform.Find("Nail Art Ready").GetComponent<AudioSource>();

            newAudioCtrl.falling = audioClone.transform.Find("Falling").GetComponent<AudioSource>();

            newAudioCtrl.walljump = audioClone.transform.Find("Walljump").GetComponent<AudioSource>();

            // OnConnect() sends the footstep table to everyone, also send a request of everyone's footstep table packet, makes sure it's all synced for everyone when a new player joins
            SilklessAPI.SendPacket(new AudioFootstepsTableRequestPacket() { RequestedPlayer = player });
            _tryRunOnConnect = true;
        }
        catch (Exception e) { LogUtil.LogError(e); }

    }

    private void OnAudioFootstepsTableRequestPacket(AudioFootstepsTableRequestPacket packet)
    {
        if (SilklessAPI.GetId() == packet.RequestedPlayer)
        {
            _tryRunOnConnect = true;
        }
    }

    private void SetVolumes()
    {
        if (!_visualSync.cachedHornetObject) return;
        Vector3 listenerPos = _visualSync.cachedHornetObject.transform.position;
        float maxDist = ModConfig.AudioRolloff;

        // ReSharper disable once InconsistentNaming
        foreach (HeroAudioController HAC in _playerAudioControllers.Values)
        {
            Vector3 srcPos = HAC.transform.position;
            float dist = Vector3.Distance(listenerPos, srcPos);

            // Linear rolloff (1 at zero distance, 0 at maxDist)
            float volume = Mathf.Clamp01(1f - (dist / maxDist));

            // Stereo panning based on x pos
            Vector3 dir = HAC.transform.position - listenerPos;
            float pan = Mathf.Clamp(dir.x / maxDist, -1f, 1f);

            // All the sources here have same position so they can all use same pan and volume
            ApplyAudioSettings(HAC, volume, pan);
        }
        foreach (AdditionSoundPlaying asp in AdditionalSoundsPlaying)
        {
            if (!asp.Source)
            {
                AdditionalSoundsPlaying.Remove(asp);
                return;
            }
            asp.Source.enabled = _visualSync.PlayerObjects[asp.Key].gameObject.activeSelf;
            Vector3 srcPos = asp.Source.transform.position;
            float dist = Vector3.Distance(listenerPos, srcPos);

            // Linear rolloff (1 at zero distance, 0 at maxDist)
            float volume = Mathf.Clamp01(1f - (dist / maxDist));

            // Stereo panning based on x pos
            Vector3 dir = asp.Source.transform.position - listenerPos;
            float pan = Mathf.Clamp(dir.x / maxDist, -1f, 1f);

            asp.Source.volume = volume;
            asp.Source.panStereo = pan;
        }
    }

    // ReSharper disable once InconsistentNaming
    private void ApplyAudioSettings(HeroAudioController HAC, float volume, float pan)
    {
        // Apply to all AudioSources
        AudioSource[] sources =
        [
            HAC.softLanding,
            HAC.hardLanding,
            HAC.jump,
            HAC.takeHit,
            HAC.backDash,
            HAC.dash,
            HAC.dashSilk,
            HAC.updraftIdle,
            HAC.windyIdle,
            HAC.footSteps,
            HAC.wallslide,
            HAC.nailArtCharge,
            HAC.nailArtReady,
            HAC.falling,
            HAC.walljump
        ];

        foreach (AudioSource src in sources)
        {
            if (src == HAC.falling)
            {
                // Current glitching idk
                src.volume = 0;
                return;
            }
            src.volume = volume;
            src.panStereo = pan;
        }
    }
    public static string GetGameObjectPath(Transform t)
    {
        if (t == null) return null;
        if (t.parent == null) return t.name;
        return GetGameObjectPath(t.parent) + "/" + t.name;
    }
}

// I think all of these are self-explanatory, it avoids a few sounds and converts enums to ints
// Basically just copying any information to other clients
[HarmonyPatch(typeof(HeroAudioController), "PlaySound")]
internal class HeroAudioPatchPlaySound
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroAudioController __instance, [HarmonyArgument("soundEffect")] HeroSounds sound, [HarmonyArgument("playVibration")] bool vibration)
    {
        try
        {
            if (sound == HeroSounds.FOOTSTEPS_RUN || sound == HeroSounds.FOOTSTEPS_SPRINT || sound == HeroSounds.FOOTSTEPS_WALK || sound == HeroSounds.UPDRAFT_IDLE || sound == HeroSounds.WINDY_IDLE) return;
            string name = __instance?.gameObject.name ?? "unknown";
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioPlaySoundPacket
            {
                HeroSound = (int)sound,
                PlayVibration = vibration
            });
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(HeroAudioController), "StopSound")]
internal class HeroAudioPatchStopSound
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroAudioController __instance, [HarmonyArgument("soundEffect")] HeroSounds sound, [HarmonyArgument("resetStarts")] bool resetStarts)
    {
        try
        {
            if (sound == HeroSounds.FOOTSTEPS_RUN || sound == HeroSounds.FOOTSTEPS_SPRINT || sound == HeroSounds.FOOTSTEPS_WALK || sound == HeroSounds.UPDRAFT_IDLE || sound == HeroSounds.WINDY_IDLE) return;
            string name = __instance?.gameObject.name ?? "unknown";
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioStopSoundPacket
            {
                HeroSound = (int)sound,
                ResetStarts = resetStarts
            });
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(HeroAudioController), "StopAllSounds")]
internal class HeroAudioPatchStopAllSounds
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroAudioController __instance)
    {
        try
        {
            string name = __instance?.gameObject.name ?? "unknown";
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioStopAllSoundsPacket());
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(HeroAudioController), "PauseAllSounds")]
internal class HeroAudioPatchPauseAllSounds
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroAudioController __instance)
    {
        try
        {
            string name = __instance?.gameObject.name ?? "unknown";
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioPauseAllSoundsPacket());
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(HeroAudioController), "UnPauseAllSounds")]
internal class HeroAudioPatchUnPauseAllSounds
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroAudioController __instance)
    {
        try
        {
            string name = __instance?.gameObject.name ?? "unknown";
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioUnPauseAllSoundsPacket());
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(HeroAudioController), "SetFootstepsTable")]
internal class HeroAudioPatchSetFootstepsTable
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroAudioController __instance, [HarmonyArgument("newTable")] RandomAudioClipTable table)
    {
        try
        {
            if (!__instance) return;
            string name = __instance.gameObject.name;
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioSetFootstepsTablePacket
            {
                TableName = table.name
            });
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(HeroAudioController), "AllowFootstepsGrace")]
internal class HeroAudioPatchAllowFootstepsGrace
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroAudioController __instance)
    {
        try
        {
            string name = __instance?.gameObject.name ?? "unknown";
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioAllowFootstepsGracePacket());
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(HeroAudioController), "ResetPitch")]
internal class HeroAudioPatchResetPitch
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroAudioController __instance, [HarmonyArgument("src")] AudioSource src)
    {
        try
        {
            string name = __instance?.gameObject.name ?? "unknown";
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioResetPitchPacket());
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(HeroAudioController), "FadeInVolume")]
internal class HeroAudioPatchFadeInVolume
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroAudioController __instance, [HarmonyArgument("src")] AudioSource src, [HarmonyArgument("duration")] float duration)
    {
        try
        {
            string name = __instance?.gameObject.name ?? "unknown";
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioFadeInVolumePacket());
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}

[HarmonyPatch(typeof(HeroController), "SpawnSoftLandingPrefab")]
internal class HeroAudioPatchSoftLanding
{
    // ReSharper disable once InconsistentNaming
    public static void Prefix(HeroController __instance)
    {
        try
        {
            if (__instance == null) return;

            if (AudioSync.LastSoftLandingPacketTime + 0.3f > Time.timeSinceLevelLoad) return;
            AudioSync.LastSoftLandingPacketTime = Time.timeSinceLevelLoad;
            // ReSharper disable once ConstantNullCoalescingCondition
            string name = __instance.gameObject.name ?? "unknown";
            if (name != "Hero_Hornet" && name != "Hero_Hornet(Clone)") return;
            SilklessAPI.SendPacket(new AudioSoftLandingPacket { PositionX = __instance.transform.position.x, PositionY = __instance.transform.position.y, EnvironmentType = (int)PlayerData.instance.environmentType });
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}
[HarmonyPatch(typeof(RandomAudioClipTable), "ReportPlayed")]
public static class RandomAudioClipTablePatch
{
    // ReSharper disable once InconsistentNaming
    public static void Postfix(RandomAudioClipTable __instance, AudioClip clip, AudioSource spawnedAudioSource)
    {
        try
        {
            // Small movement cloak??
            // TauntHornetVoice ??
            if (spawnedAudioSource == null) return;
            // Use odd Z position as a key to not send packet for the newly spawned sound (gets stuck in loop if not)
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (spawnedAudioSource.transform.position.z == 0.0405f) return;
            // ReSharper disable once ConstantNullCoalescingCondition
            string tableName = __instance.name ?? "unknown";
            if (tableName == "AttackNormalHornetVoice" || tableName == "warrior_rage_attack" || tableName == "hornet_projectile_twang Quick Sling"
                || tableName == "Wound Hornet Voice" || tableName == "hornet_wound_heavy" || tableName == "Frost Damage Hornet Voice" || tableName == "Hazard Damage Hornet Voice"
                || tableName == "Pit Fall" || tableName == "Death Hornet Voice" || tableName == "Grunt Hornet Voice")
                SilklessAPI.SendPacket(new AudioPlaySoundFromTablePacket
                {
                    TableName = tableName,
                    PositionX = spawnedAudioSource.transform.position.x,
                    PositionY = spawnedAudioSource.transform.position.y,
                    Pitch = spawnedAudioSource.pitch,
                    Volume = spawnedAudioSource.volume,

                });
            else { }
        }
        catch (Exception e)
        {
            LogUtil.LogError(e);
        }
    }
}
