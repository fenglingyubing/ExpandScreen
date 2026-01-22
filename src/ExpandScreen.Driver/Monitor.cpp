/*++

Module Name:
    Monitor.cpp

Abstract:
    虚拟监视器创建和管理实现

Environment:
    Kernel-mode Driver Framework

--*/

#include "Driver.h"
#include "Monitor.tmh"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(PAGE, CreateMonitor)
#pragma alloc_text(PAGE, ExpandScreenEvtMonitorGetDefaultModes)
#pragma alloc_text(PAGE, ExpandScreenEvtMonitorQueryTargetModes)
#pragma alloc_text(PAGE, ExpandScreenEvtMonitorAssignSwapChain)
#pragma alloc_text(PAGE, ExpandScreenEvtMonitorUnassignSwapChain)
#endif

// 监视器ID计数器
static LONG g_MonitorIdCounter = 0;

/*++

Routine Description:
    创建虚拟监视器对象

Arguments:
    Adapter - IddCx适配器对象
    Monitor - 输出的监视器对象

Return Value:
    NTSTATUS

--*/
NTSTATUS CreateMonitor(
    _In_ IDDCX_ADAPTER Adapter,
    _Out_ IDDCX_MONITOR* Monitor
)
{
    NTSTATUS status = STATUS_SUCCESS;
    IDDCX_MONITOR_INFO monitorInfo = {};
    WDF_OBJECT_ATTRIBUTES monitorAttributes;
    PMONITOR_CONTEXT monitorContext = nullptr;

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_MONITOR,
        "%!FUNC! 开始创建虚拟监视器");

    // 初始化监视器信息
    IDDCX_MONITOR_INFO_INIT(&monitorInfo);

    // 设置连接器类型
    monitorInfo.ConnectorIndex = InterlockedIncrement(&g_MonitorIdCounter);
    monitorInfo.MonitorType = DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL;
    monitorInfo.MonitorContainerId = GUID_NULL;

    // 设置监视器模式信息
    monitorInfo.MonitorDescription.Size = sizeof(IDDCX_MONITOR_DESCRIPTION);
    monitorInfo.MonitorDescription.Type = IDDCX_MONITOR_DESCRIPTION_TYPE_EDID;

    // 生成EDID数据
    BYTE edidData[EDID_SIZE] = { 0 };
    status = GenerateEdid(edidData, 1920, 1080);  // 默认1920x1080
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_MONITOR,
            "生成EDID失败，状态=%!STATUS!", status);
        return status;
    }

    monitorInfo.MonitorDescription.DataSize = EDID_SIZE;
    monitorInfo.MonitorDescription.pData = edidData;

    // 设置监视器对象属性
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&monitorAttributes, MONITOR_CONTEXT);

    // 创建监视器对象
    IDARG_IN_MONITORCREATE monitorCreate = {};
    monitorCreate.ObjectAttributes = &monitorAttributes;
    monitorCreate.pMonitorInfo = &monitorInfo;

    IDARG_OUT_MONITORCREATE monitorCreateOut;
    status = IddCxMonitorCreate(Adapter, &monitorCreate, &monitorCreateOut);

    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_MONITOR,
            "IddCxMonitorCreate失败，状态=%!STATUS!", status);
        return status;
    }

    *Monitor = monitorCreateOut.MonitorObject;

    // 初始化监视器上下文
    monitorContext = GetMonitorContext(monitorCreateOut.MonitorObject);
    monitorContext->Monitor = monitorCreateOut.MonitorObject;
    monitorContext->Adapter = Adapter;
    monitorContext->MonitorId = monitorInfo.ConnectorIndex;
    monitorContext->IsActive = FALSE;
    monitorContext->SwapChain = nullptr;

    // 设置监视器回调
    IDDCX_MONITOR_CALLBACKS monitorCallbacks = {};
    monitorCallbacks.Size = sizeof(IDDCX_MONITOR_CALLBACKS);
    monitorCallbacks.EvtMonitorGetDefaultDescriptionModes = ExpandScreenEvtMonitorGetDefaultModes;
    monitorCallbacks.EvtMonitorQueryTargetModes = ExpandScreenEvtMonitorQueryTargetModes;
    monitorCallbacks.EvtMonitorAssignSwapChain = ExpandScreenEvtMonitorAssignSwapChain;
    monitorCallbacks.EvtMonitorUnassignSwapChain = ExpandScreenEvtMonitorUnassignSwapChain;

    status = IddCxMonitorSetCallbacks(monitorCreateOut.MonitorObject, &monitorCallbacks);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_MONITOR,
            "IddCxMonitorSetCallbacks失败，状态=%!STATUS!", status);
        return status;
    }

    // 通知系统监视器已到达
    status = IddCxMonitorArrival(monitorCreateOut.MonitorObject, nullptr);
    if (!NT_SUCCESS(status))
    {
        TraceEvents(TRACE_LEVEL_ERROR, TRACE_MONITOR,
            "IddCxMonitorArrival失败，状态=%!STATUS!", status);
        return status;
    }

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_MONITOR,
        "%!FUNC! 虚拟监视器创建成功，ID=%d", monitorContext->MonitorId);

    return STATUS_SUCCESS;
}

/*++

Routine Description:
    获取监视器默认描述模式

Arguments:
    MonitorObject - IddCx监视器对象
    pInArgs - 输入参数
    pOutArgs - 输出参数

Return Value:
    NTSTATUS

--*/
NTSTATUS ExpandScreenEvtMonitorGetDefaultModes(
    _In_ IDDCX_MONITOR MonitorObject,
    _In_ const IDARG_IN_GETDEFAULTDESCRIPTIONMODES* pInArgs,
    _Out_ IDARG_OUT_GETDEFAULTDESCRIPTIONMODES* pOutArgs
)
{
    UNREFERENCED_PARAMETER(MonitorObject);

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_MONITOR,
        "%!FUNC! 获取默认描述模式，请求模式数=%d", pInArgs->DefaultMonitorModeBufferInputCount);

    // 填充支持的模式
    UINT modeCount = min(pInArgs->DefaultMonitorModeBufferInputCount, SUPPORTED_MODE_COUNT);

    for (UINT i = 0; i < modeCount; i++)
    {
        IDDCX_MONITOR_MODE* pMode = &pInArgs->pDefaultMonitorModes[i];

        pMode->Size = sizeof(IDDCX_MONITOR_MODE);
        pMode->Origin = IDDCX_MONITOR_MODE_ORIGIN_DRIVER;
        pMode->MonitorVideoSignalInfo.VideoStandard = D3DKMDT_VMS_OTHER;

        pMode->MonitorVideoSignalInfo.TotalSize.cx = g_SupportedModes[i].Width;
        pMode->MonitorVideoSignalInfo.TotalSize.cy = g_SupportedModes[i].Height;
        pMode->MonitorVideoSignalInfo.ActiveSize.cx = g_SupportedModes[i].Width;
        pMode->MonitorVideoSignalInfo.ActiveSize.cy = g_SupportedModes[i].Height;

        pMode->MonitorVideoSignalInfo.VSyncFreq.Numerator = g_SupportedModes[i].RefreshRate;
        pMode->MonitorVideoSignalInfo.VSyncFreq.Denominator = 1;
        pMode->MonitorVideoSignalInfo.HSyncFreq.Numerator = g_SupportedModes[i].RefreshRate * g_SupportedModes[i].Height;
        pMode->MonitorVideoSignalInfo.HSyncFreq.Denominator = 1;

        pMode->MonitorVideoSignalInfo.PixelRate =
            ((UINT64)g_SupportedModes[i].Width) *
            ((UINT64)g_SupportedModes[i].Height) *
            ((UINT64)g_SupportedModes[i].RefreshRate);

        pMode->MonitorVideoSignalInfo.ScanLineOrdering = D3DDDI_VSSLO_PROGRESSIVE;
    }

    pOutArgs->DefaultMonitorModeBufferOutputCount = modeCount;
    pOutArgs->PreferredMonitorModeIdx = 0;  // 首选第一个模式（1920x1080@60Hz）

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_MONITOR,
        "%!FUNC! 返回%d个默认模式", modeCount);

    return STATUS_SUCCESS;
}

/*++

Routine Description:
    查询目标模式

Arguments:
    MonitorObject - IddCx监视器对象
    pInArgs - 输入参数
    pOutArgs - 输出参数

Return Value:
    NTSTATUS

--*/
NTSTATUS ExpandScreenEvtMonitorQueryTargetModes(
    _In_ IDDCX_MONITOR MonitorObject,
    _In_ const IDARG_IN_QUERYTARGETMODES* pInArgs,
    _Out_ IDARG_OUT_QUERYTARGETMODES* pOutArgs
)
{
    UNREFERENCED_PARAMETER(MonitorObject);

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_MONITOR,
        "%!FUNC! 查询目标模式，请求模式数=%d", pInArgs->TargetModeBufferInputCount);

    // 填充目标模式（与默认模式相同）
    UINT modeCount = min(pInArgs->TargetModeBufferInputCount, SUPPORTED_MODE_COUNT);

    for (UINT i = 0; i < modeCount; i++)
    {
        IDDCX_TARGET_MODE* pMode = &pInArgs->pTargetModes[i];

        pMode->Size = sizeof(IDDCX_TARGET_MODE);
        pMode->TargetVideoSignalInfo.VideoStandard = D3DKMDT_VMS_OTHER;

        pMode->TargetVideoSignalInfo.TotalSize.cx = g_SupportedModes[i].Width;
        pMode->TargetVideoSignalInfo.TotalSize.cy = g_SupportedModes[i].Height;
        pMode->TargetVideoSignalInfo.ActiveSize.cx = g_SupportedModes[i].Width;
        pMode->TargetVideoSignalInfo.ActiveSize.cy = g_SupportedModes[i].Height;

        pMode->TargetVideoSignalInfo.VSyncFreq.Numerator = g_SupportedModes[i].RefreshRate;
        pMode->TargetVideoSignalInfo.VSyncFreq.Denominator = 1;
        pMode->TargetVideoSignalInfo.HSyncFreq.Numerator = g_SupportedModes[i].RefreshRate * g_SupportedModes[i].Height;
        pMode->TargetVideoSignalInfo.HSyncFreq.Denominator = 1;

        pMode->TargetVideoSignalInfo.PixelRate =
            ((UINT64)g_SupportedModes[i].Width) *
            ((UINT64)g_SupportedModes[i].Height) *
            ((UINT64)g_SupportedModes[i].RefreshRate);

        pMode->TargetVideoSignalInfo.ScanLineOrdering = D3DDDI_VSSLO_PROGRESSIVE;
    }

    pOutArgs->TargetModeBufferOutputCount = modeCount;

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_MONITOR,
        "%!FUNC! 返回%d个目标模式", modeCount);

    return STATUS_SUCCESS;
}

/*++

Routine Description:
    分配交换链回调

Arguments:
    MonitorObject - IddCx监视器对象
    pInArgs - 输入参数

Return Value:
    NTSTATUS

--*/
NTSTATUS ExpandScreenEvtMonitorAssignSwapChain(
    _In_ IDDCX_MONITOR MonitorObject,
    _In_ const IDARG_IN_SETSWAPCHAIN* pInArgs
)
{
    PMONITOR_CONTEXT monitorContext = GetMonitorContext(MonitorObject);

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_MONITOR,
        "%!FUNC! 为监视器ID=%d分配交换链", monitorContext->MonitorId);

    monitorContext->SwapChain = pInArgs->hSwapChain;
    monitorContext->IsActive = TRUE;

    // 注意：实际的帧处理将在SwapChain.cpp中实现

    return STATUS_SUCCESS;
}

/*++

Routine Description:
    取消分配交换链回调

Arguments:
    MonitorObject - IddCx监视器对象

Return Value:
    NTSTATUS

--*/
NTSTATUS ExpandScreenEvtMonitorUnassignSwapChain(
    _In_ IDDCX_MONITOR MonitorObject
)
{
    PMONITOR_CONTEXT monitorContext = GetMonitorContext(MonitorObject);

    PAGED_CODE();

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_MONITOR,
        "%!FUNC! 取消监视器ID=%d的交换链", monitorContext->MonitorId);

    monitorContext->SwapChain = nullptr;
    monitorContext->IsActive = FALSE;

    return STATUS_SUCCESS;
}
