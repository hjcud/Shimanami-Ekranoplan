
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 데스크톱 사용자의 Station 착석 상태와 키보드 조종 입력 수명주기 관리
/// </summary>
public class DesktopSeatCheck : UdonSharpBehaviour
{
    [SerializeField] Throttle_Controll Throttle_Controll;
    [SerializeField] Controller_Controll Controller_Controll;
    
    private bool isPilot = false;
    public bool isRightSeat;
    private VRCPlayerApi LocalPlayerSave;

    public override void Interact()  
    {  
        // 상호작용한 로컬 사용자를 이 오브젝트에 연결된 Station으로 이동
        Networking.LocalPlayer.UseAttachedStation();  
    }  

    public override void OnStationEntered(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            // 원격 사용자의 착석 이벤트는 로컬 입력 갱신 대상에서 제외
            isPilot = true;
            LocalPlayerSave = player;
        }
    }

    public override void OnStationExited(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            // 좌석 이탈과 함께 스로틀 손 기준과 조종간 축 값 초기화
            isPilot = false;
            Throttle_Controll.resetValues();
            Controller_Controll.resetValues();
        }
    }
    
    private void FixedUpdate()
    {
        if (isPilot)
        {
            // 좌석 방향에 맞는 데스크톱 조종 입력 갱신
            Throttle_Controll.UpdateTriggerCheck(isRightSeat);
            Controller_Controll.UpdateTriggerCheck(isRightSeat);

            if (Input.GetKey(KeyCode.Space) && (Networking.LocalPlayer == LocalPlayerSave))
            {
                // Space 입력은 실제로 이 좌석에 들어온 로컬 사용자에게만 적용
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
                // VR 사용자는 ColliderStayCheck 경로를 사용하므로 데스크톱 Station 비활성화
                this.gameObject.SetActive(false);
            }
        }
    }
}
