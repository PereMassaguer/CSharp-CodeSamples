using System.Collections;
using UnityEngine;

public class ShooterTurret : AbstractTurretComponent
{
    [Header("Gameplay")]
    public int numberOfShoots;
    public float delayBetweenShoots = 0.25f;
    public Transform shootSource;
    public float shootFrequency = 3;
    public WeaponDamage damage;
    public AbstractEffect[] effects;
    
    [Header("VFX")]
    public GameObject muzzlePrefab;
    public GameObject impactPrefab;
    public GameObject trailPrefab;

    [Header("SFX")]
    public AK.Wwise.Event shootSound;
    public AK.Wwise.Event impactSound;
    
    private float timeUntilNextShot = 0;
    private readonly float MAX_DISTANCE_TO_IMPACT = 50.0f;
    private readonly float TIME_TO_DESPAWN_FX = 2.0f;
    
    protected override void Update()
    {
        base.Update();
        
        timeUntilNextShot -= Time.deltaTime;
        if (!(timeUntilNextShot <= 0)) return;
        timeUntilNextShot = shootFrequency;

        if (hasTarget)
        {
            Shoot();
        }
    }
    
    private void Shoot()
    {
        LaunchShoot();
        for (var i = 0; i < numberOfShoots - 1; ++i)
        {
            GameGlobals.Delay(delayBetweenShoots * (i + 1), LaunchShoot);
        }
    }

    private void LaunchShoot()
    {
        if (muzzlePrefab)
        {
            var muzzleInstance = TrashMan.Spawn(muzzlePrefab, shootSource);
            TrashMan.DespawnAfterDelay(muzzleInstance, 2);
        }
        
        shootSound?.Post(gameObject);
        
        var impactLayerMask = FactionUtils.GetEnemiesLayerMask(faction);
        impactLayerMask |= LayerUtils.DefaultFloorWallMask;

        Vector3 hitPoint;
        if (Physics.Raycast(shootSource.position, shootSource.forward, out var hit, MAX_DISTANCE_TO_IMPACT, impactLayerMask, QueryTriggerInteraction.Ignore))
        {
            if (impactPrefab)
            {
                var impactInstance = TrashMan.Spawn(impactPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                TrashMan.DespawnAfterDelay(impactInstance, TIME_TO_DESPAWN_FX);
            }

            impactSound?.Post(gameObject);

            var targetStatus = hit.collider.GetComponent<AbstractStatusComponent>();
            if (targetStatus)
            {
                var source = statusComponent.GetSource();
                AbstractEffect.Apply(effects, source, targetStatus.gameObject);
                targetStatus.ApplyDamage(source, damage);
            }

            hitPoint = hit.point;
        }
        else
        {
            hitPoint = shootSource.position + (shootSource.forward * MAX_DISTANCE_TO_IMPACT);
        }
        
        if (!trailPrefab) return;
        var position = shootSource.position;
        var trailInstance = TrashMan.Spawn(trailPrefab, position);
        TrashMan.DespawnAfterDelay(trailInstance, TIME_TO_DESPAWN_FX);
        
        StartCoroutine(MoveObject(trailInstance, position, hitPoint, 0.1f));
        trailInstance.transform.LookAt(hitPoint);
    }

    private static IEnumerator MoveObject(GameObject objectToMove,Vector3 source, Vector3 target, float overTime)
    {
        var startTime = Time.time;
        while (Time.time < startTime + overTime)
        {
            objectToMove.transform.position = Vector3.Lerp(source, target, (Time.time - startTime) / overTime);
            yield return null;
        }
        objectToMove.transform.position = target;
    }
}
