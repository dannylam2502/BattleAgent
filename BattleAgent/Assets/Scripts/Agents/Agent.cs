using UnityEngine;

public class Agent : MonoBehaviour
{
    public AgentProperties properties;
    public int currentHP;

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
}