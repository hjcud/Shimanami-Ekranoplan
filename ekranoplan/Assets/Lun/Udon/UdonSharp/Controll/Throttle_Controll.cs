
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// VR 손 위치와 데스크톱 키 입력을 역추진·기본·추가 출력 스로틀 값으로 변환
/// </summary>
public class Throttle_Controll : UdonSharpBehaviour
{
    private Vector3 firstPos = Vector3.zero;
    public Animator TrottleAnimator; 
    public Engine_Toggle Engine_Toggle;
    public AudioSource ThrottleAudio;

    [UdonSynced] public int TriggeredUserID = 0;

    private float mappedDistance = 0;
    [UdonSynced(UdonSyncMode.Linear)] public float throttlePower = 0;
    // 0은 기본 출력, 1은 역추진, 2는 100%를 넘는 추가 출력
    private int TrottleState = 0;


    public void UpdateTriggerCheck(bool isRightSeat)
    {
        // VR에서는 좌석 방향에 따라 스로틀을 잡는 손과 그립 입력 선택
        if (Networking.LocalPlayer.IsUserInVR()) {
            if ((Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryHandTrigger") > 0.9 && !isRightSeat)
            || (Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryHandTrigger") > 0.9 && isRightSeat))
            {
                if (TriggeredUserID == 0)
                {
                    if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject)) Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
                    TriggeredUserID = VRCPlayerApi.GetPlayerId(Networking.LocalPlayer);
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
                        // 처음 잡은 위치에서 앞뒤 0.15m 범위의 손 이동량 계산
                        float forwordDistance = Mathf.Clamp(rightHandPos.z - firstPos.z, -0.15f, 0.15f);

                        // 손 이동량을 -1~1 입력값으로 변환
                        mappedDistance = ((forwordDistance + 0.15f) / 0.15f) - 1.0f;
                        CheckThrottleState(isRightSeat);
                    }
                }
            }
            else if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                // 스로틀을 놓으면 다음 입력에서 사용할 손 위치 기준과 사용자 해제
                resetValues();
            }
        }
        else
        {
            // 데스크톱에서는 Shift와 Ctrl로 출력을 일정 단위씩 증감
            bool keyShiftPressed = Input.GetKey(KeyCode.LeftShift);
            bool keyCtrlPressed = Input.GetKey(KeyCode.LeftControl);

            if (keyShiftPressed || keyCtrlPressed)
            {
                if (TriggeredUserID == 0)
                {
                    if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject)) Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
                    TriggeredUserID = VRCPlayerApi.GetPlayerId(Networking.LocalPlayer);
                    RequestSerialization();
                }
                else if (TriggeredUserID == VRCPlayerApi.GetPlayerId(Networking.LocalPlayer))
                {
                    if (keyShiftPressed)
                    {
                        mappedDistance = 0.2f;
                        Debug.Log("Desktop Calculate(Plus)");
                    }
                    else 
                    {
                        mappedDistance = -0.2f;
                        Debug.Log("Desktop Calculate(Minus)");
                    }
                    CheckThrottleState(isRightSeat);
                }
            }
            else if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                // 키 입력이 끝나면 입력 사용자만 해제하고 현재 출력 유지
                resetValues();
            }
        }
    }

    private void CheckThrottleState(bool isRightSeat)
    {
        // 0% 아래로 내릴 때 보조 입력을 누른 경우에만 역추진 진입
        if (throttlePower <= 0.0f)
        {
            if ((TrottleState != 1) && (mappedDistance < 0))
            {
                if ((Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger") > 0.9 && !isRightSeat) ||
                (Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger") > 0.9 && isRightSeat) || Input.GetKey(KeyCode.Z))
                {
                    TrottleState = 1;
                }
                else return;
            }
        }
        // 100% 위로 올릴 때 보조 입력을 누른 경우에만 추가 출력 진입
        else if (throttlePower >= 1.0f)
        {
            if ((TrottleState != 2) && (mappedDistance > 0))
            {
                if ((Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger") > 0.9 && !isRightSeat) ||
                (Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger") > 0.9 && isRightSeat) || Input.GetKey(KeyCode.Z))
                {
                    TrottleState = 2;
                }
                else return;
            }
        }
        // 0~100% 구간에서는 기본 출력 상태 복원
        else TrottleState = 0;

        // 한 번의 입력 갱신에서 손·키 이동량의 2.5%만 반영
        throttlePower += mappedDistance * 0.025f;
        if (TrottleState == 0) {
            throttlePower = Mathf.Clamp(throttlePower, 0f, 1f);
        }
        else if (TrottleState == 1) {
            throttlePower = Mathf.Clamp(throttlePower, -0.3f, 0.001f);
        }
        else {
            throttlePower = Mathf.Clamp(throttlePower, 0.999f, 1.25f);
        }
        RequestSerialization();
        
        UpdateThrottleRotation();
    }

    public override void OnDeserialization()
    {
        // 수신한 출력값으로 원격 스로틀 애니메이션 갱신
        UpdateThrottleRotation();
    }

    public void UpdateThrottleRotation()
    {
        // -30~125% 출력을 Animator가 사용하는 0~1 범위로 변환
        float throttleRotation = (throttlePower + 0.3f) / 1.55f ;
        TrottleAnimator.SetFloat("Throttle_Rotation", throttleRotation);
        if (Engine_Toggle.EngineStatus) {
            ThrottleAudio.volume = Mathf.Abs(throttlePower)/1.25f * 0.2f;
        }
    }

    public void resetValues()
    {
        // 출력값은 유지하고 손 추적 기준과 입력 사용자만 초기화
        firstPos = Vector3.zero;
        TriggeredUserID = 0;
        RequestSerialization();
    }
}
