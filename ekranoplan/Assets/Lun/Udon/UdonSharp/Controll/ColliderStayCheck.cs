
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ColliderStayCheck : UdonSharpBehaviour
{
    [SerializeField] Throttle_Controll Throttle_Controll;
    [SerializeField] Controller_Controll Controller_Controll;
    
    private bool isPilot = false;
    public bool isRightSeat;

    private void FixedUpdate()
    {
        if (isPilot)
        {
            //SendCustomEvent(nameof(UpdateTriggerCheck));
            Throttle_Controll.UpdateTriggerCheck(isRightSeat);
            Controller_Controll.UpdateTriggerCheck(isRightSeat);
        }
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        //Debug.Log("OnPlayerTriggerEnter triggered");
        if (player.isLocal && Networking.LocalPlayer.IsUserInVR()) {
            isPilot = true;
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        //Debug.Log("OnPlayerTriggerExit triggered");
        if (player.isLocal && Networking.LocalPlayer.IsUserInVR()) {
            isPilot = false;
            Throttle_Controll.resetValues();
            Controller_Controll.resetValues();
        }
    }
}
