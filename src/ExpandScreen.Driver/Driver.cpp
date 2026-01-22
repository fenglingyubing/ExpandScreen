/*++

Module Name:
    Driver.cpp

Abstract:
    ExpandScreen虚拟显示驱动程序主入口
    实现基于IddCx框架的虚拟显示器驱动

Environment:
    Kernel-mode Driver Framework

--*/

#include "Driver.h"
#include "Driver.tmh"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(INIT, DriverEntry)
#pragma alloc_text(PAGE, ExpandScreenEvtDeviceAdd)
#pragma alloc_text(PAGE, ExpandScreenEvtDeviceD0Entry)
#pragma alloc_text(PAGE, ExpandScreenEvtDeviceD0Exit)
#endif

// 全局驱动对象
WDFDRIVER g_DriverObject = nullptr;

/*++

Routine Description:
    驱动入口点，由系统调用以初始化驱动程序

Arguments:
    DriverObject - 表示此驱动程序的WDF驱动对象
    RegistryPath - 驱动程序在注册表中的服务键路径

Return Value:
    NTSTATUS

--*/
extern "C" NTSTATUS DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
)
{
    WDF_DRIVER_CONFIG config;
    NTSTATUS status;
    WDF_OBJECT_ATTRIBUTES attributes;

    // 初始化WPP跟踪
    WPP_INIT_TRACING(DriverObject, RegistryPath);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER,
        "%!FUNC! ExpandScreen虚拟显示驱动开始初始化");

    // 初始化驱动配置结构
    WDF_DRIVER_CONFIG_INIT(&config, ExpandScreenEvtDeviceAdd);

    // 设置驱动对象属性
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.EvtCleanupCallback = ExpandScreenEvtDriverCleanup;

    // 创建WDF驱动对象
    status = WdfDriverCreate(
        DriverObject,
        RegistryPath,
        &attributes,
        &config,
        &g_DriverObject
    );

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER,
            "WdfDriverCreate失败，状态=%!STATUS!", status);
        WPP_CLEANUP(DriverObject);
        return status;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER,
        "%!FUNC! ExpandScreen驱动初始化成功");

    return status;
}

/*++

Routine Description:
    设备添加回调函数，当检测到新设备时由框架调用

Arguments:
    Driver - WDF驱动对象
    DeviceInit - 设备初始化结构

Return Value:
    NTSTATUS

--*/
NTSTATUS ExpandScreenEvtDeviceAdd(
    _In_ WDFDRIVER       Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
)
{
    NTSTATUS status = STATUS_SUCCESS;
    WDF_PNPPOWER_EVENT_CALLBACKS pnpPowerCallbacks;
    WDF_OBJECT_ATTRIBUTES deviceAttributes;
    WDFDEVICE device = nullptr;
    PDEVICE_CONTEXT deviceContext = nullptr;

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER,
        "%!FUNC! 开始添加设备");

    // 设置PnP和电源管理回调
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnpPowerCallbacks);
    pnpPowerCallbacks.EvtDeviceD0Entry = ExpandScreenEvtDeviceD0Entry;
    pnpPowerCallbacks.EvtDeviceD0Exit = ExpandScreenEvtDeviceD0Exit;
    WdfDeviceInitSetPnpPowerEventCallbacks(DeviceInit, &pnpPowerCallbacks);

    // 设置设备IO类型
    WdfDeviceInitSetIoType(DeviceInit, WdfDeviceIoBuffered);

    // 初始化设备属性
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, DEVICE_CONTEXT);
    deviceAttributes.EvtCleanupCallback = ExpandScreenEvtDeviceCleanup;

    // 创建设备对象
    status = WdfDeviceCreate(&DeviceInit, &deviceAttributes, &device);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER,
            "WdfDeviceCreate失败，状态=%!STATUS!", status);
        return status;
    }

    // 获取设备上下文
    deviceContext = GetDeviceContext(device);
    RtlZeroMemory(deviceContext, sizeof(DEVICE_CONTEXT));
    deviceContext->Device = device;

    // 初始化IddCx适配器
    status = InitializeIddCxAdapter(device, deviceContext);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER,
            "初始化IddCx适配器失败，状态=%!STATUS!", status);
        return status;
    }

    // 创建设备接口，用于用户态通信
    status = WdfDeviceCreateDeviceInterface(
        device,
        &GUID_DEVINTERFACE_EXPANDSCREEN,
        nullptr
    );

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_DRIVER,
            "创建设备接口失败，状态=%!STATUS!", status);
        return status;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER,
        "%!FUNC! 设备添加成功");

    return status;
}

/*++

Routine Description:
    设备进入D0电源状态（工作状态）时的回调

Arguments:
    Device - WDF设备对象
    PreviousState - 之前的电源状态

Return Value:
    NTSTATUS

--*/
NTSTATUS ExpandScreenEvtDeviceD0Entry(
    _In_ WDFDEVICE Device,
    _In_ WDF_POWER_DEVICE_STATE PreviousState
)
{
    PDEVICE_CONTEXT deviceContext = GetDeviceContext(Device);

    PAGED_CODE();

    UNREFERENCED_PARAMETER(PreviousState);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER,
        "%!FUNC! 设备进入D0状态");

    // 初始化设备上下文
    deviceContext->PowerState = PowerDeviceD0;

    return STATUS_SUCCESS;
}

/*++

Routine Description:
    设备退出D0电源状态时的回调

Arguments:
    Device - WDF设备对象
    TargetState - 目标电源状态

Return Value:
    NTSTATUS

--*/
NTSTATUS ExpandScreenEvtDeviceD0Exit(
    _In_ WDFDEVICE Device,
    _In_ WDF_POWER_DEVICE_STATE TargetState
)
{
    PDEVICE_CONTEXT deviceContext = GetDeviceContext(Device);

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER,
        "%!FUNC! 设备退出D0状态，目标状态=%d", TargetState);

    deviceContext->PowerState = TargetState;

    return STATUS_SUCCESS;
}

/*++

Routine Description:
    设备清理回调

Arguments:
    Device - WDF设备对象

Return Value:
    无

--*/
VOID ExpandScreenEvtDeviceCleanup(
    _In_ WDFOBJECT Device
)
{
    PDEVICE_CONTEXT deviceContext = GetDeviceContext((WDFDEVICE)Device);

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER,
        "%!FUNC! 开始清理设备资源");

    // 清理IddCx适配器资源
    if (deviceContext->Adapter != nullptr)
    {
        // IddCx会自动清理
        deviceContext->Adapter = nullptr;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER,
        "%!FUNC! 设备资源清理完成");
}

/*++

Routine Description:
    驱动清理回调

Arguments:
    DriverObject - WDF驱动对象

Return Value:
    无

--*/
VOID ExpandScreenEvtDriverCleanup(
    _In_ WDFOBJECT DriverObject
)
{
    UNREFERENCED_PARAMETER(DriverObject);

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_DRIVER,
        "%!FUNC! 驱动清理");

    // 清理WPP跟踪
    WPP_CLEANUP(WdfDriverWdmGetDriverObject((WDFDRIVER)DriverObject));
}
