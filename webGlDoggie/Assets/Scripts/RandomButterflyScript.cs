using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomButterflyScript : MonoBehaviour
{
    public float movementSpeed = 5f;
    public float minDistance = 0.1f;
    public float maxDistance = 1f;

    private Vector3 targetPosition;

    void Start()
    {
        SetNewTargetPosition();
    }

    void Update()
    {
        MoveToTargetPosition();
    }

    void SetNewTargetPosition()
    {
        targetPosition = Random.insideUnitSphere * Random.Range(minDistance, maxDistance);
    }

    void MoveToTargetPosition()
    {
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, targetPosition, movementSpeed * Time.deltaTime);
        if (Vector3.Distance(transform.localPosition, targetPosition) < 0.1f)
        {
            SetNewTargetPosition();
        }
    }
}