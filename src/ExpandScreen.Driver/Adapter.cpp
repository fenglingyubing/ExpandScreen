/*++

Module Name:
    Adapter.cpp

Abstract:
    IddCx适配器初始化和管理实现

Environment:
    Kernel-mode Driver Framework

--*/

#include "Driver.h"
#include "Adapter.tmh"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(PAGE, InitializeIddCxAdapter)
#pragma alloc_text(PAGE, ExpandScreenEvtAdapterInitFinished)
#pragma alloc_text(PAGE, ExpandScreenEvtAdapterCommitModes)
#endif

/*++

Routine Description:
    初始化IddCx适配器

Arguments:
    Device - WDF设备对象
    DeviceContext - 设备上下文

Return Value:
    NTSTATUS

--*/
NTSTATUS InitializeIddCxAdapter(
    _In_ WDFDEVICE Device,
    _In_ PDEVICE_CONTEXT DeviceContext
)
{
    NTSTATUS status = STATUS_SUCCESS;
    IDDCX_ADAPTER_CAPS adapterCaps = {};
    WDF_OBJECT_ATTRIBUTES adapterAttributes;
    PADAPTER_CONTEXT adapterContext = nullptr;

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_ADAPTER,
        "%!FUNC! 开始初始化IddCx适配器");

    // 初始化IddCx版本
    IDARG_IN_ADAPTER_INIT adapterInit = {};
    adapterInit.WdfDevice = Device;
    adapterInit.pCaps = &adapterCaps;
    adapterInit.ObjectAttributes = &adapterAttributes;

    // 设置适配器能力
    adapterCaps.Size = sizeof(IDDCX_ADAPTER_CAPS);
    adapterCaps.MaxMonitorsSupported = 4;  // 最多支持4个虚拟显示器
    adapterCaps.EndPointDiagnostics.Size = sizeof(IDDCX_ENDPOINT_DIAGNOSTIC_INFO);
    adapterCaps.EndPointDiagnostics.GammaSupport = IDDCX_FEATURE_IMPLEMENTATION_NONE;
    adapterCaps.EndPointDiagnostics.TransmissionType = IDDCX_TRANSMISSION_TYPE_WIRED_OTHER;

    adapterCaps.StaticDesktopReencodeFrameCount = 0;

    // 设置适配器对象属性
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&adapterAttributes, ADAPTER_CONTEXT);
    adapterAttributes.EvtCleanupCallback = nullptr;

    // 初始化IddCx适配器
    IDARG_OUT_ADAPTER_INIT adapterInitOut;
    status = IddCxAdapterInitAsync(&adapterInit, &adapterInitOut);

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_ADAPTER,
            "IddCxAdapterInitAsync失败，状态=%!STATUS!", status);
        return status;
    }

    // 保存适配器对象
    deviceContext->Adapter = adapterInitOut.AdapterObject;

    // 获取适配器上下文并初始化
    adapterContext = GetAdapterContext(adapterInitOut.AdapterObject);
    adapterContext->Adapter = adapterInitOut.AdapterObject;
    adapterContext->DeviceContext = deviceContext;

    // 注册适配器回调
    IDDCX_ADAPTER_CALLBACKS adapterCallbacks = {};
    adapterCallbacks.Size = sizeof(IDDCX_ADAPTER_CALLBACKS);
    adapterCallbacks.EvtAdapterInitFinished = ExpandScreenEvtAdapterInitFinished;
    adapterCallbacks.EvtAdapterCommitModes = ExpandScreenEvtAdapterCommitModes;

    status = IddCxAdapterInitSetCallbacks(
        adapterInitOut.AdapterObject,
        &adapterCallbacks
    );

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_ADAPTER,
            "IddCxAdapterInitSetCallbacks失败，状态=%!STATUS!", status);
        return status;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_ADAPTER,
        "%!FUNC! IddCx适配器初始化成功");

    return status;
}

/*++

Routine Description:
    适配器初始化完成回调

Arguments:
    AdapterObject - IddCx适配器对象
    pInArgs - 输入参数

Return Value:
    NTSTATUS

--*/
NTSTATUS ExpandScreenEvtAdapterInitFinished(
    _In_ IDDCX_ADAPTER AdapterObject,
    _In_ const IDARG_IN_ADAPTER_INIT_FINISHED* pInArgs
)
{
    NTSTATUS status = STATUS_SUCCESS;
    PADAPTER_CONTEXT adapterContext = GetAdapterContext(AdapterObject);

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_ADAPTER,
        "%!FUNC! 适配器初始化完成，状态=%!STATUS!",
        pInArgs->AdapterInitStatus);

    if (!NT_SUCCESS(pInArgs->AdapterInitStatus))
    {
        return pInArgs->AdapterInitStatus;
    }

    // 创建默认监视器
    IDDCX_MONITOR monitor = nullptr;
    status = CreateMonitor(AdapterObject, &monitor);

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_ADAPTER,
            "创建默认监视器失败，状态=%!STATUS!", status);
        return status;
    }

    // 增加监视器计数
    InterlockedIncrement(&adapterContext->DeviceContext->MonitorCount);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_ADAPTER,
        "%!FUNC! 默认监视器创建成功");

    return STATUS_SUCCESS;
}

/*++

Routine Description:
    提交显示模式回调

Arguments:
    AdapterObject - IddCx适配器对象
    pInArgs - 输入参数

Return Value:
    NTSTATUS

--*/
NTSTATUS ExpandScreenEvtAdapterCommitModes(
    _In_ IDDCX_ADAPTER AdapterObject,
    _In_ const IDARG_IN_COMMITMODES* pInArgs
)
{
    UNREFERENCED_PARAMETER(AdapterObject);
    UNREFERENCED_PARAMETER(pInArgs);

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_ADAPTER,
        "%!FUNC! 提交显示模式，路径数=%d", pInArgs->PathCount);

    // 简单实现：接受所有模式
    return STATUS_SUCCESS;
}
