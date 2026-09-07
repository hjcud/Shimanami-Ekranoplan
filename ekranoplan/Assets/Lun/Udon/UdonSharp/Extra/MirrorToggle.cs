
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 같은 위치에 배치된 두 미러 중 하나만 로컬에서 활성화
/// </summary>
public class MirrorToggle : UdonSharpBehaviour
{
    public GameObject MirrorTarget;
    public Text MirrorTarget_Text;
    public GameObject MirrorSub;
    public Text MirrorSub_Text;

    public void ButtonTrigger()
    {
        // 네트워크 상태를 바꾸지 않고 버튼을 누른 사용자의 미러만 전환
        bool isTargetOn = MirrorTarget.activeSelf;
        bool isSubOn = MirrorSub.activeSelf;

        if (!isTargetOn)
        {
            if (isSubOn)
            {
                // 다른 품질의 미러를 먼저 끄고 두 미러의 동시 렌더링 방지
                MirrorSub.SetActive(false);
                MirrorSub_Text.color = new Color(0.5f, 0.5f, 0.5f);
            }
            MirrorTarget.SetActive(true);
            MirrorTarget_Text.color = new Color(1f, 1f, 1f);
        }
        else
        {
            // 켜진 미러를 다시 누르면 미러와 선택 표시 비활성화
            MirrorTarget.SetActive(false);
            MirrorTarget_Text.color = new Color(0.5f, 0.5f, 0.5f);
        }

    }
}
