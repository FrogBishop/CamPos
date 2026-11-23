# CamPosHook native module

`CamPosHook.c` is a 64-bit Windows DLL injected into the game at process start.

It:

- hooks `Camera.set_fieldOfView` at module + `0x1467230` (global) or `0x1466230` (CN);
- checks the 14-byte function prologue before writing;
- captures the IL2CPP `Camera` instance and reads the native Camera at `+0x10`, camera-to-world matrix at `+0x1680`, projection at `+0x8d8`;
- only accepts cameras whose Transform name starts with `MainCamera`, so UI cameras do not overwrite the state;
- finds `/EntityRoot/AvatarRoot` from the camera Transform, selects the active child, and calls Unity's `Transform.get_position` for the current character;
- publishes the scene X/Z of `/EntityRoot/OtherRoot/New Game Object` as the reference for the UI's ABS coordinates
- publishes 30 Hz state through `Global\CamPosState_v4` / `Local\CamPosState_v4`.

The player path uses these 7.2.0 native accessor entries:

| Function | Global | CN |
|---|---:|---:|
| `Component.get_transform` | `0x1465cf0` | `0x1464cf0` |
| `Component.get_gameObject` | `0x1465d40` | `0x1464d40` |
| `Object.get_name` | `0x144a820` | `0x1449820` |
| `Transform.get_position` | `0x1448b30` | `0x1447b30` |
| `Transform.get_parent` | `0x14492e0` | `0x14482e0` |
| `Transform.get_childCount` | `0x1449b40` | `0x1448b40` |
| `Transform.GetChild` | `0x1449ea0` | `0x1448ea0` |
| `GameObject.get_activeInHierarchy` | `0x1464f90` | `0x1463f90` |

The CN Unity table is uniformly `0x1000` below the global one. The FOV setter is `0x10000` below.

The UI uses the same ABS formula for the camera and the player.

~~~text
ABS X = scene X - OriginX
ABS Y = scene Y
ABS Z = scene Z - OriginZ
~~~

Subtracting the reference position cancels any horizontal translation shared by the reference and the sampled object. The resulting X/Z match map coordinates if the reference has map X/Z `(0, 0)`. `AbsoluteValid` indicates that a recent reference sample is available.

To check the player's ABS coordinates after a teleport, compare the stationary player's X/Z with `tranPos` for that scene and waypoint. The waypoint's `pos` field gives the position of the waypoint itself. At Windrise and Starfell Lake, the measured player X/Z matched `tranPos` to the two decimals shown by the UI.

`tranPos.y` gives the configured arrival height. The tester described the character arriving above ground and descending with the Wind Glider to give slower devices time to load the terrain. Both screenshots record the player Transform's height after landing. [Measurements in the project README](../README.md#abs-坐标与传送落点)

The v4 record is 116 bytes. An even publication sequence brackets a completed copy. Each frame includes the player and reference sample times and the reference generation. The viewer confirms an ABS sample when a later reference observation still belongs to that generation. Live scene coordinates update independently, and the UI shows the age difference between the live and ABS frames. The checked-in binary publishes the 104-byte v3 record. The viewer also accepts this format and uses the reference cache lifetime of 1500 ms for confirmation.

The prebuilt `CamPosHook.dll` targets Genshin 7.2.0. A game update may change the prologue/offsets; the DLL checks the prologue and refuses to hook when it does not match.

Build with any C compiler that produces a 64-bit Windows DLL. The checked-in binary was built with MSYS2 UCRT64 GCC:

~~~powershell
$env:PATH = 'C:/msys64/ucrt64/bin;' + $env:PATH
gcc -m64 -shared -O2 -o CamPosHook.dll CamPosHook.c -lkernel32 -lmsvcrt
~~~

MSVC equivalent:

~~~text
cl /LD /O2 CamPosHook.c /link /OUT:CamPosHook.dll
~~~

This is a high-risk component: it modifies game code, calls Unity accessors, and can trigger anti-cheat enforcement. Use only on accounts you are willing to lose.
