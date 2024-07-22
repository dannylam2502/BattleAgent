using UnityEngine;

public class BattleSetup : MonoBehaviour
{
    public BattleConfiguration battleConfiguration;

    void Start()
    {
        SetupBattlefield();
    }

    void SetupBattlefield()
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
                Instantiate(battleConfiguration.playerPrefab, new Vector3(xPos, yPos, 0), Quaternion.identity);
            }
        }

        // Place enemies on the right side
        for (int row = 0; row < battleConfiguration.numEnemyRows; row++)
        {
            for (int col = 0; col < battleConfiguration.numEnemyColumns; col++)
            {
                float xPos = halfScreenWidth - enemyZoneWidth + enemyHorizontalSpacing * (col + 1);
                float yPos = screenHeight / 2 - enemyVerticalSpacing * (row + 1);
                Instantiate(battleConfiguration.enemyPrefab, new Vector3(xPos, yPos, 0), Quaternion.identity);
            }
        }
    }
}
