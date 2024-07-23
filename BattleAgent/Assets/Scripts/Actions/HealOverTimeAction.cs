using System.Collections;
using UnityEngine;

[CreateAssetMenu(fileName = "HealOverTimeAction", menuName = "ScriptableObjects/Actions/HealOverTimeAction", order = 1)]
public class HealOverTimeAction : Action
{
    public int healPerTick;
    public float tickInterval;
    public float duration;

    public override void Execute(Agent user, Agent target)
    {
        user.StartCoroutine(ApplyHealOverTime(target));
    }

    private IEnumerator ApplyHealOverTime(Agent target)
    {
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            if (target.IsAlive())
            {
                target.Heal(healPerTick);
            }
            else break;
            elapsedTime += tickInterval;
            yield return new WaitForSeconds(tickInterval);
        }
    }
}
