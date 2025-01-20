
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
    public Engine_Toggle Engine_Toggle;

    public Transform MapRotation = null;
    public Transform MapRotationTarget = null;
    public Transform MapPosition = null;

    public GameObject RollAlarm = null;
    public GameObject PitchAlarm = null;

    //Debug
    public Text DebugText;

    //VectorMultiply
    public float ThrottleVecMulti = 10f;
    public float PitchThrustVecMulti = 0.15f;
    public float YawThrustVecMulti = 0.25f;
    public float RollThrustVecMulti = 0.3f;
    public float LiftMulti = 0.5f;
    public float RotationAddMulti = 0.001f;
    public float MovebySpeedMulti = 70f;

    //Sync Values
    [UdonSynced] public float AirplaneSpeed = 0;
    [UdonSynced] public float PitchAngle;
    [UdonSynced] public float RollAngle;
    [UdonSynced] public float SyncedAirHight;
    [UdonSynced] public Vector3 movement;
    [UdonSynced] public Vector3 SyncedRotation;
    [UdonSynced] public Vector3 SyncedPosition;
    [UdonSynced] public bool PitchLimitAlarm;
    [UdonSynced] public bool RollLimitAlarm;
    
    public void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (Networking.IsMaster) CalculateMovement(dt);
        else
        {
            //MapPosition.localPosition = Vector3.Lerp(MapPosition.localPosition, SyncedPosition, Vector3.Distance(MapPosition.localPosition, SyncedPosition) * Time.deltaTime / 75f);
            //Vector3 refVector = Vector3.zero;
            Vector3 refVector = new Vector3(movement.x, 0f, movement.z);
            MapPosition.localPosition = Vector3.SmoothDamp(MapPosition.localPosition, SyncedPosition, ref refVector, 0.2f);
            if (Vector3.Distance(MapPosition.localPosition, SyncedPosition) > 1500)
            {
                MapPosition.localPosition = SyncedPosition;
            }
        }

        if (PitchLimitAlarm)
        {
            // Pitch 15도 제한 걸리면 발생하는 이벤트
            if (AirplaneSpeed > 150) {
                PitchAlarm.SetActive(true);
            }
            else PitchAlarm.SetActive(false);
        }
        else PitchAlarm.SetActive(false);
        
        if (RollLimitAlarm)
        {
            // Pitch 15도 제한 걸리면 발생하는 이벤트
            if (AirplaneSpeed > 150) {
                RollAlarm.SetActive(true);
            }
            else RollAlarm.SetActive(false);
        }
        else RollAlarm.SetActive(false);
    }

    public void CalculateMovement(float dt)
    {
        // Get values form other scripts
        float throttle = Throttle_Controll.throttlePower;   // -0.3 ~ 1.25
        float YawThrustVec = -Controller_Controll.yaw;      // -0.5 ~ 0.5 (-L / +R)
        float PitchThrustVec = Controller_Controll.pitch;   // -0.5 ~ 0.5 (-D / +U)
        float RollThrustVec = Controller_Controll.roll;     // -0.5 ~ 0.5 (-L / +R)

        if (!Engine_Toggle.EngineStatus)
        {
            throttle = 0;
        }
        if (AirplaneSpeed < 5f)
        {
            YawThrustVec = 0;
            PitchThrustVec = 0;
            RollThrustVec = 0;
        }

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
        else RollAngleLimit = AirHight * 2.5f;

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
            PitchLimitAlarm = true;
        } else PitchLimitAlarm = false;
        
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
            RollLimitAlarm = true;
        } else RollLimitAlarm = false;

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

        //Change Height. Set height to 0 when negative to prevent the aircraft from going underground.
        if (MapRotation.position.y - AirplaneLift > 0f) MapRotation.position = new Vector3(0f, 0f, 0f);
        else MapRotation.position += new Vector3(0f, -AirplaneLift, 0f);
        
        float MovebySpeed = Vector3.Magnitude(projPitchVector);
        float MoveVecRot = Vector3.SignedAngle(projPitchVector, MapRotation.forward, MapRotation.up);
        Vector3 moveDirection = Quaternion.AngleAxis(MoveVecRot, Vector3.down) * (Vector3.forward * MovebySpeed);
        movement = -moveDirection * dt * MovebySpeedMulti;
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
            MapRotationTarget.eulerAngles = SyncedRotation;
            MapRotationTarget.position = new Vector3(0f, SyncedAirHight, 0f);
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            MapRotationTarget.eulerAngles = SyncedRotation;
            MapRotationTarget.position = new Vector3(0f, SyncedAirHight, 0f);
            MapRotation.eulerAngles = SyncedRotation;
            MapRotation.position = new Vector3(0f, SyncedAirHight, 0f);
            MapPosition.localPosition = SyncedPosition;
        }
    }
}
