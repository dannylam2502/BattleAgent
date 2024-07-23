using UnityEngine;

[CreateAssetMenu(fileName = "BuffAction", menuName = "ScriptableObjects/Actions/BuffAction", order = 1)]
public class BuffAction : Action
{
    public int buffValue;
    public float duration;
    public StatType statType; // Enum for which stat to buff (attack, defense, speed)

    public override void Execute(Agent user, Agent target)
    {
        target.ApplyBuff(statType, buffValue, duration);
        user.PlayAnimation();
    }
}

public enum StatType
{
    Attack,
    Defense,
    Speed
}