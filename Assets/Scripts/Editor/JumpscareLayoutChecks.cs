#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class JumpscareLayoutChecks
{
    [InitializeOnLoadMethod]
    private static void Requested()
    {
        if (File.Exists("Logs/RunJumpscareLayoutChecks.request")) EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/Death Presentation/Check Stable Scare Layout")]
    public static void Run()
    {
        File.Delete("Logs/RunJumpscareLayoutChecks.request");
        var owner = new GameObject("Temporary moving jumpscare owner");
        Canvas canvas = null;
        try
        {
            var presentation = owner.AddComponent<LocalJumpscarePresentation>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(LocalJumpscarePresentation).GetMethod("EnsureCanvas", flags).Invoke(presentation, null);
            canvas = (Canvas)typeof(LocalJumpscarePresentation).GetField("canvas", flags).GetValue(presentation);
            var image = (Image)typeof(LocalJumpscarePresentation).GetField("image", flags).GetValue(presentation);
            var label = (TMP_Text)typeof(LocalJumpscarePresentation).GetField("label", flags).GetValue(presentation);
            if (canvas.transform.parent != null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                throw new Exception("Scare canvas inherits a world transform");
            Canvas.ForceUpdateCanvases();
            var originalImage = Corners(image.rectTransform);
            var originalText = Corners(label.rectTransform);
            int assertions = 1;
            for (int i = 1; i <= 5; i++)
            {
                owner.transform.position = new Vector3(2000 + i * 12, 60 - i, 1200 + i * 3);
                owner.transform.rotation = Quaternion.Euler(i * 23, i * 67, i * 31);
                owner.transform.localScale = new Vector3(i * .5f, i * 1.3f, i * .8f);
                Canvas.ForceUpdateCanvases();
                AssertSame(originalImage, Corners(image.rectTransform)); assertions++;
                AssertSame(originalText, Corners(label.rectTransform)); assertions++;
            }
            File.WriteAllText("Logs/JumpscareLayoutChecks.txt", "PASS " + assertions + " assertions: screen-space isolation, fixed image/text corners across moving/rotating/scaling player.");
        }
        catch (Exception error) { File.WriteAllText("Logs/JumpscareLayoutChecks.txt", "FAIL " + error); Debug.LogException(error); }
        finally
        {
            if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    private static Vector3[] Corners(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners); return corners;
    }
    private static void AssertSame(Vector3[] expected, Vector3[] actual)
    {
        for (int i = 0; i < 4; i++)
            if ((expected[i] - actual[i]).sqrMagnitude > .0001f) throw new Exception("Jumpscare UI moved with the body");
    }
}
#endif
