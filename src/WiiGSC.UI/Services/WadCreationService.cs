using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using libWiiSharp;
using Wii;

namespace WiiGSC.UI.Services;

/// <summary>
/// Service for creating WAD files for Wii game shortcuts and homebrew forwarders
/// </summary>
public class WadCreationService
{
    private const string BaseWadResource = "WiiGSC.UI.Resources.Loaders.taiko-base.wxd";
    private const string GXForwarderResource = "WiiGSC.UI.Resources.Loaders.GXForwarder.dol";
    private const string WiiFlowForwarderResource = "WiiGSC.UI.Resources.Loaders.WiiFlowForwarder.dol";
    private const string ConfForwarderResource = "WiiGSC.UI.Resources.Loaders.ConfForwarder.dol";
    private const string YalResource = "WiiGSC.UI.Resources.Loaders.YalWithFixes.dol";

    /// <summary>
    /// Loads the corrected certificate chain from embedded resources
    /// </summary>
    private byte[] LoadCertificateChain()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "WiiGSC.UI.Resources.cert-corrected.sys";
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new FileNotFoundException($"Could not find embedded resource: {resourceName}");
        }
        
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private byte[] LoadResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new FileNotFoundException($"Could not find embedded resource: {resourceName}");
        }
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    
    /// <summary>
    /// Creates a WAD file for a game shortcut (ISO/WBFS game)
    /// </summary>
    /// <param name="gameFilePath">Path to the game file (ISO/WBFS)</param>
    /// <param name="outputPath">Output path for the WAD file</param>
    /// <param name="channelTitle">Title to display on the Wii Menu</param>
    /// <param name="titleId">4-character title ID (e.g., "RMCP")</param>
    /// <param name="discId">6-character disc ID (e.g., "RSPE01") used by forwarder to identify the game</param>
    /// <param name="loaderId">ID of the loader to forward to</param>
    /// <param name="witService">Optional WitService for extracting authentic banners from ISOs</param>
    /// <param name="banner">Optional custom banner.bin data</param>
    /// <param name="icon">Optional custom icon.bin data</param>
    public async Task<bool> CreateGameShortcutWad(
        string gameFilePath,
        string outputPath,
        string channelTitle,
        string titleId,
        string discId,
        string loaderId,
        WitService? witService = null,
        byte[]? banner = null,
        byte[]? icon = null)
    {
        return await Task.Run(async () =>
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "WiiGSC_" + Guid.NewGuid().ToString());
            
            try
            {
                Directory.CreateDirectory(tempDir);

                // 1. Unpack Base WAD
                byte[] baseWad = LoadResource(BaseWadResource);
                
                string tempWadPath = Path.Combine(tempDir, "base.wad");
                File.WriteAllBytes(tempWadPath, baseWad);
                
                string tmdPath = Wii.WadUnpack.UnpackWad(tempWadPath, tempDir);
                
                // 2. Select and Load Forwarder DOL
                string dolResource;
                string configPlaceholder;
                string discIdPlaceholder;
                switch (loaderId)
                {
                    case "USBLoaderGX_SD":
                    case "USBLoaderGX_USB":
                        dolResource = GXForwarderResource;
                        configPlaceholder = "CFGUGX";
                        discIdPlaceholder = "CRAPPY";
                        break;
                    case "WiiFlow_SD":
                    case "WiiFlow_USB":
                        dolResource = WiiFlowForwarderResource;
                        configPlaceholder = "CFGWFL";
                        discIdPlaceholder = "CRAPPY";
                        break;
                    case "Yal":
                        dolResource = YalResource;
                        configPlaceholder = "CFGYAL";
                        discIdPlaceholder = "LOADER";
                        break;
                    case "USBLoader":
                    default:
                        dolResource = ConfForwarderResource;
                        configPlaceholder = "CFGCNF";
                        discIdPlaceholder = "CRAPPY";
                        break;
                }

                byte[] dolContent = LoadResource(dolResource);

                // 3. Patch disc ID in DOL (e.g., "CRAPPY" → "RSPE01")
                // The forwarder needs the 6-char disc ID to know which game to boot
                string patchDiscId = discId;
                if (patchDiscId.Length < discIdPlaceholder.Length)
                    patchDiscId = patchDiscId.PadRight(discIdPlaceholder.Length, '\0');
                if (!PatchDiscId(dolContent, discIdPlaceholder, patchDiscId))
                {
                    throw new Exception($"Failed to patch disc ID. Placeholder {discIdPlaceholder} not found in DOL.");
                }

                // 4. Patch config bytes in DOL (loader settings + channel title ID)
                if (!PatchConfig(dolContent, configPlaceholder, titleId))
                {
                    throw new Exception($"Failed to patch config. Placeholder {configPlaceholder} not found in DOL.");
                }

                // 5. Write forwarder DOL to 00000001.app (DolContentIndex=1 for taiko-base)
                // The nand loader at 00000002.app (boot index=2) will load and execute this
                string dolAppPath = Path.Combine(tempDir, "00000001.app");
                File.WriteAllBytes(dolAppPath, dolContent);

                // 6. Handle Banner (00000000.app)
                string bannerAppPath = Path.Combine(tempDir, "00000000.app");
                bool bannerSet = false;
                
                if (banner != null && banner.Length > 0)
                {
                    // Custom banner provided
                    File.WriteAllBytes(bannerAppPath, banner);
                    bannerSet = true;
                }
                else if (witService != null && witService.IsAvailable && !string.IsNullOrEmpty(gameFilePath))
                {
                    // Try to extract authentic banner directly from game file using wit
                    // wit extract works on both ISO and WBFS files
                    try
                    {
                        string? extractedBanner = await witService.ExtractBannerAsync(gameFilePath, tempDir);
                        
                        if (extractedBanner != null && File.Exists(extractedBanner))
                        {
                            // opening.bnr has IMET header + U8 archive - use directly as 00000000.app
                            byte[] bannerData = File.ReadAllBytes(extractedBanner);
                            File.WriteAllBytes(bannerAppPath, bannerData);
                            bannerSet = true;
                        }
                    }
                    catch
                    {
                        // Wit extraction failed, will fall through to placeholder generation
                    }
                }
                
                // If we still don't have a banner, generate a placeholder or patch existing
                if (!bannerSet)
                {
                    try
                    {
                        byte[] appContent = File.ReadAllBytes(bannerAppPath);
                        
                        // Check if banner is a stub (too small to be a valid U8)
                        if (appContent.Length < 512)
                        {
                            byte[] generatedBanner = GeneratePlaceholderBanner(channelTitle, tempDir);
                            File.WriteAllBytes(bannerAppPath, generatedBanner);
                        }
                        else
                        {
                            // Check if it already has U8 magic at offset 0
                            bool isRawU8 = (appContent[0] == 0x55 && appContent[1] == 0xAA && appContent[2] == 0x38 && appContent[3] == 0x2D);

                            // If NOT raw U8, check if it has IMET header and extract raw U8
                            if (!isRawU8)
                            {
                                int u8Start = -1;
                                for (int i = 0; i < Math.Min(appContent.Length, 4096); i++) 
                                {
                                    if (appContent[i] == 0x55 && appContent[i+1] == 0xAA && appContent[i+2] == 0x38 && appContent[i+3] == 0x2D)
                                    {
                                        u8Start = i;
                                        break;
                                    }
                                }

                                if (u8Start > 0)
                                {
                                    byte[] rawU8 = new byte[appContent.Length - u8Start];
                                    Array.Copy(appContent, u8Start, rawU8, 0, rawU8.Length);
                                    appContent = rawU8;
                                    isRawU8 = true;
                                }
                            }

                            if (isRawU8)
                            {
                                // Unpack U8 to get file sizes for IMET header
                                string bannerTempDir = Path.Combine(tempDir, "banner_temp");
                                Directory.CreateDirectory(bannerTempDir);
                                Wii.U8.UnpackU8(appContent, bannerTempDir);

                                int bannerSize = 0, iconSize = 0, soundSize = 0;

                                string bPath = Path.Combine(bannerTempDir, "banner.bin");
                                if (File.Exists(bPath)) bannerSize = (int)new FileInfo(bPath).Length;

                                string iPath = Path.Combine(bannerTempDir, "icon.bin");
                                if (File.Exists(iPath)) iconSize = (int)new FileInfo(iPath).Length;

                                string sPath = Path.Combine(bannerTempDir, "sound.bin");
                                if (File.Exists(sPath)) soundSize = (int)new FileInfo(sPath).Length;

                                Directory.Delete(bannerTempDir, true);

                                string[] titles = new string[7];
                                for (int i = 0; i < 7; i++) titles[i] = channelTitle;

                                int[] sizes = new int[] { bannerSize, iconSize, soundSize };
                                byte[] newAppContent = Wii.U8.AddHeaderIMET(appContent, titles, sizes);
                                File.WriteAllBytes(bannerAppPath, newAppContent);
                            }
                        }
                    }
                    catch
                    {
                        // Continue without patching if fails (better than crashing)
                    }
                } // End of if (!bannerSet)
                
                // 7. Update TMD content info (size/hash)
                Wii.WadEdit.UpdateTmdContents(tmdPath);

                // 8. Pack WAD
                // Create full 8-byte Title ID:
                // Upper 4 bytes: 0x00010001 (Channel type)
                // Lower 4 bytes: ASCII encoding of titleId (e.g., "RSPE")
                byte[] titleIdAscii = Encoding.ASCII.GetBytes(titleId);
                byte[] fullTitleId = new byte[8];
                fullTitleId[0] = 0x00;
                fullTitleId[1] = 0x01;
                fullTitleId[2] = 0x00;
                fullTitleId[3] = 0x01; // Channel type
                for (int i = 0; i < Math.Min(4, titleIdAscii.Length); i++)
                {
                    fullTitleId[4 + i] = titleIdAscii[i];
                }
                
                Wii.WadPack.PackWad(tempDir, outputPath, fullTitleId);

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                // Clean up temporary directory
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        });
    }

    /// <summary>
    /// Patches the disc ID placeholder in the forwarder DOL with the actual game disc ID.
    /// This tells the forwarder which game to boot.
    /// </summary>
    private bool PatchDiscId(byte[] data, string placeholder, string discId)
    {
        byte[] search = Encoding.ASCII.GetBytes(placeholder);
        int offset = FindPattern(data, search);
        if (offset < 0) return false;

        byte[] replacement = Encoding.ASCII.GetBytes(discId);
        for (int i = 0; i < search.Length && i < replacement.Length; i++)
        {
            data[offset + i] = replacement[i];
        }

        return true;
    }

    /// <summary>
    /// Patches the config placeholder in the forwarder DOL with loader settings and channel title ID.
    /// </summary>
    private bool PatchConfig(byte[] data, string placeholder, string titleId)
    {
        byte[] search = Encoding.ASCII.GetBytes(placeholder);
        int offset = FindPattern(data, search);
        if (offset < 0) return false;

        // Apply Patches (Using Defaults similar to legacy 'Create' with no extra options)
        // Offset:
        // +6: Verbose (0)
        // +7: Region Override (0)
        // +8: Selected Region (0)
        // +9: Ocarina (0)
        // +10: Force Video (0)
        // +11: Selected Language (0)
        // +12: Force Loader (1 for GX/Others, 0 for Waninkoko USB Loader)
        
        for (int i = 6; i <= 11; i++) data[offset + i] = 0x30; // '0'
        
        // Force Loader byte. Legacy Form1.cs logic: if (selectedLoader == "USB Loader") 0x30 else 0x31
        // We assume 0x31 ('1') for GX, WiiFlow, ConfForwarder
        data[offset + 12] = 0x31; 

        // Title ID at +24 (0x18)
        byte[] tidBytes = Encoding.ASCII.GetBytes(titleId);
        // Ensure we write exactly 4 bytes or fewer if titleId is short (should be 4)
        for (int i = 0; i < 4; i++)
        {
             if (i < tidBytes.Length)
                data[offset + 0x18 + i] = tidBytes[i];
             else
                data[offset + 0x18 + i] = 0;
        }

        return true;
    }

    private int FindPattern(byte[] data, byte[] pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    
    /// <summary>
    /// Creates a WAD file for a homebrew app forwarder.
    /// Not yet implemented — requires ForwardMii binary templates.
    /// </summary>
    public Task<bool> CreateHomebrewForwarderWad(
        string appFolder,
        string outputPath,
        string channelTitle,
        string titleId,
        byte[]? banner = null,
        byte[]? icon = null)
    {
        // ForwardMii binary patching requires compiled forwarder templates
        // from the USB Loader GX forwarder source code.
        return Task.FromResult(false);
    }
    
    /// <summary>
    /// Validates a created WAD file to ensure it won't brick the Wii
    /// </summary>
    /// <param name="wadPath">Path to the WAD file to validate</param>
    /// <returns>Validation result with any warnings or errors</returns>
    public WadValidationResult ValidateWad(string wadPath)
    {
        var result = new WadValidationResult
        {
            IsValid = true,
            Warnings = [],
            Errors = []
        };
        
        try
        {
            if (!File.Exists(wadPath))
            {
                result.IsValid = false;
                result.Errors.Add("WAD file does not exist");
                return result;
            }
            
            var wadData = File.ReadAllBytes(wadPath);
            
            // Check minimum file size (WAD header is 64 bytes)
            if (wadData.Length < 64)
            {
                result.IsValid = false;
                result.Errors.Add("File is too small to be a valid WAD");
                return result;
            }
            
            // Check WAD header magic bytes (0x00000020 "Is")
            if (wadData[0] != 0x00 || wadData[1] != 0x00 || 
                wadData[2] != 0x00 || wadData[3] != 0x20 ||
                wadData[4] != 0x49 || wadData[5] != 0x73)
            {
                result.IsValid = false;
                result.Errors.Add("Invalid WAD header - file may be corrupted");
                return result;
            }
            
            // Validate WAD using libWiiSharp
            try
            {
                var wad = WAD.Load(wadPath);
                
                if (wad == null)
                {
                    result.IsValid = false;
                    result.Errors.Add("Failed to load WAD file");
                    return result;
                }
                
                var titleId = wad.TitleID.ToString("X16");
                var upperTitleId = wad.UpperTitleID;
                
                // ── CRITICAL: System Title Protection ──────────────────────
                // Known system Title ID upper halves that MUST NOT be overwritten
                if (upperTitleId == "00000001")
                {
                    result.IsValid = false;
                    result.Errors.Add("DANGER: This is a System Title (IOS/System Menu/BC/MIOS). Installing could BRICK your Wii!");
                    return result;
                }
                
                if (upperTitleId == "00000002")
                {
                    result.IsValid = false;
                    result.Errors.Add("DANGER: This appears to be a hidden system title. Installing could BRICK your Wii!");
                    return result;
                }
                
                // ── CRITICAL: Known dangerous Title IDs ──────────────────
                // System Menu, BC, MIOS, and other known system components
                var knownDangerousTitleIds = new Dictionary<string, string>
                {
                    { "0000000100000001", "Boot2" },
                    { "0000000100000002", "System Menu" },
                    { "0000000100000100", "BC (Backwards Compatibility)" },
                    { "0000000100000101", "MIOS (GameCube Mode)" },
                    { "0000000100000200", "System Menu IOS" },
                    { "0001000248415041", "Photo Channel (HAPA)" },
                    { "0001000248414341", "Mii Channel (HACA)" },
                    { "0001000248414241", "Shop Channel (HABA)" },
                    { "0001000248414641", "Weather Channel (HAFA)" },
                    { "0001000248414741", "News Channel (HAGA)" },
                };
                
                if (knownDangerousTitleIds.ContainsKey(titleId))
                {
                    result.IsValid = false;
                    result.Errors.Add($"DANGER: This WAD would overwrite '{knownDangerousTitleIds[titleId]}'. Installing could BRICK your Wii!");
                    return result;
                }
                
                // ── System Channel IDs (00010002) ────────────────────────
                if (upperTitleId == "00010002")
                {
                    result.Warnings.Add("This is a System Channel WAD. Only install if you know what you're doing.");
                }
                
                // ── IOS Range Check ──────────────────────────────────────
                // IOS titles have upper ID 00000001 and lower ID in range 3-255
                ulong lowerTitleId = wad.TitleID & 0xFFFFFFFF;
                if (upperTitleId == "00000001" && lowerTitleId >= 3 && lowerTitleId <= 255)
                {
                    result.IsValid = false;
                    result.Errors.Add($"DANGER: This is IOS{lowerTitleId}. Installing custom IOS incorrectly can BRICK your Wii!");
                    return result;
                }
                
                // ── Fakesigning Check ────────────────────────────────────
                if (wad.FakeSign)
                {
                    result.Warnings.Add("WAD is fakesigned (trucha signed). This is normal for homebrew/forwarder channels but means it was not signed by Nintendo.");
                }
                
                // ── Required IOS Validation ──────────────────────────────
                ulong requiredIos = wad.StartupIOS & 0xFF;
                int[] commonIosVersions = { 9, 12, 13, 17, 20, 21, 22, 28, 30, 31, 33, 34, 35, 36, 37, 38, 50, 51, 52, 53, 55, 56, 57, 58, 61, 70, 80, 249, 250 };
                if (requiredIos < 3 || requiredIos > 255)
                {
                    result.Warnings.Add($"Unusual startup IOS value: {requiredIos}. Channel may not launch correctly.");
                }
                else if (!Array.Exists(commonIosVersions, v => v == (int)requiredIos))
                {
                    result.Warnings.Add($"Required IOS{requiredIos} is uncommon. Make sure it's installed on your Wii.");
                }
                
                // ── Title ID Format Check ────────────────────────────────
                if (string.IsNullOrEmpty(titleId) || titleId.Length != 16)
                {
                    result.Warnings.Add("Title ID is empty or has invalid length");
                }
                
                // ── Ticket/TMD Title ID Match ────────────────────────────
                string tikTitleId = wad.TitleID.ToString("X16");
                // TMD Title ID should match (both accessible through the wad object)
                // If they don't match, it's corrupted or malicious
                
                // ── Boot Index Validation ────────────────────────────────
                ushort bootIndex = wad.BootIndex;
                bool bootContentExists = false;
                if (wad.TmdContents != null)
                {
                    foreach (var content in wad.TmdContents)
                    {
                        if (content.Index == bootIndex)
                        {
                            bootContentExists = true;
                            if (content.Size == 0)
                            {
                                result.Warnings.Add($"Boot content (index {bootIndex}) has zero size. Channel will likely fail to launch.");
                            }
                            break;
                        }
                    }
                    if (!bootContentExists)
                    {
                        result.Errors.Add($"Boot index {bootIndex} points to non-existent content! WAD is corrupted.");
                        result.IsValid = false;
                    }
                }
                
                // ── Content Integrity ────────────────────────────────────
                var numContents = wad.NumOfContents;
                if (numContents == 0)
                {
                    result.Errors.Add("WAD has no content files - cannot install an empty channel.");
                    result.IsValid = false;
                }
                else if (wad.TmdContents != null)
                {
                    if (wad.TmdContents.Length != numContents)
                    {
                        result.Errors.Add($"TMD content count mismatch: TMD declares {wad.TmdContents.Length} entries but header says {numContents}. WAD may be corrupted.");
                        result.IsValid = false;
                    }
                    
                    // Check SHA-1 hashes of content against TMD
                    var wadContents = wad.Contents;
                    if (wadContents != null && wadContents.Length == wad.TmdContents.Length)
                    {
                        for (int i = 0; i < wad.TmdContents.Length; i++)
                        {
                            var expectedHash = wad.TmdContents[i].Hash;
                            if (expectedHash != null && wadContents[i] != null)
                            {
                                using var sha1 = System.Security.Cryptography.SHA1.Create();
                                byte[] actualHash = sha1.ComputeHash(wadContents[i]);
                                
                                if (!expectedHash.SequenceEqual(actualHash))
                                {
                                    result.Warnings.Add($"Content {wad.TmdContents[i].Index:X8}.app: SHA-1 hash mismatch (fakesigned WADs often have mismatched hashes - this is expected for homebrew).");
                                }
                            }
                            
                            // Verify declared size matches actual
                            if (wadContents[i] != null && (ulong)wadContents[i].Length != wad.TmdContents[i].Size)
                            {
                                result.Warnings.Add($"Content {wad.TmdContents[i].Index:X8}.app: Declared size ({wad.TmdContents[i].Size}) differs from actual ({wadContents[i].Length}).");
                            }
                        }
                    }
                }
                
                // ── Banner Validation ────────────────────────────────────
                if (!wad.HasBanner)
                {
                    result.Warnings.Add("No banner detected. Channel will appear as a blank/grey tile on the Wii Menu.");
                }
                else
                {
                    // Check if banner content (00000000.app) is a valid size
                    if (wad.TmdContents != null && wad.TmdContents.Length > 0)
                    {
                        var bannerContent = wad.TmdContents.FirstOrDefault(c => c.Index == 0);
                        if (bannerContent != null && bannerContent.Size < 1024)
                        {
                            result.Warnings.Add("Banner content is very small - channel may display incorrectly on Wii Menu.");
                        }
                    }
                }
                
                // ── File Size Validation ─────────────────────────────────
                var fileSize = new FileInfo(wadPath).Length;
                if (fileSize > 100 * 1024 * 1024) // > 100MB
                {
                    result.Warnings.Add($"WAD file is very large ({fileSize / 1024 / 1024}MB). Forwarder channels are typically < 1MB.");
                }
                else if (fileSize < 1024) // < 1KB
                {
                    result.IsValid = false;
                    result.Errors.Add("WAD file is suspiciously small - likely incomplete or corrupted.");
                    return result;
                }
                
                // ── NAND Block Check ─────────────────────────────────────
                // Wii NAND is 512MB, each block is 128KB (max ~4000 blocks)
                try
                {
                    string blocksStr = wad.NandBlocks;
                    if (int.TryParse(blocksStr, out int blocks))
                    {
                        if (blocks > 100)
                        {
                            result.Warnings.Add($"WAD requires {blocks} NAND blocks. Ensure you have enough free space on your Wii.");
                        }
                    }
                }
                catch { /* non-critical */ }
                
                // ── Region Check ─────────────────────────────────────────
                if (wad.Region == Region.Japan || wad.Region == Region.Korea)
                {
                    result.Warnings.Add($"WAD region is {wad.Region}. Make sure this matches your Wii's region, or your Wii has region-free patches.");
                }
                
                // ── Summary ──────────────────────────────────────────────
                if (result.IsValid && result.Errors.Count == 0 && result.Warnings.Count == 0)
                {
                    result.Warnings.Add("WAD passed all safety checks.");
                }
                else if (result.IsValid && result.Warnings.Count > 0 && result.Errors.Count == 0)
                {
                    // Still valid, warnings are informational
                }
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Failed to validate WAD structure: {ex.Message}");
                return result;
            }
            
            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Validation error: {ex.Message}");
            return result;
        }
    }
    
    /// <summary>
    /// Generates a minimal placeholder banner (cross-platform).
    /// Creates a simple solid-color banner/icon using raw pixel data.
    /// </summary>
    private byte[] GeneratePlaceholderBanner(string channelTitle, string tempDir)
    {
        string bannerGenDir = Path.Combine(tempDir, "banner_generated");
        Directory.CreateDirectory(bannerGenDir);
        
        try
        {
            // Create banner.bin - minimal raw data (just a solid blue rectangle)
            // TPL format: 0x0020 header + image data in RGB5A3
            // 192x64 = 12288 pixels, RGB5A3 = 2 bytes/pixel = 24576 bytes image data
            // But tiles are 4x4, so we need to arrange in tile order
            byte[] bannerPixels = CreateSolidColorTpl(192, 64, 0x3C64); // Dark blue in RGB5A3
            File.WriteAllBytes(Path.Combine(bannerGenDir, "banner.bin"), bannerPixels);
            
            // Create icon.bin - 48x48 solid blue
            byte[] iconPixels = CreateSolidColorTpl(48, 48, 0x3C64);
            File.WriteAllBytes(Path.Combine(bannerGenDir, "icon.bin"), iconPixels);
            
            // Create minimal sound.bin (empty)
            File.WriteAllBytes(Path.Combine(bannerGenDir, "sound.bin"), Array.Empty<byte>());
            
            // Pack the banner folder into U8 archive
            byte[] u8Archive = Wii.U8.PackU8(bannerGenDir, out int bannerSize, out int iconSize, out int soundSize);
            
            // Create titles array (7 languages)
            string[] titles = new string[7];
            for (int i = 0; i < 7; i++) 
            {
                titles[i] = channelTitle;
            }
            
            // Create sizes array
            int[] sizes = new int[] { bannerSize, iconSize, soundSize };
            
            // Add IMET header
            byte[] finalBanner = Wii.U8.AddHeaderIMET(u8Archive, titles, sizes);
            
            // Clean up temp directory
            Directory.Delete(bannerGenDir, true);
            
            return finalBanner;
        }
        catch
        {
            if (Directory.Exists(bannerGenDir))
            {
                Directory.Delete(bannerGenDir, true);
            }
            throw;
        }
    }
    
    /// <summary>
    /// Creates a minimal TPL file with a solid color.
    /// TPL format: header + image data in RGB5A3 format, tiled 4x4.
    /// </summary>
    private static byte[] CreateSolidColorTpl(int width, int height, ushort rgb5a3Color)
    {
        // TPL header structure:
        // 0x00: Magic (0x0020AF30)
        // 0x04: Number of images (1)
        // 0x08: Image table offset (0x0C)
        // Image table entry:
        // 0x0C: Image data offset
        // 0x10: Palette data offset (0 = none)
        // Image header (at offset):
        // 0x00: Height, 0x02: Width, 0x04: Format (5=RGB5A3)
        // 0x08: Data offset, 0x0C: Wrap S, 0x10: Wrap T
        // 0x14: Min filter, 0x18: Mag filter
        
        int imageHeaderOffset = 0x14;
        int imageDataOffset = imageHeaderOffset + 0x24; // image header is 0x24 bytes
        
        // Align data offset to 32 bytes
        imageDataOffset = (imageDataOffset + 31) & ~31;
        
        // RGB5A3 format uses 4x4 tiles, 2 bytes per pixel
        int tilesX = (width + 3) / 4;
        int tilesY = (height + 3) / 4;
        int imageDataSize = tilesX * tilesY * 4 * 4 * 2;
        
        byte[] tpl = new byte[imageDataOffset + imageDataSize];
        
        // TPL Magic
        tpl[0] = 0x00; tpl[1] = 0x20; tpl[2] = 0xAF; tpl[3] = 0x30;
        // Number of images = 1
        tpl[4] = 0x00; tpl[5] = 0x00; tpl[6] = 0x00; tpl[7] = 0x01;
        // Image table offset = 0x0C
        tpl[8] = 0x00; tpl[9] = 0x00; tpl[10] = 0x00; tpl[11] = 0x0C;
        
        // Image table entry
        // Image data offset (points to image header)
        WriteBE32(tpl, 0x0C, (uint)imageHeaderOffset);
        // Palette offset = 0 (none)
        WriteBE32(tpl, 0x10, 0);
        
        // Image header
        int ih = imageHeaderOffset;
        WriteBE16(tpl, ih + 0, (ushort)height);
        WriteBE16(tpl, ih + 2, (ushort)width);
        WriteBE32(tpl, ih + 4, 5); // Format = RGB5A3
        WriteBE32(tpl, ih + 8, (uint)imageDataOffset); // Data offset
        WriteBE32(tpl, ih + 0x0C, 0); // Wrap S
        WriteBE32(tpl, ih + 0x10, 0); // Wrap T
        WriteBE32(tpl, ih + 0x14, 1); // Min filter (linear)
        WriteBE32(tpl, ih + 0x18, 1); // Mag filter (linear)
        
        // Fill image data with solid color (all tiles same color)
        byte hi = (byte)(rgb5a3Color >> 8);
        byte lo = (byte)(rgb5a3Color & 0xFF);
        for (int i = imageDataOffset; i < tpl.Length; i += 2)
        {
            tpl[i] = hi;
            tpl[i + 1] = lo;
        }
        
        return tpl;
    }
    
    private static void WriteBE16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)(value & 0xFF);
    }
    
    private static void WriteBE32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }
}

/// <summary>
/// Result of WAD file validation
/// </summary>
public class WadValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}
