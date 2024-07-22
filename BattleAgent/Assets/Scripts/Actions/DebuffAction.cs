using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DebuffAction", menuName = "ScriptableObjects/Actions/DebuffAction", order = 1)]
public class DebuffAction : BuffAction
{
    public override void Execute(Agent user, Agent target)
    {
        base.Execute(user, target);
    }
}
