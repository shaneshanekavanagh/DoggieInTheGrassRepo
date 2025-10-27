using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class BasicMousePosition : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private DoggieWalk doggie;
    [SerializeField] private Camera referenceCamera;
    [SerializeField] private LayerMask groundMask = Physics.DefaultRaycastLayers;
    [SerializeField] private float raycastDistance = 1000f;
    [SerializeField] private ParticleSystem selectionParticles;
    [SerializeField] private float selectionParticleYOffset = 1f;

    [Header("Red Ball")]
    [SerializeField] private Transform redBall;
    [SerializeField] private float redBallSelectionThreshold = 3f;
    [SerializeField] private float redBallMoveSpeed = 4f;
    [SerializeField] private float redBallChaseDelay = 1f;
    [SerializeField] private ParticleSystem redBallArrivalParticles;
    [SerializeField] private float redBallArrivalParticleYOffset = 1f;

    [Header("Random Roaming")]
    [SerializeField] private bool randomRoamEnabled = true;
    [SerializeField] private Vector2 randomXBounds = new Vector2(-16f, 16f);
    [SerializeField] private Vector2 randomZBounds = new Vector2(-7f, 20f);
    [SerializeField] private Vector2 randomDelayRange = new Vector2(1f, 5f);
    [SerializeField] private float targetY = 0f;

    private Camera cachedCamera;
    private Coroutine randomRoutine;
    private bool awaitingArrival;
    private bool playerCommandActive;
    private Vector3 redBallOriginalPosition = new Vector3(12f, 1f, -8f);
    private Vector3 redBallTargetPosition;
    private Vector3 redBallTargetGroundPosition;
    private bool redBallEngaged;
    private bool redBallAwaitingSecondTouch;
    private bool redBallMoving;
    private bool redBallAwaitingDogArrival;
    private bool redBallChaseActive;
    private float redBallChaseDelayTimer;
    private bool redBallDogFollowingTransform;

    private void Awake()
    {
        cachedCamera = referenceCamera != null ? referenceCamera : Camera.main;

        if (doggie == null)
        {
            doggie = FindObjectOfType<DoggieWalk>();
        }

        if (doggie == null)
        {
            Debug.LogError($"{nameof(BasicMousePosition)} requires a {nameof(DoggieWalk)} in the scene.", this);
            enabled = false;
            return;
        }

        if (cachedCamera == null)
        {
            Debug.LogError($"{nameof(BasicMousePosition)} requires a camera reference to perform raycasts.", this);
            enabled = false;
        }

        if (redBall != null)
        {
            redBallOriginalPosition = new Vector3(12f, 1f, -8f);
            redBall.position = redBallOriginalPosition;
        }

        ResetRedBallState(repositionBall: redBall != null);
    }

    private void OnEnable()
    {
        ResetRedBallState(repositionBall: redBall != null);

        if (doggie != null)
        {
            doggie.DestinationReached += HandleDogDestinationReached;
        }

        playerCommandActive = false;
        awaitingArrival = doggie != null && doggie.IsMoving;

        StartRandomRoutine();
    }

    private void OnDisable()
    {
        if (doggie != null)
        {
            doggie.DestinationReached -= HandleDogDestinationReached;
        }

        ResetRedBallState(repositionBall: redBall != null);
        StopRandomRoutine();
    }

    private void Update()
    {
        UpdateRedBallMovement();

        if (!TryGetInteractionPoint(out var hitPoint))
        {
            return;
        }

        HandlePointerInteraction(hitPoint);
        UpdateRedBallChase();
    }

    private void HandlePointerInteraction(Vector3 hitPoint)
    {
        if (redBallAwaitingSecondTouch)
        {
            HandleRedBallSecondTouch(hitPoint);
            return;
        }

        if (redBallMoving || redBallAwaitingDogArrival)
        {
            return;
        }

        if (IsRedBallTouch(hitPoint))
        {
            HandleRedBallFirstTouch(hitPoint);
            return;
        }

        HandleStandardTouch(hitPoint);
    }

    private bool TryGetInteractionPoint(out Vector3 worldPoint)
    {
        worldPoint = default;

        if (cachedCamera == null)
        {
            return false;
        }

        // Handle multi-touch devices first so mobile gestures take precedence.
        if (Input.touchSupported && Input.touchCount > 0)
        {
            var touches = Input.touches;

            for (int i = 0; i < touches.Length; i++)
            {
                Touch touch = touches[i];

                if (touch.phase != TouchPhase.Began && touch.phase != TouchPhase.Ended)
                {
                    continue;
                }

                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
                {
                    continue;
                }

                if (TryGetGroundPoint(touch.position, out worldPoint))
                {
                    return true;
                }
            }
        }

        if (!Input.GetMouseButtonUp(0))
        {
            return false;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return false;
        }

        return TryGetGroundPoint(Input.mousePosition, out worldPoint);
    }

    private bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 point)
    {
        point = default;

        if (cachedCamera == null)
        {
            return false;
        }

        Ray ray = cachedCamera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out var hitInfo, raycastDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        point = hitInfo.point;
        point.y = targetY;
        return true;
    }

    private void HandleDogDestinationReached(bool wasIdleMove)
    {
        awaitingArrival = false;

        if (redBallAwaitingDogArrival)
        {
            redBallAwaitingDogArrival = false;
            PlayRedBallArrivalParticles(redBallTargetGroundPosition);
            ResetRedBallState(repositionBall: redBall != null);
            playerCommandActive = false;
            return;
        }

        if (!wasIdleMove)
        {
            playerCommandActive = false;
        }
    }

    private void StartRandomRoutine()
    {
        if (!randomRoamEnabled || !isActiveAndEnabled)
        {
            return;
        }

        if (randomRoutine != null)
        {
            StopCoroutine(randomRoutine);
        }

        randomRoutine = StartCoroutine(RandomRoamRoutine());
    }

    private void StopRandomRoutine()
    {
        if (randomRoutine != null)
        {
            StopCoroutine(randomRoutine);
            randomRoutine = null;
        }
    }

    private IEnumerator RandomRoamRoutine()
    {
        while (true)
        {
            yield return new WaitUntil(() => !playerCommandActive && !awaitingArrival && !doggie.IsMoving);

            float delay = Random.Range(randomDelayRange.x, randomDelayRange.y);

            if (delay > 0f)
            {
                float elapsed = 0f;
                bool interrupted = false;

                while (elapsed < delay)
                {
                    if (playerCommandActive || awaitingArrival || doggie.IsMoving)
                    {
                        interrupted = true;
                        break;
                    }

                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (interrupted)
                {
                    continue;
                }
            }

            awaitingArrival = true;
            SetTargetPosition(GetRandomPoint());
            PlaySelectionParticles();
            doggie.MoveTo(transform, idleMove: true);
        }
    }

    private void HandleStandardTouch(Vector3 hitPoint)
    {
        playerCommandActive = true;
        awaitingArrival = true;
        SetTargetPosition(hitPoint);
        PlaySelectionParticles();
        doggie.MoveTo(transform, idleMove: false);
    }

    private bool IsRedBallTouch(Vector3 hitPoint)
    {
        if (redBall == null)
        {
            return false;
        }

        Vector2 hitXZ = new Vector2(hitPoint.x, hitPoint.z);
        Vector2 ballXZ = new Vector2(redBall.position.x, redBall.position.z);
        float threshold = Mathf.Max(redBallSelectionThreshold, 0f);
        return Vector2.SqrMagnitude(hitXZ - ballXZ) <= threshold * threshold;
    }

    private void HandleRedBallFirstTouch(Vector3 hitPoint)
    {
        if (redBall == null)
        {
            HandleStandardTouch(hitPoint);
            return;
        }

        redBallEngaged = true;
        redBallAwaitingSecondTouch = true;
        redBallMoving = false;
        redBallAwaitingDogArrival = false;
        redBallChaseActive = false;
        redBallChaseDelayTimer = 0f;
        redBallDogFollowingTransform = false;

        Vector3 hoverPosition = new Vector3(0f, 22f, -18f);
        redBall.position = hoverPosition;
        redBallTargetPosition = hoverPosition;
        redBallTargetGroundPosition = new Vector3(hitPoint.x, targetY, hitPoint.z);

        playerCommandActive = true;
        awaitingArrival = false;

        if (doggie != null)
        {
            doggie.EnterRaiseHeadState();
            doggie.FacePosition(redBall.position);
        }
    }

    private void HandleRedBallSecondTouch(Vector3 hitPoint)
    {
        redBallAwaitingSecondTouch = false;
        redBallEngaged = true;

        redBallTargetGroundPosition = new Vector3(hitPoint.x, targetY, hitPoint.z);
        redBallTargetPosition = new Vector3(hitPoint.x, redBallOriginalPosition.y, hitPoint.z);
        redBall.position = new Vector3(0f, 22f, -18f);

        SetTargetPosition(redBallTargetGroundPosition);
        PlaySelectionParticles();

        playerCommandActive = true;
        awaitingArrival = true;

        if (doggie != null)
        {
            doggie.ExitRaiseHeadState();
        }

        if (redBall == null)
        {
            BeginDogRunToRedBallTarget();
            return;
        }

        if (redBallMoveSpeed <= 0f)
        {
            redBall.position = redBallTargetPosition;
            OnRedBallMovementComplete();
            return;
        }

        redBallMoving = true;
        redBallChaseActive = redBallChaseDelay <= 0f;
        redBallChaseDelayTimer = Mathf.Max(0f, redBallChaseDelay);
        redBallDogFollowingTransform = false;

        if (doggie != null)
        {
            doggie.StopMovement();
            doggie.FacePosition(redBall.position);
        }
    }

    private void UpdateRedBallChase()
    {
        if (redBall == null || doggie == null)
        {
            return;
        }

        if (!redBallMoving)
        {
            redBallChaseActive = false;
            redBallChaseDelayTimer = 0f;
            redBallDogFollowingTransform = false;
            return;
        }

        Vector3 ballPosition = redBall.position;
        doggie.FacePosition(ballPosition);

        if (!redBallChaseActive)
        {
            if (redBallChaseDelayTimer > 0f)
            {
                redBallChaseDelayTimer -= Time.deltaTime;
                if (redBallChaseDelayTimer <= 0f)
                {
                    redBallChaseActive = true;
                }
                else
                {
                    return;
                }
            }
            else
            {
                redBallChaseActive = true;
            }
        }

        if (!redBallDogFollowingTransform)
        {
            doggie.MoveTo(redBall, idleMove: false);
            redBallDogFollowingTransform = true;
        }
    }

    private void UpdateRedBallMovement()
    {
        if (!redBallMoving || redBall == null)
        {
            return;
        }

        Vector3 currentPosition = redBall.position;
        Vector3 nextPosition = Vector3.MoveTowards(currentPosition, redBallTargetPosition, redBallMoveSpeed * Time.deltaTime);
        redBall.position = nextPosition;

        if ((nextPosition - redBallTargetPosition).sqrMagnitude <= 0.0001f)
        {
            redBall.position = redBallTargetPosition;
            OnRedBallMovementComplete();
        }

        if (doggie != null)
        {
            doggie.FacePosition(redBall.position);
        }
    }

    private void OnRedBallMovementComplete()
    {
        redBallMoving = false;
        redBallChaseActive = false;
        redBallChaseDelayTimer = 0f;
        BeginDogRunToRedBallTarget();
    }

    private void BeginDogRunToRedBallTarget()
    {
        redBallAwaitingDogArrival = true;
        awaitingArrival = true;
        playerCommandActive = true;

        if (doggie != null)
        {
            doggie.FacePosition(redBallTargetPosition);
            doggie.MoveToPosition(redBallTargetGroundPosition, idleMove: false);
        }
    }

    private void ResetRedBallState(bool repositionBall)
    {
        redBallEngaged = false;
        redBallAwaitingSecondTouch = false;
        redBallMoving = false;
        redBallAwaitingDogArrival = false;
        redBallChaseActive = false;
        redBallChaseDelayTimer = 0f;
        redBallDogFollowingTransform = false;

        redBallTargetPosition = redBallOriginalPosition;
        redBallTargetGroundPosition = new Vector3(redBallOriginalPosition.x, targetY, redBallOriginalPosition.z);

        if (repositionBall && redBall != null)
        {
            redBall.position = redBallOriginalPosition;
        }

        if (doggie != null && doggie.IsRaiseHead)
        {
            doggie.ExitRaiseHeadState();
        }
    }

    private void SetTargetPosition(Vector3 position)
    {
        position.y = targetY;
        transform.position = position;
    }

    private void PlaySelectionParticles()
    {
        if (selectionParticles == null)
        {
            return;
        }

        Transform particleTransform = selectionParticles.transform;
        Vector3 particlePosition = transform.position;
        particlePosition.y += selectionParticleYOffset;
        particleTransform.position = particlePosition;

        GameObject particleObject = selectionParticles.gameObject;
        if (!particleObject.activeSelf)
        {
            particleObject.SetActive(true);
        }

        selectionParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        selectionParticles.Play(true);
    }

    private void PlayRedBallArrivalParticles(Vector3 position)
    {
        if (redBallArrivalParticles == null)
        {
            return;
        }

        Transform particleTransform = redBallArrivalParticles.transform;
        Vector3 particlePosition = position;
        particlePosition.y += redBallArrivalParticleYOffset;
        particleTransform.position = particlePosition;

        GameObject particleObject = redBallArrivalParticles.gameObject;
        if (!particleObject.activeSelf)
        {
            particleObject.SetActive(true);
        }

        redBallArrivalParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        redBallArrivalParticles.Play(true);
    }

    private Vector3 GetRandomPoint()
    {
        float randomX = Random.Range(randomXBounds.x, randomXBounds.y);
        float randomZ = Random.Range(randomZBounds.x, randomZBounds.y);
        return new Vector3(randomX, targetY, randomZ);
    }

    private void OnValidate()
    {
        if (randomXBounds.y < randomXBounds.x)
        {
            randomXBounds.y = randomXBounds.x;
        }

        if (randomZBounds.y < randomZBounds.x)
        {
            randomZBounds.y = randomZBounds.x;
        }

        if (randomDelayRange.y < randomDelayRange.x)
        {
            randomDelayRange.y = randomDelayRange.x;
        }

        selectionParticleYOffset = Mathf.Max(0f, selectionParticleYOffset);
        redBallSelectionThreshold = Mathf.Max(0f, redBallSelectionThreshold);
        redBallMoveSpeed = Mathf.Max(0f, redBallMoveSpeed);
        redBallChaseDelay = Mathf.Max(0f, redBallChaseDelay);
        redBallArrivalParticleYOffset = Mathf.Max(0f, redBallArrivalParticleYOffset);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        const float gizmoHeight = 0.05f;
        Vector3 center = new Vector3((randomXBounds.x + randomXBounds.y) * 0.5f, targetY, (randomZBounds.x + randomZBounds.y) * 0.5f);
        Vector3 size = new Vector3(Mathf.Abs(randomXBounds.y - randomXBounds.x), gizmoHeight, Mathf.Abs(randomZBounds.y - randomZBounds.x));

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size);
    }
#endif
}
