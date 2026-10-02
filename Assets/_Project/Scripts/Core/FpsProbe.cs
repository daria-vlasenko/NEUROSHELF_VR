using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

// Замер частоты кадров: 3 с прогрева, 10 с записи, затем файл с итогами
// и автоматический выход из Play Mode. Добавляется в сцену из меню
// NEUROSHELF → Свет → 5.4, вручную вешать не нужно.
public class FpsProbe : MonoBehaviour
{
    public float warmup = 3f;
    public float duration = 10f;
    const float TargetMs = 1000f / 90f;   // 11.1 мс — бюджет кадра для 90 FPS

    float t;
    readonly List<float> ms = new List<float>(2048);
    bool done;

    void Update()
    {
        if (done) return;
        t += Time.unscaledDeltaTime;
        if (t < warmup) return;
        if (t < warmup + duration) { ms.Add(Time.unscaledDeltaTime * 1000f); return; }
        done = true;
        Write();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
#endif
    }

    void Write()
    {
        var s = ms.OrderBy(x => x).ToList();
        float avg = s.Average();
        float med = s[s.Count / 2];
        float p99 = s[Mathf.Clamp((int)(s.Count * 0.99f), 0, s.Count - 1)];
        int over = s.Count(x => x > TargetMs);

        var sb = new StringBuilder();
        sb.AppendLine("NEUROSHELF — замер FPS");
        sb.AppendLine($"Дата: {System.DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} · CPU: {SystemInfo.processorType}");
        sb.AppendLine($"Окно: {Screen.width}×{Screen.height} · XR-устройство активно: {XRSettings.isDeviceActive}");
        sb.AppendLine($"Кадров: {s.Count} за {duration:F0} с");
        sb.AppendLine($"Средний кадр: {avg:F2} мс → {1000f / avg:F1} FPS");
        sb.AppendLine($"Медиана: {med:F2} мс → {1000f / med:F1} FPS");
        sb.AppendLine($"1% худших кадров: {p99:F2} мс → {1000f / p99:F1} FPS");
        sb.AppendLine($"Бюджет 90 FPS = {TargetMs:F1} мс; кадров сверх бюджета: {over} ({100f * over / s.Count:F1}%)");
        sb.AppendLine("Примечание: замер в Game View редактора, без шлема. Редактор добавляет свои накладные расходы.");

        var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР3"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "09_Замер_FPS.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Debug.Log("[NEUROSHELF] " + path + "\n" + sb);
    }
}
