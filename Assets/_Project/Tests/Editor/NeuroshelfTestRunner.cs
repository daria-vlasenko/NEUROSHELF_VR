// NEUROSHELF — запуск автотестов Play Mode из меню (и через мост NeuroshelfRemote) с протоколом в файл.
// Меню NEUROSHELF → Сценарий → 9.6 Автотесты Play Mode → Отчёты/ЛР7/10_Автотесты_Play_Mode.txt

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

[InitializeOnLoad]
public static class NeuroshelfTestRunner
{
    const string Key = "ns_scenario_tests";
    static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР7"));

    static NeuroshelfTestRunner()
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Callbacks());
    }

    [MenuItem("NEUROSHELF/Сценарий/9.6 Автотесты Play Mode → протокол", priority = 520)]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NEUROSHELF] Выйдите из Play Mode"); return; }
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        SessionState.SetString(Key, DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode, assemblyNames = new[] { "Neuroshelf.Tests.PlayMode" } }));
        Debug.Log("[NEUROSHELF] 9.6 Автотесты Play Mode запущены");
    }

    class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            var started = SessionState.GetString(Key, "");
            if (started == "") return;
            SessionState.EraseString(Key);
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("NEUROSHELF — автотесты сценария в Play Mode (Unity Test Framework), запуск " + started);
            sb.AppendLine("Сборка Neuroshelf.Tests.PlayMode, сцена Assets/_Project/Scenes/02_Store.unity загружается заново для каждого теста.");
            sb.AppendLine();
            var leaves = result.Children.Count() == 0 ? new[] { result } : Flatten(result).Where(r => !r.HasChildren).ToArray();
            foreach (var r in leaves)
            {
                sb.AppendLine(string.Format(ci, "[{0}] {1}  ({2:0.0} с)", r.TestStatus == TestStatus.Passed ? "PASS" : r.TestStatus.ToString().ToUpperInvariant(), r.Test.Name, r.Duration));
                if (!string.IsNullOrEmpty(r.Output))
                    foreach (var l in r.Output.Split('\n').Where(l => l.Trim().Length > 0 && !l.Contains("[NS-") && !l.Contains("generators.ai") && !l.StartsWith("Saving results")))
                        sb.AppendLine("      " + l.TrimEnd());
                if (r.TestStatus == TestStatus.Failed) sb.AppendLine("      ПРИЧИНА: " + r.Message?.Trim());
            }
            sb.AppendLine();
            sb.AppendLine($"ИТОГ: пройдено {result.PassCount}, не пройдено {result.FailCount}, пропущено {result.SkipCount + result.InconclusiveCount}, всего {leaves.Length}");
            Directory.CreateDirectory(OutDir);
            var path = Path.Combine(OutDir, "40_Автотесты_Play_Mode.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("[NEUROSHELF] 9.6 Протокол автотестов: " + path + $" · пройдено {result.PassCount}, не пройдено {result.FailCount}");
        }

        static System.Collections.Generic.IEnumerable<ITestResultAdaptor> Flatten(ITestResultAdaptor r)
        {
            yield return r;
            foreach (var c in r.Children) foreach (var x in Flatten(c)) yield return x;
        }
    }
}
