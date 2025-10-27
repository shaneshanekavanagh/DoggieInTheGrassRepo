using System;
using System.Collections;
using UnityEngine;

public class DoggieWalk : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float runSpeed = 4.5f;
    [SerializeField] private float walkRotationSpeed = 120f;
    [SerializeField] private float runRotationSpeed = 360f;
    [SerializeField] private float stoppingDistance = 0.75f;

    [Header("Animation")]
    [SerializeField] private float arrivalBarkDuration = 1.5f;
    [SerializeField] private Animator animatorOverride;

    [Header("Targets")]
    [SerializeField] private ButterflyMovementScript butterfly;
    [SerializeField] private float locationLockThreshold = 2.5f;
    [SerializeField] private Vector2 randomXBounds = new Vector2(-16f, 16f);
    [SerializeField] private Vector2 randomZBounds = new Vector2(-7f, 20f);

    private Transform currentTarget;
    private Animator cachedAnimator;
    private Coroutine arrivalRoutine;
    private bool usingIdleSpeed;
    private bool isMoving;
    private bool isBarking;
    private bool isRaiseHead;
    private bool hasManualTarget;
    private Vector3 manualTargetPosition;

    public event Action<bool> DestinationReached;

    public bool IsMoving => isMoving;
    public bool IsIdleMove => isMoving && usingIdleSpeed;
    public float LocationLockThreshold => locationLockThreshold;
    public bool IsRaiseHead => isRaiseHead;

    private void Awake()
    {
        cachedAnimator = animatorOverride != null ? animatorOverride : GetComponentInChildren<Animator>(true);

        if (cachedAnimator == null)
        {
            Debug.LogWarning($"{nameof(DoggieWalk)} could not find an Animator in children.", this);
        }

        if (butterfly == null)
        {
            butterfly = FindObjectOfType<ButterflyMovementScript>();
        }
    }

    private void OnDisable()
    {
        if (arrivalRoutine != null)
        {
            StopCoroutine(arrivalRoutine);
            arrivalRoutine = null;
        }

        isMoving = false;
        isBarking = false;
        isRaiseHead = false;
        UpdateAnimatorState();
    }

    public void StopMovement()
    {
        StopActiveMovement();
        UpdateAnimatorState();
    }

    public void EnterRaiseHeadState()
    {
        if (isRaiseHead)
        {
            return;
        }

        StopActiveMovement();
        isRaiseHead = true;
        UpdateAnimatorState();
    }

    public void ExitRaiseHeadState()
    {
        if (!isRaiseHead)
        {
            return;
        }

        isRaiseHead = false;
        UpdateAnimatorState();
    }

    public void FacePosition(Vector3 worldPosition)
    {
        Vector3 flatTarget = new Vector3(worldPosition.x, transform.position.y, worldPosition.z);
        Vector3 direction = flatTarget - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= float.Epsilon)
        {
            return;
        }

        Quaternion lookRotation = Quaternion.LookRotation(direction);
        transform.rotation = lookRotation;
    }

    private void OnValidate()
    {
        walkSpeed = Mathf.Max(0f, walkSpeed);
        runSpeed = Mathf.Max(walkSpeed, runSpeed);
        walkRotationSpeed = Mathf.Max(0f, walkRotationSpeed);
        runRotationSpeed = Mathf.Max(walkRotationSpeed, runRotationSpeed);
        stoppingDistance = Mathf.Max(0.01f, stoppingDistance);
        arrivalBarkDuration = Mathf.Max(0f, arrivalBarkDuration);
        locationLockThreshold = Mathf.Max(0.01f, locationLockThreshold);

        if (randomXBounds.y < randomXBounds.x)
        {
            randomXBounds.y = randomXBounds.x;
        }

        if (randomZBounds.y < randomZBounds.x)
        {
            randomZBounds.y = randomZBounds.x;
        }
    }

    public void MoveTo(Transform targetTransform, bool idleMove)
    {
        if (targetTransform == null)
        {
            Debug.LogWarning($"{nameof(DoggieWalk)}.{nameof(MoveTo)} called with a null target.", this);
            return;
        }

        ExitRaiseHeadState();

        currentTarget = targetTransform;
        usingIdleSpeed = idleMove;
        isMoving = true;
        isBarking = false;
        hasManualTarget = false;

        if (arrivalRoutine != null)
        {
            StopCoroutine(arrivalRoutine);
            arrivalRoutine = null;
        }

        UpdateAnimatorState();
    }

    public void MoveToPosition(Vector3 worldPosition, bool idleMove)
    {
        ExitRaiseHeadState();

        manualTargetPosition = new Vector3(worldPosition.x, transform.position.y, worldPosition.z);
        hasManualTarget = true;
        currentTarget = null;
        usingIdleSpeed = idleMove;
        isMoving = true;
        isBarking = false;

        if (arrivalRoutine != null)
        {
            StopCoroutine(arrivalRoutine);
            arrivalRoutine = null;
        }

        UpdateAnimatorState();
    }

    private void StopActiveMovement()
    {
        if (arrivalRoutine != null)
        {
            StopCoroutine(arrivalRoutine);
            arrivalRoutine = null;
        }

        isMoving = false;
        isBarking = false;
        hasManualTarget = false;
        currentTarget = null;
    }

    private void Update()
    {
        if (isRaiseHead)
        {
            return;
        }

        if (!isMoving)
        {
            return;
        }

        Vector3 targetPosition;

        if (hasManualTarget)
        {
            targetPosition = manualTargetPosition;
        }
        else
        {
            if (currentTarget == null)
            {
                return;
            }

            targetPosition = currentTarget.position;
        }

        targetPosition.y = transform.position.y;

        Vector3 direction = targetPosition - transform.position;
        float arrivalThreshold = Mathf.Max(stoppingDistance, locationLockThreshold);
        float sqrArrivalThreshold = arrivalThreshold * arrivalThreshold;

        if (direction.sqrMagnitude <= sqrArrivalThreshold)
        {
            CompleteMovement();
            return;
        }

        float moveSpeed = usingIdleSpeed ? walkSpeed : runSpeed;
        float rotationSpeed = usingIdleSpeed ? walkRotationSpeed : runRotationSpeed;

        if (direction.sqrMagnitude > float.Epsilon)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        if ((transform.position - targetPosition).sqrMagnitude <= sqrArrivalThreshold)
        {
            CompleteMovement();
        }
    }

    private void LateUpdate()
    {
        UpdateAnimatorState();
    }

    private void CompleteMovement()
    {
        bool arrivedAtButterfly = IsNearButterfly();
        hasManualTarget = false;
        isMoving = false;
        currentTarget = null;
        isBarking = arrivedAtButterfly && arrivalBarkDuration > 0f;

        UpdateAnimatorState();
        DestinationReached?.Invoke(usingIdleSpeed);

        if (arrivalRoutine != null)
        {
            StopCoroutine(arrivalRoutine);
            arrivalRoutine = null;
        }

        if (!arrivedAtButterfly)
        {
            Vector3 randomDestination = GetRandomDestination();
            MoveToPosition(randomDestination, idleMove: true);
            return;
        }

        if (isBarking)
        {
            arrivalRoutine = StartCoroutine(ClearBarkAfterDelay());
        }
    }

    private IEnumerator ClearBarkAfterDelay()
    {
        yield return new WaitForSeconds(arrivalBarkDuration);
        isBarking = false;
        UpdateAnimatorState();
    }

    private bool IsNearButterfly()
    {
        if (butterfly == null)
        {
            return false;
        }

        Transform butterflyTransform = butterfly.transform;
        if (butterflyTransform == null)
        {
            return false;
        }

        Vector3 dogPosition = transform.position;
        Vector3 butterflyPosition = butterflyTransform.position;
        butterflyPosition.y = dogPosition.y;

        float threshold = locationLockThreshold;
        return (dogPosition - butterflyPosition).sqrMagnitude <= threshold * threshold;
    }

    private Vector3 GetRandomDestination()
    {
        float randomX = UnityEngine.Random.Range(randomXBounds.x, randomXBounds.y);
        float randomZ = UnityEngine.Random.Range(randomZBounds.x, randomZBounds.y);
        return new Vector3(randomX, transform.position.y, randomZ);
    }

    private void UpdateAnimatorState()
    {
        if (cachedAnimator == null)
        {
            return;
        }

        cachedAnimator.SetBool("RaiseHead", isRaiseHead);

        if (isRaiseHead)
        {
            SetAnimatorBools(false, false, false);
            return;
        }

        if (isBarking)
        {
            SetAnimatorBools(false, false, true);
            return;
        }

        if (isMoving)
        {
            SetAnimatorBools(usingIdleSpeed, !usingIdleSpeed, false);
            return;
        }

        SetAnimatorBools(false, false, false);
    }

    private void SetAnimatorBools(bool walk, bool run, bool bark)
    {
        cachedAnimator.SetBool("Walk", walk);
        cachedAnimator.SetBool("Run", run);
        cachedAnimator.SetBool("Bark", bark);
    }
}
