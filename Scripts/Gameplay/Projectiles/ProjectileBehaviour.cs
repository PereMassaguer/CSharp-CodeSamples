using Mirror;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public enum TypeOfProjectile
{
    BASIC,
    BASIC_STICKY,
    MISSILE,
    IMPULSE,
    TRACKER
}

public enum TypeOfActivation
{
    NONE,
    CONTACT,
    CONTACT_DELAY,
    TIME,
}

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ProjectileBehaviour : NetworkBehaviour, ISkillProxy, ISpawnReset
{
    public TypeOfProjectile projectileType = TypeOfProjectile.BASIC;
    public TypeOfActivation activationType = TypeOfActivation.CONTACT;
    public float timeToApplyEffects = 0.0f;
    public bool rotateInAir = false;
    public float rotationSpeedInAir = 500;
    
    [Header("Target Hit")]
    public LeveledWeaponDamage hitWeaponDamage;
    [HideInInspector] public float damageFactor = 1;
    public AbstractEffect[] initialEffectsOnTarget;
    public AbstractEffect[] effectsOnSelf;
    public AbstractEffect[] effectsOnTarget;
    public AK.Wwise.Event activationSound;

    private AbstractEffect[] startInitialEffectsOnTarget;
    private AbstractEffect[] startEffectsOnSelf;
    private AbstractEffect[] startEffectsOnTarget;

    [Header("Target Miss")]
    public bool missEnabled = false;
    public AbstractEffect[] effectsOnSelfMiss;
    public GameObject prefabOnMiss;

    [Header("Collision")]
    public bool applyCustomForceInCollision = false;
    public float forceDivisorInCollision = 2.0f;
    public GameObject collisionFX = null;
    public GameObject stickyCollisionFX = null;
    public AK.Wwise.Event collisionSound;
    public GameObject[] activateOnStickyContact;
    public bool snapToNormal;

    [Header("Missile Settings")]
    public bool isThursting = false;
    public GameObject target;

    public float thrustForce = 2;
    public float targetAlignedExtraThrust = 3;
    public float rotationSpeed = 30;

    [Header("Impulse Settings")]
    public float impulseForce = 0.0f;
    public bool randomizeDirection = false;
    public bool forcePositiveY = false;
    public Vector3 impulseDirection = Vector3.zero;

    [Header("Tracker Settings")]
    public float distanceToTrack = 5.0f;
    public float smoothTime = 0.5f;
    public float minimumSpeed = 10;
    public int numberOfTracks = 5;
    public float damageReductionPerHit = 0.0f;

    private int currentNumberOfTracks = 0;
    private bool isTracking = false;
    private List<AbstractStatusComponent> trackRecord = new List<AbstractStatusComponent>();

    private Rigidbody _rigidbody = null;
    private Coroutine activationEffect = null;
    private Vector3? randomRotationAxis;
    private Vector3? randomRotationAxisInAir;

    private bool canActivate = true;

    private const float TIME_TO_DESPAWN_FX = 2.0f;

    private SourceGameObject source;
    private bool isInAir = true;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        canActivate = true;

        startInitialEffectsOnTarget = (AbstractEffect[]) initialEffectsOnTarget.Clone();
        startEffectsOnSelf = (AbstractEffect[]) effectsOnSelf.Clone();
        startEffectsOnTarget  = (AbstractEffect[]) effectsOnTarget.Clone();

        if (activationType == TypeOfActivation.TIME)
        {
            GameGlobals.Delay(timeToApplyEffects, () => ActivationEffect(gameObject, transform.position));
        }

        if (projectileType != TypeOfProjectile.IMPULSE) return;
        
        var impulse = Vector3.zero;
        if (randomizeDirection)
        {
            var direction = Random.insideUnitSphere.normalized;
            if (forcePositiveY)
            {
                direction.y = 1;
            }
            impulse = impulseForce * direction;
        }
        else
        {
            impulse = impulseForce * impulseDirection;
        }
        Debug.DrawRay(transform.position, impulse, Color.red, 5.0f);
        _rigidbody.AddForce(impulse, ForceMode.Impulse);
    }

    public void ResetOnSpawn()
    {
        _rigidbody.isKinematic = false;
        transform.parent = null;
        canActivate = true;
        
        isTracking = isThursting = false;
        currentNumberOfTracks = 0;
        trackRecord.Clear();
        
        initialEffectsOnTarget = (AbstractEffect[]) startInitialEffectsOnTarget.Clone();
        effectsOnSelf = (AbstractEffect[]) startEffectsOnSelf.Clone();
        effectsOnTarget  = (AbstractEffect[]) startEffectsOnTarget.Clone();
    }

    void FixedUpdate()
    {
        if (rotateInAir && isInAir)
        {
            if (!randomRotationAxisInAir.HasValue)
            {
                randomRotationAxisInAir = Random.insideUnitSphere;
            }
            transform.rotation *= Quaternion.AngleAxis(Time.deltaTime * rotationSpeedInAir, randomRotationAxisInAir.Value);
        }
        
        if (projectileType == TypeOfProjectile.IMPULSE || (!isThursting && !isTracking)) return;

        float angle = 180;
        if (target)
        {
            var targetStatus = target.GetComponent<AbstractStatusComponent>();
            if (targetStatus)
            {
                targetStatus = targetStatus.GetParentStatusComponent();
                var targetCenter = targetStatus.GetGameObjectCenter();
                if (isThursting)
                {
                    var targetForward = (targetCenter - transform.position).normalized;
                    var fullRotation = Quaternion.FromToRotation(transform.forward, targetForward);
                    fullRotation.ToAngleAxis(out angle, out Vector3 axis);
                    transform.rotation *= Quaternion.AngleAxis(Time.deltaTime * rotationSpeed, axis);
                }
                else if (isTracking)
                {
                    transform.LookAt(targetCenter);
                    transform.position = Vector3.MoveTowards(transform.position, targetCenter, minimumSpeed * Time.fixedDeltaTime);
                    return;
                }
            }
        }
        else
        {
            if (!randomRotationAxis.HasValue) randomRotationAxis = Random.insideUnitSphere;
            transform.rotation *= Quaternion.AngleAxis(Time.deltaTime * rotationSpeed,
                randomRotationAxis.Value);
        }

        var thrust = thrustForce;
        angle = Mathf.Abs(angle);
        if (angle < 30)
        {
            var distanceFactor = (1 - (angle / 30));
            thrust += distanceFactor * targetAlignedExtraThrust;
        }

        _rigidbody.AddForce(transform.forward * thrust);

        // @info fail-safe in case the collision fails for some reason
        if (isThursting && target && Vector3.Distance(target.transform.position, transform.position) < 0.1f)
        {
            ActivationEffect(target, target.transform.position);
        }
    }

    protected void ActivationEffect(GameObject target, Vector3 contactPoint, bool destroyAfterActivate = true)
    {
        if (!GameGlobals.IsServer()) return;

        var ident = target.GetComponent<NetworkIdentity>();
        RpcActivationEffect(transform.position, ident ? ident.gameObject : gameObject);

        var targetStatus = target.GetComponent<AbstractStatusComponent>();
        if (targetStatus)
        {
            if (projectileType == TypeOfProjectile.TRACKER)
            {
                // @todo modify damageFactor according to damageReductionPerHit
                //contactWeaponDamage.damageFactor -= damageReductionPerHit * Math.Max(0, currentNumberOfTracks - 1);
            }

            var weaponDamage = hitWeaponDamage.GetDamage(source, targetStatus);
            targetStatus.ApplyDamage(source, weaponDamage.Scale(damageFactor));
        }
        
        activationSound?.Post(gameObject);

        if (!targetStatus && missEnabled)
        {
            AbstractEffect.Apply(effectsOnSelfMiss, source, gameObject);
            if (prefabOnMiss) TrashMan.Spawn(prefabOnMiss, transform.position);
        }
        else
        {
            AbstractEffect.Apply(effectsOnSelf, source, gameObject);
            AbstractEffect.Apply(effectsOnTarget, source, target);
        }

        _rigidbody.velocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        
        if (destroyAfterActivate)
        {
            TrashMan.Despawn(gameObject);
        }
    }

    public void AddForwardImpulse(float impulse)
    {
        AddForwardImpulse(impulse, _rigidbody.isKinematic, _rigidbody.useGravity);
    }

    public void AddImpulse(Vector3 impulse)
    {
        AddImpulse(impulse, _rigidbody.isKinematic, _rigidbody.useGravity);
    }

    public void AddForwardImpulse(float impulse, bool isKinematic, bool useGravity)
    {
        AddImpulse(transform.forward * impulse, isKinematic, useGravity);
    }

    public void AddImpulse(Vector3 impulse, bool isKinematic, bool useGravity)
    {
        _rigidbody.AddForce(impulse, ForceMode.Impulse);
        _rigidbody.isKinematic = isKinematic;
        _rigidbody.useGravity = useGravity;

        RpcAddImpulse(impulse, isKinematic, useGravity);
    }

    [ServerCallback]
    [ClientRpc(includeOwner = false)]
    public void RpcAddImpulse(Vector3 impulse, bool isKinematic, bool useGravity)
    {
        _rigidbody.AddForce(impulse, ForceMode.Impulse);
        _rigidbody.isKinematic = isKinematic;
        _rigidbody.useGravity = useGravity;
    }

    [ServerCallback]
    [ClientRpc(includeOwner = false)]
    private void RpcActivationEffect(Vector3 position, GameObject target)
    {
        transform.position = position;

        activationSound?.Post(gameObject);

        var targetStatus = target ? target.GetComponent<AbstractStatusComponent>() : null;
        if (!targetStatus && missEnabled)
        {
            AbstractEffect.Apply(effectsOnSelfMiss, source, gameObject);
            if (prefabOnMiss) TrashMan.Spawn(prefabOnMiss, transform.position);
        }
        else
        {
            AbstractEffect.Apply(effectsOnSelf, source, gameObject);
            AbstractEffect.Apply(effectsOnTarget, source, target);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!canActivate || activationType == TypeOfActivation.TIME) return;

        collisionSound?.Post(gameObject);
        isInAir = false;
        canActivate = false;
        AbstractEffect.Apply(initialEffectsOnTarget, source, collision.collider.gameObject);
        
        switch (projectileType)
        {
            case TypeOfProjectile.BASIC:
            case TypeOfProjectile.MISSILE:
            {
                if(applyCustomForceInCollision)
                {
                    _rigidbody.velocity /= forceDivisorInCollision;
                }

                if (missEnabled && collision.collider.GetComponent<AbstractStatusComponent>() || !missEnabled)
                {
                    if (collisionFX)
                    {                        
                        var fx = TrashMan.Spawn(collisionFX, collision.GetContact(0).point, Quaternion.LookRotation(collision.GetContact(0).normal));
                        TrashMan.DespawnAfterDelay(fx, TIME_TO_DESPAWN_FX);
                    }
                }
            } break;
            case TypeOfProjectile.BASIC_STICKY:
            {
                if (collision.collider.GetComponent<AbstractStatusComponent>())
                {
                    transform.parent = collision.collider.transform;
                }
                
                _rigidbody.velocity = Vector3.zero;
                _rigidbody.isKinematic = true;
                
                if (stickyCollisionFX)
                {
                    var fx = TrashMan.Spawn(stickyCollisionFX, collision.GetContact(0).point, Quaternion.LookRotation(collision.GetContact(0).normal));
                    TrashMan.DespawnAfterDelay(fx, TIME_TO_DESPAWN_FX);
                }

                foreach (var go in activateOnStickyContact)
                {
                    if (go)
                    {
                        go.SetActive(true);
                    }
                }

                if (snapToNormal)
                {
                    transform.forward = collision.GetContact(0).normal;
                }
            } break;
            case TypeOfProjectile.TRACKER:
            {
                var canGo = TrackAndSetNewTarget(collision.collider.gameObject);
                ActivationEffect(collision.collider.gameObject, collision.GetContact(0).point, !canGo);
                return;
            }
            default: break;
        }
        
        switch (activationType)
        {
            case TypeOfActivation.CONTACT:
                ActivationEffect(collision.collider.gameObject, collision.GetContact(0).point);
                break;
            case TypeOfActivation.CONTACT_DELAY:
                if(activationEffect != null)
                {
                    GameGlobals.StopGlobalCoroutine(activationEffect);
                }
                activationEffect = GameGlobals.Delay(timeToApplyEffects, () => ActivationEffect(collision.collider.gameObject, collision.GetContact(0).point));
                break;
            case TypeOfActivation.NONE:
                break;
            case TypeOfActivation.TIME:
                break;
            default: break;
        }
    }

    private void OnDisable()
    {
        GameGlobals.StopGlobalCoroutine(activationEffect);
    }

    private bool TrackAndSetNewTarget(GameObject collisionTarget)
    {
        if (numberOfTracks < currentNumberOfTracks)
        {
            return false;
        }

        if (collisionTarget == null) return false;
        
        var targetStatus = collisionTarget.GetComponent<AbstractStatusComponent>();
        if (!targetStatus) return false;

        targetStatus = targetStatus.GetParentStatusComponent();
        var targetsInRange = new List<AbstractStatusComponent>(); 
        var colliders = Physics.OverlapSphere(transform.position, distanceToTrack, LayerUtils.StatusMask);
        foreach (var col in colliders)
        {
            var status = col.GetComponent<AbstractStatusComponent>();
            if (status && !targetsInRange.Contains(status) && !status.IsDead() && FactionUtils.IsAggressive(source.gameObject, status.GetGameObject()))
            {
                targetsInRange.Add(status);
            }
        }

        targetsInRange = LinkedStatusComponent.DistinctSources(targetsInRange).ToList();
        targetsInRange.Remove(targetStatus);
        targetsInRange.Remove(source.status);
        targetsInRange = targetsInRange.OrderBy(CalculateSqrMagnitude).ToList();

        //First we try always to find a new target instead of a hitted one
        foreach (var targetInRange in targetsInRange)
        {
            if(trackRecord.Contains(targetInRange)) continue;
            var position = transform.position;
            var direction = (targetInRange.transform.position - position).normalized;
            var distance = Vector3.Distance(targetInRange.gameObject.transform.position, position);
            
            if(Physics.Raycast(transform.position, direction, distance, LayerUtils.DefaultFloorWallMask)) continue;

            trackRecord.AddUnique(targetInRange);
            AssignNewTrackedTarget(targetInRange.gameObject);
            return true;
        }
        
        //In case we don't find anyone we try to track old ones
        trackRecord = trackRecord.OrderBy(CalculateSqrMagnitude).ToList();
        foreach (var trackedStatus in trackRecord)
        {
            //Ignoring current target status or invalid statuses
            if (trackedStatus == null || trackedStatus == targetStatus || targetStatus.IsDead()) continue;

            var position = transform.position;
            var direction = (trackedStatus.transform.position - position).normalized;
            var distance = Vector3.Distance(trackedStatus.gameObject.transform.position, position);
            
            if(Physics.Raycast(transform.position, direction, distance, LayerUtils.DefaultFloorWallMask)) continue;

            AssignNewTrackedTarget(trackedStatus.gameObject);
            return true;
        }

        canActivate = false;
        return false;
    }

    private void AssignNewTrackedTarget(GameObject trackedGameObject)
    {
        target = trackedGameObject;
        isTracking = true;
        canActivate = true;
        ++currentNumberOfTracks;
    }

    private float CalculateSqrMagnitude(AbstractStatusComponent status)
    {
        return Vector3.SqrMagnitude(status.gameObject.transform.position - transform.position);
    }

    public void SetIsThrusting(bool isThursting)
    {
        this.isThursting = isThursting;
    }

    public void SetSourceGameObject(SourceGameObject source)
    {
        this.source = new SourceGameObject(source)
        {
            gameObject = gameObject,
        };
    }
    
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (projectileType != TypeOfProjectile.TRACKER) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, distanceToTrack);
    }
#endif
}
