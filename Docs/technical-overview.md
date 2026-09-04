<p align="center">
  <strong>日本語</strong> · <a href="./technical-overview.ko.md">한국어</a> · <a href="./technical-overview.en.md">English</a>
</p>

# Shimanami Ekranoplan 開発記録

飛行モデル、操縦入力、ネットワーク同期など、主な実装内容をまとめています。

## システム境界

| 分野 | 入力 | 処理 | 出力 |
| --- | --- | --- | --- |
| VR操縦 | 左右の手の位置・回転、グリップ、トリガー | 座席の向きに合わせて手をスロットルまたは操縦桿へ割り当て | 出力、ピッチ、ヨー、ロール |
| デスクトップ操縦 | Station、`WASDQE`、`Shift`、`Ctrl`、`Z`、`Space` | キー入力を共通の操縦状態へ変換 | 出力、姿勢、離席 |
| 飛行計算 | 操縦状態、エンジン状態、高度 | 速度、抗力、揚力、姿勢制限、環境座標を計算 | 飛行状態スナップショット |
| リモート表示 | Udon同期変数、`VRCObjectSync` | 水平位置補間、環境の回転・高度同期 | 所有権移動と途中参加時の復元 |
| フィードバック | エンジン、スロットル、姿勢状態 | Animator、AudioSource、警告オブジェクトを更新 | コックピットの動き、音、警告 |

## 飛行モデルと環境移動

### 入力と出力範囲

- VR操縦桿は、手の初期回転と現在回転の差をピッチ・ヨー・ロールへ正規化します。
- デスクトップでは`W/S`をピッチ、`A/D`をヨー、`Q/E`をロールに使用します。
- スロットル範囲は通常`0.0–1.0`、後進`-0.3–0.001`、追加出力`0.999–1.25`です。
- 速度が`5`未満の場合、飛行計算ではピッチ・ヨー・ロール入力を適用しません。

### 飛行モデルの計算基準

`AirplaneState`は物理フレームごとにエンジン出力と抗力から速度を更新し、速度・高度・ピッチから浮揚の変化を計算します。以下は現在のコードとコード上で宣言されたフィールドの初期値から直接確認できる実装基準で、計算結果はゲーム内の単位として扱います。

| 計算項目 | 実装式 | 役割 |
| --- | --- | --- |
| エンジン出力 | `スロットル × 101,920 × 8 × 0.581` | 8基分のエンジン出力を速度計算へ反映 |
| 抗力 | `速度² × 1.96` | 速度に応じて増える減速項 |
| 速度変化 | `((エンジン出力 - 抗力) ÷ 286,000) × ThrottleVecMulti × Δt` | 初期値`ThrottleVecMulti = 10`でフレームごとの速度を更新 |
| 基本浮揚 | `速度² × 0.000001 × LiftMulti` | 初期値`LiftMulti = 0.5`、下降姿勢ではさらに`0.8`を適用 |
| 高度補正 | `高度² × 0.0008` | 高度に応じて基本浮揚から減算 |
| ピッチ補正 | `ピッチ角 × 0.0113 × (速度 ÷ 550)` | 機首の上下方向を浮揚変化へ反映 |

後進時は算出した速度を半分に減衰し、速度が`5`未満の場合は姿勢入力を適用しません。この式は実機性能を精密に再現する空力モデルではなく、VRでの操縦感を作るために簡略化したモデルです。

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

機体とコックピットは基準点に残り、環境が逆方向へ移動します。プレイヤー周辺のTransform座標が移動距離に応じて大きくならないため、距離とともに増える浮動小数点誤差を抑えられます。飛行計算と乗員空間はこの環境座標を使います。

## ネットワーク状態と復元

### 権限と所有権

`AirplaneState`の現在の所有者だけが飛行状態を計算します。VRで最初に操縦桿を握ったユーザーは、操縦オブジェクトと`OwnerChangeTarget`の所有権を取得します。操縦中のPlayer IDは`TriggeredUserID`として同期します。

現在、所有権の移動はVRで操縦桿を最初に握る場合にのみ適用されています。デスクトップ操作、操縦終了、退出時には同じ処理がまだ適用されていません。

<sub>関連Issue · <a href="https://github.com/hjcud/Shimanami-Ekranoplan/issues/3">#3</a></sub>

### 同期データ

| UdonSharp Behaviour | 同期する値 | 個数 |
| --- | --- | ---: |
| `Controller_Controll` | 操縦ユーザーID、ヨー、ピッチ、ロール | 4 |
| `Throttle_Controll` | 操縦ユーザーID、スロットル出力 | 2 |
| `Engine_Toggle` | エンジン状態 | 1 |
| `AirplaneState` | 速度、ピッチ・ロール、高度、移動ベクトル、回転、位置、ピッチ・ロール警告 | 9 |
| `MapRotation`の`VRCObjectSync` | 環境の回転・高度Transform | — |

現在は独自のビットパッキングや量子化を行わず、Udon同期変数として送信します。水平位置は`AirplaneState`が送り、環境の回転と高度は`MapRotation`に接続した`VRCObjectSync`が送ります。操縦値が変化したときと、所有者の飛行計算ループから`RequestSerialization()`を呼び出します。送信周期とデータ構成はまだ個別に整理されていません。

<sub>関連Issue · <a href="https://github.com/hjcud/Shimanami-Ekranoplan/issues/4">#4</a></sub>

### リモート補間と途中参加

- `OnDeserialization()`で最初の水平位置を受信すると、`MapPosition`を最後の同期位置へ復元します。
- 以後、非所有者の`FixedUpdate()`は水平位置を初期値`0.2秒`の`SmoothDamp`で追従します。
- 位置誤差が初期値`1,500`ゲーム内単位を超える場合は補間せず、最後の同期位置を適用します。
- 環境の回転と高度は`MapRotation`の`VRCObjectSync`がリモートユーザーへ反映します。
- 所有権を受け取ったユーザーは現在の`MapRotation`を計算用`MapRotationTarget`へ引き継いだ後、新しい状態を送信します。

この説明はコードとワールドの`VRCObjectSync`設定を基準に作成しました。所有権移動と途中参加を含む実際のVRChatマルチクライアント環境では、追加の確認が必要です。

<sub>関連Issue · <a href="https://github.com/hjcud/Shimanami-Ekranoplan/issues/2">#2</a></sub>

## 実装の変化

### 飛行計算の統合

| スナップショット | 飛行計算スクリプト | 構成 |
| --- | ---: | --- |
| 初期構成 | 3 | `StateCal`、`RotationCal`、`AirplaneState`で状態を分担 |
| 現在の構成 | 1 | `AirplaneState`が速度、姿勢、揚力、環境移動、同期を処理 |

不要になった計算経路を削除し、飛行計算と同期を`AirplaneState`一つで処理する構成にしました。

### 計算権限の変更

初期コードではインスタンスマスターが飛行を計算していました。現在は飛行状態オブジェクトの所有者が計算します。

### プラットフォーム別入力の追加

VRの手トラッキングに加えて、デスクトップ用Stationとキーボード入力を追加しました。座席を離れると操縦ユーザーIDと手の基準状態をリセットし、デスクトップStationはVRユーザーには表示しません。

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
