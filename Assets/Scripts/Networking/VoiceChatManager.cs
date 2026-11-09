using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Services.Vivox;
using Unity.Netcode;

namespace Networking
{
    public class VoiceChatManager : MonoBehaviour
    {
        [Header("Voice Chat")]
        [SerializeField] private bool autoJoinAudioOnly = true;
        [SerializeField] private bool useProximityVoice = true;
        [SerializeField] private float audibleRange = 72f;
        [SerializeField] private float conversationalRange = 24f;
        [SerializeField] private float audioFadeModelExponent = 1f;
        [SerializeField] private float positionUpdateInterval = 0.1f;
        [SerializeField] private Transform localVoiceTransform;
        [SerializeField] private bool vivoxAvailable;
        [SerializeField] private string lastVivoxError = "";

        private bool initialized;
        private bool loggedIn;
        private bool initializingOrLoggingIn;   // Guard against concurrent EnsureReadyAsync calls.
        private string currentChannel = string.Empty;
        private string currentRadioChannel = string.Empty;
        private float nextPositionSyncTime;
        private float nextMonsterSpeech;
        private float nextRadioStationScanTime;
        private bool wantsRadioBroadcast;
        private bool radioBroadcasting;
        private Channel3DProperties currentChannel3DProperties;
        private readonly System.Threading.SemaphoreSlim channelGate = new System.Threading.SemaphoreSlim(1, 1);

        public string CurrentChannel => currentChannel;
        public bool VivoxAvailable => vivoxAvailable;
        public string LastVivoxError => lastVivoxError;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>Clear cached player transform when a new scene loads so we re-resolve it.</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            localVoiceTransform = null;
            nextRadioStationScanTime = 0f;
        }

        private void Update()
        {
            TryAttachRadioStationToSceneObject();
            ApplyReviveMute();
            if(initialized&&loggedIn&&!reviveMuted&&!paMuted&&Time.time>=nextMonsterSpeech&&BlindLungeMonster.Instance!=null){
                nextMonsterSpeech=Time.time+.6f;
                foreach(var channel in VivoxService.Instance.ActiveChannels.Values)
                foreach(var participant in channel)if(participant.IsSelf&&participant.SpeechDetected&&!participant.IsMuted){BlindLungeMonster.Instance.ReportLocalSpeech();goto SpeechChecked;}
            }
            SpeechChecked:
            if (!useProximityVoice || !initialized || !loggedIn || string.IsNullOrEmpty(currentChannel))
            {
                return;
            }

            if (Time.time < nextPositionSyncTime)
            {
                return;
            }

            nextPositionSyncTime = Time.time + Mathf.Max(0.02f, positionUpdateInterval);
            _ = UpdateLocalPositionSafelyAsync();
        }

        /// <summary>
        /// Initialises Vivox and logs in. Safe to call multiple times — idempotent.
        /// Returns true on success, false if Vivox is unavailable.
        /// </summary>
        public async Task<bool> EnsureReadyAsync()
        {
            // Already fully ready.
            if (initialized && loggedIn)
            {
                return true;
            }

            // Another call is already doing this — wait briefly then check again.
            if (initializingOrLoggingIn)
            {
                for (int i = 0; i < 50; i++)
                {
                    await Task.Delay(100);
                    if (initialized && loggedIn) return true;
                }
                return vivoxAvailable;
            }

            initializingOrLoggingIn = true;
            try
            {
                if (!initialized)
                {
                    await VivoxService.Instance.InitializeAsync();
                    initialized = true;
                    Debug.Log("[VoiceChat] Vivox initialized.");
                }

                if (!loggedIn)
                {
                    await VivoxService.Instance.LoginAsync();
                    loggedIn = true;
                    Debug.Log("[VoiceChat] Vivox login succeeded.");
                }

                ApplyReviveMute();
                vivoxAvailable = true;
                lastVivoxError = "";
                return true;
            }
            catch (Exception ex)
            {
                vivoxAvailable = false;
                lastVivoxError = ex.Message;
                Debug.LogWarning($"[VoiceChat] Vivox unavailable: {ex.Message}");
                return false;
            }
            finally
            {
                initializingOrLoggingIn = false;
            }
        }

        /// <summary>
        /// Joins a voice channel using the relay join code as the channel name.
        /// Safe to call even if Vivox is unavailable — logs a warning and returns.
        /// </summary>
        public async Task JoinLobbyVoiceAsync(string channelName)
        {
            await channelGate.WaitAsync();
            try { await JoinLobbyVoiceCoreAsync(channelName); }
            finally { channelGate.Release(); }
        }

        private async Task JoinLobbyVoiceCoreAsync(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                return;
            }

            bool ready = await EnsureReadyAsync();
            if (!ready)
            {
                Debug.LogWarning("[VoiceChat] Skipping voice join — Vivox not available.");
                return;
            }

            try
            {
                string normalized = channelName.Trim().ToUpperInvariant();

                // Already in this channel.
                if (string.Equals(currentChannel, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                // Leave existing channel first.
                if (!string.IsNullOrEmpty(currentChannel))
                {
                    await LeaveCurrentChannelCoreAsync();
                }

                ChatCapability capability = autoJoinAudioOnly ? ChatCapability.AudioOnly : ChatCapability.TextAndAudio;

                if (useProximityVoice)
                {
                    currentChannel3DProperties = new Channel3DProperties(
                        Mathf.RoundToInt(audibleRange),
                        Mathf.RoundToInt(conversationalRange),
                        audioFadeModelExponent,
                        AudioFadeModel.InverseByDistance);

                    await VivoxService.Instance.JoinPositionalChannelAsync(normalized, capability, currentChannel3DProperties);
                    // Track the successfully joined channel immediately, even if the
                    // subsequent radio/transmission setup fails and needs cleanup.
                    currentChannel = normalized;
                    try
                    {
                        currentRadioChannel = $"{normalized}_RADIO";
                        await VivoxService.Instance.JoinGroupChannelAsync(currentRadioChannel, capability);
                    }
                    catch (Exception radioEx)
                    {
                        currentRadioChannel = string.Empty;
                        Debug.LogWarning($"[VoiceChat] Radio channel join failed; proximity voice still active: {radioEx.Message}");
                    }

                    await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, normalized);
                    await UpdateLocalPositionSafelyAsync();
                }
                else
                {
                    await VivoxService.Instance.JoinGroupChannelAsync(normalized, capability);
                }

                currentChannel = normalized;
                vivoxAvailable = true;
                lastVivoxError = "";
                Debug.Log($"[VoiceChat] Joined channel: {currentChannel}");
            }
            catch (Exception ex)
            {
                vivoxAvailable = false;
                lastVivoxError = ex.Message;
                // Non-fatal — game works without voice.
                Debug.LogWarning($"[VoiceChat] Join failed: {ex.Message}");
            }
        }

        /// <summary>Leaves the current voice channel. Non-fatal if already empty.</summary>
        public async Task LeaveCurrentChannelAsync()
        {
            await channelGate.WaitAsync();
            try { await LeaveCurrentChannelCoreAsync(); }
            finally { channelGate.Release(); }
        }

        private async Task LeaveCurrentChannelCoreAsync()
        {
            if (string.IsNullOrEmpty(currentChannel))
            {
                return;
            }

            string channelToLeave = currentChannel;
            string radioChannelToLeave = currentRadioChannel;
            currentChannel = string.Empty;
            currentRadioChannel = string.Empty;
            wantsRadioBroadcast = false;
            radioBroadcasting = false;

            await TryLeaveChannelAsync(channelToLeave);
            await TryLeaveChannelAsync(radioChannelToLeave);
        }

        private async Task TryLeaveChannelAsync(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            try { await VivoxService.Instance.LeaveChannelAsync(channel); }
            catch (Exception ex)
            {
                lastVivoxError = ex.Message;
                Debug.LogWarning($"[VoiceChat] Leave failed for {channel}: {ex.Message}");
            }
        }

        public bool IsRadioBroadcasting => radioBroadcasting;
        private bool radioStartPending;
        public async void BeginRadioBroadcast()
        {
            if (reviveMuted || paMuted) return;
            wantsRadioBroadcast = true;
            if (radioStartPending) return;
            radioStartPending = true;
            await channelGate.WaitAsync();
            try { await BeginRadioBroadcastCoreAsync(); }
            finally { radioStartPending = false; channelGate.Release(); }
        }

        private async Task BeginRadioBroadcastCoreAsync()
        {
            if (!wantsRadioBroadcast || reviveMuted || paMuted || !useProximityVoice || !initialized || !loggedIn || string.IsNullOrEmpty(currentRadioChannel) || radioBroadcasting)
            {
                return;
            }

            try
            {
                await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, currentRadioChannel);
                if ((!wantsRadioBroadcast || reviveMuted || paMuted) && !string.IsNullOrEmpty(currentChannel))
                {
                    await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, currentChannel);
                    radioBroadcasting = false;
                    return;
                }

                radioBroadcasting = true;
                Debug.Log("[VoiceChat] Radio broadcast started.");
            }
            catch (Exception ex)
            {
                wantsRadioBroadcast = false;
                radioBroadcasting = false;
                Debug.LogWarning($"[VoiceChat] Radio broadcast start failed: {ex.Message}");
            }
        }

        public async void EndRadioBroadcast()
        {
            wantsRadioBroadcast = false;
            await channelGate.WaitAsync();
            try { await EndRadioBroadcastCoreAsync(); }
            finally { channelGate.Release(); }
        }

        private async Task EndRadioBroadcastCoreAsync()
        {
            wantsRadioBroadcast = false;

            if (!useProximityVoice || !initialized || !loggedIn || string.IsNullOrEmpty(currentChannel) || !radioBroadcasting)
            {
                return;
            }

            try
            {
                await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, currentChannel);
                radioBroadcasting = false;
                Debug.Log("[VoiceChat] Radio broadcast ended.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VoiceChat] Radio broadcast end failed: {ex.Message}");
            }
        }

        private Task UpdateLocalPositionSafelyAsync()
        {
            if (string.IsNullOrEmpty(currentChannel))
            {
                return Task.CompletedTask;
            }

            try
            {
                // Lobby chat is centered rather than positioned around the menu camera.
                string scene=SceneManager.GetActiveScene().name;
                if(scene=="MainMenu"||scene=="Menu")
                {
                    VivoxService.Instance.Set3DPosition(Vector3.zero,Vector3.zero,Vector3.forward,Vector3.up,currentChannel,allowPanning:false);
                    return Task.CompletedTask;
                }
                Transform source = ResolveLocalVoiceTransform();
                if(source==null)return Task.CompletedTask;
                Vector3 position = source.position;
                Transform listener = ResolveLocalVoiceListener(source);
                VivoxService.Instance.Set3DPosition(position, listener.position, listener.forward, listener.up, currentChannel);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VoiceChat] Position update failed: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private Transform ResolveLocalVoiceTransform()
        {
            // Prefer explicitly assigned transform.
            if (localVoiceTransform != null)
            {
                return localVoiceTransform;
            }

            // Try to get the local player's transform from NetworkManager.
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && nm.LocalClient?.PlayerObject != null)
            {
                localVoiceTransform = nm.LocalClient.PlayerObject.transform;
                return localVoiceTransform;
            }

            // Fallback to main camera.
            if (Camera.main != null)
            {
                return Camera.main.transform;
            }

            return null;
        }

        private Transform ResolveLocalVoiceListener(Transform speaker)
        {
            var player = NetworkManager.Singleton?.LocalClient?.PlayerObject;
            var spectator = player != null ? player.GetComponent<PlayerDeathSpectator>() : null;
            if (spectator != null && spectator.IsSpectating)
            {
                var watched = PlayerDeathSpectator.HudPlayer;
                var controller = watched != null ? watched.GetComponent<PlayerController>() : null;
                if (controller != null && controller.playerCamera != null) return controller.playerCamera;
                if (watched != null) return watched;
            }
            return speaker;
        }

        private void TryAttachRadioStationToSceneObject()
        {
            if (Time.time < nextRadioStationScanTime)
            {
                return;
            }

            nextRadioStationScanTime = Time.time + 1f;

            GameObject radioObject = GameObject.Find("Radio");
            if (radioObject == null || radioObject.GetComponent<global::RadioBroadcastStation>() != null)
            {
                return;
            }

            radioObject.AddComponent<global::RadioBroadcastStation>();
            Debug.Log("[VoiceChat] Radio broadcast station attached to scene object named Radio.");
        }
        private bool reviveMuted, paMuted, capturedMuteState, previousInputMute, previousOutputMute;
        public bool ReviveMuted => reviveMuted;
        public bool PAScreecherMuted => paMuted;
        public void SetReviveMuted(bool value) { if(reviveMuted==value)return; reviveMuted=value; RefreshMuteState(); }
        public void SetPAScreecherMuted(bool value) { if(paMuted==value)return; paMuted=value; RefreshMuteState(); }
        private void RefreshMuteState()
        {
            if(reviveMuted || paMuted){EndRadioBroadcast();ApplyReviveMute();return;}
            if(capturedMuteState && initialized){
                try {
                    if(previousInputMute)VivoxService.Instance.MuteInputDevice();else VivoxService.Instance.UnmuteInputDevice();
                    if(previousOutputMute)VivoxService.Instance.MuteOutputDevice();else VivoxService.Instance.UnmuteOutputDevice();
                }catch(Exception ex){Debug.LogWarning("[VoiceChat] Restore audio failed: "+ex.Message);}
            }
            capturedMuteState=false;
        }
        private void ApplyReviveMute()
        {
            if ((!reviveMuted && !paMuted) || !initialized) return;
            try
            {
                if (!capturedMuteState)
                {
                    previousInputMute = VivoxService.Instance.IsInputDeviceMuted;
                    previousOutputMute = VivoxService.Instance.IsOutputDeviceMuted;
                    capturedMuteState = true;
                }
                VivoxService.Instance.MuteInputDevice();
                VivoxService.Instance.MuteOutputDevice();
            }
            catch (Exception ex) { Debug.LogWarning("[VoiceChat] Revive audio mute failed: " + ex.Message); }
        }
    }
}
