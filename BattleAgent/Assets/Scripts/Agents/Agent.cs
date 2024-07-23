using System.Collections;
using UnityEditor.Animations;
using UnityEngine;

public class Agent : MonoBehaviour
{
    public AgentProperties defaultProperties;
    public int currentHP;
    public Action[] availableActions;
    public Animator animatorController;

    public const string TRIGGER_ACTION = "Action";
    protected AgentProperties properties;
    public AgentProperties Properties { get { return properties; } }

    void Awake()
    {
        properties = ScriptableObject.Instantiate(defaultProperties);
        currentHP = properties.maxHP;
        if (animatorController == null)
        {
            animatorController = GetComponent<Animator>();
        }
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

    // We only have action right now I believe, let's leave others as extensible features
    public void PlayAnimation(string action = TRIGGER_ACTION)
    {
        if (animatorController)
        {
            animatorController.SetTrigger(action);
        }
    }
}