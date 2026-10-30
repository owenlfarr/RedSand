#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Read-only preflight: cloning scene YAML also copies NGO IDs, which include
// the scene GUID. Never ship IDs that differ between Editor and standalone.
public sealed class NetworkSceneIdentityBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        var hashType = typeof(NetworkObject).Assembly.GetType("Unity.Netcode.XXHash", true);
        var hashString = hashType.GetMethod("Hash32", BindingFlags.Static | BindingFlags.Public,
            null, new[] { typeof(string) }, null);
        if (hashString == null) throw new BuildFailedException("Cannot validate Netcode scene IDs with this package version.");

        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (!scene.enabled || !File.Exists(scene.path)) continue;
            string guid = AssetDatabase.AssetPathToGUID(scene.path);
            string yaml = File.ReadAllText(scene.path);
            var hashes = new System.Collections.Generic.HashSet<uint>();
            foreach (Match block in Regex.Matches(yaml,
                @"^--- !u!114 &(\d+)[^\r\n]*\r?\n(.*?)(?=^--- !u!|\z)",
                RegexOptions.Multiline | RegexOptions.Singleline))
            {
                string body = block.Groups[2].Value;
                if (!body.Contains("guid: d5a57f767e5e46a458fc5d3c628d0cbb")) continue;
                var field = Regex.Match(body, @"^  GlobalObjectIdHash: (\d+)", RegexOptions.Multiline);
                if (!field.Success) continue;
                uint serialized = uint.Parse(field.Groups[1].Value);
                string globalId = $"GlobalObjectId_V1-2-{guid}-{block.Groups[1].Value}-0";
                uint expected = (uint)hashString.Invoke(null, new object[] { globalId });
                if (serialized != expected || !hashes.Add(serialized))
                    throw new BuildFailedException($"Stale or duplicate Netcode scene ID in {scene.path}, object {block.Groups[1].Value}. " +
                        "Open and save this scene in Edit Mode to regenerate its NetworkObject IDs, then rebuild all players.");
            }
        }
    }
}
#endif
