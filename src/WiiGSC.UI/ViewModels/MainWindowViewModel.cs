using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using libWiiSharp;
using WiiGSC.UI.Models;
using WiiGSC.UI.Services;

namespace WiiGSC.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private Window? _window;
    private readonly GameTDBService _gameTDBService = new();
    private readonly WitService _witService = new();

    public void SetWindow(Window window)
    {
        _window = window;
        // Check wit availability when window is set
        _ = CheckWitAvailabilityAsync();
    }
    // Source mode
    [ObservableProperty]
    private bool _isIsoMode = true;
    
    [ObservableProperty]
    private bool _isBannerMode;
    
    [ObservableProperty]
    private bool _isWbfsMode;

    // File paths
    [ObservableProperty]
    private string _isoFilePath = string.Empty;
    
    [ObservableProperty]
    private string _bannerFilePath = string.Empty;

    // WBFS
    [ObservableProperty]
    private ObservableCollection<string> _wbfsDrives = new();
    
    [ObservableProperty]
    private string? _selectedWbfsDrive;
    
    [ObservableProperty]
    private string _wbfsFolderPath = string.Empty;
    
    // Store game info for WBFS entries
    private readonly Dictionary<string, WbfsGameInfo> _wbfsGameMap = new();

    // Game information
    [ObservableProperty]
    private bool _isGameLoaded;
    
    [ObservableProperty]
    private string _gameName = string.Empty;
    
    [ObservableProperty]
    private string _discId = string.Empty;
    
    [ObservableProperty]
    private string _gameRegion = string.Empty;
    
    [ObservableProperty]
    private Bitmap? _coverArt;
    
    [ObservableProperty]
    private bool _isLoadingCover;

    // Configuration
    [ObservableProperty]
    private ObservableCollection<LoaderItem> _loaders = new()
    {
        new LoaderItem 
        { 
            Name = "USB Loader GX (SD)", 
            Id = "USBLoaderGX_SD", 
            Description = "Forwarder attempts to launch /apps/usbloader_gx/boot.dol from SD Card first" 
        },
        new LoaderItem 
        { 
            Name = "USB Loader GX (USB)", 
            Id = "USBLoaderGX_USB", 
            Description = "Forwarder attempts to launch /apps/usbloader_gx/boot.dol from USB Drive first" 
        },
        new LoaderItem 
        { 
            Name = "WiiFlow (SD)", 
            Id = "WiiFlow_SD", 
            Description = "Forwarder attempts to launch /apps/wiiflow/boot.dol from SD Card first" 
        },
        new LoaderItem 
        { 
            Name = "WiiFlow (USB)", 
            Id = "WiiFlow_USB", 
            Description = "Forwarder attempts to launch /apps/wiiflow/boot.dol from USB Drive first" 
        },
        new LoaderItem 
        { 
            Name = "Configurable USB Loader", 
            Id = "CFG_USB_Loader", 
            Description = "Forwarder for Configurable USB Loader" 
        },
        new LoaderItem 
        { 
            Name = "Mighty Channels", 
            Id = "Mighty_Channels", 
            Description = "Forwarder for Mighty Channels" 
        }
    };
    
    [ObservableProperty]
    private LoaderItem _selectedLoader;

    [ObservableProperty]
    private ObservableCollection<string> _languages = new()
    {
        "Console Default",
        "English",
        "German",
        "French",
        "Spanish",
        "Italian",
        "Dutch",
        "Japanese"
    };
    
    [ObservableProperty]
    private string _selectedLanguage = "Console Default";

    [ObservableProperty]
    private ObservableCollection<string> _regions = new()
    {
        "Auto",
        "NTSC",
        "PAL"
    };
    
    [ObservableProperty]
    private string _selectedRegion = "Auto";

    [ObservableProperty]
    private string _titleId = string.Empty;
    
    [ObservableProperty]
    private bool _enableOcarina;
    
    [ObservableProperty]
    private bool _forceVideoMode;

    // Homebrew Forwarder
    [ObservableProperty]
    private string _homebrewAppsDirectory = string.Empty;
    
    [ObservableProperty]
    private ObservableCollection<string> _availableApps = new();
    
    [ObservableProperty]
    private string? _selectedApp;
    
    [ObservableProperty]
    private bool _hasApps;
    
    [ObservableProperty]
    private string _homebrewAppFolder = string.Empty;
    
    [ObservableProperty]
    private string _homebrewChannelTitle = string.Empty;
    
    [ObservableProperty]
    private string _homebrewTitleId = string.Empty;
    
    [ObservableProperty]
    private string _homebrewCoverPath = string.Empty;
    
    [ObservableProperty]
    private bool _canCreateHomebrew;
    
    // WAD Validator
    [ObservableProperty]
    private string _wadValidationPath = string.Empty;
    
    [ObservableProperty]
    private bool _canValidateWad;
    
    [ObservableProperty]
    private bool _hasValidationResults;
    
    [ObservableProperty]
    private string _wadValidationStatus = string.Empty;
    
    // Wit Tool Status
    [ObservableProperty]
    private bool _isWitAvailable;
    
    [ObservableProperty]
    private bool _showWitWarning;
    
    [ObservableProperty]
    private string _witStatusMessage = "Checking for wit tool...";
    
    [ObservableProperty]
    private string _witInstallInstructions = string.Empty;
    
    [ObservableProperty]
    private string _wadValidationStatusColor = "Black";
    
    [ObservableProperty]
    private bool _hasWadInfo;
    
    [ObservableProperty]
    private string _wadTitleId = string.Empty;
    
    [ObservableProperty]
    private string _wadChannelTitle = string.Empty;
    
    [ObservableProperty]
    private string _wadRegion = string.Empty;
    
    [ObservableProperty]
    private string _wadTitleVersion = string.Empty;
    
    [ObservableProperty]
    private int _wadNumContents;
    
    [ObservableProperty]
    private string _wadFileSize = string.Empty;

    [ObservableProperty]
    private string _wadIos = string.Empty;

    [ObservableProperty]
    private string _wadBlocks = string.Empty;

    [ObservableProperty]
    private string _wadType = string.Empty;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _wadBannerImage;

    [ObservableProperty]
    private bool _hasWadBannerImage;

    [ObservableProperty]
    private string _wadDescription = string.Empty;

    [ObservableProperty]
    private bool _hasWadDescription;

    [ObservableProperty]
    private string _wadInstallInstructions = string.Empty;

    [ObservableProperty]
    private bool _hasWadInstallInstructions;

    [ObservableProperty]
    private string _wadTicketTitleId = string.Empty;

    [ObservableProperty]
    private string _wadTmdTitleId = string.Empty;

    [ObservableProperty]
    private string _wadBannerInfo = string.Empty;

    [ObservableProperty]
    private string _wadContentSizes = string.Empty;

    [ObservableProperty]
    private bool _hasDebugInfo;
    
    [ObservableProperty]
    private bool _hasWadWarnings;
    
    [ObservableProperty]
    private ObservableCollection<string> _wadWarnings = new();
    
    [ObservableProperty]
    private bool _hasWadErrors;
    
    [ObservableProperty]
    private ObservableCollection<string> _wadErrors = new();

    // Status
    [ObservableProperty]
    private string _statusText = "Ready";
    
    [ObservableProperty]
    private bool _isOperationInProgress;
    
    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _canCreate;

    public MainWindowViewModel()
    {
        // Initialize
        SelectedLoader = Loaders.FirstOrDefault(l => l.Name.Contains("SD")) ?? Loaders.FirstOrDefault();
        UpdateCanCreate();
    }

    partial void OnIsoFilePathChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            LoadGameFromIso(value);
        }
        UpdateCanCreate();
    }

    partial void OnBannerFilePathChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            LoadGameFromBanner(value);
        }
        UpdateCanCreate();
    }

    private void UpdateCanCreate()
    {
        CanCreate = IsGameLoaded && 
                   ((IsIsoMode && !string.IsNullOrEmpty(IsoFilePath)) ||
                   (IsBannerMode && !string.IsNullOrEmpty(BannerFilePath)) ||
                   (IsWbfsMode && SelectedWbfsDrive != null));
    }

    [RelayCommand]
    private async Task BrowseIso()
    {
        if (_window == null)
        {
            StatusText = "Window not initialized";
            return;
        }

        var options = new FilePickerOpenOptions
        {
            Title = "Select Wii ISO File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Wii ISO Files")
                {
                    Patterns = new[] { "*.iso", "*.wbfs" },
                    MimeTypes = new[] { "application/octet-stream" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        };

        var files = await _window.StorageProvider.OpenFilePickerAsync(options);
        
        if (files.Count > 0)
        {
            IsoFilePath = files[0].Path.LocalPath;
            StatusText = $"Selected: {files[0].Name}";
            // TODO: Parse ISO to extract game information
            LoadGameFromIso(IsoFilePath);
        }
    }

    [RelayCommand]
    private async Task BrowseBanner()
    {
        if (_window == null)
        {
            StatusText = "Window not initialized";
            return;
        }

        var options = new FilePickerOpenOptions
        {
            Title = "Select Wii Banner File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Wii Banner Files")
                {
                    Patterns = new[] { "*.bnr", "*.bin" },
                    MimeTypes = new[] { "application/octet-stream" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        };

        var files = await _window.StorageProvider.OpenFilePickerAsync(options);
        
        if (files.Count > 0)
        {
            BannerFilePath = files[0].Path.LocalPath;
            StatusText = $"Selected: {files[0].Name}";
            // TODO: Parse banner to extract game information
            LoadGameFromBanner(BannerFilePath);
        }
    }

    [RelayCommand]
    private async Task BrowseWbfsFolder()
    {
        if (_window == null)
        {
            StatusText = "Window not initialized";
            return;
        }

        var options = new FolderPickerOpenOptions
        {
            Title = "Select WBFS Folder",
            AllowMultiple = false
        };

        var folders = await _window.StorageProvider.OpenFolderPickerAsync(options);
        
        if (folders.Count > 0)
        {
            WbfsFolderPath = folders[0].Path.LocalPath;
            StatusText = $"Selected folder: {folders[0].Name}";
            RefreshWbfs();
        }
    }
    
    [RelayCommand]
    private void RefreshWbfs()
    {
        WbfsDrives.Clear();
        _wbfsGameMap.Clear();
        
        if (string.IsNullOrEmpty(WbfsFolderPath) || !Directory.Exists(WbfsFolderPath))
        {
            StatusText = "Please select a WBFS folder first";
            return;
        }

        try
        {
            // Scan for game folders
            var gameFolders = Directory.GetDirectories(WbfsFolderPath)
                .Where(dir => !Path.GetFileName(dir).StartsWith("."))
                .ToList();

            foreach (var gameFolder in gameFolders)
            {
                var folderName = Path.GetFileName(gameFolder);
                
                // Look for .wbfs files in the folder
                var wbfsFiles = Directory.GetFiles(gameFolder, "*.wbfs");
                
                if (wbfsFiles.Length > 0)
                {
                    // Use folder name as display name (e.g., "Mario Kart Wii [RMCE01]")
                    WbfsDrives.Add(folderName);
                    _wbfsGameMap[folderName] = new WbfsGameInfo
                    {
                        FolderPath = gameFolder,
                        WbfsFilePath = wbfsFiles[0],
                        DisplayName = folderName
                    };
                }
            }
            
            // Also check for loose .wbfs files in the root folder
            var rootWbfsFiles = Directory.GetFiles(WbfsFolderPath, "*.wbfs");
            foreach (var wbfsFile in rootWbfsFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(wbfsFile);
                WbfsDrives.Add(fileName);
                _wbfsGameMap[fileName] = new WbfsGameInfo
                {
                    FolderPath = WbfsFolderPath,
                    WbfsFilePath = wbfsFile,
                    DisplayName = fileName
                };
            }

            StatusText = $"Found {WbfsDrives.Count} game(s)";
            
            // Auto-select first game if available
            if (WbfsDrives.Count > 0)
            {
                SelectedWbfsDrive = WbfsDrives[0];
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error scanning folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task OpenIso()
    {
        IsIsoMode = true;
        IsBannerMode = false;
        IsWbfsMode = false;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenBanner()
    {
        IsIsoMode = false;
        IsBannerMode = true;
        IsWbfsMode = false;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenWbfs()
    {
        IsIsoMode = false;
        IsBannerMode = false;
        IsWbfsMode = true;
        
        // If no folder selected yet, prompt to browse
        if (string.IsNullOrEmpty(WbfsFolderPath))
        {
            StatusText = "Please select a WBFS folder";
        }
        else
        {
            RefreshWbfs();
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ChangeLanguage(string language)
    {
        StatusText = $"Language changed to {language}";
    }

    [RelayCommand]
    private void Exit()
    {
        Environment.Exit(0);
    }

    [RelayCommand]
    private Task About()
    {
        StatusText = "WiiGSC - Wii Game Shortcut Creator | Ported to .NET 8 / macOS";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CreateShortcut()
    {
        if (!CanCreate)
            return;

        try
        {
            IsOperationInProgress = true;
            StatusText = "Creating shortcut...";
            ProgressValue = 0;

            // Create output directory
            var outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "WiiGSC Shortcuts");
            Directory.CreateDirectory(outputDir);

            StatusText = "Building WAD file...";
            ProgressValue = 25;
            
            // Generate filename - extract just the game name without brackets/ID
            var cleanGameName = GameName;
            var bracketIndex = cleanGameName.IndexOf('[');
            if (bracketIndex > 0)
            {
                cleanGameName = cleanGameName.Substring(0, bracketIndex).Trim();
            }
            
            // Remove colons completely from game name
            cleanGameName = cleanGameName.Replace(":", "");
            
            // Create safe filename: "Game Name [DISCID].wad"
            var safeGameName = string.Join("_", cleanGameName.Split(Path.GetInvalidFileNameChars()));
            var outputPath = Path.Combine(outputDir, $"{safeGameName} [{DiscId}].wad");
            
            // Create WAD using the service
            var wadService = new WadCreationService();
            string gamePath = IsoFilePath;
            
            if (IsWbfsMode && SelectedWbfsDrive != null && _wbfsGameMap.ContainsKey(SelectedWbfsDrive))
            {
                gamePath = _wbfsGameMap[SelectedWbfsDrive].WbfsFilePath;
            }
            
            // Generate a unique channel title ID if user hasn't specified one or it's default
            string channelId = TitleId;
            if (string.IsNullOrWhiteSpace(channelId) || channelId.Length != 4)
            {
                channelId = DiscId.Length >= 4 ? DiscId.Substring(0, 4) : "WGSC";
            }
            
            var result = await wadService.CreateGameShortcutWad(
                gameFilePath: gamePath,
                outputPath: outputPath,
                channelTitle: GameName,
                titleId: channelId,
                discId: DiscId,
                loaderId: SelectedLoader.Id,
                witService: _witService
            );
            
            if (!result)
            {
                StatusText = "Error creating WAD file.";
                return;
            }
            
            ProgressValue = 100;
            StatusText = $"Shortcut created: {Path.GetFileName(outputPath)}";
            
            // Show success message and offer to open folder
            if (_window != null)
            {
                await Task.Delay(500);
                
                // Open the output directory
                if (OperatingSystem.IsMacOS())
                {
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = $"\"{outputDir}\"",
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(processInfo);
                }
                else if (OperatingSystem.IsWindows())
                {
                    System.Diagnostics.Process.Start("explorer", outputDir);
                }
                else if (OperatingSystem.IsLinux())
                {
                    System.Diagnostics.Process.Start("xdg-open", outputDir);
                }
                
                StatusText = $"Shortcut saved to: {outputDir}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error creating shortcut: {ex.Message}";
        }
        finally
        {
            IsOperationInProgress = false;
            await Task.Delay(2000);
            ProgressValue = 0;
        }
    }

    private async void LoadGameFromIso(string path)
    {
        try
        {
            // Use wit to extract game info if available, otherwise parse filename
            IsGameLoaded = true;
            
            var fileName = Path.GetFileNameWithoutExtension(path);
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(.+?)\s*\[([A-Z0-9]{4,6})\]");
            if (match.Success)
            {
                GameName = match.Groups[1].Value.Trim();
                DiscId = match.Groups[2].Value;
            }
            else
            {
                GameName = fileName;
                DiscId = "UNKN";
            }
            
            GameRegion = DiscId.Length >= 4 ? (DiscId[3] switch
            {
                'E' => "NTSC-U",
                'P' => "PAL",
                'J' => "NTSC-J",
                'K' => "NTSC-K",
                _ => "Unknown"
            }) : "Unknown";
            
            TitleId = DiscId.Length >= 4 ? DiscId.Substring(0, 4) : DiscId;
            StatusText = $"Loaded: {GameName}";
            
            await DownloadCoverArtAsync(DiscId);
            UpdateCanCreate();
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading ISO: {ex.Message}";
            IsGameLoaded = false;
            UpdateCanCreate();
        }
    }

    private async void LoadGameFromBanner(string path)
    {
        try
        {
            IsGameLoaded = true;
            
            var fileName = Path.GetFileNameWithoutExtension(path);
            GameName = fileName;
            DiscId = "CUST";
            GameRegion = "Multi";
            StatusText = $"Loaded: {fileName}";
            
            await DownloadCoverArtAsync(DiscId);
            UpdateCanCreate();
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading banner: {ex.Message}";
            IsGameLoaded = false;
            UpdateCanCreate();
        }
    }
    
    partial void OnSelectedWbfsDriveChanged(string? value)
    {
        if (value != null && _wbfsGameMap.TryGetValue(value, out var gameInfo))
        {
            LoadGameFromWbfs(gameInfo);
        }
        UpdateCanCreate();
    }
    
    private async void LoadGameFromWbfs(WbfsGameInfo gameInfo)
    {
        try
        {
            IsGameLoaded = true;
            GameName = gameInfo.DisplayName;
            
            // Try to extract game ID from folder name or filename
            // Format is usually "Game Name [GAMEID]"
            var match = System.Text.RegularExpressions.Regex.Match(
                gameInfo.DisplayName, 
                @"\[([A-Z0-9]{4,6})\]");
            
            if (match.Success)
            {
                DiscId = match.Groups[1].Value;
            }
            else
            {
                DiscId = "UNKNOWN";
            }
            
            GameRegion = "Multi";
            StatusText = $"Loaded: {gameInfo.DisplayName}";
            
            // Download cover art
            if (DiscId != "UNKNOWN")
            {
                await DownloadCoverArtAsync(DiscId);
            }
            UpdateCanCreate();
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading WBFS game: {ex.Message}";
            IsGameLoaded = false;
            UpdateCanCreate();
        }
    }
    
    private async Task DownloadCoverArtAsync(string discId)
    {
        if (string.IsNullOrWhiteSpace(discId) || discId == "UNKNOWN")
        {
            CoverArt = null;
            return;
        }

        try
        {
            IsLoadingCover = true;
            StatusText = "Downloading cover art...";
            
            // Try to get 3D cover first, fall back to regular cover
            var cover = await _gameTDBService.Get3DCoverAsync(discId);
            if (cover == null)
            {
                cover = await _gameTDBService.GetCoverArtAsync(discId);
            }
            
            CoverArt = cover;
            
            if (cover != null)
            {
                StatusText = $"Loaded: {GameName} (cover downloaded)";
            }
            else
            {
                StatusText = $"Loaded: {GameName} (no cover art available)";
            }
        }
        catch
        {
            CoverArt = null;
        }
        finally
        {
            IsLoadingCover = false;
        }
    }
    
    partial void OnDiscIdChanged(string value)
    {
        // Auto-populate Title ID from first 4 characters of Disc ID
        if (!string.IsNullOrWhiteSpace(value) && value.Length >= 4)
        {
            TitleId = value.Substring(0, 4);
        }
    }
    
    partial void OnHomebrewAppsDirectoryChanged(string value)
    {
        ScanAppsDirectory();
    }
    
    partial void OnSelectedAppChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            HomebrewAppFolder = value;
            TryAutoPopulateAppInfo(value);
        }
    }
    
    partial void OnHomebrewAppFolderChanged(string value)
    {
        UpdateCanCreateHomebrew();
    }
    
    partial void OnHomebrewChannelTitleChanged(string value)
    {
        UpdateCanCreateHomebrew();
    }
    
    partial void OnHomebrewTitleIdChanged(string value)
    {
        UpdateCanCreateHomebrew();
    }
    
    partial void OnWadValidationPathChanged(string value)
    {
        CanValidateWad = !string.IsNullOrWhiteSpace(value) && File.Exists(value);
    }
    
    private void UpdateCanCreateHomebrew()
    {
        CanCreateHomebrew = !string.IsNullOrWhiteSpace(HomebrewAppFolder) &&
                           HomebrewAppFolder.Length >= 3 &&
                           HomebrewAppFolder.Length <= 18 &&
                           !string.IsNullOrWhiteSpace(HomebrewChannelTitle) &&
                           !string.IsNullOrWhiteSpace(HomebrewTitleId) &&
                           HomebrewTitleId.Length == 4;
    }
    
    private void ScanAppsDirectory()
    {
        AvailableApps.Clear();
        HasApps = false;
        
        if (string.IsNullOrWhiteSpace(HomebrewAppsDirectory) || !Directory.Exists(HomebrewAppsDirectory))
            return;
        
        try
        {
            var appDirs = Directory.GetDirectories(HomebrewAppsDirectory)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name) && 
                             name.Length >= 3 && 
                             name.Length <= 18)
                .OrderBy(name => name);
            
            foreach (var appDir in appDirs)
            {
                if (appDir != null)
                {
                    AvailableApps.Add(appDir);
                }
            }
            
            HasApps = AvailableApps.Count > 0;
        }
        catch
        {
            // Ignore errors when scanning directory
        }
    }
    
    private void TryAutoPopulateAppInfo(string appFolderName)
    {
        if (string.IsNullOrWhiteSpace(HomebrewAppsDirectory))
            return;
        
        var appPath = Path.Combine(HomebrewAppsDirectory, appFolderName);
        if (!Directory.Exists(appPath))
            return;
        
        // Try to find and parse meta.xml for channel title
        var metaXmlPath = Path.Combine(appPath, "meta.xml");
        if (File.Exists(metaXmlPath))
        {
            try
            {
                var metaXml = File.ReadAllText(metaXmlPath);
                
                // Simple XML parsing for <name> tag
                var nameMatch = System.Text.RegularExpressions.Regex.Match(metaXml, @"<name>(.+?)</name>");
                if (nameMatch.Success && string.IsNullOrWhiteSpace(HomebrewChannelTitle))
                {
                    HomebrewChannelTitle = nameMatch.Groups[1].Value.Trim();
                }
                
                // Try to extract a short code from the app name for Title ID
                if (string.IsNullOrWhiteSpace(HomebrewTitleId))
                {
                    var shortCode = appFolderName
                        .Replace("_", "")
                        .Replace("-", "")
                        .ToUpperInvariant();
                    
                    if (shortCode.Length >= 4)
                    {
                        HomebrewTitleId = shortCode.Substring(0, 4);
                    }
                    else
                    {
                        HomebrewTitleId = shortCode.PadRight(4, 'X');
                    }
                }
            }
            catch
            {
                // Ignore XML parsing errors
            }
        }
        
        // Auto-populate from folder name if still empty
        if (string.IsNullOrWhiteSpace(HomebrewChannelTitle))
        {
            HomebrewChannelTitle = appFolderName.Replace("_", " ").Replace("-", " ");
            // Capitalize first letter of each word
            HomebrewChannelTitle = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(HomebrewChannelTitle.ToLower());
        }
        
        // Try to find banner/icon images
        // Common names: icon.png, banner.png, appicon.png
        // Wii banner is typically 192x64, icon is 128x48
        var imageExtensions = new[] { ".png", ".jpg", ".jpeg" };
        var possibleNames = new[] { "icon", "banner", "appicon", "logo" };
        
        foreach (var name in possibleNames)
        {
            foreach (var ext in imageExtensions)
            {
                var imagePath = Path.Combine(appPath, name + ext);
                if (File.Exists(imagePath))
                {
                    HomebrewCoverPath = imagePath;
                    return; // Found an image, stop searching
                }
            }
        }
    }
    
    [RelayCommand]
    private async Task BrowseAppsDirectory()
    {
        if (_window == null) return;
        
        var options = new FolderPickerOpenOptions
        {
            Title = "Select /apps/ Directory on SD Card",
            AllowMultiple = false
        };
        
        var folders = await _window.StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count > 0)
        {
            HomebrewAppsDirectory = folders[0].Path.LocalPath;
        }
    }
    
    [RelayCommand]
    private async Task BrowseHomebrewCover()
    {
        if (_window == null) return;
        
        var options = new FilePickerOpenOptions
        {
            Title = "Select Cover Image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Image Files")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" }
                }
            }
        };
        
        var files = await _window.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            HomebrewCoverPath = files[0].Path.LocalPath;
        }
    }
    
    [RelayCommand]
    private async Task CreateHomebrewForwarder()
    {
        try
        {
            IsOperationInProgress = true;
            ProgressValue = 0;
            StatusText = "Creating homebrew forwarder...";
            
            var outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "WiiGSC Shortcuts");
            Directory.CreateDirectory(outputDir);
            
            var sanitizedName = string.Join("_", HomebrewChannelTitle.Split(Path.GetInvalidFileNameChars()));
            var outputPath = Path.Combine(outputDir, $"{sanitizedName} [{HomebrewTitleId}].wad");
            
            // TODO: Implement actual WAD creation when ForwardMii templates are available
            await Task.Delay(500);
            
            StatusText = "Homebrew forwarder WAD creation not yet implemented.";
            ProgressValue = 100;
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsOperationInProgress = false;
        }
    }
    
    [RelayCommand]
    private async Task BrowseWadFile()
    {
        if (_window == null) return;
        
        var options = new FilePickerOpenOptions
        {
            Title = "Select WAD File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("WAD Files")
                {
                    Patterns = new[] { "*.wad" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        };
        
        var files = await _window.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            WadValidationPath = files[0].Path.LocalPath;
        }
    }
    
    [RelayCommand]
    private async Task ValidateWadFile()
    {
        if (string.IsNullOrWhiteSpace(WadValidationPath) || !File.Exists(WadValidationPath))
            return;
        
        try
        {
            IsOperationInProgress = true;
            StatusText = "Validating WAD file...";
            
            // Clear previous results
            HasValidationResults = false;
            HasWadInfo = false;
            HasWadWarnings = false;
            HasWadErrors = false;
            HasWadBannerImage = false;
            HasWadDescription = false;
            HasWadInstallInstructions = false;
            WadBannerImage = null;
            WadWarnings.Clear();
            WadErrors.Clear();
            
            await Task.Run(() =>
            {
                var wadService = new WadCreationService();
                var result = wadService.ValidateWad(WadValidationPath);
                
                // Update UI on dispatcher
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    HasValidationResults = true;
                    
                    if (result.IsValid)
                    {
                        WadValidationStatus = "✓ Valid - Safe to Install";
                        WadValidationStatusColor = "Green";
                    }
                    else
                    {
                        WadValidationStatus = "✗ Invalid - DO NOT INSTALL";
                        WadValidationStatusColor = "Red";
                    }
                    
                    // Load warnings and errors
                    if (result.Warnings.Count > 0)
                    {
                        HasWadWarnings = true;
                        foreach (var warning in result.Warnings)
                        {
                            WadWarnings.Add("⚠ " + warning);
                        }
                    }
                    
                    if (result.Errors.Count > 0)
                    {
                        HasWadErrors = true;
                        foreach (var error in result.Errors)
                        {
                            WadErrors.Add("✗ " + error);
                        }
                    }
                    
                    // Try to load WAD info using libWiiSharp
                    try
                    {
                        var wad = WAD.Load(WadValidationPath);
                        if (wad != null)
                        {
                            HasWadInfo = true;
                            WadTitleId = wad.TitleID.ToString("X16");
                            WadRegion = wad.Region.ToString();
                            WadTitleVersion = wad.TitleVersion.ToString();
                            WadNumContents = wad.NumOfContents;
                            
                            var fileInfo = new FileInfo(WadValidationPath);
                            WadFileSize = FormatFileSize(fileInfo.Length);
                            
                            // Load additional info
                            try {
                                WadIos = (wad.StartupIOS & 0xFF).ToString();
                                WadBlocks = wad.NandBlocks;
                                
                                // Parse upper Title ID from the full Title ID (first 8 hex chars)
                                string upper = WadTitleId.Length >= 8 ? WadTitleId.Substring(0, 8) : "00000000";
                                if (upper == "00000001") WadType = "System Title (Dangerous!)";
                                else if (upper == "00010001") WadType = "Channel";
                                else if (upper == "00010002") WadType = "System Channel";
                                else if (upper == "00010004") WadType = "Game Channel";
                                else if (upper == "00010005") WadType = "DLC";
                                else if (upper == "00010008") WadType = "Hidden Channel";
                                else WadType = "Unknown (Upper ID: " + upper + ")";
                            } catch {
                                WadIos = "Unknown";
                                WadBlocks = "Unknown";
                                WadType = "Unknown";
                            }
                            
                            // Try to get channel title from banner
                            if (wad.HasBanner && wad.ChannelTitles.Length > 0)
                            {
                                WadChannelTitle = wad.ChannelTitles[0];
                            }
                            else
                            {
                                WadChannelTitle = "(No banner)";
                            }

                            // Extract debug information from raw WAD bytes
                            try
                            {
                                byte[] wadBytes = File.ReadAllBytes(WadValidationPath);
                                
                                // Find ticket position (after cert)
                                int tikOffset = BitConverter.ToInt32(new byte[] { wadBytes[0x0B], wadBytes[0x0A], wadBytes[0x09], wadBytes[0x08] }, 0);
                                tikOffset += 64; // Skip WAD header
                                int certSize = BitConverter.ToInt32(new byte[] { wadBytes[0x0F], wadBytes[0x0E], wadBytes[0x0D], wadBytes[0x0C] }, 0);
                                certSize = (certSize + 63) & ~63; // Round up to 64-byte boundary
                                tikOffset += certSize;
                                
                                // Find TMD position
                                int tmdOffset = tikOffset;
                                int tikSize = BitConverter.ToInt32(new byte[] { wadBytes[0x13], wadBytes[0x12], wadBytes[0x11], wadBytes[0x10] }, 0);
                                tikSize = (tikSize + 63) & ~63; // Round up to 64-byte boundary
                                tmdOffset += tikSize;
                                
                                // Extract Title ID from Ticket (0x1DC-0x1E3 in ticket)
                                if (tikOffset + 0x1E3 < wadBytes.Length)
                                {
                                    string tikTitleId = "";
                                    for (int i = 0; i < 8; i++)
                                    {
                                        tikTitleId += wadBytes[tikOffset + 0x1DC + i].ToString("X2");
                                    }
                                    WadTicketTitleId = tikTitleId;
                                }
                                
                                // Extract Title ID from TMD (0x18C-0x193 in TMD)
                                if (tmdOffset + 0x193 < wadBytes.Length)
                                {
                                    string tmdTitleId = "";
                                    for (int i = 0; i < 8; i++)
                                    {
                                        tmdTitleId += wadBytes[tmdOffset + 0x18C + i].ToString("X2");
                                    }
                                    WadTmdTitleId = tmdTitleId;
                                }
                                
                                // Check banner content
                                WadBannerInfo = "";
                                if (wad.HasBanner)
                                {
                                    WadBannerInfo = $"Banner present\n";
                                    if (wad.ChannelTitles != null && wad.ChannelTitles.Length > 0)
                                    {
                                        WadBannerInfo += $"Titles: {string.Join(", ", wad.ChannelTitles.Where(t => !string.IsNullOrEmpty(t)))}\n";
                                    }
                                }
                                else
                                {
                                    WadBannerInfo = "No banner detected";
                                }
                                
                                // Get content sizes
                                WadContentSizes = "";
                                for (int i = 0; i < wad.TmdContents.Length; i++)
                                {
                                    WadContentSizes += $"Content {wad.TmdContents[i].Index:X8}.app: {wad.TmdContents[i].Size} bytes\n";
                                }
                                
                                HasDebugInfo = true;
                            }
                            catch
                            {
                                WadTicketTitleId = "Failed to extract";
                                WadTmdTitleId = "Failed to extract";
                                WadBannerInfo = "Failed to extract";
                                WadContentSizes = "Failed to extract";
                                HasDebugInfo = false;
                            }

                            // Fetch cover art from GameTDB
                            try
                            {
                                // Extract game ID from Title ID (last 8 hex chars = 4 bytes = 4 ASCII chars)
                                // Title ID format: 0001000152535045 -> last 8 chars = 52535045 -> RSPE in ASCII
                                string gameId = "";
                                if (WadTitleId.Length >= 16)
                                {
                                    string hexGameId = WadTitleId.Substring(8); // Get last 8 hex chars (4 bytes)
                                    // Convert hex to ASCII (e.g., "52535045" -> "RSPE")
                                    for (int i = 0; i < hexGameId.Length; i += 2)
                                    {
                                        if (i + 1 < hexGameId.Length)
                                        {
                                            string hexByte = hexGameId.Substring(i, 2);
                                            if (int.TryParse(hexByte, System.Globalization.NumberStyles.HexNumber, null, out int charCode))
                                            {
                                                gameId += (char)charCode;
                                            }
                                        }
                                    }
                                }
                                
                                if (!string.IsNullOrEmpty(gameId) && gameId.Length >= 4)
                                {
                                    System.Threading.Tasks.Task.Run(async () =>
                                    {
                                        try
                                        {
                                            var cover = await _gameTDBService.Get3DCoverAsync(gameId);
                                            if (cover == null)
                                            {
                                                cover = await _gameTDBService.GetCoverArtAsync(gameId);
                                            }
                                            
                                            if (cover != null)
                                            {
                                                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                                {
                                                    WadBannerImage = cover;
                                                    HasWadBannerImage = true;
                                                });
                                            }
                                        }
                                        catch
                                        {
                                            // Cover art is non-critical
                                        }
                                    });
                                }
                            }
                            catch
                            {
                                HasWadBannerImage = false;
                            }

                            // Generate helpful description based on WAD type
                            // Parse upper Title ID from the full Title ID (first 8 hex chars)
                            string upperTitleId = WadTitleId.Length >= 8 ? WadTitleId.Substring(0, 8) : "00000000";
                            if (upperTitleId == "00010001") // Channel
                            {
                                // Check if this looks like a forwarder WAD
                                bool isForwarder = WadChannelTitle.Contains("[") || 
                                                 WadFileSize.Contains("KB") && 
                                                 fileInfo.Length < 1024 * 1024; // Less than 1MB
                                
                                if (isForwarder)
                                {
                                    WadDescription = "GAME FORWARDER CHANNEL\n\n" +
                                        "This is a forwarder channel that launches a Wii game directly from the Wii Menu. " +
                                        "When you select this channel, it will load the game specified in the Title ID without " +
                                        "needing to launch a USB loader application first.\n\n" +
                                        "The actual game must be present on your USB drive or SD card for the forwarder to work.";
                                    
                                    WadInstallInstructions = "INSTALLATION INSTRUCTIONS:\n\n" +
                                        "1. Copy this WAD file to your SD card (root or in a 'wad' folder)\n\n" +
                                        "2. Use a WAD manager (like Wii Mod Lite or YAWMM) to install it\n\n" +
                                        "3. Return to the Wii System Menu - the channel should appear\n\n" +
                                        "4. Make sure your game ISO/WBFS is on USB or SD with the correct Title ID\n\n" +
                                        "5. Launch the channel to start your game!\n\n" +
                                        "WARNING: Backup your NAND before installing any WADs!";
                                }
                                else
                                {
                                    WadDescription = "WII CHANNEL\n\n" +
                                        "This is a standard Wii channel that will appear in your Wii System Menu after installation.";
                                    
                                    WadInstallInstructions = "INSTALLATION INSTRUCTIONS:\n\n" +
                                        "1. Copy to SD card\n\n" +
                                        "2. Install using a WAD manager\n\n" +
                                        "3. Find it in your Wii Menu\n\n" +
                                        "WARNING: Always backup your NAND first!";
                                }
                                
                                HasWadDescription = true;
                                HasWadInstallInstructions = true;
                            }
                            else if (upperTitleId == "00000001")
                            {
                                WadDescription = "WARNING - SYSTEM TITLE - DANGEROUS!\n\n" +
                                    "This is a system title that modifies core Wii functionality. " +
                                    "Installing incorrect system titles can BRICK your Wii!\n\n" +
                                    "Only install if you know exactly what this does and have a NAND backup.";
                                HasWadDescription = true;
                            }
                            else if (upperTitleId == "00010002")
                            {
                                WadDescription = "SYSTEM CHANNEL\n\n" +
                                    "This is a system channel (like Wii Shop or Forecast Channel).";
                                HasWadDescription = true;
                            }
                        }
                    }
                    catch
                    {
                        // Failed to load WAD details, but validation results are still shown
                    }
                    
                    StatusText = result.IsValid ? "WAD is valid" : "WAD validation failed";
                });
            });
        }
        catch (Exception ex)
        {
            HasValidationResults = true;
            WadValidationStatus = "✗ Error";
            WadValidationStatusColor = "Red";
            HasWadErrors = true;
            WadErrors.Clear();
            WadErrors.Add("✗ " + ex.Message);
            StatusText = "Validation error";
        }
        finally
        {
            IsOperationInProgress = false;
        }
    }
    
    /// <summary>
    /// Checks if wit tool is available on the system
    /// </summary>
    private async Task CheckWitAvailabilityAsync()
    {
        WitStatusMessage = "Checking for wit tool...";
        ShowWitWarning = false;
        
        bool available = await _witService.CheckAvailabilityAsync();
        IsWitAvailable = available;
        
        if (available)
        {
            WitStatusMessage = $"✓ wit tool found: {_witService.WitPath}";
            ShowWitWarning = false;
        }
        else
        {
            WitStatusMessage = "⚠ wit tool not found - Limited banner support";
            WitInstallInstructions = _witService.GetInstallInstructions();
            ShowWitWarning = true;
        }
    }
    
    [RelayCommand]
    private void OpenWitDownloadPage()
    {
        _witService.OpenDownloadPage();
    }
    
    [RelayCommand]
    private async Task InstallWit()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            StatusText = "Auto-install is only supported on macOS. Please install manually.";
            _witService.OpenDownloadPage();
            return;
        }
        
        try
        {
            IsOperationInProgress = true;
            StatusText = "Installing wit via Homebrew...";
            
            var (success, message) = await _witService.TryAutoInstallAsync();
            
            if (success)
            {
                StatusText = "✓ wit installed successfully!";
                await CheckWitAvailabilityAsync();
            }
            else
            {
                StatusText = $"Failed to install wit: {message}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error installing wit: {ex.Message}";
        }
        finally
        {
            IsOperationInProgress = false;
        }
    }
    
    [RelayCommand]
    private Task DismissWitWarning()
    {
        ShowWitWarning = false;
        return Task.CompletedTask;
    }
    
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

internal class WbfsGameInfo
{
    public string FolderPath { get; set; } = string.Empty;
    public string WbfsFilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
