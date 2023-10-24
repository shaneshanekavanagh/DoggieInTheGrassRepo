using UnityEngine;

public class ButterflyMovementScript : MonoBehaviour
{
    public Transform target;
    public float speed = 5f;
    public float rotationSpeed = 5f;
    public float stoppingDistance = 1f;

    //public bool Idle = false;

    // Update is called once per frame
    void Update()
    {
        // Get the direction to the target object
        Vector3 direction = target.position - transform.position;

        // Check if the object is within stopping distance
        if (direction.magnitude <= stoppingDistance)
        {
            return;
        }

        //if(!Idle)
        //{
            // Rotate the object towards the target object
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), (rotationSpeed * 3f) * Time.deltaTime);

            // Move the object towards the target object
            transform.position += transform.forward * (speed * 3f) * Time.deltaTime;
        //}
        //else
        //{
            // Rotate the object towards the target object
           // transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), rotationSpeed * Time.deltaTime);

            // Move the object towards the target object
           // transform.position += transform.forward * speed * Time.deltaTime;
        //}

    }
}
