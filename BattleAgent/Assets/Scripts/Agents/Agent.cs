using System.Collections;
using UnityEngine;

public class Agent : MonoBehaviour
{
    public AgentProperties properties;
    public int currentHP;
    public Action[] availableActions;

    void Awake()
    {
        currentHP = properties.maxHP;
    }

    public void TakeDamage(int damage)
    {
        currentHP -= Mathf.Max(damage - properties.defense, 0);
        currentHP = Mathf.Max(currentHP, 0);
    }

    public void Heal(int healAmount)
    {
        currentHP += healAmount;
        currentHP = Mathf.Min(currentHP, properties.maxHP);
    }

    public bool IsAlive()
    {
        return currentHP > 0;
    }

    public Action GetAction()
    {
        if (availableActions.Length == 0)
            return null;

        int randomIndex = Random.Range(0, availableActions.Length);
        return availableActions[randomIndex];
    }

    public Agent GetTarget(Action action)
    {
        if (action is DamageAction || action is DebuffAction)
        {
            // Target an enemy
            return FindObjectOfType<BattleSystem>().enemies.Find(e => e.IsAlive());
        }
        else if (action is HealAction || action is BuffAction)
        {
            // Target a player
            return FindObjectOfType<BattleSystem>().players.Find(p => p.IsAlive());
        }
        return null;
    }

    public void ApplyBuff(StatType statType, int value, float duration)
    {
        StartCoroutine(BuffCoroutine(statType, value, duration));
    }

    private IEnumerator BuffCoroutine(StatType statType, int value, float duration)
    {
        ApplyStatChange(statType, value);
        yield return new WaitForSeconds(duration);
        ApplyStatChange(statType, -value);
    }

    private void ApplyStatChange(StatType statType, int value)
    {
        switch (statType)
        {
            case StatType.Attack:
                properties.attack += value;
                break;
            case StatType.Defense:
                properties.defense += value;
                break;
            case StatType.Speed:
                properties.speed += value;
                break;
        }
    }
}