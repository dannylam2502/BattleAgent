using UnityEngine;

[CreateAssetMenu(fileName = "DamageAction", menuName = "ScriptableObjects/Actions/DamageAction", order = 1)]
public class DamageAction : Action
{
    public int damageValue;

    public override void Execute(Agent user, Agent target)
    {
        int damage = Mathf.Max(damageValue + user.Properties.attack - target.Properties.defense, 0);
        target.TakeDamage(damage);
        user.PlayAnimation();
    }
}