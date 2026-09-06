using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SkillTerminalPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text civicRankValue;
    [SerializeField] private TMP_Text skillPointsValue;

    private SkillWebTerminalInteraction _terminal;
    private int _lastRank = -1;
    private int _lastPoints = -1;

    public void Bind(SkillWebTerminalInteraction terminal)
    {
        _terminal = terminal;
        Refresh();
    }

    private void OnEnable() => Refresh();

    // SkillWeb exposes its spendable balance as a field, so purchases and refunds are read here.
    // Only changed values rebuild their text.
    private void LateUpdate() => Refresh();

    public void Refresh()
    {
        int rank = TeamProgress.CivicRank;
        int points = Esper.SkillWeb.SkillWeb.skillPoints;
        if (civicRankValue != null && rank != _lastRank)
        {
            civicRankValue.SetText("{0}", rank);
            _lastRank = rank;
        }
        if (skillPointsValue != null && points != _lastPoints)
        {
            skillPointsValue.SetText("{0}", points);
            _lastPoints = points;
        }
    }

    public void Close()
    {
        if (_terminal != null)
            _terminal.CloseWebView();
    }
}
