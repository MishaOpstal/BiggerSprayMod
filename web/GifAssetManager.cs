using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using BiggerSprayMod.gif;
using UnityEngine;
using UnityEngine.Networking;

namespace BiggerSprayMod.web
{
    /// <summary>
    /// Central manager for all GIF assets, ensuring proper caching and reference tracking
    /// </summary>
    public class GifAssetManager
    {
        private BiggerSprayMod _plugin;
        private WebUtils _webUtils;
        
        // Core cache structure for GIF assets
        private Dictionary<string, GifAsset> _gifAssets = new Dictionary<string, GifAsset>();
        
        // Track all active sprite animators by URL for bulk updates
        private Dictionary<string, List<GifSpriteAnimator>> _activeAnimatorsByUrl = new Dictionary<string, List<GifSpriteAnimator>>();
        
        // Default texture to use as fallback
        private Texture2D _defaultTexture;
        
        public GifAssetManager(BiggerSprayMod plugin, WebUtils webUtils)
        {
            _plugin = plugin;
            _webUtils = webUtils;
            
            // Create a simple default texture for fallback
            _defaultTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            
            Color[] colors = new Color[64 * 64];
            for (int i = 0; i < colors.Length; i++)
            {
                // Create a checkerboard pattern
                int x = i % 64;
                int y = i / 64;
                bool isEven = ((x / 8) + (y / 8)) % 2 == 0;
                colors[i] = isEven ? new Color(1, 0, 1, 1) : new Color(0, 0, 0, 1); // Purple and black checkerboard
            }
            
            _defaultTexture.SetPixels(colors);
            _defaultTexture.Apply();
            
            _plugin.LogMessage(LogLevel.Info, "[BiggerSprayMod] GIF Asset Manager initialized");
        }
        
        /// <summary>
        /// Represents a single GIF asset, managed centrally
        /// </summary>
        public class GifAsset
        {
            // Asset data
            public string Url { get; private set; }
            public List<Texture2D> Frames { get; private set; } = new List<Texture2D>();
            public List<float> Delays { get; private set; } = new List<float>();
            
            // State tracking
            public bool IsLoading { get; set; } = false;
            public bool IsValid { get; set; } = false;
            public bool HasFailedLoading { get; set; } = false;
            public DateTime LastAccessed { get; set; } = DateTime.Now;
            public int FailedAttempts { get; set; } = 0;
            
            public GifAsset(string url)
            {
                Url = url;
            }
            
            public void SetData(List<Texture2D> frames, List<float> delays)
            {
                Frames = frames;
                Delays = delays;
                IsValid = Frames != null && Frames.Count > 0 && !Frames.Any(f => f == null);
                IsLoading = false;
                LastAccessed = DateTime.Now;
            }
            
            public void Invalidate()
            {
                IsValid = false;
            }
            
            public void MarkAsAccessed()
            {
                LastAccessed = DateTime.Now;
            }
            
            public void Clear()
            {
                if (Frames != null)
                {
                    foreach (var frame in Frames)
                    {
                        if (frame != null)
                        {
                            UnityEngine.Object.Destroy(frame);
                        }
                    }
                    Frames.Clear();
                }
                
                if (Delays != null)
                {
                    Delays.Clear();
                }
                
                IsValid = false;
                IsLoading = false;
            }
        }
        
        /// <summary>
        /// Register a sprite animator with a specific URL for tracking
        /// </summary>
        public void RegisterAnimator(string url, GifSpriteAnimator animator)
        {
            if (string.IsNullOrEmpty(url) || animator == null) return;
            
            if (!_activeAnimatorsByUrl.TryGetValue(url, out var animators))
            {
                animators = new List<GifSpriteAnimator>();
                _activeAnimatorsByUrl[url] = animators;
            }
            
            if (!animators.Contains(animator))
            {
                animators.Add(animator);
            }
        }
        
        /// <summary>
        /// Unregister a sprite animator when it's destroyed
        /// </summary>
        public void UnregisterAnimator(string url, GifSpriteAnimator animator)
        {
            if (string.IsNullOrEmpty(url) || !_activeAnimatorsByUrl.TryGetValue(url, out var animators)) return;
            
            animators.Remove(animator);
            
            // Clean up the list if empty
            if (animators.Count == 0)
            {
                _activeAnimatorsByUrl.Remove(url);
            }
        }
        
        /// <summary>
        /// Get or load a GIF asset by URL
        /// </summary>
        public void GetOrLoadGifAsset(string url, Action<bool, GifAsset> callback)
        {
            if (string.IsNullOrEmpty(url))
            {
                callback?.Invoke(false, null);
                return;
            }
            
            // Check if we already have this asset cached
            if (_gifAssets.TryGetValue(url, out var asset))
            {
                // If it's already loading, don't start another load
                if (asset.IsLoading)
                {
                    // Just return for now, the callback will be handled when loading completes
                    return;
                }
                
                // Check if asset is valid
                if (asset.IsValid)
                {
                    asset.MarkAsAccessed();
                    callback?.Invoke(true, asset);
                    return;
                }
                
                // Asset exists but is invalid, check if we should retry
                if (asset.HasFailedLoading && asset.FailedAttempts >= 3)
                {
                    // Too many failures, return the default texture
                    _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] GIF has failed loading too many times: {url}");
                    callback?.Invoke(false, null);
                    return;
                }
                
                // Asset is invalid but we can retry
                asset.IsLoading = true;
                asset.FailedAttempts++;
                
                // Clear previous data
                asset.Clear();
            }
            else
            {
                // Create a new asset
                asset = new GifAsset(url);
                asset.IsLoading = true;
                _gifAssets[url] = asset;
            }
            
            // Start loading the GIF
            _webUtils.StartGifDownload(url, (success, gifData) => {
                asset.IsLoading = false;
                
                if (success && gifData != null && gifData.Frames != null && gifData.Frames.Count > 0)
                {
                    // Successfully loaded
                    asset.SetData(gifData.Frames, gifData.Delays);
                    asset.HasFailedLoading = false;
                    asset.FailedAttempts = 0;
                    
                    // Update all active animators using this asset
                    UpdateAllAnimatorsUsingAsset(asset);
                    
                    callback?.Invoke(true, asset);
                }
                else
                {
                    // Failed to load
                    asset.HasFailedLoading = true;
                    
                    // If we've failed too many times, use default texture
                    if (asset.FailedAttempts >= 3)
                    {
                        UseDefaultTextureForUrl(url);
                    }
                    
                    callback?.Invoke(false, null);
                }
            });
        }
        
        /// <summary>
        /// Update all sprite animators using a specific asset
        /// </summary>
        private void UpdateAllAnimatorsUsingAsset(GifAsset asset)
        {
            if (asset == null || string.IsNullOrEmpty(asset.Url)) return;
            
            if (_activeAnimatorsByUrl.TryGetValue(asset.Url, out var animators))
            {
                // Go through each animator and update it
                for (int i = animators.Count - 1; i >= 0; i--)
                {
                    var animator = animators[i];
                    if (animator != null)
                    {
                        animator.RefreshWithAsset(asset.Frames, asset.Delays);
                    }
                    else
                    {
                        // Remove null references
                        animators.RemoveAt(i);
                    }
                }
                
                // Clean up if list is empty
                if (animators.Count == 0)
                {
                    _activeAnimatorsByUrl.Remove(asset.Url);
                }
                
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Updated {animators.Count} animators for GIF: {asset.Url}");
            }
        }
        
        /// <summary>
        /// Use default texture for all sprite animators of a URL
        /// </summary>
        private void UseDefaultTextureForUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            
            if (_activeAnimatorsByUrl.TryGetValue(url, out var animators))
            {
                // Create default single-frame asset
                List<Texture2D> defaultFrames = new List<Texture2D> { _defaultTexture };
                List<float> defaultDelays = new List<float> { 0.1f };
                
                // Update all animators
                for (int i = animators.Count - 1; i >= 0; i--)
                {
                    var animator = animators[i];
                    if (animator != null)
                    {
                        animator.RefreshWithAsset(defaultFrames, defaultDelays);
                    }
                    else
                    {
                        animators.RemoveAt(i);
                    }
                }
                
                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Applied default texture to {animators.Count} animators for failed GIF: {url}");
            }
        }
        
        /// <summary>
        /// Clean up old assets that haven't been accessed for a while
        /// </summary>
        public void CleanupOldAssets()
        {
            try
            {
                DateTime threshold = DateTime.Now.AddMinutes(-30);
                List<string> keysToRemove = new List<string>();
                
                foreach (var pair in _gifAssets)
                {
                    var asset = pair.Value;
                    
                    // Don't remove assets that still have active animators
                    if (_activeAnimatorsByUrl.ContainsKey(asset.Url) && 
                        _activeAnimatorsByUrl[asset.Url].Count > 0)
                    {
                        continue;
                    }
                    
                    // Remove old assets
                    if (asset.LastAccessed < threshold)
                    {
                        asset.Clear();
                        keysToRemove.Add(pair.Key);
                    }
                }
                
                // Remove from dictionary
                foreach (var key in keysToRemove)
                {
                    _gifAssets.Remove(key);
                }
                
                if (keysToRemove.Count > 0)
                {
                    _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Cleaned up {keysToRemove.Count} unused GIF assets");
                }
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error cleaning up GIF assets: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clean up all resources
        /// </summary>
        public void Dispose()
        {
            foreach (var pair in _gifAssets)
            {
                pair.Value.Clear();
            }
            
            _gifAssets.Clear();
            _activeAnimatorsByUrl.Clear();
            
            if (_defaultTexture != null)
            {
                UnityEngine.Object.Destroy(_defaultTexture);
                _defaultTexture = null;
            }
            
            _plugin.LogMessage(LogLevel.Info, "[BiggerSprayMod] GIF Asset Manager disposed");
        }

        /// <summary>
        /// Set the paused state of all active GIF animators
        /// </summary>
        public void SetAllAnimatorsPaused(bool isPaused)
        {
            int count = 0;
            
            // Go through all registered animators and set their paused state
            foreach (var pair in _activeAnimatorsByUrl)
            {
                foreach (var animator in pair.Value)
                {
                    if (animator != null)
                    {
                        animator.SetPaused(isPaused);
                        count++;
                    }
                }
            }
            
            _plugin.LogMessage(LogLevel.Info, 
                $"[BiggerSprayMod] {(isPaused ? "Paused" : "Resumed")} {count} GIF animators");
        }
    }
} 