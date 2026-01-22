/*++

Module Name:
    Edid.cpp

Abstract:
    EDID (Extended Display Identification Data) 生成实现

Environment:
    Kernel-mode Driver Framework

--*/

#include "Driver.h"
#include "Edid.tmh"

#ifdef ALLOC_PRAGMA
#pragma alloc_text(PAGE, GenerateEdid)
#endif

/*++

Routine Description:
    生成标准EDID数据

Arguments:
    EdidBuffer - 输出EDID数据的缓冲区
    Width - 显示宽度
    Height - 显示高度

Return Value:
    NTSTATUS

--*/
NTSTATUS GenerateEdid(
    _Out_writes_bytes_(EDID_SIZE) BYTE* EdidBuffer,
    _In_ UINT Width,
    _In_ UINT Height
)
{
    PAGED_CODE();

    if (EdidBuffer == nullptr)
    {
        return STATUS_INVALID_PARAMETER;
    }

    RtlZeroMemory(EdidBuffer, EDID_SIZE);

    // EDID Header (8 bytes)
    EdidBuffer[0] = 0x00;
    EdidBuffer[1] = 0xFF;
    EdidBuffer[2] = 0xFF;
    EdidBuffer[3] = 0xFF;
    EdidBuffer[4] = 0xFF;
    EdidBuffer[5] = 0xFF;
    EdidBuffer[6] = 0xFF;
    EdidBuffer[7] = 0x00;

    // Manufacturer ID (2 bytes) - "EXP" for ExpandScreen
    EdidBuffer[8] = 0x15;  // 00010 10101 (E, X)
    EdidBuffer[9] = 0x30;  // 01 10000 (P)

    // Product Code (2 bytes)
    EdidBuffer[10] = 0x01;
    EdidBuffer[11] = 0x00;

    // Serial Number (4 bytes)
    EdidBuffer[12] = 0x01;
    EdidBuffer[13] = 0x00;
    EdidBuffer[14] = 0x00;
    EdidBuffer[15] = 0x00;

    // Week of Manufacture
    EdidBuffer[16] = 0x01;

    // Year of Manufacture (2026 - 1990 = 36)
    EdidBuffer[17] = 0x24;

    // EDID Version
    EdidBuffer[18] = 0x01;  // Version 1
    EdidBuffer[19] = 0x04;  // Revision 4

    // Video Input Definition
    EdidBuffer[20] = 0x95;  // Digital input, 8-bit color

    // Max Horizontal Image Size (cm)
    EdidBuffer[21] = (BYTE)(Width * 254 / 960 / 10);

    // Max Vertical Image Size (cm)
    EdidBuffer[22] = (BYTE)(Height * 254 / 960 / 10);

    // Display Gamma (2.2)
    EdidBuffer[23] = 0x78;

    // Feature Support
    EdidBuffer[24] = 0x2A;

    // Color Characteristics (10 bytes) - Standard sRGB
    EdidBuffer[25] = 0x0D;
    EdidBuffer[26] = 0xC9;
    EdidBuffer[27] = 0xA0;
    EdidBuffer[28] = 0x57;
    EdidBuffer[29] = 0x47;
    EdidBuffer[30] = 0x98;
    EdidBuffer[31] = 0x27;
    EdidBuffer[32] = 0x12;
    EdidBuffer[33] = 0x48;
    EdidBuffer[34] = 0x4C;

    // Established Timing Bitmap
    EdidBuffer[35] = 0x00;
    EdidBuffer[36] = 0x00;
    EdidBuffer[37] = 0x00;

    // Standard Timing Information (16 bytes)
    RtlZeroMemory(&EdidBuffer[38], 16);

    // Detailed Timing Descriptor 1 (18 bytes) - Preferred timing
    UINT pixelClock = Width * Height * 60 / 10000;  // in 10kHz units
    EdidBuffer[54] = (BYTE)(pixelClock & 0xFF);
    EdidBuffer[55] = (BYTE)((pixelClock >> 8) & 0xFF);

    EdidBuffer[56] = (BYTE)(Width & 0xFF);  // Horizontal Active (lower 8 bits)
    EdidBuffer[57] = 0x30;  // Horizontal Blanking (lower 8 bits)
    EdidBuffer[58] = (BYTE)(((Width >> 8) & 0x0F) << 4);  // Upper 4 bits

    EdidBuffer[59] = (BYTE)(Height & 0xFF);  // Vertical Active (lower 8 bits)
    EdidBuffer[60] = 0x1E;  // Vertical Blanking (lower 8 bits)
    EdidBuffer[61] = (BYTE)(((Height >> 8) & 0x0F) << 4);  // Upper 4 bits

    // Remaining detailed timing bytes
    RtlZeroMemory(&EdidBuffer[62], 10);

    // Detailed Timing Descriptor 2 - Display Name
    EdidBuffer[72] = 0x00;
    EdidBuffer[73] = 0x00;
    EdidBuffer[74] = 0x00;
    EdidBuffer[75] = 0xFC;  // Display Product Name
    EdidBuffer[76] = 0x00;

    const char* displayName = "ExpandScreen";
    size_t nameLen = strlen(displayName);
    for (size_t i = 0; i < 13 && i < nameLen; i++)
    {
        EdidBuffer[77 + i] = (BYTE)displayName[i];
    }
    EdidBuffer[77 + nameLen] = 0x0A;  // Line feed

    // Detailed Timing Descriptor 3 & 4 - Empty
    RtlZeroMemory(&EdidBuffer[90], 36);

    // Extension Flag
    EdidBuffer[126] = 0x00;

    // Checksum
    BYTE checksum = 0;
    for (int i = 0; i < 127; i++)
    {
        checksum += EdidBuffer[i];
    }
    EdidBuffer[127] = (BYTE)(256 - checksum);

    TraceEvents(TRACE_LEVEL_INFORMATION, TRACE_EDID,
        "生成EDID成功: %dx%d", Width, Height);

    return STATUS_SUCCESS;
}
