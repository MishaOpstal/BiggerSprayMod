using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Networking;
using BiggerSprayMod.gif;

namespace BiggerSprayMod
{
    public class WebUtils
    {
        private BiggerSprayMod _plugin;
        private Dictionary<string, GifData> _cachedGifs = new Dictionary<string, GifData>();
        
        // Trusted domains for GIF downloads - add more as needed
        private readonly string[] _trustedDomains = 
        {
            "tenor.com", 
            "giphy.com", 
            "imgur.com", 
            "gfycat.com", 
            "media.discordapp.net", 
            "cdn.discordapp.com",
            "discord.com",
            "tenor.googleapis.com",
            "rule34.xxx"
        };
        
        public WebUtils(BiggerSprayMod plugin)
        {
            _plugin = plugin;
        }
        
        public class GifData
        {
            public List<Texture2D> Frames = new List<Texture2D>();
            public List<float> Delays = new List<float>();
            public DateTime LastAccessed = DateTime.Now;
        }
        
        public bool IsTrustedUrl(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                string host = uri.Host;
                
                return _trustedDomains.Any(domain => host.EndsWith(domain, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error validating URL: {ex.Message}");
                return false;
            }
        }
        
        public bool HasCachedGif(string url)
        {
            return _cachedGifs.ContainsKey(url);
        }
        
        public GifData GetCachedGif(string url)
        {
            if (_cachedGifs.TryGetValue(url, out GifData gifData))
            {
                // Update last accessed time
                gifData.LastAccessed = DateTime.Now;
                return gifData;
            }
            
            return null;
        }
        
        public void StartGifDownload(string url, Action<bool, GifData> callback)
        {
            // Validate URL
            if (!IsTrustedUrl(url))
            {
                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Untrusted URL: {url}");
                callback?.Invoke(false, null);
                return;
            }
            
            // Check if already cached
            if (_cachedGifs.TryGetValue(url, out GifData existingData))
            {
                existingData.LastAccessed = DateTime.Now;
                callback?.Invoke(true, existingData);
                return;
            }
            
            // Start coroutine to download GIF
            _plugin.StartCoroutine(DownloadGifCoroutine(url, callback));
        }
        
        private IEnumerator DownloadGifCoroutine(string url, Action<bool, GifData> callback)
        {
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Downloading GIF: {url}");
            
            using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
            {
                yield return webRequest.SendWebRequest();
                
                if (webRequest.isNetworkError || webRequest.isHttpError)
                {
                    _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error downloading GIF: {webRequest.error}");
                    callback?.Invoke(false, null);
                }
                else
                {
                    try
                    {
                        byte[] gifBytes = webRequest.downloadHandler.data;
                        GifData gifData = new GifData();
                        
                        // Use mgGif.Decoder to process the GIF
                        using (var decoder = new Decoder(gifBytes))
                        {
                            // Extract all frames from the GIF
                            Image img;
                            while ((img = decoder.NextImage()) != null)
                            {
                                // Create a texture from the image frame
                                Texture2D texture = img.CreateTexture();
                                gifData.Frames.Add(texture);
                                
                                // Add delay in milliseconds (Decoder provides this in the Image object)
                                gifData.Delays.Add(img.Delay / 1000f); // Convert milliseconds to seconds
                            }
                            
                            if (gifData.Frames.Count > 0)
                            {
                                // Cache the GIF data
                                _cachedGifs[url] = gifData;
                                
                                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Successfully downloaded and processed GIF with {gifData.Frames.Count} frames");
                                callback?.Invoke(true, gifData);
                            }
                            else
                            {
                                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Failed to decode downloaded GIF - no frames found");
                                callback?.Invoke(false, null);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error processing downloaded GIF: {ex.Message}");
                        callback?.Invoke(false, null);
                    }
                }
            }
        }
        
        public void CleanupOldCache()
        {
            // Remove GIFs that haven't been accessed in 30 minutes
            DateTime cutoffTime = DateTime.Now.AddMinutes(-30);
            List<string> keysToRemove = new List<string>();
            
            foreach (var pair in _cachedGifs)
            {
                if (pair.Value.LastAccessed < cutoffTime)
                {
                    keysToRemove.Add(pair.Key);
                    
                    // Clean up textures
                    foreach (var texture in pair.Value.Frames)
                    {
                        if (texture != null)
                        {
                            UnityEngine.Object.Destroy(texture);
                        }
                    }
                }
            }
            
            foreach (string key in keysToRemove)
            {
                _cachedGifs.Remove(key);
            }
            
            if (keysToRemove.Count > 0)
            {
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Cleaned up {keysToRemove.Count} old cached GIFs");
            }
        }
    }
} 