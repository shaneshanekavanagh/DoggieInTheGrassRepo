using System.Collections;
using UnityEngine;

public class ButterflyMovementScript : MonoBehaviour
{
    private enum TreeFlightState
    {
        Free,
        Approaching,
        Landed,
        Departing
    }

    [Header("Targeting")]
    [SerializeField] private Transform target;
    [SerializeField] private DoggieWalk dog;

    [Header("Movement")]
    [SerializeField] private float speed = 4f;
    [SerializeField] private float rotationSpeed = 360f;
    [SerializeField] private float stoppingDistance = 0.5f;
    [SerializeField] private float lockedHeight = 1.1f;
    [SerializeField] private float minimumArrivalThreshold = 1.5f;
    [SerializeField] private float closeProximityDistance = 3f;
    [SerializeField] private float closeProximityArrivalThreshold = 2.5f;
    [SerializeField] private ParticleSystem arrivalParticles;

    [Header("Bounds")]
    [SerializeField] private Vector2 xBounds = new Vector2(-16f, 16f);
    [SerializeField] private Vector2 zBounds = new Vector2(-7f, 20f);
    [SerializeField] private float edgeBuffer = 1.5f;

    [Header("Dog Interaction")]
    [SerializeField] private float dogArrivalDistance = 2.5f;
    [SerializeField] private float fleeDistance = 5f;
    [SerializeField] private float additionalFleeDistance = 2f;
    [SerializeField] private float dogArrivalFleeMultiplier = 1.5f;

    [Header("Tree Landing")]
    [SerializeField] private LowPolyRuntimeTreeGenerator treeGenerator;
    [Tooltip("Horizontal distance from a canopy edge at which the butterfly begins landing.")]
    [SerializeField, Min(0f)] private float treeLandingSearchDistance = 0.9f;
    [Tooltip("How long the butterfly rests on the canopy.")]
    [SerializeField, Min(0f)] private float treeLandingDuration = 2f;
    [Tooltip("Distance travelled away from a tree after taking off.")]
    [SerializeField] private Vector2 treeDepartureDistanceRange = new Vector2(4f, 7f);
    [Tooltip("Random horizontal angle added to the direction away from the landed tree.")]
    [SerializeField, Range(0f, 75f)] private float treeDepartureAngleVariation = 35f;
    [Tooltip("Prevents immediately landing on the same tree after departure.")]
    [SerializeField, Min(0f)] private float sameTreeRelandingDelay = 4f;
    [SerializeField, Min(0.01f)] private float treeLandingArrivalDistance = 0.08f;

    private Coroutine movementCoroutine;
    private Vector3? overrideTargetPosition;
    private bool dogWithinRange;
    private TreeFlightState treeFlightState;
    private Vector3 treeFlightTarget;
    private float treeLandingTimer;
    private float sameTreeCooldownTimer;
    private int activeTreeIndex = -1;
    private int lastLandedTreeIndex = -1;

    private void Reset()
    {
        target = transform;
        if (dog == null)
        {
            dog = FindFirstObjectByType<DoggieWalk>();
        }

        if (treeGenerator == null)
        {
            treeGenerator = FindFirstObjectByType<LowPolyRuntimeTreeGenerator>();
        }
    }

    private void Awake()
    {
        if (dog == null)
        {
            dog = FindFirstObjectByType<DoggieWalk>();
        }

        if (treeGenerator == null)
        {
            treeGenerator = FindFirstObjectByType<LowPolyRuntimeTreeGenerator>();
        }
    }

    private void OnEnable()
    {
        dogWithinRange = false;
        overrideTargetPosition = null;
        treeFlightState = TreeFlightState.Free;
        treeLandingTimer = 0f;
        sameTreeCooldownTimer = 0f;
        activeTreeIndex = -1;
        lastLandedTreeIndex = -1;

        if (movementCoroutine != null)
        {
            StopCoroutine(movementCoroutine);
        }

        movementCoroutine = StartCoroutine(MovementRoutine());
    }

    private void OnDisable()
    {
        if (movementCoroutine != null)
        {
            StopCoroutine(movementCoroutine);
            movementCoroutine = null;
        }
    }

    public void SetTarget(Transform newTarget)
    {
        CancelTreeFlight();
        target = newTarget;
        overrideTargetPosition = null;
    }

    private float GetArrivalThreshold(float sqrDistanceToTarget)
    {
        float baseThreshold = Mathf.Max(stoppingDistance, minimumArrivalThreshold);

        if (sqrDistanceToTarget <= closeProximityDistance * closeProximityDistance)
        {
            return Mathf.Max(baseThreshold, closeProximityArrivalThreshold);
        }

        return baseThreshold;
    }

    private IEnumerator MovementRoutine()
    {
        while (true)
        {
            float deltaTime = Time.deltaTime;
            if (sameTreeCooldownTimer > 0f)
            {
                sameTreeCooldownTimer -= deltaTime;
            }

            UpdateFleeTarget();
            UpdateTreeFlightState(deltaTime);

            if (treeFlightState == TreeFlightState.Landed)
            {
                transform.position = treeFlightTarget;
                yield return null;
                continue;
            }

            bool usingTreeTarget = treeFlightState == TreeFlightState.Approaching ||
                                   treeFlightState == TreeFlightState.Departing;
            Vector3 desiredPosition = usingTreeTarget
                ? treeFlightTarget
                : ApplyHeightAndClamp(GetBaseTargetPosition());

            Vector3 currentPosition = transform.position;
            Vector3 movement = desiredPosition - currentPosition;
            float sqrDistance = movement.sqrMagnitude;
            float arrivalThreshold = treeFlightState == TreeFlightState.Approaching
                ? treeLandingArrivalDistance
                : GetArrivalThreshold(sqrDistance);
            float arrivalThresholdSqr = arrivalThreshold * arrivalThreshold;

            if (sqrDistance > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(movement);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            if (sqrDistance > arrivalThresholdSqr)
            {
                transform.position = Vector3.MoveTowards(currentPosition, desiredPosition, speed * deltaTime);
            }
            else if (treeFlightState == TreeFlightState.Approaching)
            {
                CompleteTreeLanding();
            }
            else if (treeFlightState == TreeFlightState.Departing)
            {
                treeFlightState = TreeFlightState.Free;
                activeTreeIndex = -1;
            }

            yield return null;
        }
    }

    private void UpdateTreeFlightState(float deltaTime)
    {
        if (treeGenerator == null)
        {
            return;
        }

        if (treeFlightState == TreeFlightState.Landed)
        {
            treeLandingTimer -= deltaTime;
            if (treeLandingTimer <= 0f)
            {
                BeginTreeDeparture();
            }

            return;
        }

        if (treeFlightState != TreeFlightState.Free || dogWithinRange)
        {
            return;
        }

        int excludedTree = sameTreeCooldownTimer > 0f ? lastLandedTreeIndex : -1;
        if (treeGenerator.TryGetCanopyLandingPoint(
                transform.position,
                treeLandingSearchDistance,
                excludedTree,
                out Vector3 landingPoint,
                out int treeIndex))
        {
            treeFlightTarget = landingPoint;
            activeTreeIndex = treeIndex;
            treeFlightState = TreeFlightState.Approaching;
        }
    }

    private void CompleteTreeLanding()
    {
        transform.position = treeFlightTarget;
        treeFlightState = TreeFlightState.Landed;
        treeLandingTimer = treeLandingDuration;
        lastLandedTreeIndex = activeTreeIndex;
        PlayArrivalParticles(treeFlightTarget);
    }

    private void BeginTreeDeparture()
    {
        Vector3 departureDirection = Vector3.forward;
        if (treeGenerator != null &&
            treeGenerator.TryGetTreeCentre(activeTreeIndex, out Vector3 treeCentre))
        {
            departureDirection = transform.position - treeCentre;
            departureDirection.y = 0f;
        }

        if (departureDirection.sqrMagnitude <= 0.0001f)
        {
            float fallbackAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            departureDirection = new Vector3(Mathf.Cos(fallbackAngle), 0f, Mathf.Sin(fallbackAngle));
        }

        departureDirection.Normalize();
        departureDirection = Quaternion.Euler(
            0f,
            Random.Range(-treeDepartureAngleVariation, treeDepartureAngleVariation),
            0f) * departureDirection;

        float departureDistance = Random.Range(
            treeDepartureDistanceRange.x,
            treeDepartureDistanceRange.y);
        Vector3 departureTarget = transform.position + departureDirection * departureDistance;
        departureTarget = ClampToBounds(departureTarget);
        departureTarget.y = lockedHeight;

        treeFlightTarget = departureTarget;
        overrideTargetPosition = departureTarget;
        sameTreeCooldownTimer = sameTreeRelandingDelay;
        treeFlightState = TreeFlightState.Departing;
    }

    private void CancelTreeFlight()
    {
        if (treeFlightState == TreeFlightState.Landed ||
            treeFlightState == TreeFlightState.Approaching)
        {
            lastLandedTreeIndex = activeTreeIndex;
            sameTreeCooldownTimer = sameTreeRelandingDelay;
        }

        treeFlightState = TreeFlightState.Free;
        treeLandingTimer = 0f;
        activeTreeIndex = -1;
    }

    private Vector3 GetBaseTargetPosition()
    {
        if (overrideTargetPosition.HasValue)
        {
            return overrideTargetPosition.Value;
        }

        if (target != null)
        {
            return target.position;
        }

        return transform.position;
    }

    private void UpdateFleeTarget()
    {
        if (dog == null)
        {
            return;
        }

        Transform dogTransform = dog.transform;
        if (dogTransform == null)
        {
            return;
        }

        Vector3 toButterfly = transform.position - dogTransform.position;
        toButterfly.y = 0f;

        float dogArrivalThreshold = Mathf.Max(dogArrivalDistance, closeProximityArrivalThreshold);
        if (dog != null)
        {
            dogArrivalThreshold = Mathf.Max(dogArrivalThreshold, dog.LocationLockThreshold);
        }
        bool withinRange = toButterfly.sqrMagnitude <= dogArrivalThreshold * dogArrivalThreshold;

        if (withinRange && !dogWithinRange)
        {
            TriggerDogArrivalResponse(dogTransform);
        }

        dogWithinRange = withinRange;
    }

    private void TriggerDogArrivalResponse(Transform dogTransform)
    {
        CancelTreeFlight();
        PlayArrivalParticles(transform.position);
        overrideTargetPosition = ComputeEscapeTarget(dogTransform, true);
    }

    private Vector3 ComputeEscapeTarget(Transform dogTransform, bool dogJustArrived = false)
    {
        Vector3 currentPosition = transform.position;
        Vector3 escapeDirection = GetEscapeDirection(dogTransform, currentPosition);

        Vector3 dogFlatPosition = new Vector3(dogTransform.position.x, currentPosition.y, dogTransform.position.z);
        float distanceFromDog = Vector3.Distance(currentPosition, dogFlatPosition);
        float fleeScale = dogJustArrived ? dogArrivalFleeMultiplier : 1f;
        float targetFleeDistance = fleeDistance * fleeScale;
        float targetAdditionalDistance = additionalFleeDistance * fleeScale;
        float desiredDistanceFromDog = Mathf.Max(distanceFromDog + targetFleeDistance, targetFleeDistance + targetAdditionalDistance);

        Vector3 candidate = dogFlatPosition + escapeDirection * desiredDistanceFromDog;

        if (!IsWithinBounds(candidate) || IsNearEdge(currentPosition))
        {
            Vector3 centerDirection = GetCenterDirection(currentPosition);

            candidate = currentPosition + centerDirection * Mathf.Max(desiredDistanceFromDog, GetArrivalThreshold(0f) * fleeScale);
        }

        return ClampToBounds(candidate);
    }

    private void PlayArrivalParticles(Vector3 arrivalPosition)
    {
        if (arrivalParticles == null)
        {
            return;
        }

        Transform particleTransform = arrivalParticles.transform;
        particleTransform.position = arrivalPosition;

        GameObject particleObject = arrivalParticles.gameObject;
        if (!particleObject.activeSelf)
        {
            particleObject.SetActive(true);
        }

        arrivalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        arrivalParticles.Play(true);
    }

    private Vector3 GetEscapeDirection(Transform dogTransform, Vector3 currentPosition)
    {
        Vector3 direction = dogTransform.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = currentPosition - dogTransform.position;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = new Vector3(currentPosition.x, 0f, currentPosition.z);
        }

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.forward;
        }

        return direction.normalized;
    }

    private Vector3 GetCenterDirection(Vector3 currentPosition)
    {
        Vector3 direction = new Vector3(-currentPosition.x, 0f, -currentPosition.z);

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = Vector3.back;
        }

        return direction.normalized;
    }

    private Vector3 ApplyHeightAndClamp(Vector3 position)
    {
        Vector3 clamped = ClampToBounds(position);

        clamped.y = lockedHeight;
        return clamped;
    }

    private Vector3 ClampToBounds(Vector3 position)
    {
        position.x = Mathf.Clamp(position.x, xBounds.x, xBounds.y);
        position.z = Mathf.Clamp(position.z, zBounds.x, zBounds.y);
        return position;
    }

    private bool IsWithinBounds(Vector3 position)
    {
        return position.x >= xBounds.x && position.x <= xBounds.y &&
               position.z >= zBounds.x && position.z <= zBounds.y;
    }

    private bool IsNearEdge(Vector3 position)
    {
        float distanceToXMin = position.x - xBounds.x;
        float distanceToXMax = xBounds.y - position.x;
        float distanceToZMin = position.z - zBounds.x;
        float distanceToZMax = zBounds.y - position.z;

        return distanceToXMin <= edgeBuffer || distanceToXMax <= edgeBuffer ||
               distanceToZMin <= edgeBuffer || distanceToZMax <= edgeBuffer;
    }

    private void OnValidate()
    {
        if (xBounds.y < xBounds.x)
        {
            xBounds.y = xBounds.x;
        }

        if (zBounds.y < zBounds.x)
        {
            zBounds.y = zBounds.x;
        }

        edgeBuffer = Mathf.Max(0f, edgeBuffer);
        dogArrivalDistance = Mathf.Max(0f, dogArrivalDistance);
        fleeDistance = Mathf.Max(0f, fleeDistance);
        stoppingDistance = Mathf.Max(0f, stoppingDistance);
        lockedHeight = Mathf.Max(lockedHeight, 0f);
        additionalFleeDistance = Mathf.Max(0f, additionalFleeDistance);
        minimumArrivalThreshold = Mathf.Max(0.01f, minimumArrivalThreshold);
        closeProximityDistance = Mathf.Max(0f, closeProximityDistance);
        closeProximityArrivalThreshold = Mathf.Max(minimumArrivalThreshold, closeProximityArrivalThreshold);
        dogArrivalFleeMultiplier = Mathf.Max(1f, dogArrivalFleeMultiplier);
        treeLandingSearchDistance = Mathf.Max(0f, treeLandingSearchDistance);
        treeLandingDuration = Mathf.Max(0f, treeLandingDuration);
        treeDepartureDistanceRange.x = Mathf.Max(0.1f, treeDepartureDistanceRange.x);
        treeDepartureDistanceRange.y = Mathf.Max(
            treeDepartureDistanceRange.x,
            treeDepartureDistanceRange.y);
        treeDepartureAngleVariation = Mathf.Clamp(treeDepartureAngleVariation, 0f, 75f);
        sameTreeRelandingDelay = Mathf.Max(0f, sameTreeRelandingDelay);
        treeLandingArrivalDistance = Mathf.Max(0.01f, treeLandingArrivalDistance);
    }
}
