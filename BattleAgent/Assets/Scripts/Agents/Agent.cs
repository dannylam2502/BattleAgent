using System.Collections;
using UnityEditor.Animations;
using UnityEngine;

public class Agent : MonoBehaviour
{
    public AgentProperties defaultProperties;
    public float currentHP;
    public Action[] availableActions;
    public Animator animatorController;
    public HealthBar healthBarPrefab; // Reference to the HealthBar prefab
    private HealthBar healthBarInstance;

    public const string TRIGGER_ACTION = "Action";
    protected AgentProperties properties;
    public AgentProperties Properties { get { return properties; } }

    protected float lastHealth; // track the last frames health

    void Awake()
    {
        properties = ScriptableObject.Instantiate(defaultProperties);
        currentHP = properties.maxHP;
        if (animatorController == null)
        {
            animatorController = GetComponent<Animator>();
        }

        // Instantiate the health bar and set it up

    }

    void Update()
    {
        if (lastHealth != currentHP)
        {
            // On health changed
            if (healthBarInstance != null)
            {
                healthBarInstance.SetHealth(currentHP);
            }
        }
        lastHealth = currentHP;
    }

    public void SetupHealthBar()
    {
        healthBarInstance = Instantiate(healthBarPrefab, FindObjectOfType<Canvas>().transform);
        healthBarInstance.SetMaxHealth(Properties.maxHP);
        healthBarInstance.gameObject.name = "HealthBar";
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position);
        healthBarInstance.transform.position = screenPos + new Vector3(0, 30, 0); // Offset above the agent
    }

    public void TakeDamage(float damage)
    {
        currentHP -= Mathf.Max(damage - properties.defense, 0);
        currentHP = Mathf.Max(currentHP, 0);
    }

    public void Heal(float healAmount)
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

    public void ApplyBuff(StatType statType, float value, float duration)
    {
        StartCoroutine(BuffCoroutine(statType, value, duration));
    }

    private IEnumerator BuffCoroutine(StatType statType, float value, float duration)
    {
        ApplyStatChange(statType, value);
        yield return new WaitForSeconds(duration);
        ApplyStatChange(statType, -value);
    }

    private void ApplyStatChange(StatType statType, float value)
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