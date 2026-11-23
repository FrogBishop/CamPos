#include <windows.h>
#include <stdint.h>
#include <stddef.h>
#include <string.h>

/* Avoid TinyCC's x87 inline math intrinsics; these are imported from msvcrt. */
extern double atan(double);
extern double fabs(double);
#define IS_FINITE(x) ((x) == (x) && fabs(x) < 1.0e30)

#define CAMPOS_MAGIC 0x43414D50u
#define CAMPOS_VERSION 4u
#define FOV_OFFSET_OS 0x1467230UL
#define FOV_OFFSET_CN 0x1466230UL

/*
 * Unity 2017.4 native accessors in the 7.2.0 IL2CPP image. These are the
 * implementation entry points (not the managed thunks). The CN build has the
 * same Unity code shifted down by 0x1000; the game's Camera.set_fieldOfView
 * differs by 0x10000.
 */
#define FN_TRANSFORM_OS  0x1465cf0UL
#define FN_GAMEOBJECT_OS 0x1465d40UL
#define FN_NAME_OS       0x144a820UL
#define FN_POSITION_OS   0x1448b30UL
#define FN_PARENT_OS     0x14492e0UL
#define FN_CHILDCOUNT_OS 0x1449b40UL
#define FN_GETCHILD_OS   0x1449ea0UL
#define FN_ACTIVE_OS     0x1464f90UL

#pragma pack(push, 4)
typedef struct CamPosState {
    unsigned int Magic;
    unsigned int Version;
    unsigned int Valid;
    volatile LONG Sequence;
    unsigned int Tick;
    float Px, Py, Pz;
    float Fx, Fy, Fz;
    float Ux, Uy, Uz;
    float Rx, Ry, Rz;
    float Fov;
    float Aspect;
    unsigned int PlayerValid;
    float PlayerX, PlayerY, PlayerZ;
    unsigned int AbsoluteValid;
    float OriginX, OriginZ;
    unsigned int PlayerTick, OriginTick, OriginGeneration;
} CamPosState;
#pragma pack(pop)
C_ASSERT(sizeof(CamPosState) == 116);
C_ASSERT(offsetof(CamPosState, Sequence) == 12);
C_ASSERT(offsetof(CamPosState, PlayerTick) == 104);

typedef struct Vec3 { float X, Y, Z; } Vec3;

typedef void (__fastcall *SetFovFn)(void* instance, float value);
typedef void* (__fastcall *GetObjectFn)(void* self);
typedef void (__fastcall *GetPositionFn)(void* self, Vec3* out);
typedef int (__fastcall *GetIntFn)(void* self);
typedef void* (__fastcall *GetChildFn)(void* self, int index);
typedef unsigned char (__fastcall *GetBoolFn)(void* self);

static BYTE* g_target;
static BYTE g_original[14];
static BYTE* g_trampoline;
static volatile void* g_instance;
static volatile LONG g_hooked;
static CamPosState* g_state;
static HANDLE g_mapping;
static SetFovFn g_original_fn;

static GetObjectFn g_get_transform;
static GetObjectFn g_get_gameobject;
static GetObjectFn g_get_name;
static GetObjectFn g_get_parent;
static GetPositionFn g_get_position;
static GetIntFn g_get_childcount;
static GetChildFn g_get_child;
static GetBoolFn g_get_active;

/* Protect the coordinates, their validity, and their sample times as one cache. */
static SRWLOCK g_coordinates_lock = SRWLOCK_INIT;
static volatile LONG g_player_updating;
static volatile LONG g_player_valid;
static volatile unsigned int g_player_tick;
static volatile float g_player_x, g_player_y, g_player_z;

static volatile LONG g_origin_valid;
static volatile unsigned int g_origin_tick;
static volatile float g_origin_x, g_origin_z;
static unsigned int g_origin_generation;

static int readable(const void* p, SIZE_T n)
{
    MEMORY_BASIC_INFORMATION mbi;
    if (!p || !VirtualQuery(p, &mbi, sizeof(mbi))) return 0;
    if (mbi.State != MEM_COMMIT || (mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD))) return 0;
    return ((const BYTE*)p + n) <= ((const BYTE*)mbi.BaseAddress + mbi.RegionSize);
}

static int name_equals(void* object, const char* ascii)
{
    if (!object || !g_get_name) return 0;
    void* str = g_get_name(object);
    if (!str || !readable(str, 0x18)) return 0;
    int len = *(int*)((BYTE*)str + 0x10);
    int want = (int)strlen(ascii);
    if (len != want || len <= 0 || len > 128) return 0;
    if (!readable((BYTE*)str + 0x14, (SIZE_T)len * 2)) return 0;
    const unsigned short* chars = (const unsigned short*)((BYTE*)str + 0x14);
    for (int i = 0; i < len; ++i) {
        if (chars[i] != (unsigned char)ascii[i]) return 0;
    }
    return 1;
}

static int name_starts_with(void* object, const char* ascii)
{
    if (!object || !g_get_name) return 0;
    void* str = g_get_name(object);
    if (!str || !readable(str, 0x18)) return 0;
    int len = *(int*)((BYTE*)str + 0x10);
    int want = (int)strlen(ascii);
    if (len < want || len > 128) return 0;
    if (!readable((BYTE*)str + 0x14, (SIZE_T)len * 2)) return 0;
    const unsigned short* chars = (const unsigned short*)((BYTE*)str + 0x14);
    for (int i = 0; i < want; ++i) {
        if (chars[i] != (unsigned char)ascii[i]) return 0;
    }
    return 1;
}

static void* find_child(void* parent, const char* name)
{
    if (!parent || !g_get_childcount || !g_get_child) return 0;
    int count = g_get_childcount(parent);
    if (count < 0 || count > 256) return 0;
    for (int i = 0; i < count; ++i) {
        void* child = g_get_child(parent, i);
        if (child && name_equals(child, name)) return child;
    }
    return 0;
}

static void set_player(float x, float y, float z)
{
    AcquireSRWLockExclusive(&g_coordinates_lock);
    g_player_x = x;
    g_player_y = y;
    g_player_z = z;
    g_player_tick = GetTickCount();
    g_player_valid = 1;
    ReleaseSRWLockExclusive(&g_coordinates_lock);
}

static void clear_player(void)
{
    AcquireSRWLockExclusive(&g_coordinates_lock);
    g_player_valid = 0;
    ReleaseSRWLockExclusive(&g_coordinates_lock);
}

static void set_origin(float x, float z)
{
    AcquireSRWLockExclusive(&g_coordinates_lock);
    if (!g_origin_valid || x != g_origin_x || z != g_origin_z)
        ++g_origin_generation;
    g_origin_x = x;
    g_origin_z = z;
    g_origin_tick = GetTickCount();
    g_origin_valid = 1;
    ReleaseSRWLockExclusive(&g_coordinates_lock);
}

static void clear_origin(void)
{
    AcquireSRWLockExclusive(&g_coordinates_lock);
    g_origin_valid = 0;
    ReleaseSRWLockExclusive(&g_coordinates_lock);
}

static void update_origin(void* camera)
{
    if (!camera || !g_get_transform || !g_get_parent || !g_get_childcount || !g_get_child || !g_get_position)
        return;

    void* transform = g_get_transform(camera);
    if (!transform) { clear_origin(); return; }

    void* node = g_get_parent(transform);
    for (int depth = 0; depth < 4 && node; ++depth) {
        void* other = find_child(node, "OtherRoot");
        if (other) {
            void* marker = find_child(other, "New Game Object");
            if (marker) {
                Vec3 p = { 0, 0, 0 };
                g_get_position(marker, &p);
                if (IS_FINITE(p.X) && IS_FINITE(p.Z) &&
                    fabs(p.X) < 200000.0f && fabs(p.Z) < 200000.0f) {
                    set_origin(p.X, p.Z);
                    return;
                }
            }
        }
        node = g_get_parent(node);
    }
    clear_origin();
}

static void update_player(void* camera)
{
    if (!camera || !g_get_transform || !g_get_gameobject || !g_get_name ||
        !g_get_parent || !g_get_position || !g_get_childcount || !g_get_child ||
        !g_get_active)
        return;

    unsigned int now = GetTickCount();
    if (now - g_player_tick < 45) return;
    if (InterlockedExchange(&g_player_updating, 1) != 0) return;

    clear_player();

    void* transform = g_get_transform(camera);
    if (!transform) goto done;

    void* avatar_root = 0;
    void* node = g_get_parent(transform);
    for (int depth = 0; depth < 4 && node; ++depth) {
        avatar_root = find_child(node, "AvatarRoot");
        if (avatar_root) break;
        node = g_get_parent(node);
    }
    if (!avatar_root) goto done;

    int count = g_get_childcount(avatar_root);
    if (count < 0 || count > 16) goto done;
    for (int i = 0; i < count; ++i) {
        void* child = g_get_child(avatar_root, i);
        if (!child) continue;
        void* go = g_get_gameobject(child);
        if (!go || !g_get_active(go)) continue;
        Vec3 p = { 0, 0, 0 };
        g_get_position(child, &p);
        if (IS_FINITE(p.X) && IS_FINITE(p.Y) && IS_FINITE(p.Z)) {
            set_player(p.X, p.Y, p.Z);
            break;
        }
    }

done:
    InterlockedExchange(&g_player_updating, 0);
}

static BOOL create_shared_state(void)
{
    const wchar_t* names[] = { L"Global\\CamPosState_v4", L"Local\\CamPosState_v4" };
    for (int i = 0; i < 2; ++i) {
        g_mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, sizeof(CamPosState), names[i]);
        if (g_mapping) {
            g_state = (CamPosState*)MapViewOfFile(g_mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(CamPosState));
            if (g_state) {
                ZeroMemory(g_state, sizeof(*g_state));
                g_state->Magic = CAMPOS_MAGIC;
                g_state->Version = CAMPOS_VERSION;
                return TRUE;
            }
            CloseHandle(g_mapping);
            g_mapping = NULL;
        }
    }
    return FALSE;
}

static void fill_state(CamPosState* state)
{
    void* instance = (void*)g_instance;
    if (!instance || !readable(instance, 0x20)) {
        return;
    }
    void* native = *(void**)((BYTE*)instance + 0x10);
    if (!native || !readable((BYTE*)native + 0x8d8, 0x1000)) {
        return;
    }

    float* mat = (float*)((BYTE*)native + 0x1680);
    unsigned int sample_tick = GetTickCount();
    float px = mat[12], py = mat[13], pz = mat[14];
    float rx = mat[0], ry = mat[1], rz = mat[2];
    float ux = mat[4], uy = mat[5], uz = mat[6];
    float bx = mat[8], by = mat[9], bz = mat[10];
    float fx = -bx, fy = -by, fz = -bz;
    float fov = 0.0f, aspect = 0.0f;

    float* proj = (float*)((BYTE*)native + 0x8d8);
    float m00 = proj[0], m11 = proj[5];
    if (fabs(m11) > 0.001f) {
        fov = 2.0f * atan(1.0f / fabs(m11)) * (180.0f / 3.14159265358979323846f);
        if (fabs(m00) > 0.001f) aspect = fabs(m11 / m00);
    }

    if (!IS_FINITE(px) || !IS_FINITE(py) || !IS_FINITE(pz) ||
        !IS_FINITE(fx) || !IS_FINITE(fy) || !IS_FINITE(fz) ||
        fov < 5.0f || fov > 150.0f ||
        (fabs(px) < 0.01f && fabs(py) < 0.01f && fabs(pz) < 0.01f) ||
        fabs(px) > 200000.0f || fabs(pz) > 200000.0f || py < -10000.0f || py > 100000.0f) {
        return;
    }

    state->Tick = sample_tick;
    state->Px = px; state->Py = py; state->Pz = pz;
    state->Fx = fx; state->Fy = fy; state->Fz = fz;
    state->Ux = ux; state->Uy = uy; state->Uz = uz;
    state->Rx = rx; state->Ry = ry; state->Rz = rz;
    state->Fov = fov;
    state->Aspect = aspect;
    state->Valid = 1;

    AcquireSRWLockShared(&g_coordinates_lock);
    state->PlayerTick = g_player_tick;
    state->OriginTick = g_origin_tick;
    state->OriginGeneration = g_origin_generation;
    unsigned int origin_age = sample_tick - g_origin_tick;
    if (g_origin_valid && origin_age <= 1500) {
        state->AbsoluteValid = 1;
        state->OriginX = g_origin_x;
        state->OriginZ = g_origin_z;
    }

    unsigned int player_age = sample_tick - g_player_tick;
    if (g_player_valid && player_age <= 1000) {
        float ax = g_player_x, ay = g_player_y, az = g_player_z;
        float dx = ax - px, dy = ay - py, dz = az - pz;
        float dist2 = dx * dx + dy * dy + dz * dz;
        if (IS_FINITE(ax) && IS_FINITE(ay) && IS_FINITE(az) &&
            dist2 < 10000.0f && (fabs(ax) + fabs(ay) + fabs(az)) > 0.05f) {
            state->PlayerX = ax;
            state->PlayerY = ay;
            state->PlayerZ = az;
            state->PlayerValid = 1;
        }
    }
    ReleaseSRWLockShared(&g_coordinates_lock);
}

static int is_main_camera(void* instance)
{
    if (!instance || !g_get_transform || !g_get_name) return 0;
    void* transform = g_get_transform(instance);
    return transform && name_starts_with(transform, "MainCamera");
}

static void __fastcall hook_set_fov(void* instance, float value)
{
    if (instance && (g_instance == NULL || is_main_camera(instance))) {
        g_instance = instance;
        update_player(instance);
        update_origin(instance);
    }
    if (g_original_fn) g_original_fn(instance, value);
}

static BOOL install_hook(HMODULE module, DWORD offset)
{
    g_target = (BYTE*)module + offset;
    const BYTE expected[] = { 0x40, 0x53, 0x48, 0x83, 0xEC, 0x30, 0x0F, 0x29, 0x74, 0x24, 0x20, 0x0F, 0x28, 0xF1 };
    if (memcmp(g_target, expected, sizeof(expected)) != 0) {
        OutputDebugStringA("CamPosHook: Camera.set_fieldOfView prologue mismatch; refusing to hook.\n");
        return FALSE;
    }

    DWORD oldProtect = 0;
    if (!VirtualProtect(g_target, 14, PAGE_EXECUTE_READWRITE, &oldProtect)) return FALSE;
    memcpy(g_original, g_target, 14);

    g_trampoline = (BYTE*)VirtualAlloc(NULL, 0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!g_trampoline) return FALSE;
    memcpy(g_trampoline, g_original, 14);
    g_trampoline[14] = 0xFF;
    g_trampoline[15] = 0x25;
    *(DWORD*)(g_trampoline + 16) = 0;
    *(ULONGLONG*)(g_trampoline + 20) = (ULONGLONG)(g_target + 14);
    g_original_fn = (SetFovFn)g_trampoline;

    g_target[0] = 0xFF;
    g_target[1] = 0x25;
    *(DWORD*)(g_target + 2) = 0;
    *(ULONGLONG*)(g_target + 6) = (ULONGLONG)&hook_set_fov;
    FlushInstructionCache(GetCurrentProcess(), g_target, 14);
    VirtualProtect(g_target, 14, oldProtect, &oldProtect);
    g_hooked = 1;
    return TRUE;
}

static DWORD WINAPI state_thread(LPVOID param)
{
    (void)param;
    while (1) {
        Sleep(33);
        if (g_state) {
            CamPosState next = {0};
            next.Magic = CAMPOS_MAGIC;
            next.Version = CAMPOS_VERSION;
            fill_state(&next);
            /* Keep the sequence odd only while copying. Never copy over the sequence itself. */
            InterlockedIncrement(&g_state->Sequence);
            memcpy(g_state, &next, offsetof(CamPosState, Sequence));
            memcpy(&g_state->Tick, &next.Tick, sizeof(next) - offsetof(CamPosState, Tick));
            InterlockedIncrement(&g_state->Sequence);
        }
    }
    return 0;
}

static DWORD WINAPI init_thread(LPVOID param)
{
    (void)param;
    if (!create_shared_state())
        OutputDebugStringA("CamPosHook: CreateFileMapping failed.\n");

    HMODULE module = GetModuleHandleW(L"GenshinImpact.exe");
    int cn = 0;
    if (!module) {
        module = GetModuleHandleW(L"YuanShen.exe");
        cn = 1;
    }
    if (!module) {
        OutputDebugStringA("CamPosHook: game module not found.\n");
        return 0;
    }

    ULONG_PTR base = (ULONG_PTR)module;
    ULONG_PTR shift = cn ? 0x1000UL : 0UL;
    g_get_transform = (GetObjectFn)(base + FN_TRANSFORM_OS - shift);
    g_get_gameobject = (GetObjectFn)(base + FN_GAMEOBJECT_OS - shift);
    g_get_name = (GetObjectFn)(base + FN_NAME_OS - shift);
    g_get_position = (GetPositionFn)(base + FN_POSITION_OS - shift);
    g_get_parent = (GetObjectFn)(base + FN_PARENT_OS - shift);
    g_get_childcount = (GetIntFn)(base + FN_CHILDCOUNT_OS - shift);
    g_get_child = (GetChildFn)(base + FN_GETCHILD_OS - shift);
    g_get_active = (GetBoolFn)(base + FN_ACTIVE_OS - shift);

    if (cn) {
        if (!install_hook(module, FOV_OFFSET_CN))
            OutputDebugStringA("CamPosHook: CN hook failed.\n");
    } else {
        if (!install_hook(module, FOV_OFFSET_OS))
            OutputDebugStringA("CamPosHook: global hook failed.\n");
    }

    HANDLE h = CreateThread(NULL, 0, state_thread, NULL, 0, NULL);
    if (h) CloseHandle(h);
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hinst);
        HANDLE h = CreateThread(NULL, 0, init_thread, NULL, 0, NULL);
        if (h) CloseHandle(h);
    }
    return TRUE;
}

__declspec(dllexport) LRESULT CALLBACK WndProc(int code, WPARAM wParam, LPARAM lParam)
{
    return CallNextHookEx(NULL, code, wParam, lParam);
}
