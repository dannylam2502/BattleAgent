using UnityEngine;

[CreateAssetMenu(fileName = "BattleConfiguration", menuName = "ScriptableObjects/BattleConfiguration", order = 1)]
public class BattleConfiguration : ScriptableObject
{
    public int numPlayerRows;
    public int numPlayerColumns;
    public int numEnemyRows;
    public int numEnemyColumns;
    public GameObject playerPrefab;
    public GameObject enemyPrefab;
}
