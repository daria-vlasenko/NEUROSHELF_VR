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

    void Update()
    {
        var sc = ScenarioController.Instance;
        if (title != null) title.text = "NEUROSHELF · сессия " + SessionId;
        if (state != null) state.text = Phrase(sc);
        if (meta != null)
        {
            int s = Mathf.FloorToInt(Time.time - t0);
            meta.text = $"Заход {(sc != null ? sc.Run : 1)} из 2   ·   {s / 60:00}:{s % 60:00}   ·   идёт запись поведения";
        }
        if (recDot != null) recDot.enabled = Mathf.Repeat(Time.time, 1.2f) < 0.8f;
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
