
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// VR 손 회전과 데스크톱 키 입력을 pitch·yaw·roll 조종값으로 변환하고 네트워크에 전송
/// </summary>
public class Controller_Controll : UdonSharpBehaviour
{
    private Quaternion firstRot = Quaternion.identity;
    private Vector3 maxAngles = new Vector3(90, 135, 135);
    public Animator ControllerAnimator;

    public GameObject OwnerChangeTarget = null;

    [UdonSynced] public int TriggeredUserID = 0;

    [UdonSynced(UdonSyncMode.Linear)] public float yaw = 0f;
    [UdonSynced(UdonSyncMode.Linear)] public float pitch = 0f;
    [UdonSynced(UdonSyncMode.Linear)] public float roll = 0f;

    public void UpdateTriggerCheck(bool isRightSeat)
    {
        // VR에서는 좌석 방향에 따라 조종간을 잡는 손과 그립 입력 선택
        if (Networking.LocalPlayer.IsUserInVR())
        {
            if ((Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryHandTrigger") > 0.9 && !isRightSeat)
            || (Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryHandTrigger") > 0.9 && isRightSeat))
            {
                if (TriggeredUserID == 0)
                {
                    if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
                    {
                        // 입력값과 비행 계산의 소유자를 함께 넘겨 서로 다른 사용자가 갱신하는 상태 방지
                        Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
                        Networking.SetOwner(Networking.LocalPlayer, OwnerChangeTarget);
                    }
                    TriggeredUserID = VRCPlayerApi.GetPlayerId(Networking.LocalPlayer);
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

                        // 처음 잡은 손 회전을 기준으로 현재 조종간의 상대 방향 계산
                        Vector3 controllerPosYaw = angleDifference * Vector3.forward;
                        Vector3 controllerPos = angleDifference * Vector3.up;

                        // 각 축의 허용 각도로 나눠 pitch·yaw·roll을 -1~1 범위로 정규화
                        yaw = (Mathf.Acos(Mathf.Clamp(controllerPosYaw.x, -1, 1)) - Mathf.PI / 2) * Mathf.Rad2Deg / maxAngles.y;
                        pitch = (Mathf.Acos(Mathf.Clamp(controllerPos.z, -1, 1)) - Mathf.PI / 2) * Mathf.Rad2Deg / maxAngles.x;
                        roll = -(Mathf.Acos(Mathf.Clamp(controllerPos.x, -1, 1)) - Mathf.PI / 2) * Mathf.Rad2Deg / maxAngles.z;

                        RequestSerialization();

                        UpdateControllerRotation();
                    }
                }
            }
            else if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                // 조종간을 놓으면 손 회전 기준과 조종값을 함께 초기화
                resetValues();

                UpdateControllerRotation();
            }
        }
        else
        {
            // 데스크톱에서는 W/S, A/D, Q/E를 pitch·yaw·roll 축에 매핑
            bool keyWPressed = Input.GetKey(KeyCode.W);
            bool keySPressed = Input.GetKey(KeyCode.S);
            bool keyAPressed = Input.GetKey(KeyCode.A);
            bool keyDPressed = Input.GetKey(KeyCode.D);
            bool keyQPressed = Input.GetKey(KeyCode.Q);
            bool keyEPressed = Input.GetKey(KeyCode.E);

            if (keyWPressed || keySPressed || keyAPressed || keyDPressed || keyQPressed || keyEPressed)
            {
                if (TriggeredUserID == 0)
                {
                    if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject)) Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
                    TriggeredUserID = VRCPlayerApi.GetPlayerId(Networking.LocalPlayer);
                    RequestSerialization();
                }
                else if (TriggeredUserID == VRCPlayerApi.GetPlayerId(Networking.LocalPlayer))
                {
                    if (keyWPressed) pitch = -0.3f;
                    else if (keySPressed) pitch = 0.3f;
                    if (keyAPressed) yaw = 0.2f;
                    else if (keyDPressed) yaw = -0.2f;
                    if (keyQPressed) roll = -0.3f;
                    else if (keyEPressed) roll = 0.3f;
                    RequestSerialization();

                    UpdateControllerRotation();
                }
            }
            else if (Networking.IsOwner(Networking.LocalPlayer, this.gameObject))
            {
                // 키 입력이 끝나면 축 값과 입력 사용자 해제
                resetValues();

                UpdateControllerRotation();
            }
        }
    }

    public override void OnDeserialization()
    {
        // 수신한 축 값으로 원격 조종간 애니메이션 갱신
        UpdateControllerRotation();
    }

    public void UpdateControllerRotation()
    {
        // 중립값이 0.5인 Animator 파라미터에 현재 조종축 값 적용
        ControllerAnimator.SetFloat("Controller_Yaw", yaw + 0.5f);
        ControllerAnimator.SetFloat("Controller_Pitch", pitch + 0.5f);
        ControllerAnimator.SetFloat("Controller_Roll", roll + 0.5f);
    }

    public void resetValues()
    {
        // 다음 입력에서 현재 손 회전을 새 기준점으로 사용하도록 보정값 제거
        firstRot = Quaternion.identity;
        TriggeredUserID = 0;
        pitch = 0f;
        yaw = 0f;
        roll = 0f;
        RequestSerialization();
    }
}
