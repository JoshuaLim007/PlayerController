using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Limworks.PlayerController
{
    public class PlayerController : MonoBehaviour
    {
        public LayerMask CollisionMask = 0b1;
        public Vector3 Gravity = Vector3.down * 9.81f;
        public Vector3 FootPosition_local;
        public Vector3 HeadPosition_local;
        public float CollisionQueryRadius = 10.0f;

        public Camera Camera;
        public Collider Collider;

        public float AirControl = 0.25f;
        public float AirControlTime = 1.0f;
        public float CameraSensitivity = 15.0f;
        public float MovementSpeed = 15;
        public float MovementAcceleration = 100;
        public float JumpStrength = 500;
        public float GroundingDistance = 0.125f;
        public float DragCoeff = 0.95f;
        public float BreakingForce = 10.0f;
        public float SlippingBreakingForce = 25.0f;
        public float JumpTimeBuffer = 0.25f;
        public float TimeSinceLastGrounding = 0.0f;
        public float MaxSlopeAngle = 70.0f;
        public int JumpsMade = 0;
        public int MaxJumps = 1;

        public Vector3 Velocity;
        public Vector3 InputDirection;

        Transform GravityTransform;
        Transform AnchorPoint;

        public delegate void ParamsAction(object[] arguments);
        class DebugGizmo
        {
            public ParamsAction function;
            public List<object> args;
            public DebugGizmo(ParamsAction function, params object[] args)
            {
                this.function = function;
                this.args = new List<object>();
                if (args == null)
                {
                    return;
                }
                this.args.AddRange(args);
            }
            public void DoGizmo()
            {
                function.Invoke(args.ToArray());
            }
        }
        static List<DebugGizmo> DebugGizmoQueue;
        static void QueueGizmo(ParamsAction function, params object[] args)
        {
            DebugGizmoQueue.Add(new DebugGizmo(function, args));
        }
        static void ClearGizmos()
        {
            DebugGizmoQueue.Clear();
        }

        // Start is called before the first frame update
        void Start()
        {
            DebugGizmoQueue = new List<DebugGizmo>();
            AnchorPoint = new GameObject().transform;
            AnchorPoint.name = "PlayerAnchorPoint_runtime";

            GravityTransform = new GameObject().transform;
            GravityTransform.name = "PlayerGravityTransform_runtime";
            GravityTransform.transform.position = transform.position;
            if (transform.parent != null)
            {
                GravityTransform.SetParent(transform.parent);
            }
            transform.parent = GravityTransform;
            AnchorPoint.transform.position = transform.position;

            //FootPosition = transform.TransformPoint(FootPosition_local);
            //HeadPosition = transform.TransformPoint(HeadPosition_local);
            //var pHeight = Vector3.Distance(FootPosition, HeadPosition);
            //var pCenter = FootPosition + transform.up * pHeight * 0.5f;
            //var colCenter_local = transform.InverseTransformPoint(pCenter);
            //Collider.center = colCenter_local;
            //Collider.height = transform.InverseTransformVector(new Vector3(0, pHeight, 0)).magnitude;
        }

        static Vector3 GetCameraControlDelta(float cameraSensitivity)
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            return new Vector3(-mouseY * cameraSensitivity, mouseX * cameraSensitivity, 0);
        }
        static Vector3 GetMovementInput()
        {
            Vector3 InputDirection = Vector3.zero;
            if (Input.GetKey(KeyCode.W))
            {
                InputDirection += Vector3.forward;
            }
            if (Input.GetKey(KeyCode.D))
            {
                InputDirection += Vector3.right;
            }
            if (Input.GetKey(KeyCode.A))
            {
                InputDirection -= Vector3.right;
            }
            if (Input.GetKey(KeyCode.S))
            {
                InputDirection -= Vector3.forward;
            }
            if (Input.GetKeyDown(KeyCode.Space))
            {
                InputDirection += Vector3.up;
            }
            return InputDirection;
        }

        float cameraXRotation = 0;
        float jump = 0;

        // Update is called once per frame
        void Update()
        {
            GravityTransform.up = -Gravity.normalized;
            
            var cameraDelta = GetCameraControlDelta(CameraSensitivity);

            {
                cameraXRotation += cameraDelta.x;
                cameraXRotation = Mathf.Clamp(cameraXRotation, -80, 80);
                var temp = Camera.transform.localEulerAngles;
                temp.x = cameraXRotation;
                Camera.transform.localEulerAngles = temp;
            }
            
            transform.localEulerAngles += Vector3.up * cameraDelta.y;
            
            InputDirection = GetMovementInput();
            if(jump == 0)
            {
                jump = InputDirection.y;
            }
            InputDirection.y = 0;

            if (Input.GetKey(KeyCode.V))
            {
                Time.timeScale = 0.25f;
            }
            else
            {
                Time.timeScale = 1.0f;
            }
        }

        static Vector3 ApplyCapsuleCollision(Transform transform, Vector3 up, CapsuleCollider collider, Vector3 previousPosition, Vector3 currentPosition, LayerMask layerMask)
        {
            var t0 = transform.TransformPoint(collider.center);
            var h0 = transform.TransformVector(new Vector3(0, collider.height, 0)).magnitude;

            var p0 = t0 + up * h0 * 0.5f;
            var p1 = t0 - up * h0 * 0.5f;

            var rad = collider.radius;
            var dir = currentPosition - previousPosition;
            var dirn = dir.normalized;

            Debug.DrawLine(p0, p1, Color.green);

            p0 -= dir;
            p1 -= dir;
            const float padding = 0.1f;
            float mag = dir.magnitude;
            var hit = Physics.CapsuleCast(p0, p1, rad, dir.normalized, out RaycastHit dhit, mag + padding, layerMask);
            if (hit)
            {
                if(dhit.distance <= mag)
                {
                    return previousPosition + dirn * dhit.distance;
                }
            }
            return currentPosition;
        }

        static Vector3 ApplyCollisionPhysics(Vector3 position, Quaternion rotation, 
            ref Vector3 velocity, Vector3[] forces, Collider collider, 
            float collisionQueryRadius, LayerMask layerMask, out bool hit, int iterations = 1)
        {
            hit = false;
            Collider[] hits = null;
            bool hasForces = forces != null;
            for (int i = 0; i < iterations; i++)
            {
                int length;
                if(i == 0)
                {
                    hits = Physics.OverlapSphere(position, collisionQueryRadius, layerMask);
                    length = hits.Length;
                }
                else
                {
                    length = Physics.OverlapSphereNonAlloc(position, collisionQueryRadius, hits, layerMask);
                }

                for (int j = 0; j < length; j++)
                {
                    var item = hits[j];
                    bool intersecting = Physics.ComputePenetration(
                    collider, position, rotation, item,
                    item.transform.position, item.transform.rotation,
                    out var Dir, out var Dist);

                    if (intersecting)
                    {
                        var dot = -Vector3.Dot(velocity, Dir);

                        var velocityOffset = Dir * dot;
                        velocity += velocityOffset;
                        hit = true;
                        if (hasForces)
                        {
                            for (int x = 0; x < forces.Length; x++)
                            {
                                dot = -Vector3.Dot(forces[x], Dir);
                                forces[x] += Dir * dot;
                            }
                        }
                        position += Dir * Dist;
                    }
                }
            }
            return position;
        }
        
        static Vector3 GetDragForce(Vector3 velocity, float dragCoeff)
        {
            var tempVel = velocity;

            float forceMag = velocity.sqrMagnitude * dragCoeff;

            Vector3 dragForce = -velocity.normalized * forceMag;

            return dragForce;
        }
        
        static Vector3 ApplyBreaks(Vector3 velocity, float breakingForce)
        {
            var tempVel = velocity;
            velocity -= velocity.normalized * breakingForce * Time.fixedDeltaTime;
            if(Vector3.Dot(tempVel, velocity) < 0)
            {
                return Vector3.zero;
            }
            return velocity;
        }
        
        static bool GroundCollision(ref Vector3 position, Vector3 velocity, 
            Vector3 rayOrigin, Vector3 up, float radius, Vector3 footPosition, 
            LayerMask layerMask, out RaycastHit hit)
        {
            float playerHeight = Vector3.Distance(rayOrigin, footPosition);
            var downSpeed = Vector3.Dot(velocity, -up);

            const float padding = 0.01f;

            float sphereRadius = 0;// radius;
            float downSpeedTime = downSpeed * Time.fixedDeltaTime;
            float minRayDistance = playerHeight + padding + downSpeedTime - sphereRadius;

            Vector3 offset = Vector3.zero;

            var isHit = Physics.Raycast(rayOrigin, - up, out hit, minRayDistance, layerMask);
            if (isHit)
            {
                //float off = downSpeedTime;// Mathf.Min(downSpeedTime, 0);

                //find exact distance to ground from foot
                float diff = playerHeight - (hit.distance - downSpeedTime + sphereRadius);

                var projectedPos = position + Vector3.Project(velocity, hit.normal) * Time.fixedDeltaTime;
                var posDiff = projectedPos - position;
                
                //subtract the projected vertical position amount from position offset
                var projHeight = Vector3.Dot(posDiff, up);
                diff += Mathf.Min(projHeight, 0);

                offset += up * diff;
                position += offset;
            }

            return isHit;
        }
       
        struct GroundingInfo
        {
            public bool grounded;
            public float groundAngle;
            public Vector3 groundNormal;
            public RaycastHit rayData;
        }
        GroundingInfo groundingInfo;
        Vector3 FootPosition;
        Vector3 HeadPosition;
        Vector3 lastPosition;
        Vector3 groundDrag;
        Vector3 anchorPointVelocity = Vector3.zero;
        bool isColliding = false;

        public float AirMovementTimer = 0;
        public float CurrentMovingSpeed = 0;

        void FixedUpdate()
        {
            anchorPointVelocity = (AnchorPoint.transform.position - transform.position) / Time.fixedDeltaTime;
            transform.position = AnchorPoint.transform.position;

            ClearGizmos();
            FootPosition = transform.TransformPoint(FootPosition_local);
            HeadPosition = transform.TransformPoint(HeadPosition_local);

            //input direction should always be perpendicular to gravity direction
            var corrected_inputDirection = transform.TransformVector(InputDirection);
            corrected_inputDirection = Vector3.ClampMagnitude(corrected_inputDirection, 1.0f);
            var jumpDirection = transform.TransformVector(Vector3.up);

            var targetForce = ((MovementSpeed * MovementSpeed) * DragCoeff);
            MovementAcceleration = targetForce;

            //calcualte input force
            Vector3 inputForce = MovementAcceleration * corrected_inputDirection;
            var gravity = Gravity;

            //jumping
            Vector3 jumpVelocityOffset = Vector3.zero;
            if (JumpsMade == 0)
            {
                if (jump == 1.0f && (groundingInfo.grounded || TimeSinceLastGrounding < JumpTimeBuffer))
                {
                    JumpsMade++;
                    jump = 1.0f;
                    AirMovementTimer = 0;

                    //we want to make jumps go higher on slopes
                    float upwardsSpeedOffset = 0;
                    if (groundingInfo.grounded)
                    {
                        var tempGroundedVel = Vector3.ProjectOnPlane(Velocity, groundingInfo.groundNormal);
                        upwardsSpeedOffset = Vector3.Dot(tempGroundedVel, transform.up);
                    }

                    Velocity = Vector3.ProjectOnPlane(Velocity, gravity.normalized);

                    jumpVelocityOffset = jumpDirection * (JumpStrength + upwardsSpeedOffset);
                    Velocity += jumpVelocityOffset;
                    TimeSinceLastGrounding += JumpTimeBuffer;
                }
            }
            else
            {
                if (JumpsMade < MaxJumps && jump == 1.0f)
                {
                    jumpVelocityOffset = jumpDirection * JumpStrength;
                    Velocity += jumpVelocityOffset;
                    JumpsMade++;
                }
            }

            ////step forward and do predictive collision
            //{
            //    var forces = new Vector3[2];
            //    forces[0] = gravity;
            //    forces[1] = inputForce;
            //    var tempVelocity = Velocity + (gravity + inputForce) * Time.fixedDeltaTime;
            //    var tempPosition = transform.position + tempVelocity * Time.fixedDeltaTime;
            //    //perform physics collision
            //    ApplyCollisionPhysics(tempPosition, transform.rotation, ref tempVelocity, forces,
            //        Collider, CollisionQueryRadius, CollisionMask, 1);
            //    gravity = forces[0];
            //    inputForce = forces[1];
            //}

            //calculate net input force
            float slopeDot = (groundingInfo.grounded ? Vector3.Dot(groundingInfo.groundNormal, transform.up) : 1);
            slopeDot = Mathf.Pow(slopeDot, 1);
            inputForce *= slopeDot;
            Vector3 appliedForces = gravity + inputForce;

            bool aboveMaxAngle = groundingInfo.groundAngle >= MaxSlopeAngle;
            bool aboveMaxSpeed = CurrentMovingSpeed >= (MovementSpeed + 1);
            bool applyNormalBreaksAndDrag = !aboveMaxSpeed && !aboveMaxAngle;

            //if were not grounded, either because we fell or jumped, we doint want to apply input forces
            //if (!groundingInfo.grounded)
            //{
            //    appliedForces -= inputForce;
            //}

            //if even after applying jump, we are projected to be on the ground, don't apply jump velocity
            //else if (jump == 1.0f)
            //{
            //    JumpsMade--;
            //    Velocity -= jumpVelocityOffset;
            //}

            //only apply drag if we are under target movement speed
            
            //apply net force
            Velocity += appliedForces * Time.fixedDeltaTime;

            //do ground drag
            if (groundingInfo.grounded)
            {
                var preVel = Velocity;
                Velocity += groundDrag * Time.fixedDeltaTime;
                float dot = Vector3.Dot(Velocity, preVel);
                if(dot < 0)
                {
                    Velocity = Vector3.zero;
                }
            }

            var position = transform.position;
            if(GroundCollision(ref position, Velocity, HeadPosition, transform.up, 0.25f, FootPosition, CollisionMask, out RaycastHit hit))
            {
                groundingInfo.grounded = true;
                groundingInfo.groundNormal = hit.normal;
                groundingInfo.groundAngle = Vector3.Angle(hit.normal, transform.up);
                groundingInfo.rayData = hit;
                transform.position = position;
            }
            else
            {
                groundingInfo.grounded = false;
                TimeSinceLastGrounding += Time.fixedDeltaTime;
            }

            if (groundingInfo.grounded)
            {
                //projecto ground
                Velocity = Vector3.ProjectOnPlane(Velocity, groundingInfo.groundNormal);

                //apply breaks if we are not slipping and under our desired movement speed
                if (applyNormalBreaksAndDrag)
                {
                    if (InputDirection == Vector3.zero && jump == 0)
                    {
                        Velocity = ApplyBreaks(Velocity, BreakingForce);
                    }
                    groundDrag = GetDragForce(Velocity, DragCoeff);
                    JumpsMade = 0;
                    AirMovementTimer = 0;
                    TimeSinceLastGrounding = 0.0f;
                }
                else
                {
                    Velocity -= inputForce * Time.fixedDeltaTime;
                    Velocity = ApplyBreaks(Velocity, SlippingBreakingForce * (aboveMaxAngle ? 0 : 1));
                    groundDrag = Vector3.zero;
                    TimeSinceLastGrounding += Time.fixedDeltaTime;
                }

                //after applying forces, project again
                Velocity = Vector3.ProjectOnPlane(Velocity, groundingInfo.groundNormal);
            }

            Debug.DrawRay(FootPosition, hit.normal * 10, Color.blue);
            Debug.DrawRay(transform.position, Velocity.normalized * 10);

            if (!groundingInfo.grounded || !applyNormalBreaksAndDrag)
            {

                //this is for when the grounding ray cannot detect the ground because of collision with body
                if (!groundingInfo.grounded && isColliding)
                {
                    if(Physics.SphereCast(transform.position, 0.25f, inputForce.normalized, out RaycastHit dhit, CollisionQueryRadius - 0.25f, CollisionMask))
                    {
                        Velocity -= inputForce * Time.fixedDeltaTime;
                        inputForce = Vector3.ProjectOnPlane(inputForce, dhit.normal);
                        inputForce = Vector3.ProjectOnPlane(inputForce, transform.up);
                        Velocity += inputForce * Time.fixedDeltaTime;
                    }
                }

                groundDrag = Vector3.zero;

                AirMovementTimer += Time.deltaTime;
                AirMovementTimer = Mathf.Clamp(AirMovementTimer, 0, AirMovementTimer);

                if (!groundingInfo.grounded)
                {
                    Velocity -= inputForce * Time.fixedDeltaTime;
                }
                
                if(aboveMaxAngle && groundingInfo.grounded)
                {
                    var dirDot = Vector3.Dot(inputForce.normalized, groundingInfo.groundNormal);
                    if(dirDot < 0)
                    {
                        inputForce = Vector3.zero;
                    }
                }

                var verticalSpeed = Vector3.Dot(Velocity, transform.up);

                var horizontalVelocity = Vector3.ProjectOnPlane(Velocity, transform.up);

                float scaler = 1 - Mathf.InverseLerp(0, AirControlTime, AirMovementTimer);

                Vector3 airForce = inputForce * Mathf.Pow(scaler, 0.5f);

                float moveDot = CurrentMovingSpeed > MovementSpeed ? (1 - Vector3.Dot(horizontalVelocity.normalized, airForce.normalized)) * 0.5f : 1.0f;
                
                horizontalVelocity += airForce * Time.fixedDeltaTime * moveDot;

                horizontalVelocity += transform.up * verticalSpeed;

                Velocity = Vector3.Lerp(Velocity, horizontalVelocity, AirControl);
            }

            //apply velocity to position
            lastPosition = transform.position;
            transform.position = lastPosition + Velocity * Time.fixedDeltaTime;

            //perform physics collision
            transform.position = ApplyCollisionPhysics(transform.position, transform.rotation, ref Velocity, null,
                Collider, CollisionQueryRadius, CollisionMask, out isColliding, 8);

            if (groundingInfo.grounded)
            {
                AnchorPoint.transform.SetParent(groundingInfo.rayData.transform, true);
            }
            else
            {
                Velocity += anchorPointVelocity;
                anchorPointVelocity = Vector3.zero;
                AnchorPoint.transform.SetParent(null, true);
            }
            AnchorPoint.transform.position = transform.position;

            CurrentMovingSpeed = Vector3.Distance(lastPosition, transform.position) / Time.fixedDeltaTime;

            //after jumping reset jump
            jump = 0;
        }
        private void OnDisable()
        {
            ClearGizmos();
        }
        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(FootPosition_local, 0.1f);
            Gizmos.DrawWireSphere(HeadPosition_local, 0.1f);
            Gizmos.matrix = Matrix4x4.identity;
            //Gizmos.DrawRay(transform.position, Gravity);
            Gizmos.color = new Color(0, 0, 1, 0.25f);
            Gizmos.DrawWireSphere(transform.position, CollisionQueryRadius);

            if (DebugGizmoQueue != null)
            {
                foreach (var item in DebugGizmoQueue)
                {
                    item.DoGizmo();
                }
            }
        }
    }

}
