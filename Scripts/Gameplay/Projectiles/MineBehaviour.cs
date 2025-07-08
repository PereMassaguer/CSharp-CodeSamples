using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class MineBehaviour : MonoBehaviour, ISkillProxy
{
    public GameObject destroyAfterActivate;
    public float delayUntilActive = 1;
    public AK.Wwise.Event soundOnActive;

    public AbstractEffect[] effectsOnTarget;
    public AbstractEffect[] effectsOnSelf;
    public WeaponDamage weaponDamage;

    private SourceGameObject source;
    private bool activated = false;
    
    private void ActivateMine()
    {
        var radius = GetComponent<SphereCollider>().radius;
        soundOnActive?.Post(gameObject);
        AbstractEffect.Apply(effectsOnSelf, source, gameObject);
        var layerMask = FactionUtils.GetEnemiesLayerMask(source.faction);
        var colliders = Physics.OverlapSphere(transform.position, radius, layerMask, QueryTriggerInteraction.Ignore);
        if (colliders.Length > 0)
        {
            ApplyEffects(colliders[0]);
        }
        TrashMan.Despawn(destroyAfterActivate);
    }

    private void ApplyEffects(Component other)
    {
        var status = other.gameObject.GetComponent<AbstractStatusComponent>();
        if (!status) return;
        
        if (!FactionUtils.IsAggressive(source.faction, other.gameObject)) return;
        
        AbstractEffect.Apply(effectsOnTarget, source, other.gameObject);
        status.ApplyDamage(source, weaponDamage);
    }

    public void OnTriggerEnter(Collider other)
    {
        if (activated) return;

        var status = other.GetComponent<AbstractStatusComponent>();
        if (!status) return;

        var enemyStatus = status.GetParentStatusComponent() as EnemyStatusComponent;
        if (!enemyStatus) return;

        activated = true;
        GameGlobals.Delay(delayUntilActive, ActivateMine);
    }

    public void SetSourceGameObject(SourceGameObject source) { this.source = source; }
}
