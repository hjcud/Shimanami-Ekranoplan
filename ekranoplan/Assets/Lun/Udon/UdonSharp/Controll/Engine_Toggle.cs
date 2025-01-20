
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

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
        EngineStatus = false;
        RequestSerialization();
    }
    
    public void EngineOffNetwork()
    {
        EngineStatus = true;
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
