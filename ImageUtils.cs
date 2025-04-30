using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using BiggerSprayMod.gif;

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
                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
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
    
    public void LoadGifTexture(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] GIF file not found at {filePath}");
            return;
        }

        try
        {
            // Clear any existing GIF data
            ClearGifData();
            
            byte[] data = File.ReadAllBytes(filePath);
            
            using (var decoder = new Decoder(data))
            {
                var img = decoder.NextImage();
                
                if (img == null)
                {
                    _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Failed to decode GIF from {filePath}");
                    return;
                }
                
                while (img != null)
                {
                    Texture2D tex = img.CreateTexture();
                    float delay = img.Delay / 1000.0f; // Convert from milliseconds to seconds
                    
                    _plugin._gifFrames.Add(tex);
                    _plugin._gifFrameDelays.Add(delay);
                    
                    img = decoder.NextImage();
                }
                
                if (_plugin._gifFrames.Count > 0)
                {
                    _plugin._isAnimatedGif = true;
                    _plugin._cachedSprayTexture = _plugin._gifFrames[0];
                    _plugin._originalImageDimensions = new Vector2(
                        _plugin._gifFrames[0].width, 
                        _plugin._gifFrames[0].height
                    );
                    
                    _plugin._currentGifFrame = 0;
                    _plugin._gifTimeSinceLastFrame = 0f;
                    
                    _plugin.LogMessage(LogLevel.Info, 
                        $"[BiggerSprayMod] Successfully loaded GIF with {_plugin._gifFrames.Count} frames from {filePath}");
                }
                else
                {
                    _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Loaded GIF has no frames: {filePath}");
                }
            }
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error loading GIF: {ex.Message}");
            ClearGifData();
        }
    }
    
    public void ClearGifData()
    {
        // Destroy all textures to prevent memory leaks
        foreach (var texture in _plugin._gifFrames)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }
        
        _plugin._gifFrames.Clear();
        _plugin._gifFrameDelays.Clear();
        _plugin._isAnimatedGif = false;
        _plugin._currentGifFrame = 0;
        _plugin._gifTimeSinceLastFrame = 0f;
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