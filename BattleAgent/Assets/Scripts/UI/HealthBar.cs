using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    public Slider slider;
    public GameObject buffContainer;
    public GameObject buffCirclePrefab; // Reference to the BuffCircle prefab

    public void SetMaxHealth(float maxHealth)
    {
        slider.maxValue = maxHealth;
        slider.value = maxHealth;
    }

    public void SetHealth(float health)
    {
        slider.value = health;
    }

    public void AddBuff(StatType statType, float duration, bool isDebuff = false)
    {
        GameObject buffCircle = Instantiate(buffCirclePrefab, buffContainer.transform);
        var imageComp = buffCircle.GetComponentInChildren<Image>();
        imageComp.fillAmount = 1.0f;
        // Set color based on Type
        imageComp.color = GetColor(statType);
        

        StartCoroutine(UpdateBuffCircle(buffCircle, duration, isDebuff));
    }

    private IEnumerator UpdateBuffCircle(GameObject objBuff, float duration, bool isDebuff = false)
    {
        // Buff case
        if (!isDebuff)
        {
            float elapsed = 0f;
            Image buffImage = objBuff.GetComponentInChildren<Image>();

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                buffImage.fillAmount = 1.0f - (elapsed / duration);
                yield return null;
            }
        }
        // Debuff case, flashing the color
        else
        {
            float elapsed = 0f;
            Image buffImage = objBuff.GetComponentInChildren<Image>();
            var originalColor = buffImage.color;
            bool toggle = false;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                buffImage.fillAmount = 1.0f - (elapsed / duration);
                toggle = !toggle;
                buffImage.color = toggle ? Color.white : originalColor;
                yield return null;
            }
        }
        
        Destroy(objBuff);
    }

    private Color32 GetColor(StatType statType)
    {
        switch (statType)
        {
            case StatType.Attack:
                return Color.red;
            case StatType.Defense:
                return Color.yellow;
            case StatType.Speed:
                return Color.blue;
            default: 
                return Color.white;
        }
    }
}