using UnityEngine;

public class RandomButterflyScript : MonoBehaviour
{
    [SerializeField] private float movementSpeed = 1.5f;
    [SerializeField] private Vector2 horizontalRange = new Vector2(-16f, 16f);
    [SerializeField] private Vector2 verticalRange = new Vector2(-7f, 20f);
    [SerializeField] private Vector2 idleTimeRange = new Vector2(0.2f, 0.6f);
    [SerializeField] private float arrivalThreshold = 0.05f;

    private Vector3 originPosition;
    private Vector3 targetPosition;
    private float idleTimer;

    private void Awake()
    {
        originPosition = transform.localPosition;
    }

    private void Start()
    {
        SetNewTargetPosition();
    }

    private void Update()
    {
        MoveToTargetPosition();
    }

    private void SetNewTargetPosition()
    {
        Vector3 offset = Random.insideUnitSphere;

        if (offset.sqrMagnitude < Mathf.Epsilon)
        {
            offset = Vector3.up;
        }

        offset.x *= horizontalRange.y;
        offset.z *= horizontalRange.y;
        offset.y *= verticalRange.y;

        float minHorizontal = Mathf.Max(0f, horizontalRange.x);
        if (minHorizontal > 0f)
        {
            Vector3 horizontal = new Vector3(offset.x, 0f, offset.z);
            if (horizontal.sqrMagnitude < minHorizontal * minHorizontal)
            {
                horizontal = horizontal.normalized * minHorizontal;
                offset.x = horizontal.x;
                offset.z = horizontal.z;
            }
        }

        float minVertical = Mathf.Max(0f, verticalRange.x);
        float maxVertical = Mathf.Max(verticalRange.y, minVertical);
        offset.y = Mathf.Clamp(offset.y, -maxVertical, maxVertical);
        if (Mathf.Abs(offset.y) < minVertical)
        {
            offset.y = Mathf.Sign(offset.y) * minVertical;
        }

        targetPosition = originPosition + offset;
        idleTimer = Random.Range(idleTimeRange.x, idleTimeRange.y);
    }

    private void MoveToTargetPosition()
    {
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, targetPosition, movementSpeed * Time.deltaTime);

        float sqrDistance = (transform.localPosition - targetPosition).sqrMagnitude;
        if (sqrDistance <= arrivalThreshold * arrivalThreshold)
        {
            idleTimer -= Time.deltaTime;

            if (idleTimer <= 0f)
            {
                SetNewTargetPosition();
            }
        }
    }

    private void OnValidate()
    {
        if (horizontalRange.y < horizontalRange.x)
        {
            horizontalRange.y = horizontalRange.x;
        }

        if (verticalRange.y < verticalRange.x)
        {
            verticalRange.y = verticalRange.x;
        }

        if (idleTimeRange.y < idleTimeRange.x)
        {
            idleTimeRange.y = idleTimeRange.x;
        }
    }
}
