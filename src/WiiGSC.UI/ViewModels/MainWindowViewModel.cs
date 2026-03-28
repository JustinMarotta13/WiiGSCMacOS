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
    private Bitmap? _homebrewIconPreview;

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
    private Avalonia.Media.Imaging.Bitmap? _wadExtractedIcon;

    [ObservableProperty]
    private bool _hasWadExtractedIcon;

    [ObservableProperty]
    private bool _hasWadWarnings;
    
    [ObservableProperty]
    private ObservableCollection<string> _wadWarnings = new();
    
    [ObservableProperty]
    private bool _hasWadErrors;
    
    [ObservableProperty]
    private ObservableCollection<string> _wadErrors = new();

    // Bulk Generation
    [ObservableProperty]
    private string _bulkFolderPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<BulkGameItem> _bulkGames = new();

    [ObservableProperty]
    private bool _hasBulkGames;

    [ObservableProperty]
    private bool _isBulkGenerating;

    [ObservableProperty]
    private int _bulkTotalGames;

    [ObservableProperty]
    private int _bulkCompletedGames;

    [ObservableProperty]
    private int _bulkFailedGames;

    [ObservableProperty]
    private string _bulkProgressText = string.Empty;

    [ObservableProperty]
    private LoaderItem _bulkSelectedLoader;

    [ObservableProperty]
    private bool _bulkAllSelected = true;

    [ObservableProperty]
    private bool _bulkOcarinaAll;

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
        SelectedLoader = Loaders.FirstOrDefault(l => l.Name.Contains("SD")) ?? Loaders[0];
        BulkSelectedLoader = Loaders.FirstOrDefault(l => l.Name.Contains("SD")) ?? Loaders[0];
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
            // Also strip FAT32-unsafe chars (|, ?, *, etc.) that are valid on macOS but not on SD cards
            var fat32Unsafe = new char[] { '|', '?', '*', '<', '>', '"', '\\' };
            cleanGameName = string.Join("", cleanGameName.Split(fat32Unsafe)).Trim();
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
                witService: _witService,
                enableOcarina: EnableOcarina
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
            // Clear previous app's data before populating the new app's info
            HomebrewChannelTitle = string.Empty;
            HomebrewTitleId = string.Empty;
            HomebrewCoverPath = string.Empty;
            HomebrewAppFolder = value;
            TryAutoPopulateAppInfo(value);
        }
    }
    
    partial void OnHomebrewAppFolderChanged(string value)
    {
        UpdateCanCreateHomebrew();
    }

    partial void OnHomebrewCoverPathChanged(string value)
    {
        LoadHomebrewIconPreview(value);
    }

    private void LoadHomebrewIconPreview(string? imagePath)
    {
        if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            try
            {
                using var stream = File.OpenRead(imagePath);
                HomebrewIconPreview = new Bitmap(stream);
                return;
            }
            catch
            {
                // Ignore unreadable images
            }
        }
        HomebrewIconPreview = null;
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

            // Also strip FAT32-unsafe chars (|, ?, *, etc.) that are valid on macOS but not on SD cards
            var fat32Unsafe = new char[] { '|', '?', '*', '<', '>', '"', '\\' };
            var cleanTitle = string.Join("", HomebrewChannelTitle.Split(fat32Unsafe)).Trim();
            var sanitizedName = string.Join("_", cleanTitle.Split(Path.GetInvalidFileNameChars()));
            var outputPath = Path.Combine(outputDir, $"{sanitizedName} [{HomebrewTitleId}].wad");

            ProgressValue = 20;
            StatusText = "Generating SDSDHC forwarder DOL...";

            // Auto-detect boot.dol vs boot.elf by checking what exists in the app folder
            bool forwardToElf = false;
            if (!string.IsNullOrWhiteSpace(HomebrewAppsDirectory))
            {
                var appPath = Path.Combine(HomebrewAppsDirectory, HomebrewAppFolder);
                if (Directory.Exists(appPath))
                {
                    bool hasDol = File.Exists(Path.Combine(appPath, "boot.dol"));
                    bool hasElf = File.Exists(Path.Combine(appPath, "boot.elf"));
                    // Prefer boot.dol if both exist, use boot.elf if only elf exists
                    forwardToElf = !hasDol && hasElf;
                }
            }

            var wadService = new WadCreationService();
            string? error = await wadService.CreateHomebrewForwarderWad(
                HomebrewAppFolder,
                outputPath,
                HomebrewChannelTitle,
                HomebrewTitleId,
                forwardToElf: forwardToElf,
                imagePath: !string.IsNullOrEmpty(HomebrewCoverPath) ? HomebrewCoverPath : null);

            ProgressValue = 90;

            if (error == null)
            {
                // Validate the created WAD
                var validationResult = wadService.ValidateWad(outputPath);

                ProgressValue = 100;

                if (validationResult.IsValid)
                {
                    StatusText = $"Homebrew forwarder created: {Path.GetFileName(outputPath)}";
                    if (validationResult.Warnings.Count > 0)
                        StatusText += $" — Warnings: {string.Join("; ", validationResult.Warnings)}";
                }
                else
                {
                    StatusText = $"WAD created but validation found issues: {string.Join("; ", validationResult.Errors)}";
                }

                // Open the output directory in Finder
                if (_window != null)
                {
                    await Task.Delay(500);

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
                }
            }
            else
            {
                StatusText = $"Failed: {error}";
            }
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
            HasWadExtractedIcon = false;
            HasWadDescription = false;
            HasWadInstallInstructions = false;
            WadBannerImage = null;
            WadExtractedIcon = null;
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
                            // IMET order: 0=Japanese, 1=English, 2=German, 3=French, 4=Spanish, 5=Italian, 6=Dutch
                            if (wad.HasBanner && wad.ChannelTitles.Length > 0)
                            {
                                // Prefer English (index 1), fall back to first non-empty title
                                if (wad.ChannelTitles.Length > 1 && !string.IsNullOrEmpty(wad.ChannelTitles[1]))
                                    WadChannelTitle = wad.ChannelTitles[1];
                                else
                                    WadChannelTitle = wad.ChannelTitles.FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "(No title)";
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

                            // Try to extract icon from WAD banner
                            try
                            {
                                if (wad.HasBanner)
                                {
                                    var extractedIcon = ExtractIconFromWad(wad);
                                    if (extractedIcon != null)
                                    {
                                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                        {
                                            WadExtractedIcon = extractedIcon;
                                            HasWadExtractedIcon = true;
                                        });
                                    }
                                }
                            }
                            catch (Exception iconEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Icon extraction failed: {iconEx.Message}");
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
    private void DismissWitWarning()
    {
        ShowWitWarning = false;
    }

    // ─── Bulk Generation ───────────────────────────────────────────────

    partial void OnBulkAllSelectedChanged(bool value)
    {
        foreach (var game in BulkGames)
        {
            game.IsSelected = value;
        }
    }

    partial void OnBulkOcarinaAllChanged(bool value)
    {
        foreach (var game in BulkGames)
        {
            if (game.IsSelected)
            {
                game.EnableOcarina = value;
            }
        }
    }

    [RelayCommand]
    private async Task BrowseBulkFolder()
    {
        if (_window == null) return;

        var options = new FolderPickerOpenOptions
        {
            Title = "Select WBFS / Game Folder",
            AllowMultiple = false
        };

        var folders = await _window.StorageProvider.OpenFolderPickerAsync(options);
        if (folders.Count > 0)
        {
            BulkFolderPath = folders[0].Path.LocalPath;
            await ScanBulkFolder();
        }
    }

    [RelayCommand]
    private Task ScanBulkFolder()
    {
        BulkGames.Clear();
        HasBulkGames = false;

        if (string.IsNullOrEmpty(BulkFolderPath) || !Directory.Exists(BulkFolderPath))
        {
            StatusText = "Please select a folder first";
            return Task.CompletedTask;
        }

        StatusText = "Scanning for games...";
        IsOperationInProgress = true;

        try
        {
            var gameFiles = new List<string>();

            // Find .wbfs and .iso files in subfolders (USB loader structure)
            foreach (var dir in Directory.GetDirectories(BulkFolderPath))
            {
                gameFiles.AddRange(Directory.GetFiles(dir, "*.wbfs"));
                gameFiles.AddRange(Directory.GetFiles(dir, "*.iso"));
            }

            // Also check root for loose files
            gameFiles.AddRange(Directory.GetFiles(BulkFolderPath, "*.wbfs"));
            gameFiles.AddRange(Directory.GetFiles(BulkFolderPath, "*.iso"));

            // Deduplicate by full path and filter out macOS resource fork files (._*)
            gameFiles = gameFiles
                .Where(f => !Path.GetFileName(f).StartsWith("._"))
                .Distinct()
                .ToList();

            // Track disc IDs already added to avoid duplicate games
            var seenDiscIds = new HashSet<string>();

            foreach (var filePath in gameFiles.OrderBy(f => Path.GetFileNameWithoutExtension(f)))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                // Also check parent folder name for disc ID
                var parentName = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
                var nameToCheck = fileName;
                if (!System.Text.RegularExpressions.Regex.IsMatch(fileName, @"\[([A-Z0-9]{4,6})\]"))
                {
                    nameToCheck = parentName; // Try parent folder name
                }

                var match = System.Text.RegularExpressions.Regex.Match(
                    nameToCheck, @"(.+?)\s*\[([A-Z0-9]{4,6})\]");

                string gameName;
                string discId;

                if (match.Success)
                {
                    gameName = match.Groups[1].Value.Trim();
                    discId = match.Groups[2].Value;
                }
                else
                {
                    gameName = fileName;
                    discId = "UNKN";
                }

                // Skip duplicate disc IDs (split WBFS files, same game in multiple locations)
                if (discId != "UNKN" && !seenDiscIds.Add(discId))
                    continue;

                var region = BulkGameItem.DetectRegion(discId);
                var language = BulkGameItem.DetectLanguage(region);

                var item = new BulkGameItem
                {
                    GameName = gameName,
                    DiscId = discId,
                    Region = region,
                    Language = language,
                    FilePath = filePath,
                    IsSelected = true
                };

                BulkGames.Add(item);
            }

            HasBulkGames = BulkGames.Count > 0;
            BulkTotalGames = BulkGames.Count;
            StatusText = $"Found {BulkGames.Count} game(s)";

            // Download cover art in background
            _ = DownloadBulkCoversAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Error scanning folder: {ex.Message}";
        }
        finally
        {
            IsOperationInProgress = false;
        }

        return Task.CompletedTask;
    }

    private async Task DownloadBulkCoversAsync()
    {
        foreach (var game in BulkGames)
        {
            if (game.DiscId == "UNKN") continue;
            try
            {
                var cover = await _gameTDBService.Get3DCoverAsync(game.DiscId)
                         ?? await _gameTDBService.GetCoverArtAsync(game.DiscId);
                if (cover != null)
                {
                    game.CoverArt = cover;
                }
            }
            catch { /* non-critical */ }
        }
    }

    [RelayCommand]
    private async Task BulkGenerate()
    {
        var selected = BulkGames.Where(g => g.IsSelected && g.DiscId != "UNKN").ToList();
        if (selected.Count == 0)
        {
            StatusText = "No games selected for bulk generation";
            return;
        }

        IsBulkGenerating = true;
        IsOperationInProgress = true;
        BulkCompletedGames = 0;
        BulkFailedGames = 0;

        var outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WiiGSC Shortcuts");
        Directory.CreateDirectory(outputDir);

        var wadService = new WadCreationService();

        for (int i = 0; i < selected.Count; i++)
        {
            var game = selected[i];
            game.IsProcessing = true;
            game.Status = "Creating WAD...";
            BulkProgressText = $"Processing {i + 1} of {selected.Count}: {game.GameName}";
            ProgressValue = (double)(i) / selected.Count * 100;
            StatusText = BulkProgressText;

            try
            {
                var cleanName = game.GameName.Replace(":", "");
                var safeName = string.Join("_", cleanName.Split(Path.GetInvalidFileNameChars()));
                var outputPath = Path.Combine(outputDir, $"{safeName} [{game.DiscId}].wad");

                var result = await wadService.CreateGameShortcutWad(
                    gameFilePath: game.FilePath,
                    outputPath: outputPath,
                    channelTitle: game.GameName,
                    titleId: game.TitleId,
                    discId: game.DiscId,
                    loaderId: BulkSelectedLoader.Id,
                    witService: _witService,
                    enableOcarina: game.EnableOcarina
                );

                if (result)
                {
                    game.Status = "Done";
                    game.IsComplete = true;
                    BulkCompletedGames++;
                }
                else
                {
                    game.Status = "Failed";
                    game.HasError = true;
                    BulkFailedGames++;
                }
            }
            catch (Exception ex)
            {
                game.Status = $"Error: {ex.Message}";
                game.HasError = true;
                BulkFailedGames++;
            }
            finally
            {
                game.IsProcessing = false;
            }
        }

        ProgressValue = 100;
        BulkProgressText = $"Complete: {BulkCompletedGames} succeeded, {BulkFailedGames} failed";
        StatusText = BulkProgressText;

        // Open output folder
        if (OperatingSystem.IsMacOS())
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{outputDir}\"",
                UseShellExecute = false
            });
        }

        IsBulkGenerating = false;
        IsOperationInProgress = false;
    }

    /// <summary>
    /// Extracts the icon image from a WAD's banner content.
    /// Pipeline: outer U8 → icon.bin → strip IMD5 → LZ77 decompress → inner U8 → TPL → RGBA → Avalonia Bitmap
    /// </summary>
    private static Avalonia.Media.Imaging.Bitmap? ExtractIconFromWad(WAD wad)
    {
        if (!wad.HasBanner) return null;

        var bannerApp = wad.BannerApp;
        var strings = bannerApp.StringTable;
        var dataArrays = bannerApp.Data;

        // Find icon.bin in the outer U8
        int iconIndex = -1;
        for (int i = 0; i < strings.Length; i++)
        {
            if (strings[i].Equals("icon.bin", StringComparison.OrdinalIgnoreCase))
            {
                iconIndex = i;
                break;
            }
        }

        if (iconIndex < 0 || iconIndex >= dataArrays.Length)
            return null;

        byte[] iconBinData = dataArrays[iconIndex];
        if (iconBinData == null || iconBinData.Length < 40)
            return null;

        // Strip IMD5 header (32 bytes) and find LZ77 data
        int lz77Offset = -1;
        for (int i = 0; i < Math.Min(iconBinData.Length - 3, 0x40); i++)
        {
            if (iconBinData[i] == 'L' && iconBinData[i + 1] == 'Z' &&
                iconBinData[i + 2] == '7' && iconBinData[i + 3] == '7')
            {
                lz77Offset = i;
                break;
            }
        }

        byte[] innerU8Data;
        if (lz77Offset >= 0)
        {
            innerU8Data = Wii.Lz77.Decompress(iconBinData, lz77Offset);
        }
        else
        {
            // Not LZ77 compressed — try raw after IMD5
            innerU8Data = new byte[iconBinData.Length - 32];
            Array.Copy(iconBinData, 32, innerU8Data, 0, innerU8Data.Length);
        }

        // Parse inner U8 archive
        var innerU8 = libWiiSharp.U8.Load(innerU8Data);
        var innerStrings = innerU8.StringTable;
        var innerData = innerU8.Data;

        // Find first TPL file in the inner U8 (usually in arc/timg/)
        int tplIndex = -1;
        for (int i = 0; i < innerStrings.Length; i++)
        {
            if (innerStrings[i].EndsWith(".tpl", StringComparison.OrdinalIgnoreCase))
            {
                tplIndex = i;
                break;
            }
        }

        if (tplIndex < 0 || tplIndex >= innerData.Length)
            return null;

        byte[] tplData = innerData[tplIndex];
        if (tplData == null || tplData.Length < 16)
            return null;

        // Load TPL and extract raw RGBA data (cross-platform, no System.Drawing)
        var tpl = TPL.Load(tplData);
        byte[] rgbaData = tpl.ExtractTextureBytes(0, out int width, out int height);

        if (rgbaData == null || width <= 0 || height <= 0)
            return null;

        // Convert RGBA → BGRA for Avalonia WriteableBitmap (Bgra8888 format)
        for (int i = 0; i < rgbaData.Length; i += 4)
        {
            byte r = rgbaData[i];
            byte b = rgbaData[i + 2];
            rgbaData[i] = b;       // B
            rgbaData[i + 2] = r;   // R
        }

        var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(
            new Avalonia.PixelSize(width, height),
            new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Unpremul);

        using (var fb = bitmap.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(rgbaData, 0, fb.Address, Math.Min(rgbaData.Length, fb.RowBytes * height));
        }

        return bitmap;
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
