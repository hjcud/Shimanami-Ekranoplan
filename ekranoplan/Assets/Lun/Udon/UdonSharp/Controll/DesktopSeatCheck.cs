
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class DesktopSeatCheck : UdonSharpBehaviour
{
    [SerializeField] Throttle_Controll Throttle_Controll;
    [SerializeField] Controller_Controll Controller_Controll;
    
    private bool isPilot = false;
    public bool isRightSeat;
    private VRCPlayerApi LocalPlayerSave;

    public override void Interact()  
    {  
        Networking.LocalPlayer.UseAttachedStation();  
    }  

    public override void OnStationEntered(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            isPilot = true;
            LocalPlayerSave = player;
        }
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            isPilot = false;
            Throttle_Controll.resetValues();
            Controller_Controll.resetValues();
        }
    }
    
    private void FixedUpdate()
    {
        if (isPilot)
        {
            //SendCustomEvent(nameof(UpdateTriggerCheck));
            Throttle_Controll.UpdateTriggerCheck(isRightSeat);
            Controller_Controll.UpdateTriggerCheck(isRightSeat);

            if (Input.GetKey(KeyCode.Space) && (Networking.LocalPlayer == LocalPlayerSave))
            {
                this.gameObject.GetComponent<VRCStation>().ExitStation(Networking.LocalPlayer);
            }
        }

    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            if (Networking.LocalPlayer.IsUserInVR())
            {
                this.gameObject.SetActive(false);
            }
        }
    }
}
