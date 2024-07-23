using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class BattleSystem : MonoBehaviour
{
    public BattleConfiguration battleConfiguration;
    public List<Agent> players;
    public List<Agent> enemies;
    private List<Agent> turnOrder;

    public BattleUIManager uiManager;

    /*
        Flow of the game
        1. Set up the battle field by initiating enemies and players and cache them to the list
        2. Calculate the turn order by sorting agents speed
        3. Start the battle by running a courtine, which run sub-coroutines for each agent
     */
    void Start()
    {
        uiManager = FindObjectOfType<BattleUIManager>();
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
                    comp.SetupHealthBar();
                    gameObj.name = "Player" + (col * row + row);
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
                    comp.SetupHealthBar();
                    gameObj.name = "Enemy" + (col * row + row);
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
    IEnumerator RoutineExecuteTurns()
    {
        // Start each agent coroutine
        foreach (var agent in turnOrder)
        {
            StartCoroutine(RoutineDoAction(agent));
        }

        while (players.Exists(p => p.IsAlive()) && enemies.Exists(e => e.IsAlive()))
        {
            yield return new WaitForSeconds(1.0f); // check battle end condition every second
        }

        // Battle End here, stop all coroutine, display the winner
        StopAllCoroutines();

        string winner = players.Exists(p => p.IsAlive()) ? "Players" : "Enemies";
        uiManager.DisplayWinner(winner);
        Debug.Log("Battle Ended");
    }

    IEnumerator RoutineDoAction(Agent agent)
    {
        while (agent.IsAlive())
        {
            // Get a random action, maybe we need to build a system to have a weight or any conditions needed
            Action action = agent.GetAction();
            Agent target = GetTarget(agent, action);
            if (target != null)
            {
                action.Execute(agent, target);
                string logMessage = $"{agent.name} used {action.GetType().Name} on {target.name}";
                uiManager.LogAction(logMessage);
            }

            yield return new WaitForSeconds(1.0f / agent.Properties.speed); // Simulate action execution time based on speed
        }
    }

    public void StartBattle()
    {
        StartCoroutine(RoutineExecuteTurns());
    }

    /*
        return an available target for this action trigger by the owner
     */
    public Agent GetTarget(Agent owner, Action action)
    {
        // Should we use an enum to handle the Type instead of asking like this?
        if (action is DamageAction || action is DebuffAction || action is DamageOverTimeAction)
        {
            // Target an enemy, maybe random? Let's focus on the first on alive for now
            if (owner is Player)
            {
                return enemies.Find(e => e.IsAlive());
            }
            else
            {
                return players.Find(p => p.IsAlive());
            }
        }
        else if (action is HealAction || action is BuffAction || action is HealOverTimeAction)
        {
            // Target an enemy, maybe random? Let's focus on the first on alive for now
            if (owner is Enemy)
            {
                return enemies.Find(e => e.IsAlive());
            }
            else
            {
                return players.Find(p => p.IsAlive());
            }
        }
        return null;
    }
}