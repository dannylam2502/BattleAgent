using System.Collections;
using UnityEngine;

[CreateAssetMenu(fileName = "DamageOverTimeAction", menuName = "ScriptableObjects/Actions/DamageOverTimeAction", order = 1)]
public class DamageOverTimeAction : Action
{
    public int damagePerTick;
    public float tickInterval;
    public float duration;

    public override void Execute(Agent user, Agent target)
    {
        user.StartCoroutine(ApplyDamageOverTime(target));
    }

    private IEnumerator ApplyDamageOverTime(Agent target)
    {
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            if (target.IsAlive())
            {
                target.TakeDamage(damagePerTick);
            }
            else break;
            elapsedTime += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }
    }
}
