using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BiggerSprayMod;

public class ImageUtils
{
    private BiggerSprayMod _plugin;
    
    public ImageUtils(BiggerSprayMod plugin)
    {
        _plugin = plugin;
    }
    
    public void LoadAvailableImages()
    {
        if (!Directory.Exists(_plugin._imagesFolderPath))
        {
            Directory.CreateDirectory(_plugin._imagesFolderPath ?? _plugin.GetPluginPath());
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Created images directory at {_plugin._imagesFolderPath}");
        }

        _plugin._availableImages = Directory.GetFiles(_plugin._imagesFolderPath ?? _plugin.GetPluginPath(), "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        if (_plugin._availableImages.Count == 0)
        {
            // Create a default spray if none exists
            _plugin._sprayUtils.CreateDefaultSpray();
        }
    }
    
    public Texture2D LoadTexture(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] File not found at {filePath}");
            return null;
        }

        try
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (texture.LoadImage(fileData))
            {
                _plugin.LogMessage(LogLevel.Info,
                    $"[BiggerSprayMod] Successfully loaded texture: {filePath} ({texture.width}x{texture.height})");
                return texture;
            }
            else
            {
                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Failed to load texture from {filePath}");
                return null;
            }
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error loading texture: {ex.Message}");
            return null;
        }
    }
    
    public byte[] CompressImage(byte[] data)
    {
        using (MemoryStream compressedStream = new MemoryStream())
        {
            using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Compress))
            {
                gzip.Write(data, 0, data.Length);
            }

            return compressedStream.ToArray();
        }
    }

    public byte[] DecompressImage(byte[] compressedData)
    {
        using (MemoryStream compressedStream = new MemoryStream(compressedData))
        using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Decompress))
        using (MemoryStream resultStream = new MemoryStream())
        {
            gzip.CopyTo(resultStream);
            return resultStream.ToArray();
        }
    }
}