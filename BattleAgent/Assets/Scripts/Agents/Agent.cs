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

    protected AgentProperties properties;
    public AgentProperties Properties { get { return properties; } }

    protected float lastHealth; // track the last frames health

    public delegate void OnDeadDelegate(Agent agent);
    public OnDeadDelegate onDead;

    public const string TRIGGER_ACTION = "Action";
    public const string TRIGGER_HIT = "Hit";
    void Awake()
    {
        properties = ScriptableObject.Instantiate(defaultProperties);
        currentHP = properties.maxHP;
        if (animatorController == null)
        {
            animatorController = GetComponent<Animator>();
        }
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
        PlayAnimation(TRIGGER_HIT);
        if (currentHP == 0)
        {
            OnDead();
        }
    }

    public void Heal(float healAmount)
    {
        currentHP += healAmount;
        currentHP = Mathf.Min(currentHP, properties.maxHP);
    }

    public void AddOnDeadDelegate(OnDeadDelegate del)
    {
        onDead += del;
    }

    // Called when HP == 0
    protected void OnDead()
    {
        onDead?.Invoke(this);
        gameObject.SetActive(false);
        if (healthBarInstance != null)
        {
            Destroy(healthBarInstance.gameObject);
        }
    }

    public bool IsAlive()
    {
        return currentHP > 0;
    }

    // Get Random action for now
    public Action GetAction()
    {
        if (availableActions.Length == 0)
            return null;

        int randomIndex = Random.Range(0, availableActions.Length);
        return availableActions[randomIndex];
    }

    // Apply buff by running a coroutine with the duration, add buff to the healthbar
    public void ApplyBuff(StatType statType, float value, float duration, bool isDebuff = false)
    {
        if (healthBarInstance)
        {
            healthBarInstance.AddBuff(statType, duration, isDebuff);
        }
        StartCoroutine(BuffCoroutine(statType, value, duration));
    }

    // Handle Buff Routine, apply stat change then revert the value after duration
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