using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using BiggerSprayMod.gif;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;

namespace BiggerSprayMod.web
{
    public class WebUtils
    {
        private BiggerSprayMod _plugin;
        private Dictionary<string, GifData> _cachedGifs = new Dictionary<string, GifData>();
        private HashSet<string> _loadingGifs = new HashSet<string>(); // Track GIFs currently being loaded

        private readonly string[] _trustedDomains =
        [
            "tenor.com",
            "giphy.com",
            "imgur.com",
            "gfycat.com",
            "media.discordapp.net",
            "cdn.discordapp.com",
            "discord.com",
            "tenor.googleapis.com",
            "media.tenor.com",
            "c.tenor.com",
            "i.imgur.com",
            "i.gyazo.com",
            "gifcdn.com",
            "rule34.xxx"
        ];

        public WebUtils(BiggerSprayMod plugin)
        {
            _plugin = plugin;
        }

        public class GifData
        {
            public List<Texture2D> Frames = new List<Texture2D>();
            public List<float> Delays = new List<float>();
            public DateTime LastAccessed = DateTime.Now;
            public bool IsValid => Frames != null && Frames.Count > 0 && Delays != null;
            
            // Check if any frames are null or destroyed
            public bool HasNullFrames()
            {
                if (Frames == null) return true;
                return Frames.Any(f => f == null);
            }
            
            // Clean up resources
            public void Dispose()
            {
                if (Frames != null)
                {
                    foreach (var tex in Frames)
                    {
                        if (tex != null)
                        {
                            UnityEngine.Object.Destroy(tex);
                        }
                    }
                    Frames.Clear();
                }
                
                if (Delays != null)
                {
                    Delays.Clear();
                }
            }
        }

        public bool IsTrustedUrl(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                return _trustedDomains.Any(domain => uri.Host.EndsWith(domain, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] URL validation failed: {ex.Message}");
                return false;
            }
        }

        public bool HasCachedGif(string url)
        {
            // Check if it's in cache and valid
            if (_cachedGifs.TryGetValue(url, out var data))
            {
                if (data != null && data.IsValid && !data.HasNullFrames())
                {
                    return true;
                }
                
                // Invalid cache entry, remove it
                _cachedGifs.Remove(url);
                data.Dispose();
                return false;
            }
            return false;
        }

        public GifData GetCachedGif(string url)
        {
            if (_cachedGifs.TryGetValue(url, out var data))
            {
                // Update last accessed time
                data.LastAccessed = DateTime.Now;
                
                // Validate the data
                if (data.IsValid && !data.HasNullFrames())
                {
                    return data;
                }
                else
                {
                    // Remove invalid data
                    _cachedGifs.Remove(url);
                    data.Dispose();
                    return null;
                }
            }
            return null;
        }

        public void StartGifDownload(string url, Action<bool, GifData> callback)
        {
            if (!IsTrustedUrl(url))
            {
                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Blocked untrusted URL: {url}");
                callback?.Invoke(false, null);
                return;
            }

            // Check if we already have a valid cached version
            if (_cachedGifs.TryGetValue(url, out var cached))
            {
                if (cached != null && cached.IsValid && !cached.HasNullFrames())
                {
                    cached.LastAccessed = DateTime.Now;
                    callback?.Invoke(true, cached);
                    return;
                }
                else
                {
                    // Remove invalid cache entry
                    _cachedGifs.Remove(url);
                    cached.Dispose();
                }
            }
            
            // Check if already being downloaded
            if (_loadingGifs.Contains(url))
            {
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] GIF already being downloaded: {url}");
                return;
            }
            
            // Mark as being downloaded
            _loadingGifs.Add(url);

            _plugin.StartCoroutine(DownloadGifCoroutine(url, (success, data) => {
                // Remove from loading set
                _loadingGifs.Remove(url);
                callback?.Invoke(success, data);
            }));
        }

        /// <summary>
        /// Downloads an image from a URL and saves it to the specified path
        /// </summary>
        /// <returns>A coroutine that can be started</returns>
        public IEnumerator DownloadImageCoroutine(string url, string fileName, Action<bool> callback)
        {
            if (!IsTrustedUrl(url))
            {
                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Blocked untrusted URL: {url}");
                callback?.Invoke(false);
                yield break;
            }

            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Downloading image: {url}");

            using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Download error: {request.error}");
                callback?.Invoke(false);
                yield break;
            }

            try
            {
                // Get the downloaded texture
                Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
                if (texture == null)
                {
                    _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Failed to download image: Texture is null");
                    callback?.Invoke(false);
                    yield break;
                }

                // Determine if we should save as PNG (for transparency) or JPG
                byte[] imageData;
                string filePath;
                
                // Save as PNG to preserve transparency
                imageData = texture.EncodeToPNG();
                filePath = Path.Combine(_plugin._imagesFolderPath, fileName + ".png");

                // Save to file
                File.WriteAllBytes(filePath, imageData);
                
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Saved image to {filePath}");
                callback?.Invoke(true);
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error saving image: {ex.Message}");
                callback?.Invoke(false);
            }
        }

        private IEnumerator DownloadGifCoroutine(string url, Action<bool, GifData> callback)
        {
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Fetching GIF: {url}");

            using UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Download error: {request.error}");
                callback?.Invoke(false, null);
                yield break;
            }

            try
            {
                byte[] data = request.downloadHandler.data;
                
                // Check if it's actually a GIF
                if (data.Length < 3 || data[0] != 'G' || data[1] != 'I' || data[2] != 'F')
                {
                    _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Not a valid GIF file: {url}");
                    callback?.Invoke(false, null);
                    yield break;
                }
                
                GifData gifData = new GifData();

                using (var decoder = new Decoder(data))
                {
                    Image frame;
                    bool success = true;

                    try
                    {
                        while ((frame = decoder.NextImage()) != null)
                        {
                            Texture2D tex = frame.CreateTexture();
                            float delay = frame.Delay / 1000f;
                            if (delay < 0.01f) delay = 0.1f;

                            gifData.Frames.Add(tex);
                            gifData.Delays.Add(delay);
                        }
                    }
                    catch (Exception ex)
                    {
                        _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error decoding GIF frames: {ex.Message}");
                        success = false;
                    }

                    if (success && gifData.Frames.Count > 0)
                    {
                        // Successfully decoded, cache it
                        _cachedGifs[url] = gifData;
                        _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Decoded {gifData.Frames.Count} frames from GIF");
                        callback?.Invoke(true, gifData);
                    }
                    else
                    {
                        // Clean up any partial frames we decoded
                        gifData.Dispose();
                        _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] No valid frames found in downloaded GIF");
                        callback?.Invoke(false, null);
                    }
                }
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] GIF decode failed: {ex.Message}");
                callback?.Invoke(false, null);
            }
        }

        public void CleanupOldCache()
        {
            try
            {
                DateTime threshold = DateTime.Now.AddMinutes(-30);
                List<string> toRemove = new List<string>();

                foreach (var pair in _cachedGifs)
                {
                    // Remove if old or invalid
                    if (pair.Value.LastAccessed < threshold || !pair.Value.IsValid || pair.Value.HasNullFrames())
                    {
                        pair.Value.Dispose();
                        toRemove.Add(pair.Key);
                    }
                }

                foreach (var key in toRemove)
                {
                    _cachedGifs.Remove(key);
                }

                if (toRemove.Count > 0)
                {
                    _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Cleaned {toRemove.Count} expired cached GIFs");
                }
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error during cache cleanup: {ex.Message}");
            }
        }
        
        // Force cleanup all GIFs on plugin shutdown
        public void DisposeAllGifs()
        {
            foreach (var pair in _cachedGifs)
            {
                if (pair.Value != null)
                {
                    pair.Value.Dispose();
                }
            }
            _cachedGifs.Clear();
            _loadingGifs.Clear();
            _plugin.LogMessage(LogLevel.Info, "[BiggerSprayMod] Cleaned up all cached GIFs");
        }
    }
}