using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DoggieWalk : MonoBehaviour
{
    bool dogOnTheMove = false;
    public bool IdleDoggie = false;
    public bool goNow = true;

    Animator DoggieAnimator;

    public GameObject targetObj;

    private Transform target;

    //float dist = 1f;

    public float speed = 5f;
    public float rotationSpeed = 5f;
    public float stoppingDistance = 1f;

    void Start()
    {
        target = targetObj.transform;

        DoggieAnimator = transform.GetChild(0).transform.GetChild(0).gameObject.GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 direction = target.position - transform.localPosition;

        if (direction.magnitude <= stoppingDistance)
        {
            targetObj.GetComponent<BasicMousePosition>().IdleWait = false;
            
            if (dogOnTheMove)
            {
                DoggieArrives();
                //targetObj.GetComponent<BasicMousePosition>().ToRandomPosition();
            }
            return;
        }
        else
        {
            SetAnimatorBools(false, false, false);
            dogOnTheMove = true;
        }

        if (goNow)
        {
            if (!IdleDoggie)
            {            
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), rotationSpeed * Time.deltaTime);
                transform.position += transform.forward * speed * Time.deltaTime;

                SetAnimatorBools(false, true, false);
            
            }
            else
            {            
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), (rotationSpeed/3f) * Time.deltaTime);
                transform.position += transform.forward * (speed/3f) * Time.deltaTime;

                SetAnimatorBools(true, false, false);

            }
        }
        else
        {
            SetAnimatorBools(true, false, false);
        }
    }

    void SetAnimatorBools(bool Walk, bool Run, bool Bark)
    {
        DoggieAnimator.SetBool("Run", Run);
        DoggieAnimator.SetBool("Walk", Walk);
        DoggieAnimator.SetBool("Bark", Bark);
    }

    void DoggieArrives()
    {
        goNow = false;
        SetAnimatorBools(false, false, true);
        dogOnTheMove = false;
        //targetObj.GetComponent<BasicMousePosition>().KickOffTimer();
        targetObj.GetComponent<BasicMousePosition>().ToRandomPosition();
    }
    /*
    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            transform.LookAt(target);

            Debug.Log(dist);

            dist = Vector3.Distance(transform.position, target.position);
        }
        else if(dist>0.1f)
        {
            dist = Vector3.Distance(transform.position, target.position);

            speed = Time.deltaTime + (dist * 0.005f);

            //transform.Translate(Vector3.forward * speed);

            transform.position = Vector3.MoveTowards(transform.position, target.position, speed);

            Debug.Log("speed"+speed);
        }
    }*/
}
