using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace WiiGSC.UI.Services;

/// <summary>
/// Service for interacting with Wiimms ISO Tools (wit)
/// </summary>
public class WitService
{
    private string? _witPath;
    private bool _isAvailable;

    /// <summary>
    /// Gets whether wit is installed and available
    /// </summary>
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// Gets the path to the wit executable
    /// </summary>
    public string? WitPath => _witPath;

    /// <summary>
    /// Checks if wit is installed on the system
    /// </summary>
    public async Task<bool> CheckAvailabilityAsync()
    {
        try
        {
            // Try to find wit in PATH
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
                    Arguments = "wit",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                _witPath = output.Trim().Split('\n')[0].Trim();
                _isAvailable = true;
                return true;
            }

            // On macOS, also check common Homebrew locations
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string[] commonPaths = 
                {
                    "/usr/local/bin/wit",
                    "/opt/homebrew/bin/wit",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/bin/wit")
                };

                foreach (var path in commonPaths)
                {
                    if (File.Exists(path))
                    {
                        _witPath = path;
                        _isAvailable = true;
                        return true;
                    }
                }
            }

            _isAvailable = false;
            return false;
        }
        catch
        {
            _isAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Gets the installation instructions based on the current platform
    /// </summary>
    public string GetInstallInstructions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "Install wit using Homebrew:\n\nbrew install wit";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Download and install from:\nhttps://wit.wiimm.de/download.html";
        }
        else // Windows
        {
            return "Download and install from:\nhttps://wit.wiimm.de/download.html";
        }
    }

    /// <summary>
    /// Opens the wit download page in the default browser
    /// </summary>
    public void OpenDownloadPage()
    {
        string url = "https://wit.wiimm.de/";
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else // Linux
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            // Silently fail if can't open browser
        }
    }

    /// <summary>
    /// Attempts to install wit using Homebrew on macOS
    /// </summary>
    public async Task<(bool success, string message)> TryAutoInstallAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return (false, "Auto-install is only supported on macOS");
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "brew",
                    Arguments = "install wit",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                await CheckAvailabilityAsync(); // Refresh availability
                return (true, "Successfully installed wit via Homebrew");
            }
            else
            {
                return (false, $"Failed to install: {error}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error: {ex.Message}\n\nPlease install Homebrew first: https://brew.sh");
        }
    }

    /// <summary>
    /// Extracts the game banner (opening.bnr) from an ISO or WBFS file.
    /// The opening.bnr contains an IMET header + U8 archive with banner.bin, icon.bin, sound.bin.
    /// It can be used directly as 00000000.app in a WAD channel.
    /// </summary>
    public async Task<string?> ExtractBannerAsync(string gameFilePath, string outputDir, IProgress<string>? progress = null)
    {
        if (!_isAvailable)
            throw new InvalidOperationException("wit is not available");

        try
        {
            string extractDir = Path.Combine(outputDir, "extracted_banner");
            Directory.CreateDirectory(extractDir);

            progress?.Report("Extracting banner from game file...");

            // Use --files +opening.bnr to extract only the banner file
            // wit extract works directly on both ISO and WBFS files
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _witPath ?? "wit",
                    Arguments = $"extract \"{gameFilePath}\" \"{extractDir}\" --files +opening.bnr --flat",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            progress?.Report($"wit exit code: {process.ExitCode}, stdout: {output.Trim()}, stderr: {error.Trim()}");

            // Look for opening.bnr in extract directory
            string bannerPath = Path.Combine(extractDir, "opening.bnr");
            if (File.Exists(bannerPath))
            {
                var info = new FileInfo(bannerPath);
                progress?.Report($"Banner extracted successfully: {info.Length} bytes");
                return bannerPath;
            }

            // Sometimes it might be in a subdirectory
            var files = Directory.GetFiles(extractDir, "opening.bnr", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                progress?.Report("Banner extracted successfully (subdirectory)");
                return files[0];
            }

            progress?.Report("Banner file not found after extraction");
            return null;
        }
        catch (Exception ex)
        {
            progress?.Report($"Error extracting banner: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets game information from ISO/WBFS file
    /// </summary>
    public async Task<(string? gameId, string? title)> GetGameInfoAsync(string isoPath)
    {
        if (!_isAvailable)
            return (null, null);

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _witPath ?? "wit",
                    Arguments = $"list -H \"{isoPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                // Parse output: ID6 TITLE
                var parts = output.Trim().Split(new[] { ' ' }, 2);
                if (parts.Length >= 2)
                {
                    return (parts[0].Trim(), parts[1].Trim());
                }
            }

            return (null, null);
        }
        catch
        {
            return (null, null);
        }
    }
}
