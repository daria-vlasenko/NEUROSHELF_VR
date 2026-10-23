// NEUROSHELF — все автотесты Play Mode (ЛР7 + ЛР8 + ЛР9) одним запуском с протоколом в Отчёты/ЛР9.
// Меню NEUROSHELF → Производительность → 11.9. Категория LR9 — бюджет кадра и оптимизации,
// LR8 — звук и интерфейсы, без категории — регрессия ЛР7 (сценарий).

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

[InitializeOnLoad]
public static class NeuroshelfPerfTestRunner
{
    const string Key = "ns_perf_tests";
    static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР9"));

    static NeuroshelfPerfTestRunner()
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Callbacks());
    }

    [MenuItem("NEUROSHELF/Производительность/11.9 Автотесты Play Mode: ЛР9 + регрессия ЛР7 и ЛР8 → протокол", priority = 750)]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NEUROSHELF] Выйдите из Play Mode"); return; }
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        SessionState.SetString(Key, DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode, assemblyNames = new[] { "Neuroshelf.Tests.PlayMode" } }));
        Debug.Log("[NEUROSHELF] 11.9 Автотесты запущены");
    }

    // Тест бюджета кадра в сборке Windows: UTF собирает плеер с тестами категории LR9 и запускает его.
    const string PlayerKey = "ns_perf_tests_player";
    [MenuItem("NEUROSHELF/Производительность/11.9b Тест бюджета кадра в сборке Windows (≈5 мин)", priority = 751)]
    public static void RunInPlayer()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NEUROSHELF] Выйдите из Play Mode"); return; }
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        SessionState.SetString(PlayerKey, DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.PlayMode, assemblyNames = new[] { "Neuroshelf.Tests.PlayMode" },
            categoryNames = new[] { "LR9" }, targetPlatform = BuildTarget.StandaloneWindows64,
        }));
        Debug.Log("[NEUROSHELF] 11.9b Тест бюджета в сборке запущен");
    }

    class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor t) { }
        public void TestStarted(ITestAdaptor t) { }
        public void TestFinished(ITestResultAdaptor r) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            var started = SessionState.GetString(Key, "");
            bool player = false;
            if (started == "") { started = SessionState.GetString(PlayerKey, ""); player = started != ""; }
            if (started == "") return;
            SessionState.EraseString(Key); SessionState.EraseString(PlayerKey);
            var ci = CultureInfo.InvariantCulture;
            var leaves = Flatten(result).Where(r => !r.HasChildren).ToArray();
            var sb = new StringBuilder();
            sb.AppendLine("NEUROSHELF — автотесты Play Mode (Unity Test Framework), запуск " + started + (player ? " — В СБОРКЕ Windows (StandaloneWindows64), только категория LR9" : " — в Play Mode редактора"));
            sb.AppendLine("Сборка Neuroshelf.Tests.PlayMode; сцена 02_Store загружается заново для каждого теста.");
            foreach (var part in new[] { ("LR9", "ЛР9 — БЮДЖЕТ КАДРА И ОПТИМИЗАЦИИ"), ("LR8", "РЕГРЕССИЯ ЛР8 — ЗВУК, ИНТЕРФЕЙСЫ, ВИБРАЦИЯ"), ("", "РЕГРЕССИЯ ЛР7 — СЦЕНАРИЙ") })
            {
                var rs = leaves.Where(r => part.Item1 == "" ? !r.Test.Categories.Contains("LR8") && !r.Test.Categories.Contains("LR9") : r.Test.Categories.Contains(part.Item1)).ToArray();
                sb.AppendLine();
                sb.AppendLine(part.Item2);
                foreach (var r in rs)
                {
                    sb.AppendLine(string.Format(ci, "[{0}] {1}  ({2:0.0} с)", r.TestStatus == TestStatus.Passed ? "PASS" : r.TestStatus.ToString().ToUpperInvariant(), r.Test.Name, r.Duration));
                    if (!string.IsNullOrEmpty(r.Output))
                        foreach (var l in r.Output.Split('\n').Where(l => l.Contains("[LR9]") || (part.Item1 != "LR9" && l.Trim().Length > 0 && !l.Contains("[NS-") && !l.Contains("generators.ai") && !l.StartsWith("Saving results"))))
                            sb.AppendLine("      " + l.TrimEnd());
                    if (r.TestStatus == TestStatus.Failed) sb.AppendLine("      ПРИЧИНА: " + r.Message?.Trim());
                }
                sb.AppendLine($"   итого: {rs.Count(r => r.TestStatus == TestStatus.Passed)} из {rs.Length}");
            }
            sb.AppendLine();
            sb.AppendLine($"ИТОГ: пройдено {result.PassCount}, не пройдено {result.FailCount}, пропущено {result.SkipCount + result.InconclusiveCount}, всего {leaves.Length}");
            Directory.CreateDirectory(OutDir);
            var path = Path.Combine(OutDir, player ? "41_Тест_бюджета_в_сборке.txt" : "40_Автотесты_Play_Mode.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("[NEUROSHELF] 11.9 Протокол автотестов: " + path + $" · пройдено {result.PassCount}, не пройдено {result.FailCount}");
        }

        static System.Collections.Generic.IEnumerable<ITestResultAdaptor> Flatten(ITestResultAdaptor r)
        {
            yield return r;
            foreach (var c in r.Children) foreach (var x in Flatten(c)) yield return x;
        }
    }
}
