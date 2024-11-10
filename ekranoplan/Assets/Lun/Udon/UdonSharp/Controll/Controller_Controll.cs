
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class Controller_Controll : UdonSharpBehaviour
{
    private Quaternion firstRot = Quaternion.identity;
    private Vector3 maxAngles = new Vector3(90, 180, 180);
    public Animator ControllerAnimator;
    public Text Controll_Yaw;
    public Text Controll_Pit;
    public Text Controll_Rol;

    [UdonSynced] public int TriggeredUserID = 0;

    [UdonSynced(UdonSyncMode.Linear)] public float pitch = 0f;
    [UdonSynced(UdonSyncMode.Linear)] public float yaw = 0f;
    [UdonSynced(UdonSyncMode.Linear)] public float roll = 0f;

    public void UpdateTriggerCheck(bool isRightSeat)
    {
        // Check if User is in VR
        if (Networking.LocalPlayer.IsUserInVR())
        {
            if ((Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryHandTrigger") > 0.9 && !isRightSeat)
            || (Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryHandTrigger") > 0.9 && isRightSeat))
            {
                if (TriggeredUserID == 0)
                {
                    if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject)) Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
                    TriggeredUserID = VRCPlayerApi.GetPlayerId(Networking.LocalPlayer);
                    //Debug.Log(">>> " + TriggeredUserID + " has Triggerd Controller");
                    RequestSerialization();
                }
                else if (TriggeredUserID == VRCPlayerApi.GetPlayerId(Networking.LocalPlayer))
                {
                    Quaternion leftHandRot;
                    if (!isRightSeat) leftHandRot = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).rotation;
                    else leftHandRot = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation;
                    if (firstRot == Quaternion.identity)
                    {
                        firstRot = leftHandRot;
                    }
                    else
                    {
                        Quaternion angleDifference = leftHandRot * Quaternion.Inverse(firstRot);

                        // rotation vector caculate
                        Vector3 controllerPosYaw = angleDifference * Vector3.forward;
                        Vector3 controllerPos = angleDifference * Vector3.up;

                        // caculate Pitch, Yaw, Roll & normalize (-1, 1)
                        pitch = (Mathf.Acos(Mathf.Clamp(controllerPos.z, -1, 1)) - Mathf.PI / 2) * Mathf.Rad2Deg / maxAngles.x;
                        yaw = (Mathf.Acos(Mathf.Clamp(controllerPosYaw.x, -1, 1)) - Mathf.PI / 2) * Mathf.Rad2Deg / maxAngles.y;
                        roll = (Mathf.Acos(Mathf.Clamp(controllerPos.x, -1, 1)) - Mathf.PI / 2) * Mathf.Rad2Deg / maxAngles.z;

                        //Debug.Log(string.Format("Current Rotation: (pitch: {0}), (yaw: {1}), (roll: {2})", pitch, yaw, roll));
                        RequestSerialization();

                        UpdateControllerRotation();
                    }
                }
            }
            else if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                // Reset Values if not Holding
                resetValues();

                UpdateControllerRotation();
            }
        }
        else
        {
            // Desktop Controll 
        }
    }

    public override void OnDeserialization()
    {
        UpdateControllerRotation();
    }

    public void UpdateControllerRotation() // Update Animator parameter
    {
        ControllerAnimator.SetFloat("Controller_Pitch", pitch + 0.5f);
        ControllerAnimator.SetFloat("Controller_Yaw", yaw + 0.5f);
        ControllerAnimator.SetFloat("Controller_Roll", roll + 0.5f);

        Controll_Yaw.text = pitch.ToString();
        Controll_Pit.text = yaw.ToString();
        Controll_Rol.text = roll.ToString();
    }

    public void resetValues()
    {
        firstRot = Quaternion.identity;
        TriggeredUserID = 0;
        pitch = 0f;
        yaw = 0f;
        roll = 0f;
        RequestSerialization();
    }
}
