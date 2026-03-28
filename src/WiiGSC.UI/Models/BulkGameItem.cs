using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WiiGSC.UI.Models;

public partial class BulkGameItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    private string _discId = string.Empty;

    [ObservableProperty]
    private string _region = string.Empty;

    [ObservableProperty]
    private string _language = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private Bitmap? _coverArt;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _enableOcarina;

    public string TitleId => DiscId.Length >= 4 ? DiscId.Substring(0, 4) : "WGSC";

    /// <summary>
    /// Detect region from the 4th character of the disc ID
    /// </summary>
    public static string DetectRegion(string discId)
    {
        if (string.IsNullOrEmpty(discId) || discId.Length < 4)
            return "Unknown";

        return discId[3] switch
        {
            'E' => "NTSC-U",
            'P' => "PAL",
            'J' => "NTSC-J",
            'K' => "NTSC-K",
            'W' => "NTSC-K",  // Korean Wii
            'D' => "PAL",     // German
            'F' => "PAL",     // French
            'I' => "PAL",     // Italian
            'S' => "PAL",     // Spanish
            'H' => "PAL",     // Dutch
            'L' => "PAL",     // Japanese import to PAL
            'M' => "PAL",     // American import to PAL
            'N' => "NTSC-J",  // Japanese import
            'Q' => "NTSC-K",  // Korean with Japanese language
            'T' => "NTSC-K",  // Korean with English language
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Auto-select language based on region
    /// </summary>
    public static string DetectLanguage(string region)
    {
        return region switch
        {
            "NTSC-U" => "English",
            "PAL" => "English",
            "NTSC-J" => "Japanese",
            "NTSC-K" => "Console Default",
            _ => "Console Default"
        };
    }
}
