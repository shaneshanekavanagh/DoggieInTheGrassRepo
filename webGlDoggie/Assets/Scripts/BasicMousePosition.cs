using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BasicMousePosition : MonoBehaviour
{
    public GameObject Doggie;

    public GameObject Butterfly;

    public bool IdleWait = true;

    Vector3 worldPosition;
    // Update is called once per frame
    IEnumerator KickOffTemp;

    private void Start()
    {
        ToRandomPosition();
    }

    void Update()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        RaycastHit hitData;

        if (Physics.Raycast(ray, out hitData, 1000))
        {
            if (Input.GetMouseButtonUp(0))
            {
                IdleWait = true;
                transform.position = hitData.point;
                Doggie.GetComponent<DoggieWalk>().IdleDoggie = false;
                Doggie.GetComponent<DoggieWalk>().goNow = true;
                //Butterfly.GetComponent<ButterflyMovementScript>().Idle = false;
                KickOffTimer();
                //ToRandomPosition();
            }
        }
    }

    public void KickOffTimer()
    {
        if(KickOffTemp != null)
        {
            StopCoroutine(KickOffTemp);
        }
        KickOffTemp = TimedRandomIdle();
        StartCoroutine(KickOffTemp);
        
    }

    IEnumerator TimedRandomIdle()
    {
        

        while (IdleWait)
        {
            yield return null;
        }

        float timeTillRandom = Random.Range(1f, 5f);
        
        yield return new WaitForSeconds(timeTillRandom);

        Doggie.GetComponent<DoggieWalk>().goNow = true;

        ToRandomPosition();
        yield break;
    }

    public void ToRandomPosition()
    {
        transform.position = new Vector3((Random.Range(-10f,10f)),0, (Random.Range(-1f, 20f)));
        Doggie.GetComponent<DoggieWalk>().IdleDoggie = true;
        //Butterfly.GetComponent<ButterflyMovementScript>().Idle = true;

        KickOffTimer();
    }
}
