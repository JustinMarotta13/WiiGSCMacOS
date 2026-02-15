using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace WiiGSC.UI.Services;

public class GameTDBService
{
    private static readonly HttpClient _httpClient = new();
    private readonly string _cacheDirectory;

    public GameTDBService()
    {
        // Cache covers in app data directory
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _cacheDirectory = Path.Combine(appData, "WiiGSC", "Covers");
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Downloads cover art from GameTDB for the specified game ID
    /// </summary>
    /// <param name="gameId">4-6 character game ID (e.g., RMCE01)</param>
    /// <param name="coverType">Type of cover: "cover", "cover3D", "disc", "coverfull"</param>
    /// <returns>Bitmap image or null if not found</returns>
    public async Task<Bitmap?> GetCoverArtAsync(string gameId, string coverType = "cover")
    {
        if (string.IsNullOrWhiteSpace(gameId) || gameId.Length < 4)
            return null;

        try
        {
            // Extract region code from game ID (4th character: E=USA, P=Europe, J=Japan, K=Korea)
            var regionCode = gameId.Length >= 4 ? gameId[3] : 'E';
            var region = regionCode switch
            {
                'E' => "US",
                'P' => "EN", // Europe (English)
                'J' => "JA", // Japan
                'K' => "KO", // Korea
                _ => "US"
            };

            // If gameId is only 4 characters, try common suffixes (most games end in 01)
            var gameIdsToTry = new List<string>();
            if (gameId.Length == 4)
            {
                // Try common suffixes for the region
                gameIdsToTry.Add($"{gameId}01"); // Most common
                gameIdsToTry.Add($"{gameId}41"); // Alternative USA release
                gameIdsToTry.Add($"{gameId}69"); // Alternative
                gameIdsToTry.Add(gameId); // Try 4-char as last resort
            }
            else
            {
                gameIdsToTry.Add(gameId);
            }

            foreach (var tryGameId in gameIdsToTry)
            {
                // Check cache first
                var cacheKey = $"{tryGameId}_{coverType}_{region}.png";
                var cachePath = Path.Combine(_cacheDirectory, cacheKey);

                if (File.Exists(cachePath))
                {
                    try
                    {
                        return new Bitmap(cachePath);
                    }
                    catch
                    {
                        // Cache file corrupted, delete and re-download
                        File.Delete(cachePath);
                    }
                }

                // Download from GameTDB
                // URL format: https://art.gametdb.com/wii/cover/US/{GAMEID}.png
                var url = $"https://art.gametdb.com/wii/{coverType}/{region}/{tryGameId}.png";

                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    
                    // Save to cache
                    await File.WriteAllBytesAsync(cachePath, imageBytes);
                    
                    // Load and return bitmap
                    using var stream = new MemoryStream(imageBytes);
                    return new Bitmap(stream);
                }
                
                // Try alternative region if primary fails
                if (region != "US")
                {
                    url = $"https://art.gametdb.com/wii/{coverType}/US/{tryGameId}.png";
                    response = await _httpClient.GetAsync(url);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var imageBytes = await response.Content.ReadAsByteArrayAsync();
                        
                        // Save to cache with US region
                        cacheKey = $"{tryGameId}_{coverType}_US.png";
                        cachePath = Path.Combine(_cacheDirectory, cacheKey);
                        await File.WriteAllBytesAsync(cachePath, imageBytes);
                        
                        // Load and return bitmap
                        using var stream = new MemoryStream(imageBytes);
                        return new Bitmap(stream);
                    }
                }
            }

            // None of the attempts worked
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads full cover art (front + back)
    /// </summary>
    public Task<Bitmap?> GetFullCoverAsync(string gameId) => GetCoverArtAsync(gameId, "coverfull");

    /// <summary>
    /// Downloads 3D box art
    /// </summary>
    public Task<Bitmap?> Get3DCoverAsync(string gameId) => GetCoverArtAsync(gameId, "cover3D");

    /// <summary>
    /// Downloads disc art
    /// </summary>
    public Task<Bitmap?> GetDiscArtAsync(string gameId) => GetCoverArtAsync(gameId, "disc");

    /// <summary>
    /// Clears the cover art cache
    /// </summary>
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                Directory.Delete(_cacheDirectory, true);
                Directory.CreateDirectory(_cacheDirectory);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
