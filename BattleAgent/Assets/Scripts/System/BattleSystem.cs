using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BattleSystem : MonoBehaviour
{
    public BattleConfiguration battleConfiguration;
    public List<Agent> players;
    public List<Agent> enemies;
    private List<Agent> turnOrder;

    void Start()
    {
        SetupBattlefield();
        CalculateTurnOrder();
        StartBattle();
    }

    public void SetupBattlefield()
    {
        //float screenWidth = Camera.main.orthographicSize * Camera.main.aspect * 2;
        //float screenHeight = Camera.main.orthographicSize * 2;
        float screenWidth = 19.20f;
        float screenHeight = 10.80f;

        float halfScreenWidth = screenWidth / 2;
        float playerZoneWidth = halfScreenWidth * 0.6f;
        float enemyZoneWidth = halfScreenWidth * 0.6f;

        float playerHorizontalSpacing = playerZoneWidth / (battleConfiguration.numPlayerColumns + 1);
        float playerVerticalSpacing = screenHeight / (battleConfiguration.numPlayerRows + 1);

        float enemyHorizontalSpacing = enemyZoneWidth / (battleConfiguration.numEnemyColumns + 1);
        float enemyVerticalSpacing = screenHeight / (battleConfiguration.numEnemyRows + 1);

        // Place players on the left side
        for (int row = 0; row < battleConfiguration.numPlayerRows; row++)
        {
            for (int col = 0; col < battleConfiguration.numPlayerColumns; col++)
            {
                float xPos = -halfScreenWidth + playerHorizontalSpacing * (col + 1);
                float yPos = screenHeight / 2 - playerVerticalSpacing * (row + 1);
                GameObject gameObj = Instantiate(battleConfiguration.playerPrefab, new Vector3(xPos, yPos, 0), Quaternion.identity);
                var comp = gameObj.GetComponent<Agent>();
                if (comp != null)
                {
                    players.Add(comp);
                }
            }
        }

        // Place enemies on the right side
        for (int row = 0; row < battleConfiguration.numEnemyRows; row++)
        {
            for (int col = 0; col < battleConfiguration.numEnemyColumns; col++)
            {
                float xPos = halfScreenWidth - enemyZoneWidth + enemyHorizontalSpacing * (col + 1);
                float yPos = screenHeight / 2 - enemyVerticalSpacing * (row + 1);
                GameObject gameObj = Instantiate(battleConfiguration.enemyPrefab, new Vector3(xPos, yPos, 0), Quaternion.identity);
                var comp = gameObj.GetComponent<Agent>();
                if (comp != null)
                {
                    enemies.Add(comp);
                }
            }
        }
    }

    void CalculateTurnOrder()
    {
        turnOrder = new List<Agent>(players);
        turnOrder.AddRange(enemies);
        turnOrder.Sort((x, y) => y.Properties.speed.CompareTo(x.Properties.speed));
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
                    Agent target = GetTarget(action);
                    if (target != null)
                    {
                        action.Execute(agent, target);
                    }

                    yield return new WaitForSeconds(1.0f / agent.Properties.speed); // Simulate action execution time based on speed
                }
            }
        }

        Debug.Log("Battle Ended");
    }

    public void StartBattle()
    {
        StartCoroutine(ExecuteTurns());
    }

    public Agent GetTarget(Action action)
    {
        if (action is DamageAction || action is DebuffAction)
        {
            // Target an enemy
            return enemies.Find(e => e.IsAlive());
        }
        else if (action is HealAction || action is BuffAction)
        {
            // Target a player
            return players.Find(p => p.IsAlive());
        }
        return null;
    }
}