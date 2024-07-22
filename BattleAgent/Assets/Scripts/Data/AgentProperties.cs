using UnityEngine;

[CreateAssetMenu(fileName = "AgentProperties", menuName = "ScriptableObjects/AgentProperties", order = 1)]
public class AgentProperties : ScriptableObject
{
    public string agentName;
    public int maxHP;
    public int attack;
    public int defense;
    public float speed;
}
