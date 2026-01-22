/*++

Module Name:
    Driver.h

Abstract:
    ExpandScreen虚拟显示驱动程序主头文件

Environment:
    Kernel-mode Driver Framework

--*/

#pragma once

#include <ntddk.h>
#include <wdf.h>
#include <IddCx.h>

// WPP跟踪
#include "Trace.h"

// GUID定义
// {E5F84A51-B5C1-4F42-9C3D-8E9A4B6C7D8E}
DEFINE_GUID(GUID_DEVINTERFACE_EXPANDSCREEN,
    0xe5f84a51, 0xb5c1, 0x4f42, 0x9c, 0x3d, 0x8e, 0x9a, 0x4b, 0x6c, 0x7d, 0x8e);

//
// 设备上下文结构
//
typedef struct _DEVICE_CONTEXT
{
    WDFDEVICE Device;                    // WDF设备对象
    IDDCX_ADAPTER Adapter;               // IddCx适配器对象
    WDF_POWER_DEVICE_STATE PowerState;   // 当前电源状态
    LONG MonitorCount;                   // 当前监视器数量
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, GetDeviceContext)

//
// 适配器上下文结构
//
typedef struct _ADAPTER_CONTEXT
{
    IDDCX_ADAPTER Adapter;               // IddCx适配器对象
    PDEVICE_CONTEXT DeviceContext;       // 指向设备上下文
} ADAPTER_CONTEXT, *PADAPTER_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(ADAPTER_CONTEXT, GetAdapterContext)

//
// 监视器上下文结构
//
typedef struct _MONITOR_CONTEXT
{
    IDDCX_MONITOR Monitor;               // IddCx监视器对象
    IDDCX_ADAPTER Adapter;               // 所属适配器
    UINT MonitorId;                      // 监视器ID
    BOOLEAN IsActive;                    // 是否激活
    IDDCX_SWAPCHAIN SwapChain;           // 交换链对象
} MONITOR_CONTEXT, *PMONITOR_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(MONITOR_CONTEXT, GetMonitorContext)

//
// 交换链上下文结构
//
typedef struct _SWAPCHAIN_CONTEXT
{
    IDDCX_SWAPCHAIN SwapChain;           // IddCx交换链对象
    PMONITOR_CONTEXT MonitorContext;     // 所属监视器
    HANDLE ProcessingThread;             // 帧处理线程
    BOOLEAN TerminateThread;             // 线程终止标志
} SWAPCHAIN_CONTEXT, *PSWAPCHAIN_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(SWAPCHAIN_CONTEXT, GetSwapChainContext)

//
// 支持的显示模式定义
//
typedef struct _DISPLAY_MODE
{
    UINT Width;
    UINT Height;
    UINT RefreshRate;
} DISPLAY_MODE;

// 支持的显示模式列表
static const DISPLAY_MODE g_SupportedModes[] =
{
    { 1920, 1080, 60 },
    { 1920, 1080, 120 },
    { 2560, 1600, 60 },
    { 1280, 720, 60 },
    { 3840, 2160, 60 }
};

#define SUPPORTED_MODE_COUNT (sizeof(g_SupportedModes) / sizeof(DISPLAY_MODE))

//
// 函数声明 - Driver.cpp
//
EVT_WDF_DRIVER_DEVICE_ADD ExpandScreenEvtDeviceAdd;
EVT_WDF_DEVICE_D0_ENTRY ExpandScreenEvtDeviceD0Entry;
EVT_WDF_DEVICE_D0_EXIT ExpandScreenEvtDeviceD0Exit;
EVT_WDF_OBJECT_CONTEXT_CLEANUP ExpandScreenEvtDeviceCleanup;
EVT_WDF_OBJECT_CONTEXT_CLEANUP ExpandScreenEvtDriverCleanup;

//
// 函数声明 - Adapter.cpp
//
NTSTATUS InitializeIddCxAdapter(
    _In_ WDFDEVICE Device,
    _In_ PDEVICE_CONTEXT DeviceContext
);

EVT_IDD_CX_ADAPTER_INIT_FINISHED ExpandScreenEvtAdapterInitFinished;
EVT_IDD_CX_ADAPTER_COMMIT_MODES ExpandScreenEvtAdapterCommitModes;

//
// 函数声明 - Monitor.cpp
//
NTSTATUS CreateMonitor(
    _In_ IDDCX_ADAPTER Adapter,
    _Out_ IDDCX_MONITOR* Monitor
);

EVT_IDD_CX_MONITOR_GET_DEFAULT_DESCRIPTION_MODES ExpandScreenEvtMonitorGetDefaultModes;
EVT_IDD_CX_MONITOR_QUERY_TARGET_MODES ExpandScreenEvtMonitorQueryTargetModes;
EVT_IDD_CX_MONITOR_ASSIGN_SWAPCHAIN ExpandScreenEvtMonitorAssignSwapChain;
EVT_IDD_CX_MONITOR_UNASSIGN_SWAPCHAIN ExpandScreenEvtMonitorUnassignSwapChain;

//
// 函数声明 - SwapChain.cpp
//
NTSTATUS ProcessSwapChainFrame(
    _In_ PSWAPCHAIN_CONTEXT SwapChainContext
);

//
// 函数声明 - Edid.cpp
//
NTSTATUS GenerateEdid(
    _Out_writes_bytes_(EDID_SIZE) BYTE* EdidBuffer,
    _In_ UINT Width,
    _In_ UINT Height
);

#define EDID_SIZE 256

//
// 函数声明 - Ioctl.cpp
//
NTSTATUS InitializeIoctlInterface(
    _In_ WDFDEVICE Device
);

EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL ExpandScreenEvtIoDeviceControl;

//
// IOCTL代码定义
//
#define IOCTL_EXPANDSCREEN_CREATE_MONITOR \
    CTL_CODE(FILE_DEVICE_VIDEO, 0x800, METHOD_BUFFERED, FILE_ANY_ACCESS)

#define IOCTL_EXPANDSCREEN_DESTROY_MONITOR \
    CTL_CODE(FILE_DEVICE_VIDEO, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)

#define IOCTL_EXPANDSCREEN_GET_ADAPTER_INFO \
    CTL_CODE(FILE_DEVICE_VIDEO, 0x802, METHOD_BUFFERED, FILE_READ_ACCESS)

//
// IOCTL数据结构
//
typedef struct _EXPANDSCREEN_CREATE_MONITOR_INPUT
{
    UINT Width;
    UINT Height;
    UINT RefreshRate;
} EXPANDSCREEN_CREATE_MONITOR_INPUT, *PEXPANDSCREEN_CREATE_MONITOR_INPUT;

typedef struct _EXPANDSCREEN_CREATE_MONITOR_OUTPUT
{
    UINT MonitorId;
    NTSTATUS Status;
} EXPANDSCREEN_CREATE_MONITOR_OUTPUT, *PEXPANDSCREEN_CREATE_MONITOR_OUTPUT;

typedef struct _EXPANDSCREEN_ADAPTER_INFO
{
    UINT MonitorCount;
    UINT MaxMonitors;
} EXPANDSCREEN_ADAPTER_INFO, *PEXPANDSCREEN_ADAPTER_INFO;
