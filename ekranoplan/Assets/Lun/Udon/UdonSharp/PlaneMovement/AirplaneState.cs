
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// 소유권자에서 비행 상태를 계산하고 원격 클라이언트에 같은 월드 상태를 표시
/// </summary>
/// <remarks>
/// 기체는 원점에 고정하고 환경을 비행 반대 방향으로 이동
/// MapRotation은 VRCObjectSync의 고도·회전 전송 기준
/// MapPosition은 타일 좌표와 타일 내부 좌표를 조합해 수평 이동을 복원
/// MapRotationTarget은 소유권자에서 다음 회전을 계산할 때만 사용하는 작업용 Transform
/// </remarks>
public class AirplaneState : UdonSharpBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector 참조
    // -------------------------------------------------------------------------

    public Throttle_Controll Throttle_Controll;
    public Controller_Controll Controller_Controll;
    public Engine_Toggle Engine_Toggle;

    [Tooltip("VRCObjectSync가 고도와 회전을 전송하는 환경 기준 Transform")]
    public Transform MapRotation = null;
    [Tooltip("소유권자가 다음 회전을 계산할 때만 사용하는 작업용 Transform")]
    public Transform MapRotationTarget = null;
    [Tooltip("타일 내부 수평 이동을 적용하는 MapRotation의 하위 Transform")]
    public Transform MapPosition = null;

    public GameObject RollAlarm = null;
    public GameObject PitchAlarm = null;

    public Text DebugText;

    // -------------------------------------------------------------------------
    // 비행 계산 설정
    // -------------------------------------------------------------------------

    public float ThrottleVecMulti = 10f;
    public float PitchThrustVecMulti = 0.15f;
    public float YawThrustVecMulti = 0.25f;
    public float RollThrustVecMulti = 0.3f;
    public float LiftMulti = 0.5f;
    public float RotationAddMulti = 0.001f;
    public float MovebySpeedMulti = 70f;

    [Header("Remote Position Sync")]
    [Tooltip("원격 사용자가 수신한 수평 위치를 따라가는 시간")]
    public float RemotePositionSmoothTime = 0.2f;
    [Tooltip("보간 없이 최신 상태로 맞추는 위치 차이(0이면 비활성화)")]
    public float RemotePositionSnapDistance = 1500f;

    [Header("Floating Origin")]
    [Tooltip("지도 타일 한 변의 길이(MapPosition은 이 값의 절반 범위 안에서 유지)")]
    public float MapTileSize = 10000f;

    // -------------------------------------------------------------------------
    // 동기화 상태와 런타임 버퍼
    // -------------------------------------------------------------------------

    [UdonSynced] public float AirplaneSpeed = 0;
    [UdonSynced] public float PitchAngle;
    [UdonSynced] public float RollAngle;
    // 고도와 회전 동기값은 상태 확인용으로 유지하고 원격 MapRotation에는 적용하지 않음
    // 화면에 보이는 Transform은 VRCObjectSync에서만 갱신
    [UdonSynced] public float SyncedAirHight;
    [UdonSynced] public Vector3 movement;
    [UdonSynced] public Vector3 SyncedRotation;
    // 전체 수평 좌표는 정수 타일 번호와 범위가 제한된 SyncedPosition의 합으로 표현
    [UdonSynced] public Vector3 SyncedPosition;
    [UdonSynced] public int SyncedMapTileX;
    [UdonSynced] public int SyncedMapTileZ;
    [UdonSynced] public bool PitchLimitAlarm;
    [UdonSynced] public bool RollLimitAlarm;

    private Vector3 remotePositionVelocity = Vector3.zero;
    private Transform[] mapContents;
    private Vector3[] mapContentStartPositions;
    private Transform mapTilesRoot;
    private Transform[] mapTiles;
    private Vector3[] mapTileStartPositions;
    private int displayedMapTileX;
    private int displayedMapTileZ;
    private bool receivedInitialPosition;
    private bool wasLocalOwner;
    private bool mapContentsCached;

    private void Start()
    {
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (!Utilities.IsValid(localPlayer)) return;

        CacheMapContents();
        displayedMapTileX = SyncedMapTileX;
        displayedMapTileZ = SyncedMapTileZ;
        ApplyMapContentOffset();

        wasLocalOwner = Networking.IsOwner(localPlayer, this.gameObject);
        if (wasLocalOwner)
        {
            // 두 네트워크 오브젝트의 소유자를 맞춘 뒤에만 권위 상태 시작
            EnsureMapRotationOwnership(localPlayer);
            ContinueFromCurrentWorldState();
            RebaseOwnerPosition();
            PublishWorldState();
        }
    }
    
    public void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        bool ownsAirplaneState = Utilities.IsValid(localPlayer)
            && Networking.IsOwner(localPlayer, this.gameObject);

        if (ownsAirplaneState)
        {
            // AirplaneState와 MapRotation의 소유자가 다르면 서로 다른 사용자가 같은
            // 비행 상태를 갱신하므로 소유권이 일치할 때만 계산 진행
            if (Networking.IsOwner(localPlayer, MapRotation.gameObject)) CalculateMovement(dt);
        }
        else
        {
            UpdateRemotePosition();
        }

        if (PitchLimitAlarm)
        {
            // 피치 제한 상태에서 저속 경고가 계속 켜지지 않도록 속도를 함께 확인
            if (AirplaneSpeed > 150) {
                PitchAlarm.SetActive(true);
            }
            else PitchAlarm.SetActive(false);
        }
        else PitchAlarm.SetActive(false);
        
        if (RollLimitAlarm)
        {
            // 롤 제한 경고도 피치 경고와 같은 속도 기준 사용
            if (AirplaneSpeed > 150) {
                RollAlarm.SetActive(true);
            }
            else RollAlarm.SetActive(false);
        }
        else RollAlarm.SetActive(false);
    }

    /// <summary>
    /// 소유권자의 조종 입력으로 다음 비행 상태를 계산하고 환경을 반대 방향으로 이동
    /// </summary>
    /// <param name="dt">한 번의 물리 계산에 사용할 시간 간격</param>
    public void CalculateMovement(float dt)
    {
        // 각 조작 장치의 출력 계약에 따른 입력 범위
        // 입력 범위를 바꾸면 아래 계수도 함께 재조정
        float throttle = Throttle_Controll.throttlePower;   // -0.3 ~ 1.25
        float YawThrustVec = -Controller_Controll.yaw;      // -0.5 ~ 0.5 (-L / +R)
        float PitchThrustVec = Controller_Controll.pitch;   // -0.5 ~ 0.5 (-D / +U)
        float RollThrustVec = Controller_Controll.roll;     // -0.5 ~ 0.5 (-L / +R)

        if (!Engine_Toggle.EngineStatus)
        {
            throttle = 0;
        }
        // 정지에 가까운 속도에서는 조종면 효과를 끄고 미세한 자세 흔들림 방지
        if (AirplaneSpeed < 5f)
        {
            YawThrustVec = 0;
            PitchThrustVec = 0;
            RollThrustVec = 0;
        }

        // 엔진 8기의 추력과 속도 제곱 항력을 기체 질량으로 나눠 속도를 적분
        // 101920N은 엔진 한 기의 추력, 286000kg은 기체 질량
        float acceleration = throttle * 101920f * 8f;

        // 1.96 = 0.5 × 공기 밀도(1.225) × 기준 면적(80) × 항력 계수(0.04)
        float SpeedDrag = Mathf.Pow(AirplaneSpeed, 2f) * 1.96f;

        AirplaneSpeed += ((acceleration * 0.581f) - SpeedDrag) / 286000 * ThrottleVecMulti * dt;
        // 역방향 이동은 허용하되 매 계산마다 감쇠해 낮은 속도로 제한
        if (AirplaneSpeed < 0) AirplaneSpeed /= 2;
        
        // 목표 Transform에서 자세를 먼저 계산하고 제한값 적용 뒤 MapRotation에 한 번만 반영
        Vector3 RotationVector = Vector3.zero;
        RotationVector.x = PitchThrustVec * PitchThrustVecMulti;
        RotationVector.y = -YawThrustVec * YawThrustVecMulti;
        RotationVector.z = RollThrustVec * RollThrustVecMulti;
        MapRotationTarget.Rotate(RotationVector, Space.World);

        float AirHight;
        AirHight = -MapRotation.position.y;
        // 지면에 가까울수록 허용 피치와 롤을 줄여 환경이 기체 위로 넘어오는 현상 방지
        float PitchAngleLimit;
        if (AirHight >= 7.5f) PitchAngleLimit = 15f;
        else PitchAngleLimit = AirHight * 2f;
        float RollAngleLimit;
        if (AirHight >= 6f) RollAngleLimit = 15f;
        else RollAngleLimit = AirHight * 2.5f;

        // 월드 전방 벡터를 기체의 수평면에 투영해 현재 피치를 계산
        Vector3 projPitchVector = Vector3.ProjectOnPlane(Vector3.forward * Mathf.Abs(AirplaneSpeed) / 550, MapRotationTarget.up);
        PitchAngle = Vector3.SignedAngle(projPitchVector, Vector3.forward, Vector3.left);
        if (PitchAngle > PitchAngleLimit || PitchAngle < -PitchAngleLimit)
        {
            if (PitchAngle > PitchAngleLimit) MapRotationTarget.Rotate(new Vector3(-PitchAngle + PitchAngleLimit, 0f, 0f), Space.World);
            else MapRotationTarget.Rotate(new Vector3(-PitchAngle - PitchAngleLimit, 0f, 0f), Space.World);

            projPitchVector = Vector3.ProjectOnPlane(Vector3.forward * Mathf.Abs(AirplaneSpeed) / 550, MapRotationTarget.up);
            PitchAngle = Vector3.SignedAngle(projPitchVector, Vector3.forward, Vector3.left);
            PitchLimitAlarm = true;
        } else PitchLimitAlarm = false;
        
        // 같은 기준면에서 롤을 구하고 현재 고도에서 허용되는 각도로 제한
        Vector3 projRollVector = Vector3.ProjectOnPlane(Vector3.right, MapRotationTarget.up);
        RollAngle = Vector3.SignedAngle(projRollVector, Vector3.up, Vector3.forward) - 90;
        if (RollAngle > RollAngleLimit || RollAngle < -RollAngleLimit)
        {
            if (RollAngle > RollAngleLimit) MapRotationTarget.Rotate(new Vector3(0f, 0f, RollAngle - RollAngleLimit), Space.World);
            else MapRotationTarget.Rotate(new Vector3(0f, 0f, RollAngle + RollAngleLimit), Space.World);

            projRollVector = Vector3.ProjectOnPlane(Vector3.right, MapRotationTarget.up);
            RollAngle = Vector3.SignedAngle(projRollVector, Vector3.up, Vector3.forward) - 90;
            RollLimitAlarm = true;
        } else RollLimitAlarm = false;

        // 피치와 롤의 조합으로 선회 방향의 추가 yaw 생성
        float RotationAdd = RollAngle * PitchAngle * RotationAddMulti;
        MapRotationTarget.Rotate(new Vector3(0f, RotationAdd, 0f), Space.World);

        // 하강 중에는 양력 계수를 낮춰 상승 때와 같은 힘이 생기는 현상 방지
        float LiftFacingMulti = PitchAngle < 0f ? 0.8f : 1f;
        
        // 속도 기반 양력에 고도 복원력과 피치 방향 성분을 조합
        float AirplaneLift = AirplaneSpeed * AirplaneSpeed * 0.000001f * LiftMulti * LiftFacingMulti;
        float GravityLiftVector = Mathf.Pow(AirHight, 2f) * 0.0008f;
        float DirectionLiftVector = PitchAngle * 0.0113f * AirplaneSpeed / 550;
        AirplaneLift -= GravityLiftVector - DirectionLiftVector;

        MapRotation.eulerAngles = MapRotationTarget.eulerAngles;

        // 기체를 원점에 두기 위해 고도 변화는 환경의 -Y 이동으로 표현
        if (MapRotation.position.y - AirplaneLift > 0f) MapRotation.position = new Vector3(0f, 0f, 0f);
        else MapRotation.position += new Vector3(0f, -AirplaneLift, 0f);
        
        float MovebySpeed = Vector3.Magnitude(projPitchVector);
        float MoveVecRot = Vector3.SignedAngle(projPitchVector, MapRotation.forward, MapRotation.up);
        Vector3 moveDirection = Quaternion.AngleAxis(MoveVecRot, Vector3.down) * (Vector3.forward * MovebySpeed);
        movement = -moveDirection * dt * MovebySpeedMulti;
        // 수평 이동도 같은 구조를 유지해 기체 대신 환경을 비행 반대 방향으로 이동
        MapPosition.localPosition += new Vector3(movement.x, 0f, movement.z);
        RebaseOwnerPosition();

        DebugText.text = ">>Controll\nSpeed: " + AirplaneSpeed.ToString("F5")
        + "\nLift: " + AirplaneLift.ToString("F5")
        + "\nPitch: " + PitchAngle.ToString("F5")
        + "\nRoll: "+ RollAngle.ToString("F5")
        + "\nRotAdd: "+ RotationAdd.ToString("F5")
        + "\nHight: " + MapRotation.position.y.ToString("F5")
        + "\nMoveVecRot: " + MoveVecRot.ToString("F5")
        + "\nMap Tile: (" + SyncedMapTileX + ", " + SyncedMapTileZ + ")"
        + "\nCoordinate: " + MapPosition.localPosition.ToString("F5");

        PublishWorldState();
    }

    public override void OnDeserialization()
    {
        UpdateCordinate();
    }

    /// <summary>
    /// 원격 사용자가 처음 받은 수평 상태를 보간 기준점으로 초기화
    /// </summary>
    public void UpdateCordinate()
    {
        if (!Networking.IsOwner(Networking.LocalPlayer, this.gameObject)
            && !receivedInitialPosition)
        {
            // 첫 상태는 보간 시작점이 없어 즉시 적용하고 이후 수신값은 FixedUpdate에서 보간
            // MapRotation은 VRCObjectSync에서만 갱신
            receivedInitialPosition = true;
            SnapRemotePosition();
        }
    }

    public override void OnOwnershipTransferred(VRCPlayerApi newOwner)
    {
        remotePositionVelocity = Vector3.zero;
        bool locallyOwnedBeforeTransfer = wasLocalOwner;
        wasLocalOwner = newOwner.isLocal;

        if (newOwner.isLocal)
        {
            // 새 소유자는 현재 화면에 보이는 타일 좌표부터 이어받아 마지막 수신값으로
            // 되돌아가며 환경이 한 타일만큼 튀는 현상 방지
            SyncedMapTileX = displayedMapTileX;
            SyncedMapTileZ = displayedMapTileZ;
            EnsureMapRotationOwnership(newOwner);
            ContinueFromCurrentWorldState();
            RebaseOwnerPosition();
            PublishWorldState();
        }
        else if (locallyOwnedBeforeTransfer)
        {
            // 소유권을 넘긴 직후의 화면 상태를 원격 보간 시작점으로 사용
            // 중간 입장으로 처리되어 다음 스냅샷에 즉시 이동하는 현상 방지
            receivedInitialPosition = true;
            displayedMapTileX = SyncedMapTileX;
            displayedMapTileZ = SyncedMapTileZ;
            ApplyMapContentOffset();
        }
    }

    private void UpdateRemotePosition()
    {
        if (!receivedInitialPosition) return;

        Vector3 remoteTargetPosition = GetRemoteTargetPosition();
        float distance = Vector3.Distance(MapPosition.localPosition, remoteTargetPosition);
        if (RemotePositionSmoothTime <= 0f
            || (RemotePositionSnapDistance > 0f && distance > RemotePositionSnapDistance))
        {
            SnapRemotePosition();
            return;
        }

        MapPosition.localPosition = Vector3.SmoothDamp(
            MapPosition.localPosition,
            remoteTargetPosition,
            ref remotePositionVelocity,
            RemotePositionSmoothTime);
        RebaseRemotePosition();
    }

    private void SnapRemotePosition()
    {
        displayedMapTileX = SyncedMapTileX;
        displayedMapTileZ = SyncedMapTileZ;
        MapPosition.localPosition = SyncedPosition;
        remotePositionVelocity = Vector3.zero;
        ApplyMapContentOffset();
    }

    private Vector3 GetRemoteTargetPosition()
    {
        if (MapTileSize <= 0f) return SyncedPosition;

        // 수신한 타일 내부 좌표를 현재 표시 중인 타일 좌표계로 변환
        // 4990에서 다음 타일의 -4990을 받으면 목표값을 -4990이 아닌 5010으로 해석
        return SyncedPosition + new Vector3(
            (SyncedMapTileX - displayedMapTileX) * MapTileSize,
            0f,
            (SyncedMapTileZ - displayedMapTileZ) * MapTileSize);
    }

    private void RebaseOwnerPosition()
    {
        if (MapTileSize <= 0f) return;

        Vector3 localPosition = MapPosition.localPosition;
        int tileShiftX = GetTileShift(localPosition.x);
        int tileShiftZ = GetTileShift(localPosition.z);
        if (tileShiftX == 0 && tileShiftZ == 0) return;

        localPosition.x -= tileShiftX * MapTileSize;
        localPosition.z -= tileShiftZ * MapTileSize;
        MapPosition.localPosition = localPosition;

        // MapPosition에서 뺀 타일 수를 정수 좌표와 지도 콘텐츠에 더해 월드 위치 유지
        // 네트워크로 보내는 실수 좌표만 타일 절반 범위에 유지
        SyncedMapTileX += tileShiftX;
        SyncedMapTileZ += tileShiftZ;
        displayedMapTileX = SyncedMapTileX;
        displayedMapTileZ = SyncedMapTileZ;
        ApplyMapContentOffset();
    }

    private void RebaseRemotePosition()
    {
        if (MapTileSize <= 0f) return;

        Vector3 localPosition = MapPosition.localPosition;
        int tileShiftX = GetTileShift(localPosition.x);
        int tileShiftZ = GetTileShift(localPosition.z);
        if (tileShiftX == 0 && tileShiftZ == 0) return;

        localPosition.x -= tileShiftX * MapTileSize;
        localPosition.z -= tileShiftZ * MapTileSize;
        MapPosition.localPosition = localPosition;

        // 원격 사용자는 권위 상태를 바꾸지 않고 표시 좌표계만 다음 타일로 전환
        displayedMapTileX += tileShiftX;
        displayedMapTileZ += tileShiftZ;
        ApplyMapContentOffset();
    }

    private int GetTileShift(float coordinate)
    {
        return Mathf.FloorToInt((coordinate + MapTileSize * 0.5f) / MapTileSize);
    }

    private void CacheMapContents()
    {
        int childCount = MapPosition.childCount;
        mapContents = new Transform[childCount];
        mapContentStartPositions = new Vector3[childCount];
        mapTilesRoot = MapPosition.Find("Tiles");

        for (int i = 0; i < childCount; i++)
        {
            Transform mapContent = MapPosition.GetChild(i);
            mapContents[i] = mapContent;
            mapContentStartPositions[i] = mapContent.localPosition;
        }

        if (Utilities.IsValid(mapTilesRoot))
        {
            int tileCount = mapTilesRoot.childCount;
            mapTiles = new Transform[tileCount];
            mapTileStartPositions = new Vector3[tileCount];

            for (int i = 0; i < tileCount; i++)
            {
                Transform mapTile = mapTilesRoot.GetChild(i);
                mapTiles[i] = mapTile;
                // Tiles 오브젝트의 180도 회전에 의존하지 않도록 각 타일의 시작 위치를
                // MapPosition 좌표계로 변환해 보관
                mapTileStartPositions[i] = MapPosition.InverseTransformPoint(mapTile.position);
            }
        }

        mapContentsCached = true;
    }

    private void ApplyMapContentOffset()
    {
        if (!mapContentsCached || MapTileSize <= 0f) return;

        Vector3 tileOffset = new Vector3(
            displayedMapTileX * MapTileSize,
            0f,
            displayedMapTileZ * MapTileSize);

        // MapPosition을 한 타일 되돌린 만큼 바다와 금지 구역을 반대로 보정
        // 두 Transform의 합을 유지해 경계 프레임의 화면상 위치 변화 방지
        for (int i = 0; i < mapContents.Length; i++)
        {
            if (mapContents[i] == mapTilesRoot)
            {
                mapContents[i].localPosition = mapContentStartPositions[i];
            }
            else
            {
                mapContents[i].localPosition = mapContentStartPositions[i] + tileOffset;
            }
        }

        if (!Utilities.IsValid(mapTilesRoot)) return;

        // Tiles 부모는 원래 위치에 두고 각 타일을 MapPosition 좌표계에서 배치
        // 회전된 부모 좌표에서 큰 값끼리 상쇄되며 생기는 정밀도 손실 방지
        for (int i = 0; i < mapTiles.Length; i++)
        {
            mapTiles[i].position = MapPosition.TransformPoint(mapTileStartPositions[i] + tileOffset);
        }
    }

    private void EnsureMapRotationOwnership(VRCPlayerApi owner)
    {
        // 위치 계산 주체와 VRCObjectSync 전송 주체가 달라지는 상태 방지
        if (!Networking.IsOwner(owner, MapRotation.gameObject))
        {
            Networking.SetOwner(owner, MapRotation.gameObject);
        }
    }

    private void ContinueFromCurrentWorldState()
    {
        // MapRotationTarget은 네트워크에 노출하지 않고 현재 표시 자세에서 계산만 계속
        MapRotationTarget.position = MapRotation.position;
        MapRotationTarget.rotation = MapRotation.rotation;
        receivedInitialPosition = true;
    }

    private void PublishWorldState()
    {
        // 타일 번호와 타일 내부 위치를 같은 직렬화 요청에 담아 경계 상태를 함께 전송
        SyncedAirHight = MapRotation.position.y;
        SyncedRotation = MapRotation.eulerAngles;
        SyncedPosition = MapPosition.localPosition;
        RequestSerialization();
    }
}
