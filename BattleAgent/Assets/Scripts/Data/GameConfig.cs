using UnityEngine;

[CreateAssetMenu(fileName = "Game Configuration", menuName = "ScriptableObjects/Game Configuration", order = 1)]
public class GameConfig : ScriptableObject
{
    public int numPlayers;
    public int numEnemies;
}
