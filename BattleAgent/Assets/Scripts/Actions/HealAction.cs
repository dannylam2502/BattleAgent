using UnityEngine;

[CreateAssetMenu(fileName = "HealAction", menuName = "ScriptableObjects/Actions/HealAction", order = 1)]
public class HealAction : Action
{
    public int healValue;

    public override void Execute(Agent user, Agent target)
    {
        target.Heal(healValue + user.properties.attack);
    }
}