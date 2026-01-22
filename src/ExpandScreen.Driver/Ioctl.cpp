/*++

Module Name:
    Ioctl.cpp

Abstract:
    IOCTL接口实现，用于用户态和驱动通信

Environment:
    Kernel-mode Driver Framework

--*/

#include "Driver.h"
#include "Ioctl.tmh"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(PAGE, InitializeIoctlInterface)
#pragma alloc_text(PAGE, ExpandScreenEvtIoDeviceControl)
#endif

/*++

Routine Description:
    初始化IOCTL接口

Arguments:
    Device - WDF设备对象

Return Value:
    NTSTATUS

--*/
NTSTATUS InitializeIoctlInterface(
    _In_ WDFDEVICE Device
)
{
    NTSTATUS status = STATUS_SUCCESS;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDFQUEUE queue = nullptr;

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_IOCTL,
        "%!FUNC! 初始化IOCTL接口");

    // 创建默认队列用于处理IOCTL请求
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = ExpandScreenEvtIoDeviceControl;

    status = WdfIoQueueCreate(
        Device,
        &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES,
        &queue
    );

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_IOCTL,
            "创建IO队列失败，状态=%!STATUS!", status);
        return status;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_IOCTL,
        "%!FUNC! IOCTL接口初始化成功");

    return status;
}

/*++

Routine Description:
    处理IOCTL请求

Arguments:
    Queue - IO队列对象
    Request - IO请求对象
    OutputBufferLength - 输出缓冲区长度
    InputBufferLength - 输入缓冲区长度
    IoControlCode - IOCTL控制码

Return Value:
    无

--*/
VOID ExpandScreenEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
)
{
    NTSTATUS status = STATUS_SUCCESS;
    WDFDEVICE device = WdfIoQueueGetDevice(Queue);
    PDEVICE_CONTEXT deviceContext = GetDeviceContext(device);
    size_t bytesReturned = 0;

    PAGED_CODE();

    UNREFERENCED_PARAMETER(InputBufferLength);
    UNREFERENCED_PARAMETER(OutputBufferLength);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_IOCTL,
        "%!FUNC! 收到IOCTL请求，代码=0x%X", IoControlCode);

    switch (IoControlCode)
    {
    case IOCTL_EXPANDSCREEN_CREATE_MONITOR:
    {
        // 创建虚拟监视器
        PEXPANDSCREEN_CREATE_MONITOR_INPUT pInput = nullptr;
        PEXPANDSCREEN_CREATE_MONITOR_OUTPUT pOutput = nullptr;

        status = WdfRequestRetrieveInputBuffer(
            Request,
            sizeof(EXPANDSCREEN_CREATE_MONITOR_INPUT),
            (PVOID*)&pInput,
            nullptr
        );

        if (!NT_SUCCESS(status))
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_IOCTL,
                "获取输入缓冲区失败，状态=%!STATUS!", status);
            break;
        }

        status = WdfRequestRetrieveOutputBuffer(
            Request,
            sizeof(EXPANDSCREEN_CREATE_MONITOR_OUTPUT),
            (PVOID*)&pOutput,
            nullptr
        );

        if (!NT_SUCCESS(status))
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_IOCTL,
                "获取输出缓冲区失败，状态=%!STATUS!", status);
            break;
        }

        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_IOCTL,
            "创建监视器: %dx%d@%dHz",
            pInput->Width, pInput->Height, pInput->RefreshRate);

        // 创建监视器
        IDDCX_MONITOR monitor = nullptr;
        status = CreateMonitor(deviceContext->Adapter, &monitor);

        if (NT_SUCCESS(status))
        {
            InterlockedIncrement(&deviceContext->MonitorCount);
            pOutput->MonitorId = (UINT)InterlockedIncrement(&g_MonitorIdCounter);
            pOutput->Status = STATUS_SUCCESS;
            bytesReturned = sizeof(EXPANDSCREEN_CREATE_MONITOR_OUTPUT);

            TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_IOCTL,
                "监视器创建成功，ID=%d", pOutput->MonitorId);
        }
        else
        {
            pOutput->MonitorId = 0;
            pOutput->Status = status;
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_IOCTL,
                "监视器创建失败，状态=%!STATUS!", status);
        }

        status = STATUS_SUCCESS;  // IOCTL本身成功
        break;
    }

    case IOCTL_EXPANDSCREEN_DESTROY_MONITOR:
    {
        // TODO: 实现监视器销毁
        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_IOCTL,
            "销毁监视器（未实现）");
        status = STATUS_NOT_IMPLEMENTED;
        break;
    }

    case IOCTL_EXPANDSCREEN_GET_ADAPTER_INFO:
    {
        // 获取适配器信息
        PEXPANDSCREEN_ADAPTER_INFO pOutput = nullptr;

        status = WdfRequestRetrieveOutputBuffer(
            Request,
            sizeof(EXPANDSCREEN_ADAPTER_INFO),
            (PVOID*)&pOutput,
            nullptr
        );

        if (!NT_SUCCESS(status))
        {
            TraceEvents(TRACE_LEVEL_ERROR, TRACE_IOCTL,
                "获取输出缓冲区失败，状态=%!STATUS!", status);
            break;
        }

        pOutput->MonitorCount = (UINT)deviceContext->MonitorCount;
        pOutput->MaxMonitors = 4;  // 最大支持4个监视器
        bytesReturned = sizeof(EXPANDSCREEN_ADAPTER_INFO);

        TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_IOCTL,
            "返回适配器信息: 当前监视器=%d, 最大=%d",
            pOutput->MonitorCount, pOutput->MaxMonitors);

        status = STATUS_SUCCESS;
        break;
    }

    default:
        TraceEvents(TRACE_LEVEL_WARNING, TRACE_IOCTL,
            "未知的IOCTL代码: 0x%X", IoControlCode);
        status = STATUS_INVALID_DEVICE_REQUEST;
        break;
    }

    WdfRequestCompleteWithInformation(Request, status, bytesReturned);
}
