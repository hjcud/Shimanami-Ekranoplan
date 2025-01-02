
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class AirplaneState : UdonSharpBehaviour
{
    // Other Scripts
    public Throttle_Controll Throttle_Controll;
    public Controller_Controll Controller_Controll;

    public Transform MapRotation = null;
    public Transform MapRotationTarget = null;
    public Transform DebugPlaneTrans = null;

    //Debug
    public LineRenderer DebugLine_1 = null;
    public LineRenderer DebugLine_2 = null;
    public LineRenderer DebugLine_3 = null;
    public Text DebugText;

    //VectorMultiply
    public float ThrottleVecMulti = 10f;
    public float PitchThrustVecMulti = 0.5f;
    public float YawThrustVecMulti = 0.5f;
    public float RollThrustVecMulti = 1.0f;
    public float LiftMulti = 0.05f;

    // Airplane Transform
    [Tooltip("Current velocity vector")]
    [UdonSynced] public Vector3 airplaneVelocity = Vector3.zero;
    [UdonSynced] public Vector3 PrevFrameVelocity = Vector3.zero;
    [Tooltip("Rotation in Euler angles")]
    [UdonSynced] public Vector3 airplaneRotation = Vector3.zero;
    [Tooltip("Rotation in World rotation")]
    [UdonSynced] public Vector3 worldRotation = Vector3.zero;

    [UdonSynced] public float AirplaneSpeed = 0;
    [UdonSynced] public Vector3 lift = Vector3.zero;
    [UdonSynced] public Vector3 drag = Vector3.zero;
    //[UdonSynced] public Vector3 forwardForce = Vector3.zero;
    //[UdonSynced] public Vector3 acceleration = Vector3.zero;

    // Airplane Properties
    private float mass = 286000f;
    private float dragCoefficient = 0.8f;
    private float liftCoefficient = 0.01f;
    private float dragFactor = 49f; // 1/2 * Air Density(1.225) * Surface Area(80 m^2)
    
    public void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        CalculateVelocity(dt);
    }

    public void CalculateVelocity(float dt)
    {
        // Get values form other scripts
        float throttle = Throttle_Controll.throttlePower;   // -0.3 ~ 1.25
        float YawThrustVec = -Controller_Controll.yaw;      // -0.5 ~ 0.5 (-L / +R)
        float PitchThrustVec = Controller_Controll.pitch;   // -0.5 ~ 0.5 (-D / +U)
        float RollThrustVec = Controller_Controll.roll;     // -0.5 ~ 0.5 (-L / +R)

        // ===== CODE FOR DEBUG =====
        // Calculate airplane velocity
        Vector3 direction = new Vector3(YawThrustVec, PitchThrustVec, 1f).normalized;
        Quaternion rotation = Quaternion.AngleAxis(RollThrustVec, Vector3.forward);
        airplaneVelocity = rotation * direction * throttle;
        // Draw line to visuallize vector(velocity)
        DebugLine_1.SetPosition(1, (airplaneVelocity * 0.25f) + DebugLine_1.GetPosition(0));
        DebugLine_2.SetPosition(1, Vector3.forward * 0.25f + DebugLine_2.GetPosition(0));
        /// ===== END DEBUG =====

        UpdateState(dt, YawThrustVec, PitchThrustVec, RollThrustVec, throttle);
        RequestSerialization();
    }

    private void UpdateState(float dt, float YawThrustVec, float PitchThrustVec, float RollThrustVec, float throttle)
    {
        Vector3 RotationVector = Vector3.zero;
        //Rotation Vector Multiply
        RotationVector.x = PitchThrustVec * PitchThrustVecMulti;
        RotationVector.y = -YawThrustVec * YawThrustVecMulti;
        RotationVector.z = RollThrustVec * RollThrustVecMulti;

        //Calculate:: Acceleration
        //a = F/m * Output Ratio (F = Power of each engine * Number of engines) = (101920 * 8) /286000
        float acceleration = throttle * 101920f * 8f;

        //Calculate:: Drag by Speed
        //DragForce = 1/2*pv^2AC = v^2 * 0.5(1/2) * air density(p: 1.225) * surface area(A: 80) * drag coefficient(C: 0.04) / mass of an airplane (286ton = 286000kg) = v^2 * 1.96/286000
        float SpeedDrag = Mathf.Pow(AirplaneSpeed, 2f) * 1.96f;

        //Calculate:: Speed 
        AirplaneSpeed += ((acceleration * 0.581f) - SpeedDrag) / 286000 * ThrottleVecMulti * dt;
        
        //Check if Airplane is looking down
        //float dotRotation = Vector3.Dot(airplaneRotation, Vector3.forward);
        //float LiftFacingMulti = (dotRotation < 0 ? 0.8f : 1f);

        //Calculate:: Lift
        float AirplaneLift = AirplaneSpeed * AirplaneSpeed * 0.000001f * LiftMulti;// * LiftFacingMulti;
        
        MapRotationTarget.Rotate(RotationVector, Space.World);
        // Rotation Limit (15Deg, Only Pitch and Roll)
        Vector3 clampedEulerAngles = MapRotationTarget.eulerAngles;
        clampedEulerAngles.x = Mathf.Clamp(clampedEulerAngles.x > 180f ? clampedEulerAngles.x - 360f : clampedEulerAngles.x, -15f, 15f);
        clampedEulerAngles.z = Mathf.Clamp(clampedEulerAngles.z > 180f ? clampedEulerAngles.z - 360f : clampedEulerAngles.z, -15f, 15f);
        MapRotationTarget.eulerAngles = clampedEulerAngles;

        // =====
        Vector3 adjustedForward = Quaternion.Euler(1f, 0f, 1f) * MapRotationTarget.forward;
        float GroundAngle = (1 - Vector3.Dot(adjustedForward, Vector3.forward)) * 100;
        DebugLine_3.SetPosition(1, adjustedForward * 0.25f + DebugLine_3.GetPosition(0));

        MapRotation.eulerAngles = MapRotationTarget.eulerAngles;
        DebugPlaneTrans.eulerAngles = MapRotationTarget.eulerAngles; //For Debugging
        MapRotation.position += new Vector3(0f, -AirplaneLift, 0f);
        DebugPlaneTrans.position += new Vector3(0f, -AirplaneLift * 0.001f, 0f); //For Debugging

        DebugText.text = ">>Controll\nSpeed: " + AirplaneSpeed.ToString() + "\nLift: " + AirplaneLift.ToString() + "\nFacing: " + GroundAngle.ToString();
    }

    public void UpdateWorldCordinate()
    {

    }

    public void PitchLimitAlarm()
    {
        // Pitch 15도 제한 걸리면 발생하는 이벤트
    }

    public void YawLimitAlarm()
    {
        // Yaw 15도 제한 걸리면 발생하는 이벤트
    }
}
