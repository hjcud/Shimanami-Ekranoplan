
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 엔진의 네트워크 상태와 시동음·공회전음·팬 애니메이션 전환 순서 관리
/// </summary>
public class Engine_Toggle : UdonSharpBehaviour
{
    [UdonSynced] public bool EngineStatus = false;

    public AudioSource EngineStart;
    public AudioSource EngineIdle;
    public Animator EngineAnimator; 
    
    float PlayTimer = 0;
    bool isPlaying = false;
    bool isIdle = false;

    public override void Interact()
    {
        // 상태 변경은 현재 소유자에서만 실행해 하나의 EngineStatus를 직렬화
        if (EngineStatus)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, "EngineOnNetwork");
        }
        else
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, "EngineOffNetwork");
        }
    }

    public void EngineOnNetwork()
    {
        // 기존 네트워크 이벤트 이름을 유지하면서 실제 상태는 엔진 정지로 전환
        EngineStatus = false;
        RequestSerialization();
    }
    
    public void EngineOffNetwork()
    {
        // 기존 네트워크 이벤트 이름을 유지하면서 실제 상태는 엔진 시동으로 전환
        EngineStatus = true;
        // 시동음이 끝날 때까지 반복 상호작용 방지
        this.gameObject.GetComponent<BoxCollider> ().enabled = false;
        RequestSerialization();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        PlayTimer += dt;

        if (EngineStatus)
        {
            if (!isPlaying)
            {
                // 첫 프레임에서 팬 애니메이션과 시동음 시작
                isPlaying = true;
                EngineAnimator.SetBool("Fan_Rotate", true);
                EngineStart.Play();
            }
            else
            {
                if (PlayTimer > 3.5f)
                {
                    if (!isIdle)
                    {
                        // 시동음 재생 뒤 공회전음을 시작하고 목표 음량까지 증가
                        isIdle = true;
                        EngineIdle.Play();
                    }
                    else
                    {
                        if (EngineIdle.volume < 0.3f)
                        {
                            EngineIdle.volume += dt * 0.075f;
                        }
                        else
                        {
                            // 시동 과정이 끝난 뒤 다시 엔진 스위치 조작 허용
                            this.gameObject.GetComponent<BoxCollider> ().enabled = true;
                        }
                    }

                    if (EngineStart.volume > 0f)
                    {
                        EngineStart.volume -= dt * 0.05f;
                    }
                    else
                    {
                        EngineStart.Stop();
                    }
                }
            }
        }
        else
        {
            // 정지 상태에서 오디오·타이머·애니메이션을 다음 시동의 초기값으로 복원
            EngineStart.Stop();
            EngineIdle.Stop();
            EngineStart.volume = 0.2f;
            EngineIdle.volume = 0f;
            PlayTimer = 0f;
            isPlaying = false;
            isIdle = false;
            EngineAnimator.SetBool("Fan_Rotate", false);
        }
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            if (EngineStatus)
            {
                // 실행 중인 인스턴스에 들어온 사용자는 시동음을 건너뛰고 공회전 상태부터 재생
                isIdle = true;
                isPlaying = true;
                EngineIdle.volume = 0.3f;
                EngineIdle.Play();
                EngineStart.Stop();
                EngineAnimator.SetBool("Fan_Rotate", true);
            }
        }
    }
}
