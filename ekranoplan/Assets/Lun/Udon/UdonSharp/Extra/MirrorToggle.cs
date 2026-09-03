
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

public class MirrorToggle : UdonSharpBehaviour
{
    public GameObject MirrorTarget;
    public Text MirrorTarget_Text;
    public GameObject MirrorSub;
    public Text MirrorSub_Text;

    public void ButtonTrigger()
    {
        bool isTargetOn = MirrorTarget.activeSelf;
        bool isSubOn = MirrorSub.activeSelf;

        if (!isTargetOn)
        {
            if (isSubOn)
            {
                MirrorSub.SetActive(false);
                MirrorSub_Text.color = new Color(0.5f, 0.5f, 0.5f);
            }
            MirrorTarget.SetActive(true);
            MirrorTarget_Text.color = new Color(1f, 1f, 1f);
        }
        else
        {
            MirrorTarget.SetActive(false);
            MirrorTarget_Text.color = new Color(0.5f, 0.5f, 0.5f);
        }

    }
}
