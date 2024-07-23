using UnityEngine;

[CreateAssetMenu(fileName = "AgentProperties", menuName = "ScriptableObjects/AgentProperties", order = 1)]
public class AgentProperties : ScriptableObject
{
    public string agentName;
    public float maxHP;
    public float attack;
    public float defense;
    public float speed;
}
