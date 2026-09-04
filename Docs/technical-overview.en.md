<p align="center">
  <a href="./technical-overview.md">日本語</a> · <a href="./technical-overview.ko.md">한국어</a> · <strong>English</strong>
</p>

# Shimanami Ekranoplan Development Notes

This document covers the main implementation work for the flight model, control input, and network synchronization.

## System Boundary

| Area | Input | Processing | Output |
| --- | --- | --- | --- |
| VR controls | Left and right hand position and rotation, grip, trigger | Assign each hand to the throttle or control column relative to the seat | Power, pitch, yaw, roll |
| Desktop controls | Station, `WASDQE`, `Shift`, `Ctrl`, `Z`, `Space` | Convert keyboard input into the shared control state | Power, pitch, yaw, roll, exit |
| Flight calculation | Control state, engine state, altitude | Calculate speed, drag, lift, attitude limits, and environment coordinates | Flight-state snapshot |
| Remote view | Udon synchronized variables, `VRCObjectSync` | Smooth horizontal position and synchronize environment rotation and altitude | Ownership-transfer and late-join recovery |
| Feedback | Engine, throttle, and attitude state | Update Animator, AudioSource, and warning objects | Cockpit animation, audio, and warnings |

## Flight Model and Environment Movement

### Input and Output Ranges

- The VR control column normalizes the difference between the initial and current hand rotations into pitch, yaw, and roll.
- Desktop control uses `W/S` for pitch, `A/D` for yaw, and `Q/E` for roll.
- Throttle ranges are `0.0–1.0` for normal power, `-0.3–0.001` for reverse, and `0.999–1.25` for extra power.
- Attitude input is ignored by the flight calculation while speed is below `5`.

### Flight-Model Calculation Basis

On every physics frame, `AirplaneState` updates speed from engine output and drag, then calculates lift from speed, altitude, and pitch. The following expressions are taken directly from the current code and the field defaults declared in it. Their results are used as internal game units.

| Term | Implementation | Purpose |
| --- | --- | --- |
| Engine output | `throttle × 101,920 × 8 × 0.581` | Apply the output of eight engines to the speed calculation |
| Drag | `speed² × 1.96` | Increase deceleration as speed rises |
| Speed change | `((engine output - drag) ÷ 286,000) × ThrottleVecMulti × Δt` | Update speed per frame with the default `ThrottleVecMulti = 10` |
| Base lift | `speed² × 0.000001 × LiftMulti` | Default `LiftMulti = 0.5`; multiply by `0.8` while pitched downward |
| Altitude correction | `altitude² × 0.0008` | Subtract from base lift as altitude increases |
| Pitch correction | `pitch angle × 0.0113 × (speed ÷ 550)` | Apply nose-up or nose-down attitude to lift |

In reverse, the calculated speed is halved, and attitude input is ignored below speed `5`. These expressions are a simplified model designed for VR handling, not a precise aerodynamic reproduction of the real aircraft.

### References

- Vazgriz, [*Creating a Flight Simulator in Unity3D (Part 1)*](https://youtu.be/7vAHo2B1zLc), YouTube, 2022-09-12 — drag and lift proportional to speed squared and a Unity flight-model structure
- Wikipedia, [*Lun-class ekranoplan*](https://en.wikipedia.org/wiki/Lun-class_ekranoplan) — aircraft mass, dimensions, engines, and speed specifications

### Attitude Limits and Direction

The maximum pitch and roll angles increase with altitude and are each capped at `15°`. A combination of pitch and roll produces additional yaw, while the calculated lift and pitch direction affect altitude and horizontal movement.

### Why the Environment Moves Instead of the Aircraft

The first implementation moved the aircraft and passengers directly through world space. As the distance from the origin increased, floating-point precision fell and the sea and islands visible from the cockpit began to shake.

The revised system keeps the aircraft and cockpit at the origin and divides environment movement across three Transforms:

- `MapRotationTarget`: target attitude used by the calculation
- `MapRotation`: visible environment attitude and altitude
- `MapPosition`: horizontal travel coordinates

The aircraft and cockpit remain at the reference point while the environment moves in the opposite direction. Nearby Transform coordinates therefore stay small, reducing the floating-point error that grows with travel distance. The flight calculation and passenger space both use these environment coordinates.

## Network State and Recovery

### Authority and Ownership

Only the current owner of `AirplaneState` calculates the flight state. The first user to grip the VR control column takes ownership of the control object and `OwnerChangeTarget`. The active player's ID is synchronized as `TriggeredUserID`.

Applying the same ownership lifecycle to desktop input, control release, and player exit is tracked in [Issue #3](https://github.com/hjcud/Shimanami-Ekranoplan/issues/3).

### Synchronized Data

| UdonSharp behavior | Synchronized values | Count |
| --- | --- | ---: |
| `Controller_Controll` | Active player ID, yaw, pitch, roll | 4 |
| `Throttle_Controll` | Active player ID, throttle power | 2 |
| `Engine_Toggle` | Engine state | 1 |
| `AirplaneState` | Speed, pitch and roll, altitude, movement vector, rotation, position, pitch and roll warnings | 9 |
| `VRCObjectSync` on `MapRotation` | Environment rotation and altitude Transform | — |

The values are sent as Udon synchronized variables without custom bit packing or quantization. `AirplaneState` sends horizontal position, while the `VRCObjectSync` attached to `MapRotation` sends environment rotation and altitude. `RequestSerialization()` runs when control values change and from the owner's flight-calculation loop. Work on the send rate and payload structure is tracked in [Issue #4](https://github.com/hjcud/Shimanami-Ekranoplan/issues/4).

### Remote Smoothing and Late Joins

- When `OnDeserialization()` receives the first horizontal position, it restores `MapPosition` to the latest synchronized position.
- Subsequent non-owner `FixedUpdate()` calls follow the horizontal position with `SmoothDamp`, using a default smoothing time of `0.2` seconds.
- If the position error exceeds the default threshold of `1,500` internal units, the environment snaps to the latest synchronized position.
- `VRCObjectSync` on `MapRotation` applies environment rotation and altitude for remote users.
- A user receiving ownership copies the current `MapRotation` state into the calculation-only `MapRotationTarget` before publishing a new state.

This description is based on the code and the world's `VRCObjectSync` configuration. Multi-client VRChat validation, including ownership transfers and late joins, remains tracked in [Issue #2](https://github.com/hjcud/Shimanami-Ekranoplan/issues/2).

## Implementation Changes

### Consolidating the Flight Calculation

| Snapshot | Flight-calculation scripts | Structure |
| --- | ---: | --- |
| Initial | 3 | `StateCal`, `RotationCal`, and `AirplaneState` divided the state calculation |
| Current | 1 | `AirplaneState` handles speed, attitude, lift, environment movement, and synchronization |

Removing the unused calculation paths places the flight state's source and network authority in one behavior.

### Changing Calculation Authority

The initial code assigned flight calculation to the instance master. The current code assigns it to the owner of the flight-state object, keeping control transfer and calculation authority on the same target.

### Platform-Specific Input

The project adds a desktop Station and keyboard input to the original VR hand-tracking flow. Leaving the seat resets the active player ID and reference hand state, and the desktop Station remains hidden from VR users.

## External Flight-Display Integration

The cockpit uses Tokiwa's [VRChat用 ワイワイフライトディスプレイシステム](https://tokiwa-carlo.booth.pm/items/6424462). Its `FLIGHT_DISPLAY` reads pitch, roll, altitude, speed, rotation, and position from `AirplaneState`, then updates the attitude indicator, altimeter, speedometer, heading ring, and Seto Inland Sea map.

Following the distribution terms, the product's scripts, prefabs, UI textures, fonts, and render textures are not included in this repository and are not covered by this repository's MIT License.

## Code Index

| File | Responsibility |
| --- | --- |
| [`AirplaneState.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/PlaneMovement/AirplaneState.cs) | Owner-side flight calculation, environment Transforms, synchronization, and remote recovery |
| [`Controller_Controll.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/Controller_Controll.cs) | VR and desktop attitude input and control ownership |
| [`Throttle_Controll.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/Throttle_Controll.cs) | Throttle input, output ranges, animation, and volume |
| [`Engine_Toggle.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/Engine_Toggle.cs) | Engine network events, audio, and animation |
| [`ColliderStayCheck.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/ColliderStayCheck.cs) | VR control area |
| [`DesktopSeatCheck.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Controll/DesktopSeatCheck.cs) | Desktop Station |
| [`MirrorToggle.cs`](../ekranoplan/Assets/Lun/Udon/UdonSharp/Extra/MirrorToggle.cs) | Local mirror-quality selection |

[Back to the README](../README.en.md)
