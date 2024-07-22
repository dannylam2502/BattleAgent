using UnityEngine;

public abstract class Action : ScriptableObject
{
    public abstract void Execute(Agent user, Agent target);
}