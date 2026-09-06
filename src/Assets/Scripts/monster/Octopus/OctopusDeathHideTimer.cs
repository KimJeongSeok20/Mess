using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
internal sealed class OctopusDeathHideTimer : MonoBehaviour
{
    private OctopusSwarmMember _member;

    internal static void Schedule(OctopusSwarmMember member, float delay)
    {
        if (member == null)
            return;

        OctopusDeathHideTimer timer = member.GetComponent<OctopusDeathHideTimer>();
        if (timer == null)
            timer = member.gameObject.AddComponent<OctopusDeathHideTimer>();

        timer.Cancel();
        timer._member = member;
        timer.StartCoroutine(timer.HideAfterDelay(Mathf.Max(0f, delay)));
    }

    internal void Cancel()
    {
        StopAllCoroutines();
        _member = null;
    }

    private IEnumerator HideAfterDelay(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        OctopusSwarmMember member = _member;
        _member = null;
        member?.CompleteDeathPresentation();
    }
}
