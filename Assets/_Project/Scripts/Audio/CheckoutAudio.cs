using System.Collections;
using UnityEngine;

// Касса: пикание сканера на каждую позицию чека и печать чека. Слушает только смену состояния сценария
// (ScenarioController.StateChanged → Checkout) и ничего в сценарии не меняет.
[RequireComponent(typeof(AudioSource))]
public class CheckoutAudio : MonoBehaviour
{
    public AudioClip beep, receipt;
    public float beepGap = 0.45f;
    public int BeepsPlayed { get; private set; }
    public int ReceiptsPrinted { get; private set; }

    AudioSource src;

    void Awake() => src = GetComponent<AudioSource>();
    void OnEnable() => ScenarioController.StateChanged += OnState;
    void OnDisable() => ScenarioController.StateChanged -= OnState;

    void OnState(ScenarioState from, ScenarioState to)
    {
        if (to != ScenarioState.Checkout || !isActiveAndEnabled) return;
        var r = ScenarioController.Instance != null ? ScenarioController.Instance.CurrentRun : null;
        StartCoroutine(Scan(r != null ? r.receipt.Count : 0));
    }

    IEnumerator Scan(int items)
    {
        for (int i = 0; i < items; i++)
        {
            if (beep != null) src.PlayOneShot(beep, 0.8f);
            BeepsPlayed++;
            yield return new WaitForSeconds(beepGap);
        }
        if (receipt != null) src.PlayOneShot(receipt, 0.9f);
        ReceiptsPrinted++;
    }
}
