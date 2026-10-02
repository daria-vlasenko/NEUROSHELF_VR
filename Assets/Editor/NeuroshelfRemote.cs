// Служебный мост для автоматизации: редактор раз в полсекунды проверяет файл
// Temp/ns_cmd.txt (одна строка = путь пункта меню), выполняет его и удаляет файл.
// Все сообщения консоли дублируются в Temp/ns_log.txt.
// Папка Temp не попадает в git и очищается при закрытии Unity.

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class NeuroshelfRemote
{
    static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp"));
    static readonly string Cmd = Path.Combine(Dir, "ns_cmd.txt");
    static readonly string Log = Path.Combine(Dir, "ns_log.txt");
    static double next;

    static NeuroshelfRemote()
    {
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        UnityEditor.Compilation.CompilationPipeline.assemblyCompilationFinished += (asm, msgs) =>
        {
            foreach (var m in msgs)
                if (m.type == UnityEditor.Compilation.CompilerMessageType.Error) Append("COMPILE ERROR: " + m.message);
        };
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
        Append($"=== domain reload {DateTime.Now:HH:mm:ss} · playing={EditorApplication.isPlaying} ===");
    }

    static void OnLog(string msg, string stack, LogType type)
    {
        Append($"[{DateTime.Now:HH:mm:ss}] {type}: {msg}" +
               (type == LogType.Error || type == LogType.Exception ? "\n" + stack : ""));
    }

    static void Append(string s)
    {
        try { Directory.CreateDirectory(Dir); File.AppendAllText(Log, s + "\n"); } catch { }
    }

    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 0.5;
        if (!File.Exists(Cmd)) return;

        string[] lines;
        try { lines = File.ReadAllLines(Cmd); File.Delete(Cmd); } catch { return; }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            Append($"[{DateTime.Now:HH:mm:ss}] CMD: {line}");
            try
            {
                if (line == "refresh")
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                }
                else if (line.StartsWith("open:")) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(line.Substring(5));
                else if (!EditorApplication.ExecuteMenuItem(line)) Append("  → пункт меню не найден");
            }
            catch (Exception e) { Append("  → " + e); }
        }
    }
}
