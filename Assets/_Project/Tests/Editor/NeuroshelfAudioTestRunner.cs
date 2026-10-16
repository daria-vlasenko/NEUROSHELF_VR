// NEUROSHELF — все автотесты Play Mode (ЛР7 + ЛР8) одним запуском с протоколом в Отчёты/ЛР8.
// Меню NEUROSHELF → Звук и интерфейс → 10.11. Тесты ЛР8 помечены категорией LR8, остальные — регрессия ЛР7.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

[InitializeOnLoad]
public static class NeuroshelfAudioTestRunner
{
    const string Key = "ns_audio_tests";
    static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР8"));

    static NeuroshelfAudioTestRunner()
    {
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Callbacks());
    }

    [MenuItem("NEUROSHELF/Звук и интерфейс/10.11 Автотесты Play Mode: ЛР8 + регрессия ЛР7 → протокол", priority = 621)]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NEUROSHELF] Выйдите из Play Mode"); return; }
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        SessionState.SetString(Key, DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.PlayMode, assemblyNames = new[] { "Neuroshelf.Tests.PlayMode" } }));
        Debug.Log("[NEUROSHELF] 10.11 Автотесты запущены");
    }

    class Callbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor t) { }
        public void TestStarted(ITestAdaptor t) { }
        public void TestFinished(ITestResultAdaptor r) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            var started = SessionState.GetString(Key, "");
            if (started == "") return;
            SessionState.EraseString(Key);
            var ci = CultureInfo.InvariantCulture;
            var leaves = Flatten(result).Where(r => !r.HasChildren).ToArray();
            var sb = new StringBuilder();
            sb.AppendLine("NEUROSHELF — автотесты Play Mode (Unity Test Framework), запуск " + started);
            sb.AppendLine("Сборка Neuroshelf.Tests.PlayMode; сцена 02_Store загружается заново для каждого теста.");
            foreach (var lr8 in new[] { true, false })
            {
                var part = leaves.Where(r => r.Test.Categories.Contains("LR8") == lr8).ToArray();
                sb.AppendLine();
                sb.AppendLine(lr8 ? "ЛР8 — ЗВУК, ИНТЕРФЕЙСЫ, ВИБРАЦИЯ" : "РЕГРЕССИЯ ЛР7 — СЦЕНАРИЙ");
                foreach (var r in part)
                {
                    sb.AppendLine(string.Format(ci, "[{0}] {1}  ({2:0.0} с)", r.TestStatus == TestStatus.Passed ? "PASS" : r.TestStatus.ToString().ToUpperInvariant(), r.Test.Name, r.Duration));
                    if (!string.IsNullOrEmpty(r.Output))
                        foreach (var l in r.Output.Split('\n').Where(l => l.Trim().Length > 0 && !l.Contains("[NS-") && !l.Contains("generators.ai") && !l.StartsWith("Saving results")))
                            sb.AppendLine("      " + l.TrimEnd());
                    if (r.TestStatus == TestStatus.Failed) sb.AppendLine("      ПРИЧИНА: " + r.Message?.Trim());
                }
                sb.AppendLine($"   итого: {part.Count(r => r.TestStatus == TestStatus.Passed)} из {part.Length}");
            }
            sb.AppendLine();
            sb.AppendLine($"ИТОГ: пройдено {result.PassCount}, не пройдено {result.FailCount}, пропущено {result.SkipCount + result.InconclusiveCount}, всего {leaves.Length}");
            Directory.CreateDirectory(OutDir);
            var path = Path.Combine(OutDir, "40_Автотесты_Play_Mode.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            Debug.Log("[NEUROSHELF] 10.11 Протокол автотестов: " + path + $" · пройдено {result.PassCount}, не пройдено {result.FailCount}");
        }

        static System.Collections.Generic.IEnumerable<ITestResultAdaptor> Flatten(ITestResultAdaptor r)
        {
            yield return r;
            foreach (var c in r.Children) foreach (var x in Flatten(c)) yield return x;
        }
    }
}
