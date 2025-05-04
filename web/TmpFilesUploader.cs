using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Networking;

namespace BiggerSprayMod.web
{
    public class TmpFilesUploader
    {
        private BiggerSprayMod _plugin;
        private readonly string API_URL = "https://tmpfiles.org/api/v1/upload";
        
        // Cache for uploaded image URLs - key is the sprite name/path, value is the URL
        private Dictionary<string, string> _cachedUrls = new Dictionary<string, string>();
        // Dictionary to track upload timestamps for cache expiry
        private Dictionary<string, DateTime> _uploadTimestamps = new Dictionary<string, DateTime>();
        // Cache expiry time in minutes (set to 55 to be safe)
        private const int CACHE_EXPIRY_MINUTES = 55;
        
        // Cache for downloaded images - key is the URL, value is the texture
        private Dictionary<string, Texture2D> _downloadCache = new Dictionary<string, Texture2D>();
        // Dictionary to track download failure counts for URLs
        private Dictionary<string, int> _downloadFailCounts = new Dictionary<string, int>(); 
        // Maximum number of download failures before using default texture
        private const int MAX_DOWNLOAD_FAILURES = 3;
        // Default texture for fallback
        private Texture2D _defaultTexture;
        
        public TmpFilesUploader(BiggerSprayMod plugin)
        {
            _plugin = plugin;
            CreateDefaultTexture();
        }
        
        /// <summary>
        /// Creates a default texture for fallback when downloads fail
        /// </summary>
        private void CreateDefaultTexture()
        {
            _defaultTexture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            
            // Create a simple pattern (checkerboard)
            Color[] pixels = new Color[256 * 256];
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    bool isChecker = ((x / 32) + (y / 32)) % 2 == 0;
                    pixels[y * 256 + x] = isChecker ? new Color(0.8f, 0.2f, 0.2f, 0.8f) : new Color(0.2f, 0.2f, 0.2f, 0.8f);
                }
            }
            
            _defaultTexture.SetPixels(pixels);
            _defaultTexture.Apply();
        }
        
        /// <summary>
        /// Uploads an image to tmpfiles.org and caches the resulting URL
        /// </summary>
        /// <param name="imageName">The name/identifier of the image</param>
        /// <param name="imageData">The raw bytes of the image</param>
        /// <param name="callback">Callback with the resulting URL</param>
        public void UploadImage(string imageName, byte[] imageData, Action<bool, string> callback)
        {
            // Check if we already have a valid cached URL for this image
            if (HasValidCachedUrl(imageName))
            {
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Using cached URL for {imageName}");
                callback?.Invoke(true, _cachedUrls[imageName]);
                return;
            }
            
            _plugin.StartCoroutine(UploadImageCoroutine(imageName, imageData, callback));
        }
        
        /// <summary>
        /// Coroutine to handle the actual upload process
        /// </summary>
        private IEnumerator UploadImageCoroutine(string imageName, byte[] imageData, Action<bool, string> callback)
        {
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Uploading image {imageName} to tmpfiles.org...");
            
            // Create form with file data
            WWWForm form = new WWWForm();
            form.AddBinaryData("file", imageData, $"{imageName}.png", "image/png");
            
            // Create the request
            using (UnityWebRequest request = UnityWebRequest.Post(API_URL, form))
            {
                // Send the request and wait for response
                yield return request.SendWebRequest();
                
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Upload failed: {request.error}");
                    callback?.Invoke(false, null);
                    yield break;
                }
                
                // Parse the response - expected format is JSON with a data.url field
                string response = request.downloadHandler.text;
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Upload response: {response}");
                
                try 
                {
                    // Extract the URL using regex - try different patterns to be more robust
                    string url = null;
                    
                    // Pattern for direct URL
                    string pattern1 = "\"url\":\"(https://tmpfiles\\.org/[^\"]+)\"";
                    Match match1 = Regex.Match(response, pattern1);
                    
                    if (match1.Success && match1.Groups.Count > 1)
                    {
                        url = match1.Groups[1].Value;
                    }
                    else
                    {
                        // Alternative pattern for API response that might include a data object
                        string pattern2 = "\"data\":\\s*{[^}]*\"url\":\\s*\"(https://tmpfiles\\.org/[^\"]+)\"";
                        Match match2 = Regex.Match(response, pattern2);
                        
                        if (match2.Success && match2.Groups.Count > 1)
                        {
                            url = match2.Groups[1].Value;
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(url))
                    {
                        // the url at this point is like this: https://tmpfiles.org/27071620/shrek.png.png
                        // it needs to become: https://tmpfiles.org/dl/27071620/shrek.png.png

                        // Extract the file ID and file name from the URL
                        string[] parts = url.Split('/');
                        string fileId = parts[3];
                        string fileName = parts[4];

                        // Construct the final URL
                        url = $"https://tmpfiles.org/dl/{fileId}/{fileName}";

                        // Cache the URL with timestamp
                        _cachedUrls[imageName] = url;
                        _uploadTimestamps[imageName] = DateTime.Now;
                        
                        _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Uploaded successfully to tmpfiles.org, URL: {url}");
                        callback?.Invoke(true, url);
                    }
                    else
                    {
                        _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Could not parse tmpfiles.org upload response URL");
                        callback?.Invoke(false, null);
                    }
                }
                catch (Exception ex)
                {
                    _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error parsing upload response: {ex.Message}");
                    callback?.Invoke(false, null);
                }
            }
        }
        
        /// <summary>
        /// Checks if we have a valid (non-expired) cached URL for this image
        /// </summary>
        public bool HasValidCachedUrl(string imageName)
        {
            if (_cachedUrls.TryGetValue(imageName, out string url) &&
                _uploadTimestamps.TryGetValue(imageName, out DateTime timestamp))
            {
                // Check if the URL is still valid (less than CACHE_EXPIRY_MINUTES old)
                TimeSpan age = DateTime.Now - timestamp;
                return !string.IsNullOrEmpty(url) && age.TotalMinutes < CACHE_EXPIRY_MINUTES;
            }
            return false;
        }
        
        /// <summary>
        /// Gets a cached URL for an image if available
        /// </summary>
        public string GetCachedUrl(string imageName)
        {
            return HasValidCachedUrl(imageName) ? _cachedUrls[imageName] : null;
        }
        
        /// <summary>
        /// Clear expired cache entries
        /// </summary>
        public void CleanupExpiredCache()
        {
            List<string> keysToRemove = new List<string>();
            
            foreach (var pair in _uploadTimestamps)
            {
                TimeSpan age = DateTime.Now - pair.Value;
                if (age.TotalMinutes >= CACHE_EXPIRY_MINUTES)
                {
                    keysToRemove.Add(pair.Key);
                }
            }
            
            foreach (string key in keysToRemove)
            {
                _cachedUrls.Remove(key);
                _uploadTimestamps.Remove(key);
            }
            
            if (keysToRemove.Count > 0)
            {
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Cleaned up {keysToRemove.Count} expired URL cache entries");
            }
        }
        
        /// <summary>
        /// Get stats about the cached URLs
        /// </summary>
        public override string ToString()
        {
            int validCount = 0;
            int expiredCount = 0;
            
            foreach (var pair in _uploadTimestamps)
            {
                TimeSpan age = DateTime.Now - pair.Value;
                if (age.TotalMinutes < CACHE_EXPIRY_MINUTES)
                {
                    validCount++;
                }
                else
                {
                    expiredCount++;
                }
            }
            
            return $"TmpFilesUploader: {validCount} valid URLs, {expiredCount} expired URLs";
        }
        
        /// <summary>
        /// Downloads an image from a URL with caching, returns the default texture after multiple failures
        /// </summary>
        public IEnumerator DownloadImageWithCacheCoroutine(string url, Action<Texture2D> callback)
        {
            // Check if we already have this URL cached
            if (_downloadCache.TryGetValue(url, out Texture2D cachedTexture) && cachedTexture != null)
            {
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Using cached texture for URL: {url}");
                callback?.Invoke(cachedTexture);
                yield break;
            }
            
            // Check if this URL has failed too many times
            if (_downloadFailCounts.TryGetValue(url, out int failCount) && failCount >= MAX_DOWNLOAD_FAILURES)
            {
                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] URL has failed {failCount} times, using default texture: {url}");
                callback?.Invoke(_defaultTexture);
                yield break;
            }
            
            // Download the image
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Downloading image from URL: {url}");
            
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();
                
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Failed to download image: {request.error}");
                    
                    // Increment the failure count
                    if (!_downloadFailCounts.ContainsKey(url))
                    {
                        _downloadFailCounts[url] = 1;
                    }
                    else
                    {
                        _downloadFailCounts[url]++;
                    }
                    
                    // If we've failed too many times, use the default texture
                    if (_downloadFailCounts[url] >= MAX_DOWNLOAD_FAILURES)
                    {
                        _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Too many download failures for URL, using default texture");
                        callback?.Invoke(_defaultTexture);
                    }
                    else
                    {
                        // Just return null this time, but allow future attempts
                        callback?.Invoke(null);
                    }
                    
                    yield break;
                }
                
                try
                {
                    // Get the downloaded texture
                    Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
                    if (texture == null)
                    {
                        _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Downloaded texture is null");
                        callback?.Invoke(null);
                        yield break;
                    }
                    
                    // Cache the texture
                    _downloadCache[url] = texture;
                    
                    // Reset failure count on success
                    _downloadFailCounts.Remove(url);
                    
                    _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Successfully downloaded and cached image: {url}");
                    callback?.Invoke(texture);
                }
                catch (Exception ex)
                {
                    _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error processing downloaded image: {ex.Message}");
                    callback?.Invoke(null);
                }
            }
        }
        
        /// <summary>
        /// Cleans up the download cache to avoid memory leaks
        /// </summary>
        public void CleanupDownloadCache()
        {
            // Limit the download cache size (keep the most recent 10 textures)
            int maxCacheSize = 10;
            if (_downloadCache.Count > maxCacheSize)
            {
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Cleaning up download cache, current size: {_downloadCache.Count}");
                
                // Get keys in order to remove some
                var keys = _downloadCache.Keys.ToList();
                
                // Remove oldest entries until we're at the max size
                for (int i = 0; i < keys.Count - maxCacheSize; i++)
                {
                    string key = keys[i];
                    
                    // Destroy the texture to prevent memory leaks
                    if (_downloadCache[key] != null && _downloadCache[key] != _defaultTexture)
                    {
                        UnityEngine.Object.Destroy(_downloadCache[key]);
                    }
                    
                    _downloadCache.Remove(key);
                }
                
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Download cache cleaned, new size: {_downloadCache.Count}");
            }
            
            // Also clean up the failure counts for URLs that we haven't tried recently
            if (_downloadFailCounts.Count > maxCacheSize * 2)
            {
                var keysToRemove = _downloadFailCounts.Keys.Take(_downloadFailCounts.Count - maxCacheSize).ToList();
                foreach (var key in keysToRemove)
                {
                    _downloadFailCounts.Remove(key);
                }
            }
        }
        
        /// <summary>
        /// Clean up all textures to prevent memory leaks when the plugin is destroyed
        /// </summary>
        public void DisposeTextures()
        {
            foreach (var texture in _downloadCache.Values)
            {
                if (texture != null && texture != _defaultTexture)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
            
            _downloadCache.Clear();
            
            if (_defaultTexture != null)
            {
                UnityEngine.Object.Destroy(_defaultTexture);
                _defaultTexture = null;
            }
        }
    }
} 