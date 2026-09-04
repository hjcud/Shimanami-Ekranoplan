<p align="center">
  <strong>日本語</strong> · <a href="./technical-overview.ko.md">한국어</a>
</p>

# Shimanami Ekranoplan 開発記録

飛行モデル、操縦入力、ネットワーク同期、モデル構成など、主な実装内容をまとめています。

## システム境界

| 分野 | 入力 | 処理 | 出力 |
| --- | --- | --- | --- |
| VR操縦 | 左右の手の位置・回転、グリップ、トリガー | 座席の向きに合わせて手をスロットルまたは操縦桿へ割り当て | 出力、ピッチ、ヨー、ロール |
| デスクトップ操縦 | Station、`WASDQE`、`Shift`、`Ctrl`、`Z`、`Space` | キー入力を共通の操縦状態へ変換 | 出力、姿勢、離席 |
| 飛行計算 | 操縦状態、エンジン状態、高度 | 速度、抗力、揚力、姿勢制限、環境座標を計算 | 飛行状態スナップショット |
| リモート表示 | Udon同期変数 | 水平位置補間を実装、回転・高度の表示反映は修正中 | マルチクライアント検証待ち |
| フィードバック | エンジン、スロットル、姿勢状態 | Animator、AudioSource、警告オブジェクトを更新 | コックピットの動き、音、警告 |

## 飛行モデルと環境移動

### 入力と出力範囲

- VR操縦桿は、手の初期回転と現在回転の差をピッチ・ヨー・ロールへ正規化します。
- デスクトップでは`W/S`をピッチ、`A/D`をヨー、`Q/E`をロールに使用します。
- スロットル範囲は通常`0.0–1.0`、後進`-0.3–0.001`、追加出力`0.999–1.25`です。
- 速度が`5`未満の場合、飛行計算ではピッチ・ヨー・ロール入力を適用しません。

### 分析シートから飛行モデルへ

飛行速度の計算シートでは、スロットルと速度を交差させて正味加速度を比較し、別の表で速度、高度、ピッチが浮揚へ与える影響を検討しました。現在の`AirplaneState`は、その計算で使った定数と式を同じ形で実装しています。

| 計算項目 | 計算シート | `AirplaneState` |
| --- | --- | --- |
| 推力加速度 | `スロットル × (101,920 N × 8) ÷ 286,000 kg` | 同じ推力と質量に`0.581`の反映率を適用 |
| 抗力減速度 | `速度² × 1.96 ÷ 286,000 kg` | `0.5 × 1.225 × 80 × 0.04 = 1.96`を抗力項に使用 |
| 基本浮揚 | `速度² × 0.0000005` | `速度² × 0.000001 × LiftMulti`、初期値は`0.5` |
| 高度補正 | `高度² × 0.0008` | `GravityLiftVector`で同じ式を減算 |
| ピッチ補正 | `ピッチ × 0.0113 × (速度 ÷ 550)` | `DirectionLiftVector`で同じ式を加算 |

コードでは正味加速度へ`ThrottleVecMulti`と`Time.fixedDeltaTime`を適用し、後進速度の減衰と下降姿勢の浮揚係数`0.8`を追加しています。各数値は実機性能の精密な再現ではなく、VRでの操縦感に合わせて調整しました。

### 参考資料

- Vazgriz, [*Creating a Flight Simulator in Unity3D (Part 1)*](https://youtu.be/7vAHo2B1zLc), YouTube, 2022-09-12 — 速度の二乗に比例する抗力・揚力とUnityでの飛行モデル構成
- Wikipedia, [*Lun-class ekranoplan*](https://en.wikipedia.org/wiki/Lun-class_ekranoplan) — 機体重量、寸法、エンジン、速度の諸元

### 姿勢制限と方向計算

ピッチとロールの最大角は高度に応じて広がり、それぞれ`15°`が上限です。ピッチとロールの組み合わせから追加のヨー回転を作り、算出した浮揚とピッチ方向を高度・水平移動へ反映します。

### 機体ではなく環境を動かす理由

初期実装では、機体と乗員をワールド座標上で直接移動させていました。原点から離れて座標値が大きくなるほど浮動小数点の精度が低下し、コックピットから見える水面や島を含む環境全体が細かく揺れる問題が発生しました。

現在は機体とコックピットを原点へ固定し、移動を表現するTransformを次のように分離しています。

- `MapRotationTarget`：計算中の目標姿勢
- `MapRotation`：乗員が見る環境の姿勢と高度
- `MapPosition`：水平移動座標

機体とコックピットは基準点に残り、環境が逆方向へ移動します。プレイヤー周辺のTransform座標が移動距離に応じて大きくならないため、距離とともに増える浮動小数点誤差を抑えられます。飛行計算と乗員空間はこの環境座標を使いますが、リモート表示は以下のとおり検証中です。

## ネットワーク状態と復元

### 権限と所有権

`AirplaneState`の現在の所有者だけが飛行状態を計算します。VRで最初に操縦桿を握ったユーザーは、操縦オブジェクトと`OwnerChangeTarget`の所有権を取得します。操縦中のPlayer IDは`TriggeredUserID`として同期します。

デスクトップ入力、操縦終了、ユーザー退出まで同じ所有権ライフサイクルを適用する作業は[Issue #3](https://github.com/hjcud/Shimanami-Ekranoplan/issues/3)で管理しています。

### 同期データ

| UdonSharp Behaviour | 同期する値 | 個数 |
| --- | --- | ---: |
| `Controller_Controll` | 操縦ユーザーID、ヨー、ピッチ、ロール | 4 |
| `Throttle_Controll` | 操縦ユーザーID、スロットル出力 | 2 |
| `Engine_Toggle` | エンジン状態 | 1 |
| `AirplaneState` | 速度、ピッチ・ロール、高度、移動ベクトル、回転、位置、ピッチ・ロール警告 | 9 |

現在は独自のビットパッキングや量子化を行わず、Udon同期変数として送信します。操縦値が変化したときと、所有者の飛行計算ループから`RequestSerialization()`を呼び出します。送信周期とデータ構成の整理は[Issue #4](https://github.com/hjcud/Shimanami-Ekranoplan/issues/4)で進めます。

### リモート補間と途中参加

- `OnDeserialization()`は`MapRotationTarget`の回転と位置を更新します。
- 非所有者の`FixedUpdate()`は水平の`MapPosition`だけを`0.2秒`の`SmoothDamp`で追従します。
- 位置誤差が`1,500`を超える場合は補間せず、最後の同期位置を適用します。
- ローカルユーザーが途中参加すると、最後の回転、高度、水平位置をすぐに適用します。

現在接続中の非所有者経路では、`MapRotationTarget`を表示用の`MapRotation`へ継続してコピーしていません。そのため回転・高度同期は完成済み機能ではなく、修正とマルチクライアント検証を[Issue #2](https://github.com/hjcud/Shimanami-Ekranoplan/issues/2)で追跡します。

## 実装の変化

### 飛行計算の統合

| スナップショット | 飛行計算スクリプト | 構成 |
| --- | ---: | --- |
| 初期構成 | 3 | `StateCal`、`RotationCal`、`AirplaneState`で状態を分担 |
| 現在の構成 | 1 | `AirplaneState`が速度、姿勢、揚力、環境移動、同期を処理 |

不要になった計算経路を削除し、飛行状態の生成元とネットワーク権限を一つのBehaviourから確認できる構成にしました。

### 計算権限の変更

初期コードではインスタンスマスターが飛行を計算していました。現在は飛行状態オブジェクトの所有者が計算するため、操縦権の移動と計算権限を同じ対象で管理できます。

### プラットフォーム別入力の追加

VRの手トラッキングに加えて、デスクトップ用Stationとキーボード入力を追加しました。座席を離れると操縦ユーザーIDと手の基準状態をリセットし、デスクトップStationはVRユーザーには表示しません。

## モデルとレンダリング

### 機体FBXの変化

| Gitスナップショット | ファイルサイズ | 基準 |
| --- | ---: | --- |
| 初期コックピットスナップショット | 31.62 MiB | ペダル、スロットル、操縦桿を統合した後の`Lun.fbx` |
| 最新プロジェクトスナップショット | 11.78 MiB | 現在の制作ブランチと同じモデルリビジョン |
| 変化 | **62.7%削減** | Gitオブジェクトの元バイト数 |

これはモデルソースの保存サイズ比較です。ポリゴン数、実行時メモリ、ドローコールの変化を示す値ではありません。

### シーン構成

| 項目 | 値 |
| --- | ---: |
| 機体PrefabのMesh Renderer | 127 |
| 機体PrefabのMesh Filter | 127 |
| 機体PrefabのMesh Collider | 3 |
| 機体PrefabのLOD Group | 0 |
| メインシーンのMesh Renderer | 93 |
| メインシーンのMesh Collider | 4 |
| ベイク済みライトマップ | 1セット |
| Reflection Probeテクスチャ | 18 |

シーンにはベイク済み照明データとReflection Probeが接続されています。機体PrefabにはLOD Groupがなく、シーンにもオクルージョンカリングデータは接続されていません。

## 外部フライトディスプレイの統合

コックピットには、ときわの[「VRChat用 ワイワイフライトディスプレイシステム」](https://tokiwa-carlo.booth.pm/items/6424462)を使用しています。`FLIGHT_DISPLAY`が`AirplaneState`のピッチ、ロール、高度、速度、回転、位置を読み取り、姿勢計、高度計、速度計、方位リング、瀬戸内海マップを更新します。

販売ページの利用条件に従い、製品のスクリプト、Prefab、UIテクスチャ、フォント、Render Textureはリポジトリに含めていません。本リポジトリのMIT Licenseもこれらのファイルには適用されません。

## コード索引

| ファイル | 役割 |
| --- | --- |
| [`AirplaneState.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/PlaneMovement/AirplaneState.cs) | 所有者側の飛行計算、環境Transform、同期、リモート復元 |
| [`Controller_Controll.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/Controller_Controll.cs) | VR・デスクトップ姿勢入力と操縦所有権 |
| [`Throttle_Controll.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/Throttle_Controll.cs) | スロットル入力、出力範囲、アニメーション、音量 |
| [`Engine_Toggle.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/Engine_Toggle.cs) | エンジンのネットワークイベント、音声、アニメーション |
| [`ColliderStayCheck.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/ColliderStayCheck.cs) | VR操縦範囲 |
| [`DesktopSeatCheck.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/DesktopSeatCheck.cs) | デスクトップStation |
| [`MirrorToggle.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Extra/MirrorToggle.cs) | ローカルのミラー品質選択 |

[READMEへ戻る](../README.md)
