using System;
using UnityEngine;
using UnityEngine.UI;

// Табличка у входа с состоянием сессии: номер сессии, заход, этап, время, индикатор записи.
// Условие эксперимента (A/C) на табличке НЕ показывается: участник не должен знать, в каком он условии —
// иначе исчезает «наивность» первого захода. Факт записи, наоборот, показывается: согласие на запись
// поведения — этическое требование, а раскрытие гипотезы — нет (разбор после захода A это делает).
public class SessionSign : MonoBehaviour
{
    public Text title, state, meta;
    public Image recDot;

    public string SessionId { get; private set; }
    float t0;

    void Start()
    {
        SessionId = DateTime.Now.ToString("ddMM-HHmm");
        t0 = Time.time;
    }

    // Текст обновляется по событию — когда сменилась секунда, этап или заход, а не каждый кадр:
    // раньше каждый кадр собирались три новые строки (выделение памяти в куче каждый кадр).
    int lastSecond = -1, lastRun = -1; ScenarioState lastState; bool lastActive, lastDot, drawn;
    public int Redraws { get; private set; }

    void Update()
    {
        var sc = ScenarioController.Instance;
        int s = Mathf.FloorToInt(Time.time - t0);
        int run = sc != null ? sc.Run : 1;
        var st = sc != null ? sc.State : ScenarioState.WaitingForEntry;
        bool active = sc != null && sc.Active;
        if (!drawn || s != lastSecond || run != lastRun || st != lastState || active != lastActive)
        {
            drawn = true; lastSecond = s; lastRun = run; lastState = st; lastActive = active; Redraws++;
            if (title != null) title.text = "NEUROSHELF · сессия " + SessionId;
            if (state != null) state.text = Phrase(sc);
            if (meta != null) meta.text = $"Заход {run} из 2   ·   {s / 60:00}:{s % 60:00}   ·   идёт запись поведения";
        }
        bool dot = Mathf.Repeat(Time.time, 1.2f) < 0.8f;
        if (recDot != null && dot != lastDot) { lastDot = dot; recDot.enabled = dot; }
    }

    public static string Phrase(ScenarioController sc)
    {
        if (sc == null || !sc.Active) return "Стенд в режиме проверки";
        switch (sc.State)
        {
            case ScenarioState.WaitingForEntry: return "Магазин открыт — входите";
            case ScenarioState.Preparation:
            case ScenarioState.Ready: return "Подготовка к покупкам";
            case ScenarioState.RunA:
            case ScenarioState.RunC: return "Идут покупки";
            case ScenarioState.Paused: return "Пауза";
            case ScenarioState.Checkout: return "Оплата на кассе";
            case ScenarioState.Debrief: return "Разбор первого захода";
            case ScenarioState.Results: return "Сессия завершена";
        }
        return sc.State.ToString();
    }
}
