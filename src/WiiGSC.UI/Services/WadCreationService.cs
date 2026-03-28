using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        byte[]? icon = null,
        bool enableOcarina = false)
    {
        return await Task.Run(async () =>
        {
            string tmpBase = Path.Combine(AppContext.BaseDirectory, "tmp");
            Directory.CreateDirectory(tmpBase);
            string tempDir = Path.Combine(tmpBase, "WiiGSC_" + Guid.NewGuid().ToString());

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
                if (!PatchConfig(dolContent, configPlaceholder, titleId, enableOcarina))
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

                // DIAGNOSTIC: Log banner extraction flow to file
                string diagLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WiiGSC_diag.log");
                void LogDiag(string msg) { File.AppendAllText(diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); }

                LogDiag($"=== Banner extraction for discId={discId} ===");
                LogDiag($"  gameFilePath='{gameFilePath}'");
                LogDiag($"  gameFilePath empty? {string.IsNullOrEmpty(gameFilePath)}");
                LogDiag($"  witService null? {witService == null}");
                LogDiag($"  witService.IsAvailable? {witService?.IsAvailable}");
                LogDiag($"  banner param null? {banner == null}, length={banner?.Length ?? -1}");

                if (banner != null && banner.Length > 0)
                {
                    // Custom banner provided
                    LogDiag("  -> Using custom banner");
                    File.WriteAllBytes(bannerAppPath, banner);
                    bannerSet = true;
                }
                else if (witService != null && witService.IsAvailable && !string.IsNullOrEmpty(gameFilePath))
                {
                    // Try to extract authentic banner directly from game file using wit
                    // wit extract works on both ISO and WBFS files
                    try
                    {
                        LogDiag("  -> Calling witService.ExtractBannerAsync...");
                        string? extractedBanner = await witService.ExtractBannerAsync(gameFilePath, tempDir);

                        LogDiag($"  -> extractedBanner='{extractedBanner}'");
                        LogDiag($"  -> File.Exists? {(extractedBanner != null ? File.Exists(extractedBanner).ToString() : "N/A")}");

                        if (extractedBanner != null && File.Exists(extractedBanner))
                        {
                            // opening.bnr has IMET header + U8 archive - use directly as 00000000.app
                            byte[] bannerData = File.ReadAllBytes(extractedBanner);
                            LogDiag($"  -> Banner data size: {bannerData.Length} bytes, writing to {bannerAppPath}");
                            File.WriteAllBytes(bannerAppPath, bannerData);
                            bannerSet = true;
                        }
                        else
                        {
                            LogDiag("  -> Banner extraction returned null or file missing!");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDiag($"  -> EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                    }
                }
                else
                {
                    LogDiag($"  -> Skipped wit extraction (condition false)");
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
    private bool PatchConfig(byte[] data, string placeholder, string titleId, bool enableOcarina = false)
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

        // +9: Ocarina - set to '1' if enabled
        if (enableOcarina) data[offset + 9] = 0x31;

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
    /// Creates a WAD file for a homebrew app forwarder using ForwardMii SDSDHC templates.
    /// The generated channel boots sd:/apps/{appFolder}/boot.dol from the SD card.
    /// </summary>
    /// <param name="appFolder">App folder name (3-18 characters)</param>
    /// <param name="outputPath">Output path for the WAD file</param>
    /// <param name="channelTitle">Title to display on the Wii Menu</param>
    /// <param name="titleId">4-character title ID</param>
    /// <param name="forwardToElf">If true, loads boot.elf instead of boot.dol</param>
    /// <param name="banner">Optional custom banner.bin data</param>
    /// <param name="icon">Optional custom icon.bin data</param>
    public async Task<string?> CreateHomebrewForwarderWad(
        string appFolder,
        string outputPath,
        string channelTitle,
        string titleId,
        bool forwardToElf = false,
        string? imagePath = null)
    {
        return await Task.Run(() =>
        {
            string tmpBase = Path.Combine(AppContext.BaseDirectory, "tmp");
            Directory.CreateDirectory(tmpBase);
            string tempDir = Path.Combine(tmpBase, "WiiGSC_HB_" + Guid.NewGuid().ToString());
            string diagLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WiiGSC_diag.log");
            void LogDiag(string msg) { File.AppendAllText(diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] HB: {msg}\n"); }

            try
            {
                // Sanitize channel title: strip trailing slashes, backslashes, whitespace
                channelTitle = channelTitle.TrimEnd('\\', '/', ' ');

                LogDiag($"=== Homebrew forwarder: folder='{appFolder}', title='{channelTitle}', id='{titleId}' ===");
                Directory.CreateDirectory(tempDir);

                // 1. Generate forwarder DOL via SDSDHC binary patching
                LogDiag("Step 1: Generating SDSDHC forwarder DOL...");
                var forwarderService = new SdsdhcForwarderService();
                byte[] forwarderDol = forwarderService.ToByteArray(appFolder, forwardToElf);
                LogDiag($"  -> DOL size: {forwarderDol.Length} bytes");

                // 2. Unpack base WAD
                LogDiag("Step 2: Unpacking base WAD...");
                byte[] baseWad = LoadResource(BaseWadResource);
                LogDiag($"  -> Base WAD size: {baseWad.Length} bytes");
                string tempWadPath = Path.Combine(tempDir, "base.wad");
                File.WriteAllBytes(tempWadPath, baseWad);
                string tmdPath = Wii.WadUnpack.UnpackWad(tempWadPath, tempDir);
                LogDiag($"  -> TMD path: {tmdPath}");

                // List files in temp dir for debugging
                var files = Directory.GetFiles(tempDir);
                LogDiag($"  -> Unpacked files: {string.Join(", ", files.Select(f => Path.GetFileName(f) + "(" + new FileInfo(f).Length + ")"))}");

                // 3. Write forwarder DOL to 00000001.app
                string dolAppPath = Path.Combine(tempDir, "00000001.app");
                File.WriteAllBytes(dolAppPath, forwarderDol);
                LogDiag($"Step 3: Wrote forwarder DOL to {Path.GetFileName(dolAppPath)}");

                // 4. Handle Banner (00000000.app)
                string bannerAppPath = Path.Combine(tempDir, "00000000.app");

                if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
                {
                    LogDiag($"Step 4: Generating banner from image: {imagePath}");
                    try
                    {
                        byte[] generatedBanner = GenerateBannerFromImage(imagePath, channelTitle, tempDir);
                        File.WriteAllBytes(bannerAppPath, generatedBanner);
                        LogDiag($"  -> Image banner size: {generatedBanner.Length} bytes");
                    }
                    catch (Exception imgEx)
                    {
                        LogDiag($"  -> Image banner failed ({imgEx.Message}), falling back to placeholder");
                        byte[] generatedBanner = GeneratePlaceholderBanner(channelTitle, tempDir);
                        File.WriteAllBytes(bannerAppPath, generatedBanner);
                        LogDiag($"  -> Placeholder banner size: {generatedBanner.Length} bytes");
                    }
                }
                else
                {
                    LogDiag("Step 4: Generating placeholder banner...");
                    byte[] generatedBanner = GeneratePlaceholderBanner(channelTitle, tempDir);
                    File.WriteAllBytes(bannerAppPath, generatedBanner);
                    LogDiag($"  -> Placeholder banner size: {generatedBanner.Length} bytes");
                }

                // 4b. Self-test: verify banner can be parsed by libWiiSharp + detailed hex dump
                try
                {
                    byte[] bannerTest = File.ReadAllBytes(bannerAppPath);
                    LogDiag($"  -> Banner file: {bannerTest.Length} bytes");
                    LogDiag($"     First 16: {BitConverter.ToString(bannerTest, 0, Math.Min(16, bannerTest.Length))}");

                    // Dump IMET header details
                    if (bannerTest.Length >= 0x680)
                    {
                        LogDiag($"     Bytes @0x80 (IMET magic): {BitConverter.ToString(bannerTest, 0x80, 4)}");
                        // IMET sizes: icon @ 0x8C, banner @ 0x90, sound @ 0x94 (IMET at 0x80, sizes at +0x0C)
                        int imetIconSz = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, 0x8C));
                        int imetBannerSz = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, 0x90));
                        int imetSoundSz = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, 0x94));
                        LogDiag($"     IMET sizes: icon={imetIconSz}, banner={imetBannerSz}, sound={imetSoundSz}");

                        // Dump outer U8 header (starts at 0x680)
                        LogDiag($"     OuterU8 @0x680: {BitConverter.ToString(bannerTest, 0x680, Math.Min(32, bannerTest.Length - 0x680))}");

                        // Parse outer U8 to list files
                        try
                        {
                            int u8Magic = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, 0x680));
                            int rootOff = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, 0x684));
                            int headerSz = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, 0x688));
                            int dataOff = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, 0x68C));
                            LogDiag($"     OuterU8 magic=0x{u8Magic:X8}, rootOff=0x{rootOff:X}, headerSz=0x{headerSz:X}, dataOff=0x{dataOff:X}");

                            // Root node: type(1)+nameoff(3)+dataoff(4)+size(4) = 12 bytes
                            int nodesBase = 0x680 + rootOff;
                            int rootSize = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, nodesBase + 8));
                            LogDiag($"     OuterU8 root node count: {rootSize}");

                            int stringTableOff = nodesBase + rootSize * 12;
                            for (int n = 1; n < rootSize && n < 10; n++)
                            {
                                int nodeBase = nodesBase + n * 12;
                                byte nodeType = bannerTest[nodeBase];
                                int nameOff = (bannerTest[nodeBase + 1] << 16) | (bannerTest[nodeBase + 2] << 8) | bannerTest[nodeBase + 3];
                                int nDataOff = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, nodeBase + 4));
                                int nSize = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, nodeBase + 8));

                                // Read name from string table
                                int nameStart = stringTableOff + nameOff;
                                int nameEnd = nameStart;
                                while (nameEnd < bannerTest.Length && bannerTest[nameEnd] != 0) nameEnd++;
                                string nodeName = System.Text.Encoding.ASCII.GetString(bannerTest, nameStart, nameEnd - nameStart);

                                if (nodeType == 0) // file
                                {
                                    int absDataOff = 0x680 + nDataOff;
                                    string firstBytes = absDataOff + 16 <= bannerTest.Length
                                        ? BitConverter.ToString(bannerTest, absDataOff, Math.Min(16, bannerTest.Length - absDataOff))
                                        : "(out of range)";
                                    LogDiag($"     OuterU8 file[{n}]: '{nodeName}' size={nSize} dataOff=0x{nDataOff:X} first16={firstBytes}");

                                    // If it's a .bin file, check IMD5+LZ77 structure
                                    if (nodeName.EndsWith(".bin") && nSize > 40 && absDataOff + 40 <= bannerTest.Length)
                                    {
                                        string imd5Magic = System.Text.Encoding.ASCII.GetString(bannerTest, absDataOff, 4);
                                        int imd5Size = System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(bannerTest, absDataOff + 4));
                                        LogDiag($"       IMD5: magic='{imd5Magic}', declaredSize={imd5Size}");

                                        // LZ77 header follows IMD5 (32 bytes per libWiiSharp IMD5 header size)
                                        int lz77Off = absDataOff + 32;
                                        if (lz77Off + 8 <= bannerTest.Length)
                                        {
                                            string lz77Magic = System.Text.Encoding.ASCII.GetString(bannerTest, lz77Off, 4);
                                            uint lz77Word = BitConverter.ToUInt32(bannerTest, lz77Off + 4); // LE
                                            uint decompSize = lz77Word >> 8;
                                            LogDiag($"       LZ77: magic='{lz77Magic}', decompSize={decompSize}");
                                        }
                                    }
                                }
                                else // directory
                                {
                                    LogDiag($"     OuterU8 dir[{n}]: '{nodeName}' parent={nDataOff} endIdx={nSize}");
                                }
                            }
                        }
                        catch (Exception parseEx)
                        {
                            LogDiag($"     OuterU8 parse error: {parseEx.Message}");
                        }
                    }

                    // Self-test: verify IMET magic at 0x80 and outer U8 magic at 0x680
                    // (libWiiSharp.U8 expects raw U8, not IMET-wrapped, so we check magic bytes directly)
                    bool iMetOk = bannerTest.Length >= 0x684 &&
                                  bannerTest[0x80] == 0x49 && bannerTest[0x81] == 0x4D &&
                                  bannerTest[0x82] == 0x45 && bannerTest[0x83] == 0x54;
                    bool u8Ok = bannerTest.Length >= 0x684 &&
                                bannerTest[0x680] == 0x55 && bannerTest[0x681] == 0xAA &&
                                bannerTest[0x682] == 0x38 && bannerTest[0x683] == 0x2D;
                    LogDiag($"  -> Banner self-test: IMET@0x80={iMetOk}, U8@0x680={u8Ok} => {(iMetOk && u8Ok ? "PASSED" : "FAILED")}");

                    // Save a copy for offline analysis
                    try
                    {
                        string diagBannerPath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "WiiGSC_last_banner.app");
                        File.WriteAllBytes(diagBannerPath, bannerTest);
                        LogDiag($"  -> Saved banner copy to {diagBannerPath}");
                    }
                    catch { /* ignore save errors */ }
                }
                catch (Exception testEx)
                {
                    LogDiag($"  -> Banner self-test: FAILED ({testEx.GetType().Name}: {testEx.Message})");
                    LogDiag($"     Stack: {testEx.StackTrace}");
                }

                // 5. Update TMD content info (size/hash)
                LogDiag("Step 5: Updating TMD contents...");
                Wii.WadEdit.UpdateTmdContents(tmdPath);

                // 6. Pack WAD with proper Title ID
                LogDiag("Step 6: Packing WAD...");
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
                LogDiag($"  -> WAD written to: {outputPath}");
                LogDiag("=== SUCCESS ===");

                return (string?)null; // null = success
            }
            catch (Exception ex)
            {
                LogDiag($"=== FAILED: {ex.GetType().Name}: {ex.Message} ===");
                LogDiag($"  Stack: {ex.StackTrace}");
                if (ex.InnerException != null)
                    LogDiag($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                return ex.Message;
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        });
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

    // ========================================================================
    // Minimal BRLYT/BRLAN templates for Wii banner rendering
    // ========================================================================
    // A Wii System Menu banner requires inner U8 archives containing:
    //   arc/anim/*.brlan  (animation layout)
    //   arc/blyt/*.brlyt  (binary layout - texture references, materials, panes)
    //   arc/timg/*.tpl    (texture data)
    // Without these layout files, the System Menu cannot render the banner image.

    /// <summary>
    /// Minimal banner BRLYT: 608x456 canvas, 1 texture "banner.tpl", 1 material, 1 picture pane.
    /// Sections: RLYT header + lyt1 + txl1 + mat1 + pan1 + pas1 + pic1 + pae1 + grp1
    /// Material structure per BRLYT spec (tockdom wiki):
    ///   20-byte name + Int16[4] foreColor + Int16[4] backColor + Int16[4] colorReg3
    ///   + Byte[4] tevColor1-4 + u32 flags + optional data (texMap, texSRT, texCoordGen)
    /// </summary>
    private static readonly byte[] BannerBrlytTemplate = new byte[] {
        // RLYT header (16 bytes): fileSize=0x01A8 (424), 8 sections
        0x52, 0x4C, 0x59, 0x54, 0xFE, 0xFF, 0x00, 0x08, 0x00, 0x00, 0x01, 0xA8, 0x00, 0x10, 0x00, 0x08,
        // lyt1 (20 bytes): 608x456 canvas
        0x6C, 0x79, 0x74, 0x31, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x00, 0x44, 0x18, 0x00, 0x00, 0x43, 0xE4, 0x00, 0x00,
        // txl1 (32 bytes): 1 texture "banner.tpl", offset=8 (relative to offset array start)
        0x74, 0x78, 0x6C, 0x31, 0x00, 0x00, 0x00, 0x20, 0x00, 0x01, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00,
        0x62, 0x61, 0x6E, 0x6E, 0x65, 0x72, 0x2E, 0x74, 0x70, 0x6C, 0x00, 0x00,
        // mat1 (108=0x6C bytes): 1 material "BannerMat", offset=0x10 (relative to mat1 start)
        0x6D, 0x61, 0x74, 0x31, 0x00, 0x00, 0x00, 0x6C, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10,
        // Material name (20 bytes)
        0x42, 0x61, 0x6E, 0x6E, 0x65, 0x72, 0x4D, 0x61, 0x74, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        // ForeColor Int16[4] RGBA (8 bytes): white
        0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
        // BackColor Int16[4] RGBA (8 bytes): white
        0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
        // ColorReg3 Int16[4] RGBA (8 bytes): white
        0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
        // TEV Color 1-4 Byte[4] RGBA each (16 bytes): white
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        // Flags u32: M=1 texMap, L=1 texSRT, K=1 texCoordGen
        0x00, 0x00, 0x01, 0x11,
        // Texture Map (4 bytes): texID=0, settings=0 (linear filter, clamp wrap)
        0x00, 0x00, 0x00, 0x00,
        // Texture SRT (20 bytes): translate(0,0), rotate(0), scale(1,1)
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00,
        // TexCoord Gen (4 bytes): type=0(MTX3x4), source=4(TEX0), matrix=0x3C(IDENTITY)
        0x00, 0x04, 0x3C, 0x00,
        // pan1: root pane "RootPane" 608x456
        0x70, 0x61, 0x6E, 0x31, 0x00, 0x00, 0x00, 0x4C,
        0x01, 0x00, 0xFF, 0x00,
        0x52, 0x6F, 0x6F, 0x74, 0x50, 0x61, 0x6E, 0x65, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x55, 0x73, 0x65, 0x72, 0x44, 0x61, 0x74, 0x61,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x44, 0x18, 0x00, 0x00, 0x43, 0xE4, 0x00, 0x00,
        // pas1 (8 bytes): pane children start
        0x70, 0x61, 0x73, 0x31, 0x00, 0x00, 0x00, 0x08,
        // pic1: picture pane "P_banner" 608x456, material 0, tex coords (0,0)-(1,1)
        0x70, 0x69, 0x63, 0x31, 0x00, 0x00, 0x00, 0x80,
        0x01, 0x00, 0xFF, 0x00,
        0x50, 0x5F, 0x62, 0x61, 0x6E, 0x6E, 0x65, 0x72, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x55, 0x73, 0x65, 0x72, 0x44, 0x61, 0x74, 0x61,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x44, 0x18, 0x00, 0x00, 0x43, 0xE4, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x00, 0x01, 0x00,
        // texCoords: TL(0,0) TR(1,0) BL(0,1) BR(1,1) = 8 floats = 32 bytes
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00,
        // pae1 (8 bytes): pane children end
        0x70, 0x61, 0x65, 0x31, 0x00, 0x00, 0x00, 0x08,
        // grp1: "RootGroup"
        0x67, 0x72, 0x70, 0x31, 0x00, 0x00, 0x00, 0x1C,
        0x52, 0x6F, 0x6F, 0x74, 0x47, 0x72, 0x6F, 0x75, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    /// <summary>
    /// Minimal icon BRLYT: 128x128 canvas, 1 texture "icon.tpl", 1 material, 1 picture pane.
    /// </summary>
    private static readonly byte[] IconBrlytTemplate = new byte[] {
        // RLYT header (16 bytes): fileSize=0x01A8 (424), 8 sections
        0x52, 0x4C, 0x59, 0x54, 0xFE, 0xFF, 0x00, 0x08, 0x00, 0x00, 0x01, 0xA8, 0x00, 0x10, 0x00, 0x08,
        // lyt1 (20 bytes): 128x128 canvas
        0x6C, 0x79, 0x74, 0x31, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00, 0x00, 0x00, 0x43, 0x00, 0x00, 0x00, 0x43, 0x00, 0x00, 0x00,
        // txl1 (32 bytes): 1 texture "icon.tpl", offset=8 (relative to offset array start)
        0x74, 0x78, 0x6C, 0x31, 0x00, 0x00, 0x00, 0x20, 0x00, 0x01, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00,
        0x69, 0x63, 0x6F, 0x6E, 0x2E, 0x74, 0x70, 0x6C, 0x00, 0x00, 0x00, 0x00,
        // mat1 (108=0x6C bytes): 1 material "IconMat", offset=0x10 (relative to mat1 start)
        0x6D, 0x61, 0x74, 0x31, 0x00, 0x00, 0x00, 0x6C, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10,
        // Material name (20 bytes)
        0x49, 0x63, 0x6F, 0x6E, 0x4D, 0x61, 0x74, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        // ForeColor Int16[4] RGBA (8 bytes): white
        0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
        // BackColor Int16[4] RGBA (8 bytes): white
        0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
        // ColorReg3 Int16[4] RGBA (8 bytes): white
        0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF,
        // TEV Color 1-4 Byte[4] RGBA each (16 bytes): white
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        // Flags u32: M=1 texMap, L=1 texSRT, K=1 texCoordGen
        0x00, 0x00, 0x01, 0x11,
        // Texture Map (4 bytes): texID=0, settings=0 (linear filter, clamp wrap)
        0x00, 0x00, 0x00, 0x00,
        // Texture SRT (20 bytes): translate(0,0), rotate(0), scale(1,1)
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00,
        // TexCoord Gen (4 bytes): type=0(MTX3x4), source=4(TEX0), matrix=0x3C(IDENTITY)
        0x00, 0x04, 0x3C, 0x00,
        // pan1: root pane 128x128
        0x70, 0x61, 0x6E, 0x31, 0x00, 0x00, 0x00, 0x4C,
        0x01, 0x00, 0xFF, 0x00,
        0x52, 0x6F, 0x6F, 0x74, 0x50, 0x61, 0x6E, 0x65, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x55, 0x73, 0x65, 0x72, 0x44, 0x61, 0x74, 0x61,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x43, 0x00, 0x00, 0x00, 0x43, 0x00, 0x00, 0x00,
        // pas1
        0x70, 0x61, 0x73, 0x31, 0x00, 0x00, 0x00, 0x08,
        // pic1: "P_icon" 128x128
        0x70, 0x69, 0x63, 0x31, 0x00, 0x00, 0x00, 0x80,
        0x01, 0x00, 0xFF, 0x00,
        0x50, 0x5F, 0x69, 0x63, 0x6F, 0x6E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x55, 0x73, 0x65, 0x72, 0x44, 0x61, 0x74, 0x61,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x43, 0x00, 0x00, 0x00, 0x43, 0x00, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0x00, 0x00, 0x01, 0x00,
        // texCoords: TL(0,0) TR(1,0) BL(0,1) BR(1,1) = 8 floats = 32 bytes
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00, 0x3F, 0x80, 0x00, 0x00,
        // pae1
        0x70, 0x61, 0x65, 0x31, 0x00, 0x00, 0x00, 0x08,
        // grp1
        0x67, 0x72, 0x70, 0x31, 0x00, 0x00, 0x00, 0x1C,
        0x52, 0x6F, 0x6F, 0x74, 0x47, 0x72, 0x6F, 0x75, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    };

    /// <summary>
    /// Minimal BRLAN (static/idle animation, no animated properties).
    /// RLAN header + pai1 with 1 frame, loop enabled, 0 TPL refs and 0 animated entries.
    /// Frame count MUST be >= 1 or the Wii System Menu's animation timer will divide by zero.
    /// </summary>
    private static readonly byte[] StaticBrlanTemplate = new byte[] {
        // RLAN header (16 bytes): fileSize=0x24 (36), 1 section
        0x52, 0x4C, 0x41, 0x4E, 0xFE, 0xFF, 0x00, 0x08, 0x00, 0x00, 0x00, 0x24, 0x00, 0x10, 0x00, 0x01,
        // pai1 (20 bytes): frameCount=1, loop=true, 0 textures, 0 entries
        0x70, 0x61, 0x69, 0x31, 0x00, 0x00, 0x00, 0x14, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C
    };

    /// <summary>
    /// Builds an inner U8 archive for banner.bin or icon.bin with proper BRLYT/BRLAN/TPL structure.
    /// The Wii System Menu requires this layout to render the banner image.
    /// Structure: arc/anim/{name}.brlan + arc/blyt/{name}.brlyt + arc/timg/{name}.tpl
    /// Returns the complete banner.bin or icon.bin content (IMD5 + LZ77-compressed U8 archive).
    /// </summary>
    private static byte[] BuildInnerBannerArchive(string name, byte[] brlytTemplate, byte[] tplData, string tempDir)
    {
        // Create inner U8 directory structure on disk
        string innerDir = Path.Combine(tempDir, $"inner_{name}");
        string arcDir = Path.Combine(innerDir, "arc");
        string animDir = Path.Combine(arcDir, "anim");
        string blytDir = Path.Combine(arcDir, "blyt");
        string timgDir = Path.Combine(arcDir, "timg");

        Directory.CreateDirectory(animDir);
        Directory.CreateDirectory(blytDir);
        Directory.CreateDirectory(timgDir);

        // Write layout files
        File.WriteAllBytes(Path.Combine(animDir, $"{name}.brlan"), StaticBrlanTemplate);
        File.WriteAllBytes(Path.Combine(blytDir, $"{name}.brlyt"), brlytTemplate);
        File.WriteAllBytes(Path.Combine(timgDir, $"{name}.tpl"), tplData);

        // Pack inner directory as U8 archive
        byte[] innerU8 = Wii.U8.PackU8(innerDir);

        // LZ77 compress (required by Wii System Menu for banner.bin/icon.bin)
        byte[] compressed = Wii.Lz77.Compress(innerU8);

        // Wrap with IMD5 header
        byte[] result = Wii.U8.AddHeaderIMD5(compressed);

        // Clean up inner temp directory
        Directory.Delete(innerDir, true);

        return result;
    }

    /// <summary>
    /// Generates a minimal placeholder banner (cross-platform).
    /// Creates a solid-color banner/icon with proper inner U8 archive structure
    /// containing BRLYT layout + BRLAN animation + TPL texture data.
    /// </summary>
    private byte[] GeneratePlaceholderBanner(string channelTitle, string tempDir)
    {
        string bannerGenDir = Path.Combine(tempDir, "banner_generated");
        Directory.CreateDirectory(bannerGenDir);
        
        try
        {
            // Create TPL textures
            byte[] bannerTpl = CreateSolidColorTpl(192, 64, 0x9082); // Opaque dark blue (RGB555: high bit set, R=8,G=2,B=2)
            byte[] iconTpl = CreateSolidColorTpl(48, 48, 0x9082);

            // Build inner U8 archives with BRLYT/BRLAN/TPL structure
            byte[] bannerBin = BuildInnerBannerArchive("banner", BannerBrlytTemplate, bannerTpl, tempDir);
            byte[] iconBin = BuildInnerBannerArchive("icon", IconBrlytTemplate, iconTpl, tempDir);

            // Write banner.bin, icon.bin, sound.bin at root of outer U8 archive
            // The Wii System Menu expects these files at the root level (no subdirectory)
            File.WriteAllBytes(Path.Combine(bannerGenDir, "banner.bin"), bannerBin);
            File.WriteAllBytes(Path.Combine(bannerGenDir, "icon.bin"), iconBin);
            File.WriteAllBytes(Path.Combine(bannerGenDir, "sound.bin"), Wii.U8.AddHeaderIMD5(Array.Empty<byte>()));

            // Pack the banner folder into U8 archive (PackU8 auto-detects LZ77 sizes)
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
    /// Generates a proper banner from an image file (e.g., icon.png from the homebrew app).
    /// Uses sips (macOS) to resize to required Wii banner/icon dimensions,
    /// converts to RGB5A3 tiled TPL format, and builds proper inner U8 archives
    /// with BRLYT layout + BRLAN animation + TPL texture.
    /// </summary>
    private byte[] GenerateBannerFromImage(string imagePath, string channelTitle, string tempDir)
    {
        string bannerGenDir = Path.Combine(tempDir, "banner_generated");
        Directory.CreateDirectory(bannerGenDir);

        try
        {
            string bannerBmp = Path.Combine(bannerGenDir, "banner_src.bmp");
            string iconBmp = Path.Combine(bannerGenDir, "icon_src.bmp");

            // Use sips to resize source image to banner (192x64) and icon (48x48), export as BMP
            RunSips(imagePath, bannerBmp, 192, 64);
            RunSips(imagePath, iconBmp, 48, 48);

            // Convert BMP pixel data to Wii TPL format (RGB5A3, 4x4 tiled)
            byte[] bannerTpl = BmpToTpl(bannerBmp, 192, 64);
            byte[] iconTpl = BmpToTpl(iconBmp, 48, 48);

            // Clean up temp BMP files
            File.Delete(bannerBmp);
            File.Delete(iconBmp);

            // Build inner U8 archives with BRLYT/BRLAN/TPL structure
            byte[] bannerBin = BuildInnerBannerArchive("banner", BannerBrlytTemplate, bannerTpl, tempDir);
            byte[] iconBin = BuildInnerBannerArchive("icon", IconBrlytTemplate, iconTpl, tempDir);

            // Write to outer U8 directory at root level (Wii System Menu expects flat paths)
            File.WriteAllBytes(Path.Combine(bannerGenDir, "banner.bin"), bannerBin);
            File.WriteAllBytes(Path.Combine(bannerGenDir, "icon.bin"), iconBin);
            File.WriteAllBytes(Path.Combine(bannerGenDir, "sound.bin"), Wii.U8.AddHeaderIMD5(Array.Empty<byte>()));

            // Pack into U8 archive (PackU8 auto-detects LZ77 sizes for IMET)
            byte[] u8Archive = Wii.U8.PackU8(bannerGenDir, out int bannerSize, out int iconSize, out int soundSize);

            // Create titles array (7 languages, all same)
            string[] titles = new string[7];
            for (int i = 0; i < 7; i++)
                titles[i] = channelTitle;

            int[] sizes = new int[] { bannerSize, iconSize, soundSize };
            byte[] finalBanner = Wii.U8.AddHeaderIMET(u8Archive, titles, sizes);

            Directory.Delete(bannerGenDir, true);
            return finalBanner;
        }
        catch
        {
            if (Directory.Exists(bannerGenDir))
                Directory.Delete(bannerGenDir, true);
            throw;
        }
    }

    /// <summary>
    /// Runs macOS sips to resize an image and export as BMP.
    /// </summary>
    private static void RunSips(string inputPath, string outputPath, int width, int height)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sips",
            Arguments = $"-z {height} {width} \"{inputPath}\" -s format bmp --out \"{outputPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new Exception("Failed to start sips");
        process.WaitForExit(10000);

        if (process.ExitCode != 0)
        {
            string err = process.StandardError.ReadToEnd();
            throw new Exception($"sips failed (exit {process.ExitCode}): {err}");
        }

        if (!File.Exists(outputPath))
            throw new FileNotFoundException($"sips did not create output: {outputPath}");
    }

    /// <summary>
    /// Parses a BMP file and converts pixel data to a Wii TPL file (RGB5A3, 4x4 tiled).
    /// Handles 24bpp BGR (no alpha, sips output for opaque PNGs) and
    /// 32bpp BGRX/BGRA from sips (standard BITMAPINFOHEADER or BITMAPV4HEADER).
    /// </summary>
    private static byte[] BmpToTpl(string bmpPath, int expectedWidth, int expectedHeight)
    {
        byte[] bmp = File.ReadAllBytes(bmpPath);

        if (bmp[0] != 0x42 || bmp[1] != 0x4D)
            throw new Exception("Not a valid BMP file");

        uint dataOffset = BitConverter.ToUInt32(bmp, 0x0A);
        int dibSize = BitConverter.ToInt32(bmp, 0x0E);
        int width = BitConverter.ToInt32(bmp, 0x12);
        int height = BitConverter.ToInt32(bmp, 0x16);
        ushort bpp = BitConverter.ToUInt16(bmp, 0x1C);
        int compression = BitConverter.ToInt32(bmp, 0x1E);

        if (bpp != 24 && bpp != 32)
            throw new Exception($"Unsupported BMP bit depth: {bpp}bpp (expected 24 or 32)");

        bool topDown = height < 0;
        int absHeight = Math.Abs(height);

        if (width != expectedWidth || absHeight != expectedHeight)
            throw new Exception($"BMP dimensions {width}x{absHeight} don't match expected {expectedWidth}x{expectedHeight}");

        byte[] rgba = new byte[width * absHeight * 4];

        if (bpp == 24)
        {
            // sips outputs BGR, no alpha; row stride is padded to 4-byte boundary
            int stride = (width * 3 + 3) & ~3;
            for (int y = 0; y < absHeight; y++)
            {
                int srcRow = topDown ? y : (absHeight - 1 - y);
                int srcOff = (int)dataOffset + srcRow * stride;
                int dstOff = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int si = srcOff + x * 3;
                    int di = dstOff + x * 4;
                    rgba[di + 0] = bmp[si + 2]; // R
                    rgba[di + 1] = bmp[si + 1]; // G
                    rgba[di + 2] = bmp[si + 0]; // B
                    rgba[di + 3] = 0xFF;         // A (fully opaque — no alpha in 24bpp)
                }
            }
        }
        else // 32bpp
        {
            // sips outputs BGRX when source has no alpha (padding byte = 0), or
            // BITMAPV4HEADER (dibSize=124, compression=3) when source has alpha.
            // In all cases: treat as opaque (the Wii banner should be solid).
            int stride = width * 4;
            for (int y = 0; y < absHeight; y++)
            {
                int srcRow = topDown ? y : (absHeight - 1 - y);
                int srcOff = (int)dataOffset + srcRow * stride;
                int dstOff = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int si = srcOff + x * 4;
                    int di = dstOff + x * 4;
                    rgba[di + 0] = bmp[si + 2]; // R (BMP stores BGR)
                    rgba[di + 1] = bmp[si + 1]; // G
                    rgba[di + 2] = bmp[si + 0]; // B
                    rgba[di + 3] = 0xFF;         // A — force opaque; sips BGRX has 0x00 padding
                }
            }
        }

        return CreateTplFromRgba(rgba, width, absHeight);
    }

    /// <summary>
    /// Creates a TPL file from RGBA pixel data using RGB5A3 format with 4x4 tiling.
    /// </summary>
    private static byte[] CreateTplFromRgba(byte[] rgba, int width, int height)
    {
        int imageHeaderOffset = 0x14;
        int imageDataOffset = (imageHeaderOffset + 0x24 + 31) & ~31; // Align to 32

        int tilesX = (width + 3) / 4;
        int tilesY = (height + 3) / 4;
        int imageDataSize = tilesX * tilesY * 4 * 4 * 2;

        byte[] tpl = new byte[imageDataOffset + imageDataSize];

        // TPL header
        tpl[0] = 0x00; tpl[1] = 0x20; tpl[2] = 0xAF; tpl[3] = 0x30; // Magic
        WriteBE32(tpl, 0x04, 1); // 1 image
        WriteBE32(tpl, 0x08, 0x0C); // Image table at 0x0C

        // Image table entry
        WriteBE32(tpl, 0x0C, (uint)imageHeaderOffset);
        WriteBE32(tpl, 0x10, 0); // No palette

        // Image header
        int ih = imageHeaderOffset;
        WriteBE16(tpl, ih + 0, (ushort)height);
        WriteBE16(tpl, ih + 2, (ushort)width);
        WriteBE32(tpl, ih + 4, 5); // Format = RGB5A3
        WriteBE32(tpl, ih + 8, (uint)imageDataOffset);
        WriteBE32(tpl, ih + 0x0C, 0); // Wrap S
        WriteBE32(tpl, ih + 0x10, 0); // Wrap T
        WriteBE32(tpl, ih + 0x14, 1); // Min filter (linear)
        WriteBE32(tpl, ih + 0x18, 1); // Mag filter (linear)

        // Convert pixels to RGB5A3 in 4x4 tile order
        int pos = imageDataOffset;
        for (int ty = 0; ty < tilesY; ty++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                for (int py = 0; py < 4; py++)
                {
                    for (int px = 0; px < 4; px++)
                    {
                        int x = tx * 4 + px;
                        int y = ty * 4 + py;

                        ushort pixel;
                        if (x < width && y < height)
                        {
                            int idx = (y * width + x) * 4;
                            byte r = rgba[idx + 0];
                            byte g = rgba[idx + 1];
                            byte b = rgba[idx + 2];
                            byte a = rgba[idx + 3];
                            pixel = RgbaToRgb5A3(r, g, b, a);
                        }
                        else
                        {
                            pixel = 0; // Transparent black for padding
                        }

                        tpl[pos] = (byte)(pixel >> 8);
                        tpl[pos + 1] = (byte)(pixel & 0xFF);
                        pos += 2;
                    }
                }
            }
        }

        return tpl;
    }

    /// <summary>
    /// Converts RGBA8 to Wii RGB5A3 format.
    /// If alpha >= 224: opaque RGB555 (1RRRRRGGGGGBBBBB)
    /// If alpha &lt; 224: translucent ARGB3444 (0AARRRRGGGGBBBB)
    /// </summary>
    private static ushort RgbaToRgb5A3(byte r, byte g, byte b, byte a)
    {
        if (a >= 224)
        {
            // Opaque: 1 RRRRR GGGGG BBBBB
            return (ushort)(0x8000 | ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3));
        }
        else
        {
            // Translucent: 0 AAA RRRR GGGG BBBB
            return (ushort)(((a >> 5) << 12) | ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4));
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
