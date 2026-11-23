# CamPosHook native module

`CamPosHook.c` is a 64-bit Windows DLL injected into the game at process start.

It:

- hooks `Camera.set_fieldOfView` at module + `0x1467230` (global) or `0x1466230` (CN);
- checks the 14-byte function prologue before writing;
- captures the IL2CPP `Camera` instance and reads the native Camera at `+0x10`, camera-to-world matrix at `+0x1680`, projection at `+0x8d8`;
- only accepts cameras whose Transform name starts with `MainCamera`, so UI cameras do not overwrite the state;
- finds `/EntityRoot/AvatarRoot` from the camera Transform, selects the active child, and calls Unity's `Transform.get_position` for the current character;
- reads the world-shift anchor from `/EntityRoot/OtherRoot/New Game Object` and publishes it so the UI can compute absolute X/Z;
- publishes 30 Hz state through `Global\CamPosState_v3` / `Local\CamPosState_v3`.

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

The absolute coordinate formula is:

~~~text
absolute X = relative X - anchor X
absolute Z = relative Z - anchor Z
absolute Y = relative Y
~~~

The anchor is the `New Game Object` child under `OtherRoot`. It changes with the world shift; a missing anchor marks absolute data invalid.

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
