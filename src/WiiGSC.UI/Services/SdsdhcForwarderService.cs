// Ported from ForwardMii (part of CustomizeMii) by Leathl
// Original: https://github.com/Brawl345/customizemii
// License: GNU General Public License v3.0
//
// ForwardMii is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.

using System;
using System.IO;
using System.Reflection;

namespace WiiGSC.UI.Services;

/// <summary>
/// Generates SDSDHC forwarder DOLs that boot homebrew apps from SD card.
/// The forwarder boots the Wii, mounts SD, and loads sd:/apps/{folder}/boot.dol
/// Ported from ForwardMii SDSDHC_Forwarder by Leathl (GPL-3.0).
/// </summary>
public class SdsdhcForwarderService
{
    private const string ResourcePrefix = "WiiGSC.UI.Resources.ForwardMii.";

    /// <summary>
    /// Generates a forwarder DOL that boots the specified app folder from SD card.
    /// </summary>
    /// <param name="appFolder">App folder name (3-18 characters), e.g. "wiimc" or "usbloader_gx"</param>
    /// <param name="forwardToElf">If true, loads boot.elf instead of boot.dol</param>
    /// <returns>Complete forwarder DOL byte array ready to embed in a WAD</returns>
    public byte[] ToByteArray(string appFolder, bool forwardToElf = false)
    {
        if (string.IsNullOrEmpty(appFolder))
            throw new ArgumentException("App folder name cannot be empty.", nameof(appFolder));
        if (appFolder.Length < 3 || appFolder.Length > 18)
            throw new ArgumentException("App folder name must be 3-18 characters.", nameof(appFolder));

        using var baseStream = GetBase(appFolder.Length);
        using var tailStream = GetTail(appFolder.Length);

        var convertedBase = ConvertBase(baseStream, appFolder.Length);
        var patchedTail = EditTailFolder(tailStream, appFolder, forwardToElf);

        byte[] baseArray = convertedBase.ToArray();
        byte[] tailArray = patchedTail.ToArray();

        using var result = new MemoryStream(baseArray.Length + tailArray.Length);
        result.Write(baseArray, 0, baseArray.Length);
        result.Write(tailArray, 0, tailArray.Length);
        return result.ToArray();
    }

    private MemoryStream GetBase(int length) => length switch
    {
        >= 3 and <= 5 => LoadResource("3CharsBase.bin"),
        >= 6 and <= 16 => LoadResource("6CharsBase.bin"),
        17 or 18 => LoadResource("17CharsBase.bin"),
        _ => throw new ArgumentOutOfRangeException(nameof(length))
    };

    private MemoryStream GetTail(int length) =>
        LoadResource($"{length}CharsTail.bin");

    private static MemoryStream ConvertBase(MemoryStream baseStream, int length)
    {
        // Some lengths use the base as-is; others need byte-level patching
        (int offset, byte value)[]? patches = length switch
        {
            3 or 4 => null,       // 3CharsBase used as-is
            5 => PatchTable5,
            6 or 7 or 8 => null,  // 6CharsBase used as-is
            9 => PatchTable9,
            10 or 11 or 12 => PatchTable10,
            13 => PatchTable13,
            14 or 15 or 16 => PatchTable14,
            17 => null,           // 17CharsBase used as-is
            18 => PatchTable18,
            _ => throw new ArgumentOutOfRangeException(nameof(length))
        };

        if (patches != null)
            ApplyPatches(baseStream, patches);

        return baseStream;
    }

    private static MemoryStream EditTailFolder(MemoryStream tailStream, string appFolder, bool forwardToElf)
    {
        char[] folderChars = appFolder.ToCharArray();
        int[] offsets = GetOffsets(folderChars.Length);

        // Write folder name at 3 locations in the tail
        tailStream.Seek(1, SeekOrigin.Begin);
        foreach (char c in folderChars)
            tailStream.WriteByte((byte)c);

        tailStream.Seek(offsets[0], SeekOrigin.Begin);
        foreach (char c in folderChars)
            tailStream.WriteByte((byte)c);

        tailStream.Seek(offsets[1], SeekOrigin.Begin);
        foreach (char c in folderChars)
            tailStream.WriteByte((byte)c);

        if (forwardToElf)
            EditTailToElf(tailStream, appFolder.Length);

        return tailStream;
    }

    private static void EditTailToElf(MemoryStream tailStream, int charCount)
    {
        int[] offsets = GetDolOffsets(charCount);
        byte[] elf = { 0x65, 0x6c, 0x66 }; // "elf"

        foreach (int offset in offsets)
        {
            tailStream.Seek(offset, SeekOrigin.Begin);
            tailStream.Write(elf, 0, elf.Length);
        }
    }

    private static int[] GetOffsets(int charCount) => charCount switch
    {
        3 or 4 => [57, 100],
        5 => [57, 104],
        6 or 7 or 8 => [61, 108],
        9 => [61, 112],
        10 or 11 or 12 or 13 => [65, 120],
        14 or 15 or 16 => [69, 124],
        17 => [69, 128],
        18 => [73, 132],
        _ => throw new ArgumentOutOfRangeException(nameof(charCount))
    };

    private static int[] GetDolOffsets(int charCount) => charCount switch
    {
        3 => [10, 25, 87, 336, 344],
        4 => [11, 25, 87, 336, 344],
        5 => [12, 25, 91, 340, 348],
        6 => [13, 29, 95, 348, 356],
        7 => [14, 29, 95, 348, 356],
        8 => [15, 29, 95, 348, 356],
        9 => [16, 29, 99, 352, 360],
        10 => [17, 33, 103, 360, 368],
        11 => [18, 33, 103, 360, 368],
        12 => [19, 33, 103, 360, 368],
        13 => [20, 33, 107, 364, 372],
        14 => [21, 37, 111, 372, 380],
        15 => [22, 37, 111, 372, 380],
        16 => [23, 37, 111, 372, 380],
        17 => [24, 37, 115, 376, 384],
        18 => [25, 41, 119, 384, 392],
        _ => throw new ArgumentOutOfRangeException(nameof(charCount))
    };

    private static void ApplyPatches(MemoryStream stream, (int offset, byte value)[] patches)
    {
        foreach (var (offset, value) in patches)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            stream.WriteByte(value);
        }
    }

    private MemoryStream LoadResource(string filename)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = ResourcePrefix + filename;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
        byte[] data = new byte[stream.Length];
        stream.Read(data, 0, data.Length);
        return new MemoryStream(data);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Base conversion patch tables (offset → byte value)
    // Ported from ForwardMii_BaseEdit.cs (SDSDHC_ConvertBaseStream)
    // ═══════════════════════════════════════════════════════════════════

    private static readonly (int, byte)[] PatchTable5 =
    [
        (16823, 0xd0), (16939, 0xd0), (17067, 0xd0), (17439, 0xa4), (17835, 0xa4),
        (21203, 0xa4), (29399, 0xfc), (33439, 0xa4),
        (69738, 0x01), (69739, 0x00),
        (69767, 0x2c), (69799, 0x5c), (69815, 0x70), (69839, 0x90), (69855, 0x94),
        (69871, 0xc8), (69999, 0xe4), (70011, 0xfc), (70047, 0x0c), (70071, 0x48),
        (70279, 0x1c), (70319, 0xcc), (70351, 0x44), (70795, 0x4c), (71315, 0xbc),
        (71627, 0xb0), (71655, 0xb4), (72171, 0xbc),
        (91719, 0xc0), (93627, 0x18), (94039, 0xc0),
        (95527, 0x08), (95895, 0x18), (96383, 0x08), (96667, 0xc0),
        (97171, 0xf4), (98075, 0xf4),
        (98711, 0x40), (99071, 0x14), (99079, 0x2c), (99082, 0x04), (99083, 0x00),
        (100619, 0xec), (100631, 0xe0), (100662, 0x05), (100663, 0x00),
        (100687, 0x10), (100723, 0x40), (100755, 0x70), (100787, 0xa0), (100819, 0xd0),
        (100850, 0x06), (100851, 0x00),
        (100883, 0x30), (100915, 0x60), (100947, 0x90), (100975, 0xb8), (101019, 0xd0),
        (101043, 0xe0), (101047, 0xe8), (101063, 0xec), (101067, 0xf0),
        (101287, 0x20), (101311, 0x38), (101315, 0x44),
        (101507, 0xf4), (101523, 0x04),
        (101615, 0xa4), (101803, 0xa4),
        (102259, 0x28), (102283, 0x80), (102299, 0x80), (102311, 0x80), (102327, 0x80),
        (102343, 0x80), (102359, 0x80), (102375, 0x80), (102391, 0x80),
        (106287, 0xfc), (114255, 0x20), (116755, 0x40), (116767, 0x58),
        (120895, 0x24), (128803, 0xa8), (129483, 0x90),
        (136707, 0xd0), (137175, 0xd0),
        (140083, 0x68), (140535, 0x90), (140847, 0xd4), (140855, 0x18), (140859, 0xb8),
        (144215, 0xc0), (144735, 0xe8),
        (144951, 0x78), (145071, 0x7c), (145235, 0x84), (145315, 0x88), (145395, 0x78),
        (145563, 0xac), (145687, 0xb4), (145847, 0x84), (145955, 0x5c),
        (146127, 0xac), (146251, 0xb4), (146375, 0xc8), (146647, 0xcc),
        (146794, 0x0e), (146795, 0x00), (146923, 0xd8), (147151, 0xdc),
        (153442, 0x0f), (153443, 0x00), (153719, 0x20),
        (178711, 0x18), (178715, 0xa4), (178719, 0xa4), (178723, 0xa4), (178727, 0xa4),
        (178731, 0xa4), (178735, 0xa4), (178739, 0xa4), (178743, 0xa4), (178747, 0xa4)
    ];

    private static readonly (int, byte)[] PatchTable9 =
    [
        (16823, 0xdc), (16939, 0xdc), (17067, 0xdc), (17439, 0xb0), (17835, 0xb0),
        (21203, 0xb0), (29399, 0x08), (33439, 0xb0),
        (69739, 0x08),
        (69767, 0x38), (69799, 0x68), (69815, 0x7c), (69839, 0x9c), (69855, 0xa0),
        (69871, 0xd4), (69999, 0xf0), (70011, 0x08), (70047, 0x18), (70071, 0x54),
        (70279, 0x28), (70319, 0xd8), (70351, 0x50), (70795, 0x58), (71315, 0xc8),
        (71627, 0xbc), (71655, 0xc0), (72171, 0xc8),
        (91719, 0xcc), (93627, 0x24), (94039, 0xcc),
        (95527, 0x14), (95895, 0x24), (96383, 0x14), (96667, 0xcc),
        (97170, 0x03), (97171, 0x00), (98074, 0x03), (98075, 0x00),
        (98711, 0x4c), (99071, 0x20), (99079, 0x38), (99083, 0x0c),
        (100619, 0xf8), (100631, 0xec), (100663, 0x0c),
        (100687, 0x1c), (100723, 0x4c), (100755, 0x7c), (100787, 0xac), (100819, 0xdc),
        (100851, 0x0c),
        (100883, 0x3c), (100915, 0x6c), (100947, 0x9c), (100975, 0xc4), (101019, 0xdc),
        (101043, 0xec), (101047, 0xf4), (101063, 0xf8), (101067, 0xfc),
        (101287, 0x2c), (101311, 0x44), (101315, 0x50),
        (101506, 0x07), (101507, 0x00), (101523, 0x10),
        (101615, 0xb0), (101803, 0xb0),
        (102259, 0x34), (102283, 0x8c), (102299, 0x8c), (102311, 0x8c), (102327, 0x8c),
        (102343, 0x8c), (102359, 0x8c), (102375, 0x8c), (102391, 0x8c),
        (106287, 0x08), (114255, 0x2c), (116755, 0x4c), (116767, 0x64),
        (120895, 0x30), (128803, 0xb4), (129483, 0x9c),
        (136707, 0xdc), (137175, 0xdc),
        (140083, 0x74), (140535, 0x9c), (140847, 0xe0), (140855, 0x24), (140859, 0xc4),
        (144215, 0xcc), (144735, 0xf4),
        (144951, 0x84), (145071, 0x88), (145235, 0x90), (145315, 0x94), (145395, 0x84),
        (145563, 0xb8), (145687, 0xc0), (145847, 0x90), (145955, 0x68),
        (146127, 0xb8), (146251, 0xc0), (146375, 0xd4), (146647, 0xd8),
        (146795, 0x0c), (146923, 0xe4), (147151, 0xe8),
        (153443, 0x0c), (153719, 0x2c),
        (178711, 0x24), (178715, 0xb0), (178719, 0xb0), (178723, 0xb0), (178727, 0xb0),
        (178731, 0xb0), (178735, 0xb0), (178739, 0xb0), (178743, 0xb0), (178747, 0xb0)
    ];

    private static readonly (int, byte)[] PatchTable10 =
    [
        (16823, 0xe4), (16939, 0xe4), (17067, 0xe4), (17439, 0xb8), (17835, 0xb8),
        (21203, 0xb8), (29399, 0x10), (33439, 0xb8),
        (69695, 0xc8), (69739, 0x0c),
        (69767, 0x40), (69799, 0x70), (69815, 0x84), (69839, 0xa4), (69855, 0xa8),
        (69871, 0xdc), (69999, 0xf8), (70011, 0x10), (70047, 0x20), (70071, 0x5c),
        (70279, 0x30), (70319, 0xe0), (70351, 0x58), (70371, 0xcc),
        (70795, 0x60), (71315, 0xd0),
        (71627, 0xc4), (71655, 0xc8), (72171, 0xd0),
        (91719, 0xd4), (93627, 0x2c), (94039, 0xd4),
        (95527, 0x1c), (95895, 0x2c), (96383, 0x1c), (96667, 0xd4),
        (97170, 0x03), (97171, 0x08), (98074, 0x03), (98075, 0x08),
        (98711, 0x54), (99071, 0x28), (99079, 0x40), (99083, 0x14),
        (100618, 0x08), (100619, 0x00), (100631, 0xf4), (100663, 0x14),
        (100687, 0x24), (100723, 0x54), (100755, 0x84), (100787, 0xb4), (100819, 0xe4),
        (100851, 0x14),
        (100883, 0x44), (100915, 0x74), (100947, 0xa4), (100975, 0xcc), (101019, 0xe4),
        (101043, 0xf4), (101047, 0xfc),
        (101062, 0x07), (101063, 0x00), (101066, 0x07), (101067, 0x04),
        (101287, 0x34), (101311, 0x4c), (101315, 0x58),
        (101506, 0x07), (101507, 0x08), (101523, 0x18),
        (101615, 0xb8), (101803, 0xb8),
        (102259, 0x3c), (102283, 0x94), (102299, 0x94), (102311, 0x94), (102327, 0x94),
        (102343, 0x94), (102359, 0x94), (102375, 0x94), (102391, 0x94),
        (106287, 0x10), (114255, 0x34), (116755, 0x54), (116767, 0x6c),
        (120895, 0x38), (128803, 0xbc), (129483, 0xa4),
        (136707, 0xe4), (137175, 0xe4),
        (140083, 0x7c), (140535, 0xa4), (140847, 0xe8), (140855, 0x2c), (140859, 0xcc),
        (144215, 0xd4), (144735, 0xfc),
        (144951, 0x8c), (145071, 0x90), (145235, 0x98), (145315, 0x9c), (145395, 0x8c),
        (145563, 0xc0), (145687, 0xc8), (145847, 0x98), (145955, 0x70),
        (146127, 0xc0), (146251, 0xc8), (146375, 0xdc), (146647, 0xe0),
        (146795, 0x14), (146923, 0xec), (147151, 0xf0),
        (153443, 0x14), (153719, 0x34),
        (178711, 0x2c), (178715, 0xb8), (178719, 0xb8), (178723, 0xb8), (178727, 0xb8),
        (178731, 0xb8), (178735, 0xb8), (178739, 0xb8), (178743, 0xb8), (178747, 0xb8)
    ];

    private static readonly (int, byte)[] PatchTable13 =
    [
        (16823, 0xe8), (16939, 0xe8), (17067, 0xe8), (17439, 0xbc), (17835, 0xbc),
        (21203, 0xbc), (29399, 0x14), (33439, 0xbc),
        (69695, 0xc8), (69739, 0x10),
        (69767, 0x44), (69799, 0x74), (69815, 0x88), (69839, 0xa8), (69855, 0xac),
        (69871, 0xe0), (69999, 0xfc), (70011, 0x14), (70047, 0x24), (70071, 0x60),
        (70279, 0x34), (70319, 0xe4), (70351, 0x5c), (70371, 0xcc),
        (70795, 0x64), (71315, 0xd4),
        (71627, 0xc8), (71655, 0xcc), (72171, 0xd4),
        (91719, 0xd8), (93627, 0x30), (94039, 0xd8),
        (95527, 0x20), (95895, 0x30), (96383, 0x20), (96667, 0xd8),
        (97170, 0x03), (97171, 0x0c), (98074, 0x03), (98075, 0x0c),
        (98711, 0x58), (99071, 0x2c), (99079, 0x44), (99083, 0x18),
        (100618, 0x08), (100619, 0x04), (100631, 0xf8), (100663, 0x18),
        (100687, 0x28), (100723, 0x58), (100755, 0x88), (100787, 0xb8), (100819, 0xe8),
        (100851, 0x18),
        (100883, 0x48), (100915, 0x78), (100947, 0xa8), (100975, 0xd0), (101019, 0xe8),
        (101043, 0xf8), (101046, 0x07), (101047, 0x00),
        (101062, 0x07), (101063, 0x04), (101066, 0x07), (101067, 0x08),
        (101287, 0x38), (101311, 0x50), (101315, 0x5c),
        (101506, 0x07), (101507, 0x0c), (101523, 0x1c),
        (101615, 0xbc), (101803, 0xbc),
        (102259, 0x40), (102283, 0x98), (102299, 0x98), (102311, 0x98), (102327, 0x98),
        (102343, 0x98), (102359, 0x98), (102375, 0x98), (102391, 0x98),
        (106287, 0x14), (114255, 0x38), (116755, 0x58), (116767, 0x70),
        (120895, 0x3c), (128803, 0xc0), (129483, 0xa8),
        (136707, 0xe8), (137175, 0xe8),
        (140083, 0x80), (140535, 0xa8), (140847, 0xec), (140855, 0x30), (140859, 0xd0),
        (144215, 0xd8), (144734, 0x0e), (144735, 0x00),
        (144951, 0x90), (145071, 0x94), (145235, 0x9c), (145315, 0xa0), (145395, 0x90),
        (145563, 0xc4), (145687, 0xcc), (145847, 0x9c), (145955, 0x74),
        (146127, 0xc4), (146251, 0xcc), (146375, 0xe0), (146647, 0xe4),
        (146795, 0x18), (146923, 0xf0), (147151, 0xf4),
        (153443, 0x18), (153719, 0x38),
        (178711, 0x30), (178715, 0xbc), (178719, 0xbc), (178723, 0xbc), (178727, 0xbc),
        (178731, 0xbc), (178735, 0xbc), (178739, 0xbc), (178743, 0xbc), (178747, 0xbc)
    ];

    private static readonly (int, byte)[] PatchTable14 =
    [
        (16823, 0xf0), (16939, 0xf0), (17067, 0xf0), (17439, 0xc4), (17835, 0xc4),
        (21203, 0xc4), (29399, 0x1c), (33439, 0xc4),
        (69695, 0xcc), (69739, 0x14),
        (69767, 0x4c), (69799, 0x7c), (69815, 0x90), (69839, 0xb0), (69855, 0xb4),
        (69871, 0xe8), (69998, 0x02), (69999, 0x04), (70011, 0x1c), (70047, 0x2c),
        (70071, 0x68), (70279, 0x3c), (70319, 0xec), (70351, 0x64), (70371, 0xd0),
        (70795, 0x6c), (71315, 0xdc),
        (71627, 0xd0), (71655, 0xd4), (72171, 0xdc),
        (91719, 0xe0), (93627, 0x38), (94039, 0xe0),
        (95527, 0x28), (95895, 0x38), (96383, 0x28), (96667, 0xe0),
        (97170, 0x03), (97171, 0x14), (98074, 0x03), (98075, 0x14),
        (98711, 0x60), (99071, 0x34), (99079, 0x4c), (99083, 0x20),
        (100618, 0x08), (100619, 0x0c), (100630, 0x05), (100631, 0x00),
        (100663, 0x20),
        (100687, 0x30), (100723, 0x60), (100755, 0x90), (100787, 0xc0), (100819, 0xf0),
        (100851, 0x20),
        (100883, 0x50), (100915, 0x80), (100947, 0xb0), (100975, 0xd8), (101019, 0xf0),
        (101042, 0x07), (101043, 0x00), (101046, 0x07), (101047, 0x08),
        (101062, 0x07), (101063, 0x0c), (101066, 0x07), (101067, 0x10),
        (101287, 0x40), (101311, 0x58), (101315, 0x64),
        (101506, 0x07), (101507, 0x14), (101523, 0x24),
        (101615, 0xc4), (101803, 0xc4),
        (102259, 0x48), (102283, 0xa0), (102299, 0xa0), (102311, 0xa0), (102327, 0xa0),
        (102343, 0xa0), (102359, 0xa0), (102375, 0xa0), (102391, 0xa0),
        (106287, 0x1c), (114255, 0x40), (116755, 0x60), (116767, 0x78),
        (120895, 0x44), (128803, 0xc8), (129483, 0xb0),
        (136707, 0xf0), (137175, 0xf0),
        (140083, 0x88), (140535, 0xb0), (140847, 0xf4), (140855, 0x38), (140859, 0xd8),
        (144215, 0xe0), (144734, 0x0e), (144735, 0x08),
        (144951, 0x98), (145071, 0x9c), (145235, 0xa4), (145315, 0xa8), (145395, 0x98),
        (145563, 0xcc), (145687, 0xd4), (145847, 0xa4), (145955, 0x7c),
        (146127, 0xcc), (146251, 0xd4), (146375, 0xe8), (146647, 0xec),
        (146795, 0x20), (146923, 0xf8), (147151, 0xfc),
        (153443, 0x20), (153719, 0x40),
        (178711, 0x38), (178715, 0xc4), (178719, 0xc4), (178723, 0xc4), (178727, 0xc4),
        (178731, 0xc4), (178735, 0xc4), (178739, 0xc4), (178743, 0xc4), (178747, 0xc4)
    ];

    private static readonly (int, byte)[] PatchTable18 =
    [
        (16823, 0xfc), (16939, 0xfc), (17067, 0xfc), (17439, 0xd0), (17835, 0xd0),
        (21203, 0xd0), (29399, 0x28), (33439, 0xd0),
        (69695, 0xd0), (69739, 0x1c),
        (69767, 0x58), (69799, 0x88), (69815, 0x9c), (69839, 0xbc), (69855, 0xc0),
        (69871, 0xf4), (69999, 0x10), (70011, 0x28), (70047, 0x38), (70071, 0x74),
        (70279, 0x48), (70319, 0xf8), (70351, 0x70), (70371, 0xd4),
        (70795, 0x78), (71315, 0xe8),
        (71627, 0xdc), (71655, 0xe0), (72171, 0xe8),
        (91719, 0xec), (93627, 0x44), (94039, 0xec),
        (95527, 0x34), (95895, 0x44), (96383, 0x34), (96667, 0xec),
        (97171, 0x20), (98075, 0x20),
        (98711, 0x6c), (99071, 0x40), (99079, 0x58), (99083, 0x2c),
        (100619, 0x18), (100631, 0x0c), (100663, 0x2c),
        (100687, 0x3c), (100723, 0x6c), (100755, 0x9c), (100787, 0xcc), (100819, 0xfc),
        (100851, 0x2c),
        (100883, 0x5c), (100915, 0x8c), (100947, 0xbc), (100975, 0xe4), (101019, 0xfc),
        (101043, 0x0c), (101047, 0x14), (101063, 0x18), (101067, 0x1c),
        (101287, 0x4c), (101311, 0x64), (101315, 0x70),
        (101507, 0x20), (101523, 0x30),
        (101615, 0xd0), (101803, 0xd0),
        (102259, 0x54), (102283, 0xac), (102299, 0xac), (102311, 0xac), (102327, 0xac),
        (102343, 0xac), (102359, 0xac), (102375, 0xac), (102391, 0xac),
        (106287, 0x28), (114255, 0x4c), (116755, 0x6c), (116767, 0x84),
        (120895, 0x50), (128803, 0xd4), (129483, 0xbc),
        (136707, 0xfc), (137175, 0xfc),
        (140083, 0x94), (140535, 0xbc), (140846, 0x0c), (140847, 0x00),
        (140855, 0x44), (140859, 0xe4),
        (144215, 0xec), (144735, 0x14),
        (144951, 0xa4), (145071, 0xa8), (145235, 0xb0), (145315, 0xb4), (145395, 0xa4),
        (145563, 0xd8), (145687, 0xe0), (145847, 0xb0), (145955, 0x88),
        (146127, 0xd8), (146251, 0xe0), (146375, 0xf4), (146647, 0xf8),
        (146795, 0x2c), (146922, 0x0f), (146923, 0x04), (147151, 0x08),
        (153443, 0x2c), (153719, 0x4c),
        (178711, 0x44), (178715, 0xd0), (178719, 0xd0), (178723, 0xd0), (178727, 0xd0),
        (178731, 0xd0), (178735, 0xd0), (178739, 0xd0), (178743, 0xd0), (178747, 0xd0)
    ];
}
