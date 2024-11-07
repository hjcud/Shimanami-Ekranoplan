
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ColliderStayCheck : UdonSharpBehaviour
{
    private bool isPilot = false;
    public bool isRightSeat;

    [SerializeField] Throttle_Controll Throttle_Controll;
    [SerializeField] Controller_Controll Controller_Controll;

    private void Update()
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
        if (player.isLocal) {
            isPilot = true;
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        //Debug.Log("OnPlayerTriggerExit triggered");
        if (player.isLocal) {
            isPilot = false;
            Throttle_Controll.resetValues();
            Controller_Controll.resetValues();
        }
    }
}
