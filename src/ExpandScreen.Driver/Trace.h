/*++

Module Name:
    Trace.h

Abstract:
    WPP跟踪定义和宏

Environment:
    Kernel-mode Driver Framework

--*/

#pragma once

//
// 定义跟踪标志
//
#define WPP_CONTROL_GUIDS \
    WPP_DEFINE_CONTROL_GUID( \
        ExpandScreenTraceGuid, (E5F84A51,B5C1,4F42,9C3D,8E9A4B6C7D8E), \
        WPP_DEFINE_BIT(TRACE_DRIVER)      /* bit  0 = 0x00000001 */ \
        WPP_DEFINE_BIT(TRACE_ADAPTER)     /* bit  1 = 0x00000002 */ \
        WPP_DEFINE_BIT(TRACE_MONITOR)     /* bit  2 = 0x00000004 */ \
        WPP_DEFINE_BIT(TRACE_SWAPCHAIN)   /* bit  3 = 0x00000008 */ \
        WPP_DEFINE_BIT(TRACE_EDID)        /* bit  4 = 0x00000010 */ \
        WPP_DEFINE_BIT(TRACE_IOCTL)       /* bit  5 = 0x00000020 */ \
        )

#define WPP_LEVEL_FLAGS_LOGGER(lvl,flags) \
           WPP_LEVEL_LOGGER(flags)

#define WPP_LEVEL_FLAGS_ENABLED(lvl, flags) \
           (WPP_LEVEL_ENABLED(flags) && WPP_CONTROL(WPP_BIT_ ## flags).Level >= lvl)

//
// 此注释块由WPP扫描器扫描
// begin_wpp config
// FUNC TraceEvents(LEVEL, FLAGS, MSG, ...);
// end_wpp
//
