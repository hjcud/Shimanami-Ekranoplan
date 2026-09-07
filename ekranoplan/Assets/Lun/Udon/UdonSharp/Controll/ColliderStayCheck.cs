
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// VR 좌석의 조종 범위에 들어온 로컬 사용자의 손 입력을 조종 장치로 전달
/// </summary>
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
            // 좌석 방향에 맞는 손을 선택해 스로틀과 조종간 입력 갱신
            Throttle_Controll.UpdateTriggerCheck(isRightSeat);
            Controller_Controll.UpdateTriggerCheck(isRightSeat);
        }
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        // 데스크톱 사용자는 Station에서 처리하므로 로컬 VR 사용자만 등록
        if (player.isLocal && Networking.LocalPlayer.IsUserInVR()) {
            isPilot = true;
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        // 조종 범위를 벗어나면 손 추적 기준과 입력 소유 상태 초기화
        if (player.isLocal && Networking.LocalPlayer.IsUserInVR()) {
            isPilot = false;
            Throttle_Controll.resetValues();
            Controller_Controll.resetValues();
        }
    }
}
