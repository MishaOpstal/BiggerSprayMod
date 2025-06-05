using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BiggerSprayMod.web
{
    public class GifManager
    {
        private BiggerSprayMod _plugin;
        private WebUtils _webUtils;
        
        private GifConfig _gifConfig;
        private string _gifConfigPath;
        private Texture2D _currentGifFrame;
        private int _currentGifFrameIndex = 0;
        private float _nextGifFrameTime = 0;
        private WebUtils.GifData _currentGifData;
        private string _currentGifUrl = string.Empty;
        
        private bool _isGifMode = false;
        private bool _isPlayingGif = false;
        private bool _isGifEnabled = true;
        
        // Properties
        public bool IsGifMode => _isGifMode;
        public List<string> AvailableGifs { get; private set; } = new List<string> { "No GIFs Available" };
        public string CurrentGifName { get; private set; } = string.Empty;
        
        public GifManager(BiggerSprayMod plugin, WebUtils webUtils)
        {
            _plugin = plugin;
            _webUtils = webUtils;
            _gifConfigPath = Path.Combine(BepInEx.Paths.ConfigPath, "BiggerSprayGifs.json");
        }
        
        public void InitializeGifConfig()
        {
            try
            {
                if (!File.Exists(_gifConfigPath))
                {
                    // Create a default configuration
                    _gifConfig = GifConfig.CreateDefault();
                    _gifConfig.Save(_gifConfigPath);
                    _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Created default GIF config at {_gifConfigPath}");
                }
                else
                {
                    _gifConfig = GifConfig.Load(_gifConfigPath);
                }
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Failed to initialize GIF config: {ex.Message}");
                _isGifEnabled = false;
                // Ensure we have at least one item in case of error
                AvailableGifs.Clear();
                AvailableGifs.Add("No GIFs Available");
            }
        }

        public void initializeGifList()
        {
            // Build available GIFs list
            RefreshGifList();
            
            // Make sure we always have at least one item in the list
            if (AvailableGifs.Count == 0)
            {
                AvailableGifs.Add("No GIFs Available");
            }
        }
        
        public void RefreshGifList()
        {
            if (!_isGifEnabled) return;
            
            try
            {
                _gifConfig = GifConfig.Load(_gifConfigPath);
                
                // Update the available GIFs list
                AvailableGifs.Clear();
                foreach (var gif in _gifConfig.Gifs)
                {
                    if (!string.IsNullOrEmpty(gif.Url) && 
                        (gif.Url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || 
                         _webUtils.IsTrustedUrl(gif.Url)))
                    {
                        AvailableGifs.Add(gif.Name);
                    }
                }
                
                if (AvailableGifs.Count == 0)
                {
                    AvailableGifs.Add("No GIFs Available");
                }
                
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Found {AvailableGifs.Count} valid GIFs");
                
                // Update the config list
                UpdateGifListConfig();
                
                // If we're in GIF mode, make sure the current GIF is still valid
                if (_isGifMode)
                {
                    if (!AvailableGifs.Contains(CurrentGifName) || 
                        CurrentGifName == "No GIFs Available" || 
                        CurrentGifName == "GIFs Disabled - Invalid JSON")
                    {
                        // Select the first valid GIF
                        if (AvailableGifs.Count > 0 && 
                            AvailableGifs[0] != "No GIFs Available" && 
                            AvailableGifs[0] != "GIFs Disabled - Invalid JSON")
                        {
                            SelectGif(AvailableGifs[0]);
                            
                            // Update the config to match
                            _plugin._configManager.SelectedGifName.Value = AvailableGifs[0];
                        }
                        else
                        {
                            // No valid GIFs available
                            StopCurrentGif();
                            CurrentGifName = string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Failed to refresh GIF list: {ex.Message}");
                _isGifEnabled = false;
                AvailableGifs.Clear();
                AvailableGifs.Add("GIFs Disabled - Invalid JSON");
                
                // Update the config list
                UpdateGifListConfig();
            }
        }
        
        public void ToggleGifMode()
        {
            bool previousMode = _isGifMode;
            _isGifMode = !_isGifMode;
            
            // Stop any playing GIF when switching modes
            if (previousMode)
            {
                StopCurrentGif();
                CurrentGifName = string.Empty;
            }
            
            // When switching to GIF mode, select the GIF from the config
            if (_isGifMode && !previousMode)
            {
                // Select the GIF from the config
                string configSelectedGif = _plugin._configManager.SelectedGifName.Value;
                if (!string.IsNullOrEmpty(configSelectedGif) && 
                    AvailableGifs.Contains(configSelectedGif) && 
                    configSelectedGif != "No GIFs Available" && 
                    configSelectedGif != "GIFs Disabled - Invalid JSON")
                {
                    SelectGif(configSelectedGif);
                }
                else if (AvailableGifs.Count > 0 && 
                        AvailableGifs[0] != "No GIFs Available" && 
                        AvailableGifs[0] != "GIFs Disabled - Invalid JSON")
                {
                    // Fall back to the first available GIF
                    SelectGif(AvailableGifs[0]);
                    
                    // Update the config to match
                    _plugin._configManager.SelectedGifName.Value = AvailableGifs[0];
                }
            }
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] GIF Mode: {(_isGifMode ? "On" : "Off")}");
        }
        
        public void SelectGif(string gifName)
        {
            if (!_isGifEnabled || !_isGifMode || !AvailableGifs.Contains(gifName)) return;
            
            // First stop the current GIF if it's playing
            StopCurrentGif();
            
            // Find the URL for the selected GIF
            var gifEntry = _gifConfig.Gifs.FirstOrDefault(g => g.Name == gifName);
            if (gifEntry != null)
            {
                CurrentGifName = gifName;
                _currentGifUrl = gifEntry.Url;
                StartGif(_currentGifUrl);
            }
        }
        
        public void SelectNextGif()
        {
            if (!_isGifEnabled || !_isGifMode || AvailableGifs.Count <= 1) return;
            
            int currentIndex = AvailableGifs.IndexOf(CurrentGifName);
            int nextIndex = (currentIndex + 1) % AvailableGifs.Count;
            SelectGif(AvailableGifs[nextIndex]);
        }
        
        public void SelectPreviousGif()
        {
            if (!_isGifEnabled || !_isGifMode || AvailableGifs.Count <= 1) return;
            
            int currentIndex = AvailableGifs.IndexOf(CurrentGifName);
            int prevIndex = (currentIndex - 1 + AvailableGifs.Count) % AvailableGifs.Count;
            SelectGif(AvailableGifs[prevIndex]);
        }
        
        private void StartGif(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            
            _isPlayingGif = true;
            
            // Check if we already have this GIF cached
            if (_webUtils.HasCachedGif(url))
            {
                _currentGifData = _webUtils.GetCachedGif(url);
                if (_currentGifData != null && _currentGifData.Frames.Count > 0)
                {
                    _currentGifFrameIndex = 0;
                    _currentGifFrame = _currentGifData.Frames[0];
                    _nextGifFrameTime = Time.time + _currentGifData.Delays[0];
                    _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Started GIF from cache: {url}");
                    UpdateTextureWithGif();
                    return;
                }
            }
            
            // Start downloading the GIF
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Downloading GIF: {url}");
            _webUtils.StartGifDownload(url, OnGifDownloaded);
        }
        
        private void OnGifDownloaded(bool success, WebUtils.GifData gifData)
        {
            if (!success || gifData == null || gifData.Frames.Count == 0)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Failed to download GIF: {_currentGifUrl}");
                _isPlayingGif = false;
                return;
            }
            
            _currentGifData = gifData;
            _currentGifFrameIndex = 0;
            _currentGifFrame = _currentGifData.Frames[0];
            _nextGifFrameTime = Time.time + _currentGifData.Delays[0];
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Downloaded GIF with {gifData.Frames.Count} frames");
            UpdateTextureWithGif();
        }
        
        private void StopCurrentGif()
        {
            _isPlayingGif = false;
            _currentGifFrame = null;
            _currentGifData = null;
            _currentGifUrl = string.Empty;
        }
        
        public void Update()
        {
            // Make sure we have all the required elements before proceeding
            if (!_isGifEnabled || !_isGifMode || !_isPlayingGif || _currentGifData == null) return;
            
            // Check if we have valid frames to display
            if (_currentGifData.Frames == null || _currentGifData.Frames.Count == 0 || _currentGifData.Delays == null || _currentGifData.Delays.Count == 0)
            {
                // The GIF data has been corrupted or cleared, stop playing
                StopCurrentGif();
                return;
            }
            
            if (Time.time >= _nextGifFrameTime)
            {
                // Make sure we don't exceed bounds of frames collection
                if (_currentGifFrameIndex >= _currentGifData.Frames.Count) 
                {
                    _currentGifFrameIndex = 0;
                }
                
                // Advance to the next frame
                _currentGifFrameIndex = (_currentGifFrameIndex + 1) % _currentGifData.Frames.Count;
                
                // Check again to ensure we're not getting a destroyed texture
                if (_currentGifData.Frames[_currentGifFrameIndex] == null) 
                {
                    StopCurrentGif();
                    return;
                }
                
                // Safely get delay time, providing a default if delay is 0
                float delay = 0.033f; // Default ~ 30fps
                if (_currentGifFrameIndex < _currentGifData.Delays.Count)
                {
                    delay = _currentGifData.Delays[_currentGifFrameIndex];
                    // Check for zero or invalid delay
                    if (delay <= 0.001f) delay = 0.033f;
                }
                
                _currentGifFrame = _currentGifData.Frames[_currentGifFrameIndex];
                _nextGifFrameTime = Time.time + delay;
                
                UpdateTextureWithGif();
            }
        }
        
        private void UpdateTextureWithGif()
        {
            if (_currentGifFrame != null)
            {
                _plugin._cachedSprayTexture = _currentGifFrame;
                _plugin._originalImageDimensions = new Vector2(_currentGifFrame.width, _currentGifFrame.height);
            }
        }
        
        public void OpenGifConfigFolder()
        {
            try
            {
                string directory = Path.GetDirectoryName(_gifConfigPath);
                if (Directory.Exists(directory))
                {
                    Application.OpenURL(directory);
                    _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Opened GIF config folder: {directory}");
                }
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Failed to open GIF config folder: {ex.Message}");
            }
        }
        
        public string GetGifUrlByName(string name)
        {
            if (!_isGifEnabled) return string.Empty;
            
            var gifEntry = _gifConfig.Gifs.FirstOrDefault(g => g.Name == name);
            return gifEntry?.Url ?? string.Empty;
        }
        
        public void UpdateGifListConfig()
        {
            try
            {
                // Check if plugin and config manager are initialized
                if (_plugin == null || _plugin._configManager == null)
                {
                    _plugin?.LogMessage(LogLevel.Error, "[BiggerSprayMod] Cannot update GIF list config: Plugin or ConfigManager is null");
                    return;
                }

                // Check if the AvailableGifs list is initialized
                if (AvailableGifs == null)
                {
                    AvailableGifs = new List<string> { "No GIFs Available" };
                }

                // Check if the config and SelectedGifName are initialized
                if (_plugin.Config == null || _plugin._configManager.SelectedGifName == null)
                {
                    _plugin.LogMessage(LogLevel.Error, "[BiggerSprayMod] Cannot update GIF list config: Config or SelectedGifName is null");
                    return;
                }

                // Remove the old config entry
                _plugin.Config.Remove(_plugin._configManager.SelectedGifName.Definition);
                
                // Get the current selection or default to the first item
                string selectedGif = _plugin._configManager.SelectedGifName.Value;
                if (string.IsNullOrEmpty(selectedGif) || !AvailableGifs.Contains(selectedGif))
                {
                    selectedGif = AvailableGifs.Count > 0 ? AvailableGifs[0] : "No GIFs Available";
                }
                
                // Create a new config entry
                _plugin._configManager.SelectedGifName = _plugin.Config.Bind(
                    "GIF Settings",
                    "Selected GIF",
                    selectedGif,
                    new ConfigDescription(
                        "The GIF used for spraying when in GIF mode.",
                        new AcceptableValueList<string>(AvailableGifs.ToArray())
                    )
                );
                
                // Re-attach the setting changed event
                if (_plugin._configManager.SelectedGifName == null) return;
                
                _plugin._configManager.SelectedGifName.SettingChanged += (_, _) =>
                {
                    if (!_isGifMode ||
                        _plugin._configManager.SelectedGifName.Value == CurrentGifName) return;
                    
                    _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] GIF selection changed to: {_plugin._configManager.SelectedGifName.Value}");
                    SelectGif(_plugin._configManager.SelectedGifName.Value);
                };

                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Updated GIF selection list with {AvailableGifs.Count} entries");
            }
            catch (Exception ex)
            {
                _plugin?.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error updating GIF list config: {ex.Message}");
            }
        }
    }
}