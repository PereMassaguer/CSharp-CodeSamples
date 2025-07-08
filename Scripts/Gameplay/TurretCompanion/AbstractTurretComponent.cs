using System.Linq;
using UnityEngine;

[RequireComponent(typeof(SimpleStatusComponent))]
public abstract class AbstractTurretComponent : MonoBehaviour
{
    [Header("Rotation")]
    [SerializeField] private float actionRadius = 50.0f;
    [SerializeField] private float rotationSpeed = 0.1f;
    [SerializeField] private float idleRotationSpeed = 1.0f;

    protected bool hasTarget = false;
    protected Faction faction;
    protected AbstractStatusComponent statusComponent;

    private int idleRotationDirection = -1;
    private float currentTimeToChangeIdleDirection = 0.0f;
    private float timeToChangeIdleDirection = 0.0f;

    private void Start()
    {
        var factionComponent = GetComponent<FactionComponent>();
        if (factionComponent)
        {
            faction = factionComponent.faction;
        }

        statusComponent = GetComponent<AbstractStatusComponent>();
        if (statusComponent)
        {
            statusComponent.OnDeathDelegate += OnDeath;
        }
    }

    private void OnDeath(AbstractStatusComponent status, SourceGameObject killer)
    {
        //@todo(pmassaguer) trigger animations/fxs/dissolve
        Destroy(gameObject.transform.parent.gameObject);
    }

    protected virtual void Update()
    {
        if (statusComponent.IsDead()) return;
        
        var enemyLayerMask = FactionUtils.GetEnemiesLayerMask(faction);
        
        var colliders = Physics.OverlapSphere(transform.position, actionRadius, enemyLayerMask, QueryTriggerInteraction.Ignore);
        var closestStatus = LinkedStatusComponent.DistinctSources(colliders.Select(c => c.gameObject))
            .Where(s => s.CanBeTarget() && IsInLineOfSight(s)).OrderBy(s => Vector3.Distance(s.transform.position, transform.position))
            .FirstOrDefault();

        hasTarget = closestStatus;
        if (closestStatus)
        {
            var lookAt = Quaternion.LookRotation(closestStatus.GetGameObjectCenter() - transform.position);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, lookAt, rotationSpeed * Time.deltaTime);
            currentTimeToChangeIdleDirection = 0.0f;
        }
        else
        {
            currentTimeToChangeIdleDirection += Time.deltaTime;
            if (currentTimeToChangeIdleDirection >= timeToChangeIdleDirection)
            {
                idleRotationDirection *= -1;
                timeToChangeIdleDirection = Random.Range(1.5f, 2.5f);
                currentTimeToChangeIdleDirection = 0.0f;
            }
            
            transform.Rotate(0, idleRotationDirection * idleRotationSpeed * Time.deltaTime, 0);
        }
    }

    private bool IsInLineOfSight(AbstractStatusComponent statusToCheck)
    {
        var position = transform.position;
        var direction = statusToCheck.GetGameObjectCenter() - position;

        // Check obstacles
        var hit = Physics.Raycast(position, direction.normalized, out _, direction.magnitude, LayerUtils.DefaultFloorWallMask, QueryTriggerInteraction.Ignore);
        Debug.DrawRay(position, direction.normalized * direction.magnitude, hit ? Color.red : Color.green, 2.0f);
        return !hit;
    }
    
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, actionRadius);
    }
}
