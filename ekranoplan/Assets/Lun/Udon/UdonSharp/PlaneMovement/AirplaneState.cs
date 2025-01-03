
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

    public GameObject RollAlarm = null;
    public GameObject PitchAlarm = null;

    //Debug
    public LineRenderer DebugLine_1 = null;
    public LineRenderer DebugLine_2 = null;
    public LineRenderer DebugLine_3 = null;
    public Text DebugText;

    //VectorMultiply
    public float ThrottleVecMulti = 10f;
    public float PitchThrustVecMulti = 0.5f;
    public float YawThrustVecMulti = 0.25f;
    public float RollThrustVecMulti = 1.0f;
    public float LiftMulti = 0.5f;
    public float RotationAddMulti = 0.0005f;

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
        // ===== END DEBUG =====

        UpdateState(dt, YawThrustVec, PitchThrustVec, RollThrustVec, throttle);
        RequestSerialization();
    }

    private void UpdateState(float dt, float YawThrustVec, float PitchThrustVec, float RollThrustVec, float throttle)
    {
        //Calculate:: Acceleration
        //a = F/m * Output Ratio (F = Power of each engine * Number of engines) = (101920 * 8) /286000
        float acceleration = throttle * 101920f * 8f;

        //Calculate:: Drag by Speed
        //DragForce = 1/2*pv^2AC = v^2 * 0.5(1/2) * air density(p: 1.225) * surface area(A: 80) * drag coefficient(C: 0.04) / mass of an airplane (286ton = 286000kg) = v^2 * 1.96/286000
        float SpeedDrag = Mathf.Pow(AirplaneSpeed, 2f) * 1.96f;

        //Calculate:: Speed 
        AirplaneSpeed += ((acceleration * 0.581f) - SpeedDrag) / 286000 * ThrottleVecMulti * dt;
        
        //Set Rotation Vector and Multiply
        Vector3 RotationVector = Vector3.zero;
        RotationVector.x = PitchThrustVec * PitchThrustVecMulti;
        RotationVector.y = -YawThrustVec * YawThrustVecMulti;
        RotationVector.z = RollThrustVec * RollThrustVecMulti;
        MapRotationTarget.Rotate(RotationVector, Space.World);

        //Get Projected Pitch Vector & Angle
        Vector3 projPitchVector = Vector3.ProjectOnPlane(Vector3.forward * AirplaneSpeed / 550, MapRotationTarget.up);
        float PitchAngle = Vector3.SignedAngle(projPitchVector, Vector3.forward, Vector3.left);
        //Pitch Rotation Limit (15Deg)
        if (PitchAngle >= 15f || PitchAngle <= -15f)
        {
            if (PitchAngle >= 15f) MapRotationTarget.Rotate(new Vector3(-PitchAngle + 15f, 0f, 0f), Space.World);
            else MapRotationTarget.Rotate(new Vector3(-PitchAngle - 15f, 0f, 0f), Space.World);

            projPitchVector = Vector3.ProjectOnPlane(Vector3.forward * AirplaneSpeed / 550, MapRotationTarget.up);
            PitchAngle = Vector3.SignedAngle(projPitchVector, Vector3.forward, Vector3.left);
            PitchLimitAlarm();
        } else PitchAlarm.SetActive(false);
        DebugLine_3.SetPosition(1, projPitchVector * 0.25f + DebugLine_3.GetPosition(0));
        
        //Get Projected Roll Vector & Angle
        Vector3 projRollVector = Vector3.ProjectOnPlane(Vector3.right, MapRotationTarget.up);
        float RollAngle = Vector3.SignedAngle(projRollVector, Vector3.up, Vector3.forward) - 90;
        //Roll Rotation Limit (15Deg)
        if (RollAngle >= 15f || RollAngle <= -15f)
        {
            if (RollAngle >= 15f) MapRotationTarget.Rotate(new Vector3(0f, 0f, RollAngle - 15f), Space.World);
            else MapRotationTarget.Rotate(new Vector3(0f, 0f, RollAngle + 15f), Space.World);

            projRollVector = Vector3.ProjectOnPlane(Vector3.right, MapRotationTarget.up);
            RollAngle = Vector3.SignedAngle(projRollVector, Vector3.up, Vector3.forward) - 90;
            RollLimitAlarm();
        } else RollAlarm.SetActive(false);

        //Calculate:: Additional Rotation Effected by Pitch & Roll
        //Turn Right: +Roll, +Pitch / -Roll, -Pitch
        //Turn Left: +Roll, -Pitch / -Roll, +Pitch
        float RotationAdd = RollAngle * PitchAngle * RotationAddMulti;
        MapRotationTarget.Rotate(new Vector3(0f, RotationAdd, 0f), Space.World);

        //Check if Airplane is looking down
        float LiftFacingMulti = PitchAngle < 0f ? 0.8f : 1f;
        
        //Calculate:: Lift (Max: 0.015125)
        float AirplaneLift = AirplaneSpeed * AirplaneSpeed * 0.000001f * LiftMulti * LiftFacingMulti;
        float GravityLiftVector = Mathf.Pow(MapRotation.position.y, 2f) * 0.0008f;
        float DirectionLiftVector = PitchAngle * 0.0113f * AirplaneSpeed / 550;
        AirplaneLift -= GravityLiftVector - DirectionLiftVector;

        MapRotation.eulerAngles = MapRotationTarget.eulerAngles;
        DebugPlaneTrans.eulerAngles = MapRotationTarget.eulerAngles; //For Debugging
        MapRotation.position += new Vector3(0f, -AirplaneLift, 0f);
        DebugPlaneTrans.position += new Vector3(0f, -AirplaneLift * 0.005f, 0f); //For Debugging

        DebugText.text = ">>Controll\nSpeed: " + AirplaneSpeed.ToString()
        + "\nLift: " + AirplaneLift.ToString()
        + "\nPitch: " + PitchAngle.ToString()
        + "\nRoll: "+ RollAngle.ToString()
        + "\nRotA: "+ RotationAdd.ToString();
    }

    public void UpdateWorldCordinate()
    {

    }

    public void PitchLimitAlarm()
    {
        // Pitch 15도 제한 걸리면 발생하는 이벤트
        PitchAlarm.SetActive(true);
    }

    public void RollLimitAlarm()
    {
        // Yaw 15도 제한 걸리면 발생하는 이벤트
        RollAlarm.SetActive(true);
    }
}
