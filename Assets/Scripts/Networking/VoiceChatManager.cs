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
        private float nextPositionSyncTime;
        private Channel3DProperties currentChannel3DProperties;

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
        }

        private void Update()
        {
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
                    try
                    {
                        await VivoxService.Instance.LeaveChannelAsync(currentChannel);
                    }
                    catch (Exception leaveEx)
                    {
                        Debug.LogWarning($"[VoiceChat] Leave previous channel failed (continuing): {leaveEx.Message}");
                    }
                    currentChannel = string.Empty;
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
            if (string.IsNullOrEmpty(currentChannel))
            {
                return;
            }

            string channelToLeave = currentChannel;
            currentChannel = string.Empty;

            try
            {
                await VivoxService.Instance.LeaveChannelAsync(channelToLeave);
                Debug.Log($"[VoiceChat] Left channel: {channelToLeave}");
            }
            catch (Exception ex)
            {
                lastVivoxError = ex.Message;
                Debug.LogWarning($"[VoiceChat] Leave failed: {ex.Message}");
            }
        }

        private Task UpdateLocalPositionSafelyAsync()
        {
            if (string.IsNullOrEmpty(currentChannel))
            {
                return Task.CompletedTask;
            }

            Transform source = ResolveLocalVoiceTransform();
            if (source == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                Vector3 position = source.position;
                Vector3 forward = source.forward;
                Vector3 up = source.up;
                VivoxService.Instance.Set3DPosition(position, position, forward, up, currentChannel);
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
    }
}
