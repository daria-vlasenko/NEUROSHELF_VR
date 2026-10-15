using UnityEngine;

// Метка текущего момента сессии: состояние сценария, номер захода, условие эксперимента.
// Пишет её только слой аналитики (ScenarioSessionRecorder) по событию смены состояния;
// логгеры (ProductInteractionHub, RouteLogger) только читают и приклеивают к своим записям.
// Сценарий о метке не знает — так связь «сценарий → аналитика» остаётся односторонней.
public static class SessionStamp
{
    public static string State = "";
    public static int Run;              // 0 — вне захода, 1 — заход A, 2 — заход C
    public static string Condition = "";

    public static string Tag => Run > 0 ? $"run{Run}/{State}" : State;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { State = ""; Run = 0; Condition = ""; }
}
