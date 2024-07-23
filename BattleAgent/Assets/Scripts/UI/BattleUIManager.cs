using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class BattleUIManager : MonoBehaviour
{
    public TextMeshProUGUI winnerText;
    public TextMeshProUGUI battleLogText;

    private Queue<string> battleLogs = new Queue<string>();
    private int maxLogCount = 2;

    private void Start()
    {
        winnerText.enabled = false;
    }
    public void DisplayWinner(string winner)
    {
        winnerText.enabled = true;
        winnerText.text = $"{winner} Wins!";
    }

    public void LogAction(string action)
    {
        if (battleLogs.Count >= maxLogCount)
        {
            battleLogs.Dequeue();
        }
        battleLogs.Enqueue(action);
        UpdateBattleLogText();
    }

    private void UpdateBattleLogText()
    {
        battleLogText.text = string.Join("\n", battleLogs.ToArray());
    }
}
