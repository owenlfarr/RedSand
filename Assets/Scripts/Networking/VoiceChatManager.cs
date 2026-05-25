using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Networking
{
    public class VoiceChatManager : MonoBehaviour
    {
        [Header("Voice Chat")]
        [SerializeField] private bool autoJoinAudioOnly = true;

        private bool initialized;
        private bool loggedIn;
        private string currentChannel = string.Empty;

        public string CurrentChannel => currentChannel;

        public async Task EnsureReadyAsync()
        {
            await Task.CompletedTask;
            initialized = true;
            loggedIn = true;
        }

        public async Task JoinLobbyVoiceAsync(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                return;
            }

            await EnsureReadyAsync();

            string normalized = channelName.Trim().ToUpperInvariant();
            if (string.Equals(currentChannel, normalized, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(currentChannel))
            {
                currentChannel = string.Empty;
            }

            currentChannel = normalized;
            Debug.Log($"[VoiceChat] Channel set (fallback): {currentChannel}");
        }

        public async Task LeaveCurrentChannelAsync()
        {
            if (string.IsNullOrEmpty(currentChannel))
            {
                return;
            }

            currentChannel = string.Empty;
            await Task.CompletedTask;
        }
    }
}
