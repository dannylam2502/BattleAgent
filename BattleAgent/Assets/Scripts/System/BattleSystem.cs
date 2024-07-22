using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BattleSystem : MonoBehaviour
{
    public List<Agent> players;
    public List<Agent> enemies;
    private List<Agent> turnOrder;

    void Start()
    {
        CalculateTurnOrder();
    }

    void CalculateTurnOrder()
    {
        turnOrder = new List<Agent>(players);
        turnOrder.AddRange(enemies);
        turnOrder.Sort((x, y) => y.properties.speed.CompareTo(x.properties.speed));
    }

    IEnumerator ExecuteTurns()
    {
        while (players.Exists(p => p.IsAlive()) && enemies.Exists(e => e.IsAlive()))
        {
            foreach (var agent in turnOrder)
            {
                if (agent.IsAlive())
                {
                    // Get a random action, maybe we need to build a system to have a weight or any conditions needed
                    Action action = agent.GetAction();
                    Agent target = agent.GetTarget(action);
                    action.Execute(agent, target);

                    yield return new WaitForSeconds(1.0f / agent.properties.speed); // Simulate action execution time based on speed
                }
            }
        }

        Debug.Log("Battle Ended");
    }

    public void StartBattle()
    {
        StartCoroutine(ExecuteTurns());
    }
}