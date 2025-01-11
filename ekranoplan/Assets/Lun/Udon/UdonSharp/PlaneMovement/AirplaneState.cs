
using System;
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
    public Transform MapPosition = null;
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
    public float PitchThrustVecMulti = 0.15f;
    public float YawThrustVecMulti = 0.25f;
    public float RollThrustVecMulti = 0.3f;
    public float LiftMulti = 0.5f;
    public float RotationAddMulti = 0.001f;
    public float MovebySpeedMulti = 70f;

    // Airplane Transform
    [Tooltip("Current velocity vector")]
    public Vector3 airplaneVelocity = Vector3.zero;

    //Sync Values
    [UdonSynced] public float AirplaneSpeed = 0;
    [UdonSynced] public float PitchAngle;
    [UdonSynced] public float RollAngle;
    [UdonSynced] public float SyncedAirHight;
    [UdonSynced] public Vector3 SyncedRotation;
    [UdonSynced] public Vector3 SyncedPosition;
    
    public void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (Networking.IsMaster) CalculateMovement(dt);
    }

    public void CalculateMovement(float dt)
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

        //Calculate:: Acceleration
        //a = F/m * Output Ratio (F = Power of each engine * Number of engines) = (101920 * 8) /286000
        float acceleration = throttle * 101920f * 8f;

        //Calculate:: Drag by Speed
        //DragForce = 1/2*pv^2AC = v^2 * 0.5(1/2) * air density(p: 1.225) * surface area(A: 80) * drag coefficient(C: 0.04) / mass of an airplane (286ton = 286000kg) = v^2 * 1.96/286000
        float SpeedDrag = Mathf.Pow(AirplaneSpeed, 2f) * 1.96f;

        //Calculate:: Speed 
        AirplaneSpeed += ((acceleration * 0.581f) - SpeedDrag) / 286000 * ThrottleVecMulti * dt;
        //If the speed is negative, reduce the movement amount (limit backward movement to slow speeds)
        if (AirplaneSpeed < 0) AirplaneSpeed /= 2;
        
        //Set Rotation Vector and Multiply
        Vector3 RotationVector = Vector3.zero;
        RotationVector.x = PitchThrustVec * PitchThrustVecMulti;
        RotationVector.y = -YawThrustVec * YawThrustVecMulti;
        RotationVector.z = RollThrustVec * RollThrustVecMulti;
        MapRotationTarget.Rotate(RotationVector, Space.World);

        float AirHight;
        AirHight = -MapRotation.position.y;
        //Calculate:: Limit Angle by Hight
        float PitchAngleLimit;
        if (AirHight >= 7.5f) PitchAngleLimit = 15f;
        else PitchAngleLimit = AirHight * 2f;
        float RollAngleLimit;
        if (AirHight >= 6f) RollAngleLimit = 15f;
        else RollAngleLimit = AirHight * 0.4f;

        //Get Projected Pitch Vector & Angle
        Vector3 projPitchVector = Vector3.ProjectOnPlane(Vector3.forward * Mathf.Abs(AirplaneSpeed) / 550, MapRotationTarget.up);
        //if (AirplaneSpeed == 0) PitchAngle = 0f;
        PitchAngle = Vector3.SignedAngle(projPitchVector, Vector3.forward, Vector3.left);
        //Pitch Rotation Limit (15Deg)
        if (PitchAngle > PitchAngleLimit || PitchAngle < -PitchAngleLimit)
        {
            if (PitchAngle > PitchAngleLimit) MapRotationTarget.Rotate(new Vector3(-PitchAngle + PitchAngleLimit, 0f, 0f), Space.World);
            else MapRotationTarget.Rotate(new Vector3(-PitchAngle - PitchAngleLimit, 0f, 0f), Space.World);

            projPitchVector = Vector3.ProjectOnPlane(Vector3.forward * Mathf.Abs(AirplaneSpeed) / 550, MapRotationTarget.up);
            PitchAngle = Vector3.SignedAngle(projPitchVector, Vector3.forward, Vector3.left);
            PitchLimitAlarm();
        } else PitchAlarm.SetActive(false);
        DebugLine_3.SetPosition(1, projPitchVector * 0.25f + DebugLine_3.GetPosition(0));
        
        //Get Projected Roll Vector & Angle
        Vector3 projRollVector = Vector3.ProjectOnPlane(Vector3.right, MapRotationTarget.up);
        RollAngle = Vector3.SignedAngle(projRollVector, Vector3.up, Vector3.forward) - 90;
        //Roll Rotation Limit (15Deg)
        if (RollAngle > RollAngleLimit || RollAngle < -RollAngleLimit)
        {
            if (RollAngle > RollAngleLimit) MapRotationTarget.Rotate(new Vector3(0f, 0f, RollAngle - RollAngleLimit), Space.World);
            else MapRotationTarget.Rotate(new Vector3(0f, 0f, RollAngle + RollAngleLimit), Space.World);

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
        float GravityLiftVector = Mathf.Pow(AirHight, 2f) * 0.0008f;
        float DirectionLiftVector = PitchAngle * 0.0113f * AirplaneSpeed / 550;
        AirplaneLift -= GravityLiftVector - DirectionLiftVector;

        MapRotation.eulerAngles = MapRotationTarget.eulerAngles;
        DebugPlaneTrans.eulerAngles = MapRotationTarget.eulerAngles; //For Debugging

        //Change Height. Set height to 0 when negative to prevent the aircraft from going underground.
        if (MapRotation.position.y - AirplaneLift > 0f) MapRotation.position = new Vector3(0f, 0f, 0f);
        else MapRotation.position += new Vector3(0f, -AirplaneLift, 0f);

        if (MapRotation.position.y - AirplaneLift > 0f) DebugPlaneTrans.position = new Vector3(0f, 0f, 0f);
        else DebugPlaneTrans.position += new Vector3(0f, -AirplaneLift * 0.005f, 0f); //For Debugging

        float MovebySpeed = Vector3.Magnitude(projPitchVector);
        float MoveVecRot = Vector3.SignedAngle(projPitchVector, MapRotation.forward, MapRotation.up);
        Vector3 moveDirection = Quaternion.AngleAxis(MoveVecRot, Vector3.down) * (Vector3.forward * MovebySpeed);
        Vector3 movement = -moveDirection * dt * MovebySpeedMulti;
        MapPosition.localPosition += new Vector3(movement.x, 0f, movement.z);

        DebugText.text = ">>Controll\nSpeed: " + AirplaneSpeed.ToString("F5")
        + "\nLift: " + AirplaneLift.ToString("F5")
        + "\nPitch: " + PitchAngle.ToString("F5")
        + "\nRoll: "+ RollAngle.ToString("F5")
        + "\nRotAdd: "+ RotationAdd.ToString("F5")
        + "\nHight: " + MapRotation.position.y.ToString("F5")
        + "\nMoveVecRot: " + MoveVecRot.ToString("F5")
        + "\nCoordinate: " + MapPosition.localPosition.ToString("F5");

        //Value Sync
        SyncedAirHight = MapRotation.position.y;
        SyncedRotation = MapRotation.eulerAngles;
        SyncedPosition = MapPosition.localPosition;
        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        UpdateCordinate();
    }

    public void UpdateCordinate()
    {
        if (!Networking.IsMaster)
        {
            MapRotation.position = new Vector3(0f, SyncedAirHight, 0f);
            MapRotation.eulerAngles = SyncedRotation;
            MapPosition.localPosition = SyncedPosition;
        }
        //마스터 변경시 모든 유저 혹은 새로운 마스터에게 tranceform target 값 갱신해줄것
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
