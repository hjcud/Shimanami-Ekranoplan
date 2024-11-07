
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class Throttle_Controll : UdonSharpBehaviour
{
    private Vector3 firstPos = Vector3.zero;
    public Animator TrottleAnimator;
    
    public Text Controll_Thr;

    [UdonSynced] public int TriggeredUserID = 0;

    private float mappedDistance = 0;
    [UdonSynced(UdonSyncMode.Linear)] public float Throttle_Rotation = 0;

    private int TrottleState = 0; // TrottleState (0: Default, 1: Reverse, 2: Oveclock)

    [SerializeField] SpeedCal SpeedCal;

    public void UpdateTriggerCheck(bool isRightSeat)
    {
        // Check if User is in VR
        if (Networking.LocalPlayer.IsUserInVR()) {
            if ((Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryHandTrigger") > 0.9 && !isRightSeat)
            || (Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryHandTrigger") > 0.9 && isRightSeat))
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
                    Vector3 rightHandPos;
                    if (!isRightSeat) rightHandPos = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
                    else rightHandPos = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;

                    if (firstPos == Vector3.zero)
                    {
                        firstPos = rightHandPos;
                    }
                    else
                    {
                        // POWER max(-0.15) lowest(0.15)
                        float forwordDistance = Mathf.Clamp(rightHandPos.z - firstPos.z, -0.15f, 0.15f);
                        
                        // Value mapping between [-1, 1]
                        mappedDistance = ((forwordDistance + 0.15f) / 0.15f) - 1.0f;
                        //Debug.Log("Current Distance: " + mappedDistance);
                        CheckThrottleState(isRightSeat);
                    }
                }
            }
            else if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                // Reset firstPos if not Holding
                resetValues();
            }
        }
        else
        {
            // Desktop Controll 
        }
    }

    private void CheckThrottleState(bool isRightSeat)
    {
        // Reverse (less then 20 Deg)
        if (Throttle_Rotation < 0.22)
        {
            if ((TrottleState != 1) && (mappedDistance < 0)) // Only When Deceleration
            {
                if ((Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger") > 0.9 && !isRightSeat) ||
                (Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger") > 0.9 && isRightSeat))
                {
                    TrottleState = 1;
                }
                else return;
            }
        }
        // Overclock (over 80 Deg)
        else if (Throttle_Rotation > 0.78)
        {
            if ((TrottleState != 2) && (mappedDistance > 0)) // Only When acceleration
            {
                if ((Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger") > 0.9 && !isRightSeat) ||
                (Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger") > 0.9 && isRightSeat))
                {
                    TrottleState = 2;
                }
                else return;
            }
        }
        // Default
        else TrottleState = 0;

        Throttle_Rotation += mappedDistance * 0.025f;
        Throttle_Rotation = Mathf.Clamp(Throttle_Rotation, 0, 0.999f);
        RequestSerialization();
        
        SpeedCal.CalculatePlaneSpeed(Throttle_Rotation);
        UpdateThrottleRotation();
    }

    public override void OnDeserialization()
    {
        UpdateThrottleRotation();
    }

    public void UpdateThrottleRotation() // Update Animator parameter
    {
        TrottleAnimator.SetFloat("Throttle_Rotation", Throttle_Rotation);
        
        Controll_Thr.text = (Mathf.Round((Throttle_Rotation - 0.22f) * 1000) / 1000).ToString();
    }

    public void resetValues()
    {
        firstPos = Vector3.zero;
        TriggeredUserID = 0;
        RequestSerialization();
    }
}
