// NEUROSHELF — финальная десктопная сборка Windows x64 (Release, без Development/Profiler).
// Меню NEUROSHELF → Сборка → Release Windows x64 → Builds/Release/NEUROSHELF_VR.exe, протокол в Temp/ns_log.txt.

using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class NeuroshelfReleaseBuild
{
    const string Scene = "Assets/_Project/Scenes/02_Store.unity";

    [MenuItem("NEUROSHELF/Сборка/Release Windows x64", priority = 900)]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NEUROSHELF] Выйдите из Play Mode"); return; }
        EditorSceneManager.SaveOpenScenes();

        if (!EditorBuildSettings.scenes.Any(s => s.path == Scene && s.enabled))
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(s => s.path != Scene)
                .Append(new EditorBuildSettingsScene(Scene, true)).ToArray();

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.connectProfiler = false;

        var exe = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Release", "NEUROSHELF_VR.exe"));
        var opts = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        };
        var sw = Stopwatch.StartNew();
        var s = BuildPipeline.BuildPlayer(opts).summary;
        Debug.Log($"[NEUROSHELF] RELEASE BUILD: {s.result}, {sw.Elapsed.TotalSeconds:0} с, {s.totalSize / 1048576.0:0.0} МБ, " +
                  $"ошибок {s.totalErrors}, предупреждений {s.totalWarnings}, сцены: {string.Join(", ", opts.scenes)} → {exe}");
    }
}
