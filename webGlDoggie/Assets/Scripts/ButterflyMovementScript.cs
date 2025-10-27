using System.Collections;
using UnityEngine;

public class ButterflyMovementScript : MonoBehaviour
{
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

    private Coroutine movementCoroutine;
    private Vector3? overrideTargetPosition;
    private bool dogWithinRange;

    private void Reset()
    {
        target = transform;
        if (dog == null)
        {
            dog = FindObjectOfType<DoggieWalk>();
        }
    }

    private void Awake()
    {
        if (dog == null)
        {
            dog = FindObjectOfType<DoggieWalk>();
        }
    }

    private void OnEnable()
    {
        dogWithinRange = false;
        overrideTargetPosition = null;

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
            UpdateFleeTarget();

            Vector3 desiredPosition = ApplyHeightAndClamp(GetBaseTargetPosition());

            Vector3 currentPosition = transform.position;
            Vector3 movement = desiredPosition - currentPosition;
            float sqrDistance = movement.sqrMagnitude;
            float arrivalThreshold = GetArrivalThreshold(sqrDistance);
            float arrivalThresholdSqr = arrivalThreshold * arrivalThreshold;

            if (sqrDistance > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(movement);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            if (sqrDistance > arrivalThresholdSqr)
            {
                transform.position = Vector3.MoveTowards(currentPosition, desiredPosition, speed * Time.deltaTime);
            }

            yield return null;
        }
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
    }
}
