using System;
using System.Linq;
using UnityEngine;

public abstract class AbstractModifier : ScriptableObject
{
    public I2.Loc.LocalizedString descriptionKey;

    protected virtual string MapCustomDescription(string desc) { return desc; }

    public string GetCustomDescription()
    {
        if (descriptionKey == null) return string.Empty;

        return MapCustomDescription(descriptionKey);
    }
    
    public string UpdateCustomDescription(string desc)
    {
        return MapCustomDescription(desc);
    }
    
    protected abstract void OnActivate(GameObject parent);
    protected abstract void OnDeactivate(GameObject parent);

    public virtual void OnUpdate(GameObject parent) { }

    protected void Activate(GameObject parent)
    {
        if (!parent) return;

        var status = parent.GetComponent<StatusComponent>();
        if (status) status.activeModifierInstances.Add(this);
        
        OnActivate(parent);
    }

    protected void Deactivate(GameObject parent)
    {
        if (!parent) return;

        var status = parent.GetComponent<StatusComponent>();
        if (status) status.activeModifierInstances.Remove(this);
        
        OnDeactivate(parent);
    }

    public static AbstractModifier[] Activate(AbstractModifier[] modifiers, GameObject parent)
    {
        if (modifiers == null) return Array.Empty<AbstractModifier>();

        var modifierInstances = modifiers.Select(m => Instantiate(m)).ToArray();
        foreach (var instance in modifierInstances)
        {
            instance.Activate(parent);
        }

        return modifierInstances;
    }

    public static AbstractModifier Activate(AbstractModifier modifier, GameObject parent)
    {
        if (modifier == null) return null;

        var modifierInstance = Instantiate(modifier);
        modifierInstance.Activate(parent);

        return modifierInstance;
    }

    public static void Deactivate(AbstractModifier[] modifierInstances, GameObject parent)
    {
        if (modifierInstances == null) return;

        foreach (var instance in modifierInstances)
        {
            instance.Deactivate(parent);
            Destroy(instance);
        }
    }

    protected string GetPrefabInstanceName(GameObject prefab)
    {
        var type_name = GetType().Name;
        return $"{prefab.name}__{type_name}";
    }
    
    protected void SetCasterInfo(GameObject gameObjectToSetCaster, SourceGameObject source)
    {
        var casterInfos = gameObjectToSetCaster.GetComponentsInChildren<ISkillProxy>(true);
        foreach (var casterInfo in casterInfos)
        {
            casterInfo.SetSourceGameObject(source);
        }
    }
}
