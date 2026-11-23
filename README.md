# CamPos · 原神相机与角色坐标监视器

CamPos 是一个 Windows x64 的《原神》进程内遥测工具。它通过注入 `CamPosHook.dll` 读取：

- 当前主相机的相对坐标和绝对坐标；
- 相机 Yaw / Pitch / Roll、朝向单位向量；
- 相机垂直 FOV；
- **当前场上角色的脚底相对坐标和绝对坐标**。

界面提供置顶悬浮窗和点击穿透，所有操作通过界面按钮或悬浮窗右键菜单完成，**不注册任何全局快捷键**。本版本只保留注入模式，外部 `OpenProcess` / `ReadProcessMemory` 模式已经删除。

> **风险提示**
>
> 注入会修改游戏进程内的 `Camera.set_fieldOfView` 入口，并调用 Unity 的 Transform 访问器读取角色位置。这是明确违反游戏 ToS 的高风险行为，可能导致封号。请只在测试账号或你愿意承担损失的环境使用。
>
> 本项目没有改名成 `genshin-fps-unlock` 或任何白名单工具，也不会提供冒充、白名单绕过或反作弊规避。工具名就是 CamPos。

![CamPos 主界面](docs/main.png)

> 截图来自演示模式；真实注入数据使用同一套布局。

## 功能

- 相机相对坐标 X / Y / Z（Unity 场景坐标）
- 相机绝对坐标 X / Z（自动还原 world shift；Y 不变）
- Yaw / Pitch / Roll（度）
- 相机朝向单位向量
- 垂直 FOV（度）
- 角色相对坐标 X / Y / Z（脚底，当前场上角色）
- 角色绝对坐标 X / Z（自动还原 world shift；Y 不变）
- 置顶悬浮窗：拖动、透明度、点击穿透；通过按钮或右键菜单操作，无全局快捷键
- 演示模式：不连接游戏也能检查 UI 和布局
- 30 Hz 共享内存传输，界面只读共享内存

## 运行环境

- Windows 10/11 x64
- .NET 10 Desktop Runtime（framework-dependent 构建）
- 原神 PC 客户端 **7.2.0**（国际服 GenshinImpact.exe / 国服 YuanShen.exe）
- 管理员权限（注入器需要创建挂起进程、写入远程内存、创建远程线程）

## 配置文件（必须手动指定游戏目录）

CamPos **不会**搜索注册表、启动器配置或默认安装路径，也不会猜测游戏目录。注入器只读取同目录的：

~~~text
CamPos.Injector.json
~~~

默认内容随发行包提供；请按自己的安装位置修改：

~~~json
{
  "GameDirectory": "C:/Program Files/miHoYo Launcher/games/Genshin Impact Game"
}
~~~

字段说明：

| 字段 | 必填 | 说明 |
|---|---|---|
| GameDirectory | 是 | 原神安装文件夹，不是 exe 路径。必须存在。 |
| GameExecutable | 否 | 文件名，例如 GenshinImpact.exe 或 YuanShen.exe。文件夹里同时有两个 exe 时必须填写。 |
| Arguments | 否 | 追加给游戏的命令行参数。一般留空。 |

如果目录里同时存在国际服和国服 exe，请写成：

~~~json
{
  "GameDirectory": "D:/Games/Genshin Impact Game",
  "GameExecutable": "GenshinImpact.exe"
}
~~~

注意：

- JSON 字符串里的反斜杠要写成两个反斜杠，或者直接使用正斜杠，例如 D:/Games/Genshin Impact Game。
- GameDirectory 只写到文件夹，不要写 GenshinImpact.exe。
- 修改后保存为 UTF-8。注入器不会自动修复路径。
- 配置文件缺失、目录不存在、找不到 exe、两个 exe 同时存在但没有指定 GameExecutable，注入器都会直接报错退出，不会退回隐式查找。

同目录的 CamPos.Injector.example.json 是模板；可以复制为 CamPos.Injector.json 后编辑。也可以用 --config 指定另一份配置（主要用于测试）。

## 使用步骤

1. **完全退出游戏**，包括后台残留的 GenshinImpact.exe / YuanShen.exe。
2. 编辑 CamPos.Injector.json，确认 GameDirectory 指向你的原神安装目录。
3. 右键以管理员身份运行 CamPos.Injector.exe。
   - 它读取 JSON，解析出唯一的游戏 exe；
   - 以 CREATE_SUSPENDED 启动游戏；
   - 用 VirtualAllocEx + WriteProcessMemory + CreateRemoteThread(LoadLibraryW) 加载同目录的 CamPosHook.dll；
   - 恢复主线程后退出。
4. 登录并进入大世界（角色站在场景里，能看到角色和场景）。
5. 运行 CamPos.exe --injected，或者普通启动后点击 **注入模式**。
6. 点击 **显示悬浮窗**，把窗口拖到合适位置。

如果注入器提示“游戏已经在运行”，先彻底退出游戏。注入器必须在进程启动时加载 DLL；游戏跑起来后再注入会错过主相机初始化，也更容易被反作弊拦截。

### 命令行

~~~text
CamPos.exe [--demo] [--injected]
CamPos.Injector.exe [--config <json>] [--dll <dll>] [--args <args>]
~~~

- --demo：演示数据，不连接游戏。
- --injected：启动后直接连接共享内存。
- --config：使用另一份注入器 JSON；默认是同目录的 CamPos.Injector.json。
- --dll：使用另一个 hook DLL；默认是同目录的 CamPosHook.dll。
- --args：覆盖 JSON 里的 Arguments。

CamPos.exe 不再支持 --profile、--autodetect 或外部连接参数。

## 悬浮窗

![悬浮窗](docs/overlay.png)

> 悬浮窗截图同样来自演示模式。

左键拖动；右键菜单可以显示主窗口、切换点击穿透或隐藏。悬浮窗按自身实际尺寸绘制，不会在右侧/底部留下固定空白；同时显示相对坐标和 ABS 绝对坐标，次级文字已提高对比度。程序不注册任何全局快捷键，不会占用 `Ctrl+Alt+...` 等组合键，也不会干扰其他应用。

## 数据来源

### 相机

CamPosHook.dll 在游戏主线程恢复前 hook：

- 国际服 GenshinImpact.exe：模块基址 + 0x1467230
- 国服 YuanShen.exe：模块基址 + 0x1466230

目标函数是 Camera.set_fieldOfView。Hook 先检查 14 字节函数序言：

~~~text
40 53 48 83 EC 30 0F 29 74 24 20 0F 28 F1
~~~

不匹配就拒绝 hook，不会盲目写入。匹配后用 14 字节绝对跳转覆盖入口，trampoline 保存原指令并跳回。

从 IL2CPP 的 Camera 对象：

- Camera + 0x10：native Camera 指针
- native Camera + 0x1680：camera-to-world 矩阵（行主序，最后一行是相机世界坐标）
- native Camera + 0x8d8：透视投影矩阵；m11 对应垂直 FOV
- 位置取矩阵 [12..14]
- 右/上/后取矩阵前三行，前向 = -back
- FOV = 2 * atan(1 / |m11|) * 180 / pi

只接受名称以 MainCamera 开头的主相机，避免 UI/过场相机覆盖共享状态。

### 角色坐标

角色坐标不再通过外部 RPM 扫描，而是从 Unity 场景层级读取：

1. 从主相机 Component 取得 Transform；
2. 从相机 Transform 的父节点（/EntityRoot）找到名为 AvatarRoot 的子节点；
3. 遍历 AvatarRoot 的子节点，取 activeInHierarchy == true 的那个（当前场上角色）；
4. 调用 Unity 的 Transform.get_position，得到角色脚底世界坐标。

7.2.0 的 Unity 访问器实现入口（native 函数，不是托管 thunk）：

| 功能 | 国际服 RVA | 国服 RVA |
|---|---:|---:|
| Component.get_transform | 0x1465cf0 | 0x1464cf0 |
| Component.get_gameObject | 0x1465d40 | 0x1464d40 |
| Object.get_name | 0x144a820 | 0x1449820 |
| Transform.get_position | 0x1448b30 | 0x1447b30 |
| Transform.get_parent | 0x14492e0 | 0x14482e0 |
| Transform.get_childCount | 0x1449b40 | 0x1448b40 |
| Transform.GetChild | 0x1449ea0 | 0x1448ea0 |
| GameObject.get_activeInHierarchy | 0x1464f90 | 0x1463f90 |

国服这张表的地址统一比国际服低 0x1000；Camera.set_fieldOfView 则低 0x10000。这些偏移只对应 7.2.0；游戏更新后需要重新定位。

角色坐标与相机坐标使用同一套 Unity 场景坐标，但 Genshin 在传送/加载时会对世界做平移（world shift），所以 Transform 里的 X/Z 是相对坐标。CamPosHook 同时从 EntityRoot / OtherRoot 下的 world shift 锚点读取平移量，按：

~~~text
绝对 X = 相对 X - 锚点 X
绝对 Z = 相对 Z - 锚点 Z
绝对 Y = 相对 Y
~~~

还原绝对坐标。该锚点与场景传送点数据中的绝对坐标可以逐点对上；进入大世界后主界面和悬浮窗会同时显示相对值和 ABS 值。若当前场景没有可用的锚点（例如部分副本/加载界面），ABS 显示为 —。

### 共享内存

CamPosHook.dll 创建：

- 首选 Global\CamPosState_v3
- 失败时退回 Local\CamPosState_v3

每 33 ms 写入以下状态（Pack=4，共 104 字节）：

~~~text
uint Magic; uint Version; uint Valid; uint Reserved; uint Tick;
float Px, Py, Pz;          // 相机世界坐标
float Fx, Fy, Fz;          // 相机前向
float Ux, Uy, Uz;          // 相机上向
float Rx, Ry, Rz;          // 相机右向
float Fov; float Aspect;
uint PlayerValid;
float PlayerX, PlayerY, PlayerZ;  // 角色脚底相对坐标
uint AbsoluteValid;
float OriginX, OriginZ;           // world shift 锚点；绝对 = 相对 - Origin
~~~

CamPos.exe 只读这个共享内存，不直接接触游戏进程。

## 构建

需要 .NET 10 SDK：

~~~powershell
dotnet build CamPos.sln -c Release
~~~

发布完整发行包：

~~~powershell
./build.ps1
~~~

输出到 dist/CamPos：

- CamPos.exe：主界面
- CamPos.Injector.exe：启动游戏并注入
- CamPosHook.dll：7.2.0 原生 hook
- CamPos.Injector.json：当前配置
- CamPos.Injector.example.json：模板
- docs/、LICENSE、README.md

build.ps1 不会自动编译 CamPosHook.dll。仓库里的 DLL 是 7.2.0 预编译版本。重新编译需要 64 位 C 编译器；本机使用 MSYS2 UCRT64 GCC：

~~~powershell
$env:PATH = 'C:/msys64/ucrt64/bin;' + $env:PATH
gcc -m64 -shared -O2 -o native/CamPosHook.dll native/CamPosHook.c -lkernel32 -lmsvcrt
~~~

MSVC 也可以：

~~~text
cl /LD /O2 CamPosHook.c /link /OUT:CamPosHook.dll
~~~

本项目不使用任何第三方运行时 NuGet 包，也不依赖 MelonLoader、BepInEx 或 Il2CppInterop。

## 已知限制

- 仅支持 7.2.0 国际服/国服；游戏更新后 FOV 序言或 Unity 访问器 RVA 变化，hook 会拒绝或角色坐标失效。
- 只支持 64 位 Windows 进程。
- 注入模式需要管理员权限，且必须先退出游戏。
- 角色坐标只在进入大世界后才可靠；登录界面、加载界面、角色选择界面可能显示场景占位角色或 —。
- 角色坐标取的是脚底 Transform；游泳、下落、被击飞、坐骑/机关上可能不是视觉中心。
- 绝对坐标依赖当前场景的 world shift 锚点；副本、加载界面或锚点缺失时 ABS 显示 —。
- 绝对坐标是场景世界坐标，不是游戏地图 UI 的像素坐标。
- 只支持当前场上角色，不提供队伍其他角色或敌人的坐标。
- 不提供地图坐标转换、路径点、传送、ESP 或任何写入游戏状态的作弊功能。
- 没有外部只读模式；官方客户端的 ReadProcessMemory 即使管理员也会被 HoYoProtect 拒绝，本版本已完全移除该路径。
- 注入共享内存同一台机器同时只应运行一个实例。

## 故障排查

| 现象 | 处理 |
|---|---|
| 注入器提示找不到配置文件 | 确认 CamPos.Injector.json 和 CamPos.Injector.exe 在同一目录；或复制示例文件并改名 |
| 注入器提示 GameDirectory 不存在 | 编辑 JSON，写原神安装文件夹；反斜杠要转义，或直接使用正斜杠 |
| 注入器提示同时存在两个 exe | 增加 GameExecutable，值为 GenshinImpact.exe 或 YuanShen.exe |
| 注入器提示游戏已经在运行 | 完全退出 GenshinImpact.exe / YuanShen.exe，包括后台进程 |
| LoadLibraryW 返回 NULL / CreateRemoteThread 失败 | 确认管理员权限、DLL 路径存在、杀软未拦截；确认注入的是同目录的 CamPosHook.dll |
| CamPos 显示“未找到共享内存” | 注入器没有成功加载 DLL；重看注入器控制台输出 |
| CamPos 一直“等待相机数据” | 还没进入大世界，或登录/加载界面没有可用的 MainCamera；进入场景后再看 |
| 相机有数据，角色坐标是 — | 当前场景没有 AvatarRoot 活跃角色，或 hook 的 Unity RVA 不匹配；确认是 7.2.0，并进入大世界 |
| 数值不动 | 角色静止时坐标不变；转动视角应改变 Yaw/Pitch；FOV 只在相机 FOV 改变时变 |
| 角色坐标和相机坐标差得很远 | 可能处于过场/自由镜头，或 world shift；以角色脚底和当前场景为准 |
| 游戏更新后无数据 | 7.2.0 偏移过期；CamPosHook.dll 会检查序言并拒绝 hook，不会硬改未知函数 |

## 安全与合规

注入模式会：

- 读取 JSON 后以挂起方式启动游戏；
- 向游戏进程写入 DLL 路径并创建远程线程；
- 在 Camera.set_fieldOfView 入口写入 inline hook；
- 从游戏内调用 Unity 的 Transform 访问器；
- 创建共享内存。

这些行为可能被反作弊视为篡改，可能违反 ToS 并导致封号。**不要在主账号上使用。** 本项目不包含内核驱动、DMA、反作弊绕过、白名单伪装或自动更新偏移。

## 目录结构

~~~text
src/CamPos.Core/          相机/角色共享内存读取、姿态数学、配置、监视器
src/CamPos.App/           WinForms 主界面、悬浮窗
src/CamPos.Injector/      读取 JSON、启动游戏、注入 CamPosHook.dll
native/CamPosHook.c       进程内 FOV hook、相机矩阵、角色 Transform、共享内存
native/CamPosHook.dll     7.2.0 预编译 hook
docs/                     界面截图
~~~

## 参考

- [genshin-unlock](https://github.com/z3lx/genshin-unlock)：7.2.0 set_fieldOfView 偏移与早期注入思路。
- [genshin-fps-unlock](https://github.com/34736384/genshin-fps-unlock)：确认“父进程创建 + 早期注入”的可行性。
- [CameraTools](https://github.com/portra400nc/CameraTools)：Unity 场景路径 /EntityRoot/MainCamera(Clone)、/EntityRoot/AvatarRoot 的行为参考。
- genshin-il2cpp-re：7.2.0 IL2CPP 元数据与 Unity 访问器 RVA 的静态分析参考。

本项目没有复用上述项目的二进制或代码；数学、UI、注入器、hook 和共享内存均为独立实现。

## License

GNU General Public License v3.0（GPLv3）。Copyright (C) 2026 CamPos contributors。见 [LICENSE](LICENSE)。
