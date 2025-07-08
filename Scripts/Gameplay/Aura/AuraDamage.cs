using System.Collections.Generic;
using UnityEngine;

public class AuraDamage : MonoBehaviour, ISkillProxy
{
    [SerializeField] private AbstractModifier[] auraModifiers;

    [SerializeField] private WeaponDamage auraDamagePerSecond;
    [SerializeField] private float damageTickPeriod = 0.5f;

    private Dictionary<AbstractStatusComponent, AuraInfo> affectedStatuses = new Dictionary<AbstractStatusComponent, AuraInfo>();

    private SourceGameObject source;

    private void OnTriggerEnter(Collider other)
    {
        AbstractStatusComponent parentStatus = AbstractStatusComponent.GetParentStatus(other);

        if (parentStatus == null) return;

        if (!affectedStatuses.ContainsKey(parentStatus))
        {
            affectedStatuses.Add(parentStatus, 
                new AuraInfo
                {
                    appliedModifiers = AbstractModifier.Activate(auraModifiers, parentStatus.gameObject),
                    lastEffectTimestamp = Time.time
                }
            );

            if (FactionUtils.IsAggressive(source.gameObject, parentStatus.gameObject))
            {
                parentStatus.ApplyDamage(source, auraDamagePerSecond.Scale(damageTickPeriod));
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        AbstractStatusComponent parentStatus = AbstractStatusComponent.GetParentStatus(other);

        if (parentStatus == null) return;

        if (affectedStatuses.ContainsKey(parentStatus))
        {
            AbstractModifier.Deactivate(affectedStatuses[parentStatus].appliedModifiers, parentStatus.gameObject);
            affectedStatuses.Remove(parentStatus);
        }
    }

    private void Update()
    {
        //New workaround, must fix
        if (source == null)
        {
            source = SceneGlobals.Instance.PlayerStatus.GetSource();

            if(source == null)
            {
                enabled = false;
                return;
            }
        }

        var tickAffectedStatuses = new List<AbstractStatusComponent>();

        foreach (var item in affectedStatuses)
        {
            if (item.Key == null) continue;

            if (Time.time - item.Value.lastEffectTimestamp > damageTickPeriod)
            {
                tickAffectedStatuses.Add(item.Key);

                if(FactionUtils.IsAggressive(source.gameObject, item.Key.gameObject))
                {
                    item.Key.ApplyDamage(source, auraDamagePerSecond.Scale(damageTickPeriod));
                }
            }
        }

        foreach (var item in tickAffectedStatuses)
        {
            affectedStatuses[item].lastEffectTimestamp += damageTickPeriod;
        }
    }

    public void SetSourceGameObject(SourceGameObject source)
    {
        this.source = source;
        enabled = true;
    }

    private class AuraInfo
    {
        public float lastEffectTimestamp;
        public AbstractModifier[] appliedModifiers;
    }
}
