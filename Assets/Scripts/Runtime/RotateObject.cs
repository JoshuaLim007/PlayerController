using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Limworks.PlayerController
{
    public class RotateObject : MonoBehaviour
    {
        public float speed = 10.0f;
        // Start is called before the first frame update
        void Start()
        {
        
        }
        float angle = 0;
        // Update is called once per frame
        void Update()
        {
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, angle, transform.eulerAngles.z);
            angle += Time.deltaTime * speed;
        }
    }
}
