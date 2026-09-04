<p align="center">
  <a href="./technical-overview.md">日本語</a> · <strong>한국어</strong> · <a href="./technical-overview.en.md">English</a>
</p>

# Shimanami Ekranoplan 개발 기록

비행 모델, 조종 입력과 네트워크 동기화 등 주요 구현 내용을 정리했습니다.

## 시스템 경계

| 영역 | 입력 | 처리 | 출력 |
| --- | --- | --- | --- |
| VR 조종 | 좌·우 손 위치와 회전, 그립·트리거 | 조종석 방향에 맞춰 손을 스로틀 또는 조종간에 연결 | 출력, 피치, 요, 롤 |
| 데스크톱 조종 | Station, `WASDQE`, `Shift`, `Ctrl`, `Z`, `Space` | 키 입력을 같은 조종 상태로 변환 | 출력, 피치, 요, 롤, 하차 |
| 비행 계산 | 조종 상태, 엔진 상태, 고도 | 속도·항력·양력·자세 제한과 환경 좌표 계산 | 비행 상태 스냅샷 |
| 원격 표시 | Udon 동기화 변수, `VRCObjectSync` | 수평 위치 보간, 환경 회전·고도 동기화 | 소유권 이전과 중간 입장 복원 |
| 피드백 | 엔진·스로틀·자세 상태 | Animator, AudioSource, 경고 오브젝트 갱신 | 조종석 애니메이션·소리·경고 |

## 비행 모델과 환경 이동

### 입력과 출력 범위

- VR 조종간은 손의 기준 회전과 현재 회전의 차이를 피치·요·롤로 정규화합니다.
- 데스크톱은 `W/S` 피치, `A/D` 요, `Q/E` 롤을 사용합니다.
- 스로틀은 기본 `0.0–1.0`, 후진 `-0.3–0.001`, 추가 출력 `0.999–1.25` 범위입니다.
- 속도가 `5`보다 낮으면 피치·요·롤 입력을 비행 계산에서 적용하지 않습니다.

### 비행 모델 계산 기준

`AirplaneState`는 매 물리 프레임에서 엔진 출력과 항력으로 속도를 갱신하고, 속도·고도·피치로 부양 변화를 계산합니다. 아래 식은 현재 코드와 코드에 선언된 필드 기본값에서 직접 확인되는 구현 기준이며, 계산 결과는 게임 내부 단위로 사용합니다.

| 계산 항목 | 구현식 | 역할 |
| --- | --- | --- |
| 엔진 출력 | `스로틀 × 101,920 × 8 × 0.581` | 8기 엔진의 출력을 속도 계산에 반영 |
| 항력 | `속도² × 1.96` | 속도가 높아질수록 커지는 감속항 |
| 속도 변화 | `((엔진 출력 - 항력) ÷ 286,000) × ThrottleVecMulti × Δt` | 기본 `ThrottleVecMulti = 10`으로 프레임별 속도 갱신 |
| 기본 부양 | `속도² × 0.000001 × LiftMulti` | 기본 `LiftMulti = 0.5`; 하강 자세에서는 `0.8`을 추가 적용 |
| 고도 보정 | `고도² × 0.0008` | 고도가 높아질수록 기본 부양에서 감산 |
| 피치 보정 | `피치각 × 0.0113 × (속도 ÷ 550)` | 기수의 상하 방향을 부양 변화에 반영 |

후진 상태에서는 계산된 속도를 절반으로 감쇠하고, 속도가 `5`보다 낮을 때는 자세 입력을 적용하지 않습니다. 이 식은 실제 기체의 성능을 정밀하게 재현하는 공력 모델이 아니라 VR에서의 조종감을 만들기 위해 단순화한 모델입니다.

### 참고 자료

- Vazgriz, [*Creating a Flight Simulator in Unity3D (Part 1)*](https://youtu.be/7vAHo2B1zLc), YouTube, 2022-09-12 — 속도 제곱에 비례하는 항력·양력과 Unity 비행 모델 구성
- Wikipedia, [*Lun-class ekranoplan*](https://en.wikipedia.org/wiki/Lun-class_ekranoplan) — 기체 중량, 크기, 엔진과 속도 제원

### 자세 제한과 방향 계산

피치와 롤의 최대 각도는 고도에 따라 증가하고 각각 최대 `15°`로 제한됩니다. 피치와 롤의 조합으로 추가 요 회전을 만들며, 계산된 부양과 피치 방향을 고도·수평 이동에 반영합니다.

### 기체가 아니라 환경을 움직이는 이유

초기 구현은 기체와 탑승자를 월드 좌표 위로 직접 이동시켰습니다. 원점에서 멀어질수록 좌표 값이 커지면서 부동 소수점 정밀도가 낮아졌고, 조종석에서 바라본 수면과 섬을 포함한 환경 전체가 떨려 보이는 문제가 생겼습니다.

이를 해결하기 위해 기체와 조종석은 원점에 고정하고, 이동을 표현하는 Transform을 다음과 같이 분리했습니다.

- `MapRotationTarget`: 계산 중인 목표 자세
- `MapRotation`: 탑승자가 보는 환경 자세와 고도
- `MapPosition`: 수평 이동 좌표

기체·조종석은 기준점에 남고 환경이 반대 방향으로 이동합니다. 플레이어 주변의 실제 Transform 좌표가 커지지 않으므로 이동 거리에 따라 증가하는 부동 소수점 오차를 줄일 수 있습니다. 비행 계산과 탑승자 공간은 이 환경 좌표를 사용합니다.

## 네트워크 상태와 복원

### 권한과 소유권

`AirplaneState`의 현재 소유자만 비행 상태를 계산합니다. VR 조종간을 처음 잡은 사용자는 조종 오브젝트와 `OwnerChangeTarget`의 소유권을 함께 가져옵니다. 조종 중인 사용자 ID는 `TriggeredUserID`로 공유합니다.

데스크톱 입력과 조종 종료·퇴장까지 같은 소유권 수명주기를 적용하는 작업은 [Issue #3](https://github.com/hjcud/Shimanami-Ekranoplan/issues/3)에서 추적합니다.

### 동기화 데이터

| UdonSharp 동작 | 동기화 값 | 개수 |
| --- | --- | ---: |
| `Controller_Controll` | 조종 사용자 ID, 요, 피치, 롤 | 4 |
| `Throttle_Controll` | 조종 사용자 ID, 스로틀 출력 | 2 |
| `Engine_Toggle` | 엔진 상태 | 1 |
| `AirplaneState` | 속도, 피치·롤, 고도, 이동 벡터, 회전, 위치, 피치·롤 경고 | 9 |
| `MapRotation`의 `VRCObjectSync` | 환경의 회전과 고도 Transform | — |

현재 값은 별도 비트 패킹이나 양자화 없이 Udon 동기화 변수로 전송합니다. 수평 위치는 `AirplaneState`가 전달하고, 환경의 회전과 고도는 `MapRotation`에 연결된 `VRCObjectSync`가 전달합니다. 조종 값이 바뀔 때와 소유자의 비행 계산 루프에서 `RequestSerialization()`을 호출합니다. 동기화 주기와 데이터 구성을 정리하는 작업은 [Issue #4](https://github.com/hjcud/Shimanami-Ekranoplan/issues/4)에서 진행합니다.

### 원격 보간과 중간 입장

- `OnDeserialization()`에서 첫 수평 위치를 받으면 `MapPosition`을 마지막 동기화 위치로 복원합니다.
- 이후 비소유자의 `FixedUpdate()`는 수평 위치를 기본 `0.2초`의 `SmoothDamp`로 따라갑니다.
- 위치 오차가 기본 `1,500` 내부 단위를 넘으면 보간하지 않고 마지막 동기화 위치로 이동합니다.
- 환경의 회전과 고도는 `MapRotation`의 `VRCObjectSync`가 원격 사용자에게 반영합니다.
- 소유권을 넘겨받은 사용자는 현재 `MapRotation` 상태를 계산용 `MapRotationTarget`으로 이어받은 뒤 새 상태를 발행합니다.

이 구조는 코드와 월드의 `VRCObjectSync` 구성을 기준으로 정리했습니다. 소유권 교대와 중간 입장을 포함한 실제 VRChat 멀티클라이언트 검증은 [Issue #2](https://github.com/hjcud/Shimanami-Ekranoplan/issues/2)에서 추적합니다.

## 구현 변화

### 비행 계산 통합

| 스냅샷 | 비행 계산 스크립트 | 구조 |
| --- | ---: | --- |
| 초기 구조 | 3 | `StateCal`, `RotationCal`, `AirplaneState`가 상태를 나눠 계산 |
| 현재 구조 | 1 | `AirplaneState`가 속도, 자세, 양력, 환경 이동과 동기화를 처리 |

사용하지 않는 계산 경로를 제거해 비행 상태의 생성 위치와 네트워크 권한을 하나의 동작에서 확인할 수 있게 했습니다.

### 계산 권한 변경

초기 코드는 인스턴스 마스터가 비행을 계산했습니다. 현재 코드는 기체 상태 오브젝트의 소유자가 계산하므로 조종권 교대와 계산 권한을 같은 대상으로 관리할 수 있습니다.

### 조종 장치와 플랫폼 입력

VR 손 추적만 있던 조종 흐름에 데스크톱 Station과 키보드 입력을 추가했습니다. 좌석에서 나갈 때 조종 사용자 ID와 기준 손 위치·회전을 초기화하며, 데스크톱 Station은 VR 사용자에게 표시하지 않습니다.

## 외부 비행 디스플레이 통합

조종석에는 ときわ의 [「VRChat用 ワイワイフライトディスプレイシステム」](https://tokiwa-carlo.booth.pm/items/6424462)을 사용했습니다. `FLIGHT_DISPLAY`가 `AirplaneState`의 피치·롤·고도·속도·회전·위치를 읽어 자세계, 고도계, 속도계, 방향 링과 세토우치 지도를 갱신합니다.

판매 페이지의 이용 조건에 따라 제품의 스크립트, 프리팹, UI 텍스처, 폰트와 렌더 텍스처는 저장소에 포함하지 않으며, 이 저장소의 MIT License도 해당 파일에 적용되지 않습니다.

## 코드 인덱스

| 파일 | 책임 |
| --- | --- |
| [`AirplaneState.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/PlaneMovement/AirplaneState.cs) | 소유자 비행 계산, 환경 Transform, 동기화와 원격 복원 |
| [`Controller_Controll.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/Controller_Controll.cs) | VR·데스크톱 자세 입력과 조종 소유권 |
| [`Throttle_Controll.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/Throttle_Controll.cs) | 스로틀 입력, 출력 단계, 애니메이션과 음량 |
| [`Engine_Toggle.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/Engine_Toggle.cs) | 엔진 네트워크 이벤트, 오디오와 애니메이션 |
| [`ColliderStayCheck.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/ColliderStayCheck.cs) | VR 조종 영역 |
| [`DesktopSeatCheck.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/DesktopSeatCheck.cs) | 데스크톱 Station |
| [`MirrorToggle.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Extra/MirrorToggle.cs) | 로컬 미러 품질 선택 |

[README로 돌아가기](../README.ko.md)
