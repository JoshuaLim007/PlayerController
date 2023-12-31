using PlasticGui.Help.Conditions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEngine.GraphicsBuffer;

namespace Limworks.PlayerController
{
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerController : MonoBehaviour
    {
        public Camera Camera;
        public CapsuleCollider Collider;
        public PhysicMaterial PhysicMaterial;

        [Header("Exposed fields")]
        public LayerMask CollisionMask = 0b1;
        public Vector3 Gravity = Vector3.down * 9.81f;
        public Vector3 FootPosition_local = -Vector3.up;
        public Vector3 HeadPosition_local = Vector3.up;
        public float CollisionQueryRadius = 10.0f;
        public float AirControl = 0.25f;
        public float AirControlTime = 1.0f;
        public float CameraSensitivity = 15.0f;
        public float MovementSpeed = 15;
        public float JumpStrength = 5;
        public float DragCoeff = 0.95f;
        public float BreakingForce = 10.0f;
        public float SlippingBreakingForce = 25.0f;
        public float JumpTimeBuffer = 0.25f;
        public float MaxSlopeAngle = 70.0f;
        public float MaxStepHeight = 0.25f;
        public int MaxJumps = 1;

        [Header("Read only fields")]
        [SerializeField] float MovementAcceleration = 100;
        [SerializeField] Vector3 Velocity = Vector3.zero;

        public GroundingInfo GroundedData => groundingInfo;

        float TimeSinceLastGrounding = 0.0f;
        int JumpsMade = 0;
        Transform GravityTransform;
        Transform AnchorPoint;
        Rigidbody rigidbody1;
        bool hasrgb => rigidbody1 != null && !rigidbody1.isKinematic;

        delegate void ParamsAction(object[] arguments);
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

        bool init = false;
        //Vector3 originalColliderCenter;
        //float originalColliderHeight;
        // Start is called before the first frame update
        void Start()
        {
            rigidbody1 = GetComponent<Rigidbody>();
            if (hasrgb)
            {
                rigidbody1.useGravity = false;
                rigidbody1.freezeRotation = true;
                Collider.material = PhysicMaterial;
                rigidbody1.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody1.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                projectedRgbPosition = rigidbody1.position;
            }

            Collider = GetComponent<CapsuleCollider>();
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


            init = true;

            FootPosition = transform.TransformPoint(FootPosition_local);
            HeadPosition = transform.TransformPoint(HeadPosition_local);
            var pHeight = Vector3.Distance(FootPosition, HeadPosition);
            var pCenter = FootPosition + transform.up * pHeight * 0.5f;
            var colCenter_local = transform.InverseTransformPoint(pCenter);
            Collider.center = colCenter_local;
            Collider.height = transform.InverseTransformVector(new Vector3(0, pHeight, 0)).magnitude;
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

        bool flipGravity = false;
        float cameraXRotation = 0;
        // Update is called once per frame
        void Update()
        {
            GravityTransform.up = -Gravity.normalized;

            //INPUT
            if (Input.GetKeyDown(KeyCode.T))
            {
                flipGravity = !flipGravity;
            }

            if (flipGravity)
            {
                var target = Vector3.RotateTowards(Gravity, new Vector3(0, 9.81f, 0), 15f* Time.deltaTime, 0);
                SetGravity(target);
            }
            else
            {
                var target = Vector3.RotateTowards(Gravity, new Vector3(0, -9.81f, 0), 15f * Time.deltaTime, 0);
                SetGravity(target);
            }

            var cameraDelta = GetCameraControlDelta(CameraSensitivity);
            {
                cameraXRotation += cameraDelta.x;
                cameraXRotation = Mathf.Clamp(cameraXRotation, -80, 80);
                var temp = Camera.transform.localEulerAngles;
                temp.x = cameraXRotation;
                Camera.transform.localEulerAngles = temp;
            }
            transform.localEulerAngles += Vector3.up * cameraDelta.y;
            var InputDirection = GetMovementInput();
            if(jump == 0 && InputDirection.y == 1)
            {
                Jump();
            }
            SetInputForce(InputDirection, MovementSpeed);
            //
        }

        Vector3 inputForce = Vector3.zero;
        Vector3 totalExternalForces = Vector3.zero;
        Vector3 finalForce = Vector3.zero;
        float jump = 0;
        public void SetInputForce(Vector3 direction, float speed)
        {
            MovementSpeed = speed;

            var targetForce = ((MovementSpeed * MovementSpeed) * DragCoeff);
            MovementAcceleration = targetForce;

            direction.y = 0;
            var corrected_inputDirection = transform.TransformVector(direction);
            corrected_inputDirection = Vector3.ClampMagnitude(corrected_inputDirection, 1.0f);
            inputForce = corrected_inputDirection * MovementAcceleration;
        }
        public Vector3 position
        {
            get
            {
                if (hasrgb)
                {
                    return rigidbody1.position;
                }
                else
                {
                    return transform.position;
                }
            }
            set
            {
                if (hasrgb)
                {
                    rigidbody1.position = value;
                }
                else
                {
                    transform.position = value;
                }
            }
        }
        public void SetGravity(Vector3 gravity)
        {
            var originalPosition = transform.position;
            GravityTransform.up = -gravity.normalized;
            transform.position = originalPosition;
            Gravity = gravity;
        }
        public Vector3 GetVelocity()
        {
            return Velocity;
        }
        public void AddVelocity(Vector3 velocity)
        {
            Velocity += velocity;
        }
        public void SetVelocity(Vector3 velocity)
        {
            Velocity = velocity;
        }
        public Vector3 GetInputForce()
        {
            return inputForce;
        }
        public void Jump()
        {
            jump = 1;
        }
        public void AddPersistentForce(Vector3 force)
        {
            totalExternalForces += force;
        }
        public void SetPersistentForce(Vector3 force)
        {
            totalExternalForces = force;
        }
        public Vector3 GetPersistentForce()
        {
            return totalExternalForces;
        }
        public Vector3 GetFinalForce()
        {
            return finalForce;
        }
        public float GetMovingSpeed()
        {
            return CurrentMovingSpeed;
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
                        //var rgb = item.TryGetComponent(out Rigidbody rigidbody);
                        //if (rgb)
                        //{
                        //    var dir = (rigidbody.worldCenterOfMass - position).normalized;
                        //    var hisVel = rigidbody.velocity;
                        //    float m1 = 1.0f;
                        //    float m2 = rigidbody.mass;
                        //    var totalMass = m2 + m1;
                        //    var v1 = Vector3.Project(velocity, dir);
                        //    var v2 = Vector3.Project(hisVel, -dir);
                        //    var netVel = v1 + v2;
                        //    //perform ellastic collision
                        //    rigidbody.velocity = (2 * m1 / totalMass) * netVel  - ((m1 - m2) / totalMass) * v2;
                        //    velocity = ((m1 - m2) / totalMass) * netVel         + (2 * m2 / totalMass) * v2;
                        //}

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

                        //if (rgb)
                        //{
                        //    float m1 = 1.0f;
                        //    float m2 = rigidbody.mass;
                        //    var totalMass = m2 + m1;
                        //    position += Dir * Dist * m2 / totalMass;
                        //    rigidbody.position -= Dir * Dist * m1 / totalMass;
                        //}
                        //else
                        //{
                        //    position += Dir * Dist;
                        //}

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
            LayerMask layerMask, out RaycastHit hit, float maxStepHeight = 0.25f)
        {
            rayOrigin -= up * radius;

            float playerHeight = Vector3.Distance(rayOrigin, footPosition);
            var downSpeed = Vector3.Dot(velocity, -up);

            const float padding = 0.0125f;

            float sphereRadius = radius;
            float downSpeedTime = downSpeed * Time.fixedDeltaTime;
            float minRayDistance = playerHeight + padding + downSpeedTime - sphereRadius;

            Vector3 offset = Vector3.zero;

            var isHit = Physics.BoxCast(rayOrigin, Vector3.one * sphereRadius, -up, out hit, Quaternion.identity, minRayDistance, layerMask);
            
            QueueGizmo((a) =>
            {
                Gizmos.color = new Color(1, 1, 0, 0.25f);
                var origin = rayOrigin  + up * radius * 0.5f - up * playerHeight * 0.5f;
                var size = Vector3.one * sphereRadius * 2;
                size.y = minRayDistance * 2;
                Gizmos.DrawCube(origin, size);
            });

            if (isHit)
            {
                //float off = downSpeedTime;// Mathf.Min(downSpeedTime, 0);
                //find exact distance to ground from foot
                float diff = playerHeight - (hit.distance - downSpeedTime + sphereRadius);
                if (diff > maxStepHeight)
                {
                    isHit = false;
                }
                else
                {
                    hit.distance = diff;
                    var projectedPos = position + Vector3.Project(velocity, hit.normal) * Time.fixedDeltaTime;
                    var posDiff = projectedPos - position;

                    //subtract the projected vertical position amount from position offset
                    var projHeight = Vector3.Dot(posDiff, up);
                    diff += Mathf.Min(projHeight, 0);

                    offset += up * diff;
                    position += offset;
                }
            }

            return isHit;
        }

        public struct GroundingInfo
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
        float AirMovementTimer = 0;
        float CurrentMovingSpeed = 0;

        Vector3 projectedRgbPosition;
        Vector3 GetPosition()
        {
            if (hasrgb)
            {
                return projectedRgbPosition;
            }
            else
            {
                return transform.position;
            }
        }
        void SetPosition(Vector3 position)
        {
            if (hasrgb)
            {
                projectedRgbPosition = position;
            }
            else
            {
                transform.position = position;
            }
        }

        Vector3 initialRgbPos;
        void FixedUpdate()
        {
            transform.hasChanged = false;

            if (hasrgb)
            {
                initialRgbPos = GetPosition();
            }

            if (!hasrgb)
            {
                anchorPointVelocity = (AnchorPoint.transform.position - GetPosition()) / Time.fixedDeltaTime;
                SetPosition(AnchorPoint.transform.position);
            }

            ClearGizmos();
            FootPosition = transform.TransformPoint(FootPosition_local);
            HeadPosition = transform.TransformPoint(HeadPosition_local);

            //input direction should always be perpendicular to gravity direction
            //var corrected_inputDirection = transform.TransformVector(InputDirection);
            //corrected_inputDirection = Vector3.ClampMagnitude(corrected_inputDirection, 1.0f);

            var jumpDirection = transform.TransformVector(Vector3.up);

            var targetForce = ((MovementSpeed * MovementSpeed) * DragCoeff);
            MovementAcceleration = targetForce;

            //calcualte input force
            //Vector3 inputForce = MovementAcceleration * corrected_inputDirection;
            var gravity = Gravity;
            bool aboveMaxAngle = groundingInfo.groundAngle >= MaxSlopeAngle;
            bool aboveMaxSpeed = CurrentMovingSpeed >= (MovementSpeed * 1.2f);
            bool applyNormalBreaksAndDrag = !aboveMaxSpeed && !aboveMaxAngle;

            //jumping
            Vector3 jumpVelocityOffset = Vector3.zero;
            if (JumpsMade == 0)
            {
                if (jump == 1.0f && ((groundingInfo.grounded && !aboveMaxAngle) || TimeSinceLastGrounding < JumpTimeBuffer))
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
            Vector3 appliedForces = gravity + totalExternalForces + inputForce;

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
            finalForce = appliedForces;
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

            //ground collision
            var position = GetPosition();
            if (GroundCollision(ref position, Velocity, HeadPosition, transform.up, Collider.radius, FootPosition, CollisionMask, out RaycastHit hit, MaxStepHeight))
            {
                groundingInfo.grounded = true;
                groundingInfo.groundNormal = hit.normal;
                groundingInfo.groundAngle = Vector3.Angle(hit.normal, transform.up);
                groundingInfo.rayData = hit;
                SetPosition(position);
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
                    if (inputForce == Vector3.zero && jump == 0)
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

            //velocity management
            if (!groundingInfo.grounded || !applyNormalBreaksAndDrag)
            {
                //this is for when the grounding ray cannot detect the ground because of collision with body
                if (!groundingInfo.grounded && isColliding)
                {
                    if(Physics.SphereCast(GetPosition(), 0.25f, inputForce.normalized, out RaycastHit dhit, CollisionQueryRadius - 0.25f, CollisionMask))
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

            //void FillCollider(Vector3 foot, Vector3 top)
            //{
            //    const float padding = 0.1f;
            //    Vector3 paddedFootPos = (foot + transform.up * padding);
            //    Vector3 newCenter = (top + paddedFootPos) * 0.5f;
            //    float newHeight = Vector3.Distance(top, paddedFootPos);
            //    Collider.center = transform.InverseTransformPoint(newCenter);
            //    Collider.height = newHeight;
            //}
            //void RevertCollider()
            //{
            //    Collider.center = originalColliderCenter;
            //    Collider.height = originalColliderHeight;
            //}
            //void PerformWedgeCollision()
            //{
            //    var vel = Velocity * Time.fixedDeltaTime;
            //    var nextHead = HeadPosition + vel;
            //    Ray ray = new Ray(nextHead, transform.up);
            //    if (Physics.Raycast(ray, out RaycastHit hitinfo, float.MaxValue, CollisionMask))
            //    {
            //        var nextFoot = FootPosition + vel;
            //        Vector3 topCollider = transform.TransformPoint(Collider.center + Vector3.up * Collider.height * 0.5f) + vel;
            //        var topToFootDist = Vector3.Distance(topCollider, nextFoot);
            //        var hitDistance = Vector3.Distance(hitinfo.point, nextFoot);
            //        QueueGizmo((a) => { Gizmos.color = Color.white; });
            //        QueueGizmo((a) => { Gizmos.DrawSphere((Vector3)a[0], (float)a[1]); }, topCollider, 0.2f);
            //        QueueGizmo((a) => { Gizmos.color = new Color(1, 0, 1, 0.5f); });
            //        QueueGizmo((a) => { Gizmos.DrawWireSphere((Vector3)a[0], (float)a[1]); }, hitinfo.point, 0.25f);
            //        if (hitDistance <= topToFootDist + 0.1f)
            //        {
            //            FillCollider(nextFoot, topCollider);
            //        }
            //    }
            //}

            Debug.DrawRay(FootPosition, hit.normal * 10, Color.blue);
            Debug.DrawRay(transform.position, Velocity.normalized * 10);

            //apply velocity to position
            lastPosition = GetPosition();
            SetPosition(lastPosition + Velocity * Time.fixedDeltaTime);
            //perform physics collision

            if (!hasrgb)
            {
                SetPosition(ApplyCollisionPhysics(GetPosition(), transform.rotation, ref Velocity, null, Collider, CollisionQueryRadius, CollisionMask, out isColliding, 8));
            }

            //RevertCollider();
            //PerformWedgeCollision();

            else
            {
                var pos = ApplyCollisionPhysics(GetPosition(), transform.rotation, ref Velocity, null, Collider, CollisionQueryRadius, CollisionMask, out isColliding, 8);

                var wantedPosition = GetPosition();
                SetPosition(initialRgbPos);

                var velocity = (wantedPosition - initialRgbPos) / Time.fixedDeltaTime;
                rigidbody1.velocity = velocity;

                CurrentMovingSpeed = velocity.magnitude;
            }

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
            AnchorPoint.transform.position = GetPosition();

            if (!hasrgb)
            {
                CurrentMovingSpeed = Vector3.Distance(lastPosition, GetPosition()) / Time.fixedDeltaTime;
            }

            //reset inptus
            jump = 0;
            inputForce = Vector3.zero;
        }
        private void OnDisable()
        {
            ClearGizmos();
        }
        private void OnDrawGizmosSelected()
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
