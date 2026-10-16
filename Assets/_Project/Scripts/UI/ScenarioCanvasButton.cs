using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Кнопка сценария ЛР7 на World Space холсте.
// Логика кнопки остаётся в ScenarioButton (доступность, слой взаимодействия = Nothing у недоступной,
// событие Pressed) — на неё опираются сценарий и автотесты ЛР7. Этот компонент только показывает её
// на холсте: доступность зеркалится в XRCanvasButton, нажатие лучом по холсту вызывает ScenarioButton.Press().
// Старый куб кнопки скрыт, его коллайдер выключен — луч попадает только в холст, двойного нажатия нет.
public class ScenarioCanvasButton : MonoBehaviour
{
    public ScenarioButton logic;
    public XRCanvasButton view;

    void OnEnable() { if (view != null) view.Clicked += OnClick; }
    void OnDisable() { if (view != null) view.Clicked -= OnClick; }

    void Start() => Sync();
    void LateUpdate() => Sync();

    void Sync()
    {
        if (logic == null || view == null) return;
        view.SetAvailable(logic.Available);
        if (view.label != null && view.label.text != logic.label) view.label.text = logic.label;
    }

    void OnClick(XRCanvasButton b, InteractorHandedness hand)
    {
        if (logic != null) logic.Press();
    }
}
