using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Vivox;
using Unity.Netcode;

namespace Networking
{
    public class VoiceChatManager : MonoBehaviour
    {
        [Header("Voice Chat")]
        [SerializeField] private bool autoJoinAudioOnly = true;
        [SerializeField] private bool useProximityVoice = true;
        [SerializeField] private float audibleRange = 18f;
        [SerializeField] private float conversationalRange = 6f;
        [SerializeField] private float audioFadeModelExponent = 1f;
        [SerializeField] private float positionUpdateInterval = 0.1f;
        [SerializeField] private Transform localVoiceTransform;
        [SerializeField] private bool vivoxAvailable;
        [SerializeField] private string lastVivoxError = "";

        private bool initialized;
        private bool loggedIn;
        private string currentChannel = string.Empty;
        private float nextPositionSyncTime;
        private Channel3DProperties currentChannel3DProperties;

        public string CurrentChannel => currentChannel;
        public bool VivoxAvailable => vivoxAvailable;
        public string LastVivoxError => lastVivoxError;

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

        public async Task EnsureReadyAsync()
        {
            try
            {
                if (!initialized)
                {
                    await VivoxService.Instance.InitializeAsync();
                    initialized = true;
                    vivoxAvailable = true;
                    lastVivoxError = "";
                    Debug.Log("[VoiceChat] Vivox initialized.");
                }

                if (!loggedIn)
                {
                    await VivoxService.Instance.LoginAsync();
                    loggedIn = true;
                    vivoxAvailable = true;
                    lastVivoxError = "";
                    Debug.Log("[VoiceChat] Vivox login succeeded.");
                }
            }
            catch (Exception ex)
            {
                vivoxAvailable = false;
                lastVivoxError = ex.Message;
                Debug.LogWarning($"[VoiceChat] Vivox unavailable: {ex.Message}");
                throw;
            }
        }

        public async Task JoinLobbyVoiceAsync(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                return;
            }
            try
            {
                await EnsureReadyAsync();

                string normalized = channelName.Trim().ToUpperInvariant();
                if (string.Equals(currentChannel, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                if (!string.IsNullOrEmpty(currentChannel))
                {
                    await VivoxService.Instance.LeaveChannelAsync(currentChannel);
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
                Debug.LogWarning($"[VoiceChat] Join failed: {ex.Message}");
                throw;
            }
        }

        public async Task LeaveCurrentChannelAsync()
        {
            if (string.IsNullOrEmpty(currentChannel))
            {
                return;
            }

            try
            {
                await VivoxService.Instance.LeaveChannelAsync(currentChannel);
                currentChannel = string.Empty;
            }
            catch (Exception ex)
            {
                lastVivoxError = ex.Message;
                Debug.LogWarning($"[VoiceChat] Leave failed: {ex.Message}");
                throw;
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
            if (localVoiceTransform != null)
            {
                return localVoiceTransform;
            }

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && nm.LocalClient != null)
            {
                var playerObject = nm.LocalClient.PlayerObject;
                if (playerObject != null)
                {
                    localVoiceTransform = playerObject.transform;
                    return localVoiceTransform;
                }
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                return cam.transform;
            }

            return null;
        }
    }
}
