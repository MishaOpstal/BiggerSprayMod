using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BiggerSprayMod
{
    public class ConfigManager
    {
        private readonly BiggerSprayMod _plugin;
        
        // Config Entries
        public ConfigEntry<KeyCode> SprayKey;
        public ConfigEntry<KeyCode> PreviousSprayKey;
        public ConfigEntry<KeyCode> NextSprayKey;
        public ConfigEntry<KeyCode> ScaleKey;
        public ConfigEntry<KeyCode> IncreaseScaleKey;
        public ConfigEntry<KeyCode> DecreaseScaleKey;
        public ConfigEntry<float> SprayScale;
        public ConfigEntry<float> SprayLifetimeSeconds;
        public ConfigEntry<int> MaxSpraysAllowed;
        public ConfigEntry<string> SelectedSprayImage;
        public ConfigEntry<bool> RefreshSpraysButton;
        public ConfigEntry<Color> ScalePreviewColor;
        public ConfigEntry<float> MinScaleSize;
        public ConfigEntry<float> MaxScaleSize;
        public ConfigEntry<float> ScaleSpeed;
        public ConfigEntry<bool> OpenSprayImageFolder;
        public ConfigEntry<bool> UseScrollWheel;
        public ConfigEntry<bool> ShowSprayIfLarge;
        public ConfigEntry<bool> AnimateGifs;
        public ConfigEntry<int> GifFps;
        
        public ConfigManager(BiggerSprayMod plugin)
        {
            _plugin = plugin;
        }

        public void Initialize()
        {
            // Set up all configuration entries
            SetupConfig();
        }

        private void SetupConfig()
        {
            if (_plugin._availableImages == null || _plugin._availableImages.Count == 0)
                _plugin._availableImages = ["DefaultSpray.png"];

            SprayKey = _plugin.Config.Bind(
                "Spray Settings",
                "Spray Key",
                KeyCode.F,
                new ConfigDescription("The key used to spray.")
            );
            
            PreviousSprayKey = _plugin.Config.Bind(
                "Spray Settings",
                "Previous Spray Key",
                KeyCode.Q,
                new ConfigDescription("The key used to select the previous spray image.")
            );
            
            NextSprayKey = _plugin.Config.Bind(
                "Spray Settings",
                "Next Spray Key",
                KeyCode.E,
                new ConfigDescription("The key used to select the next spray image.")
            );
            
            ShowSprayIfLarge = _plugin.Config.Bind(
                "Spray Settings",
                "Show spray if it exceeds the size limit locally",
                true,
                new ConfigDescription("Show the spray even if the image is large (Locally).")
            );

            ScaleKey = _plugin.Config.Bind(
                "Scale Settings",
                "Scale Preview Key",
                KeyCode.LeftAlt,
                new ConfigDescription("Hold this key to preview the scale.")
            );
            
            IncreaseScaleKey = _plugin.Config.Bind(
                "Scale Settings",
                "Increase Scale Key",
                KeyCode.Equals, // + key
                new ConfigDescription("Press this key to increase the spray scale (+ key by default).")
            );
            
            DecreaseScaleKey = _plugin.Config.Bind(
                "Scale Settings",
                "Decrease Scale Key",
                KeyCode.Minus, // - key
                new ConfigDescription("Press this key to decrease the spray scale (- key by default).")
            );
            
            UseScrollWheel = _plugin.Config.Bind(
                "Scale Settings",
                "Use Scroll Wheel",
                true,
                new ConfigDescription("Enable scroll wheel to adjust scale while holding the Scale Preview Key.")
            );

            SprayScale = _plugin.Config.Bind(
                "Scale Settings",
                "Spray Scale",
                1.0f,
                new ConfigDescription(
                    "The size of the spray.",
                    new AcceptableValueRange<float>(0.1f, 5.0f)
                )
            );

            MinScaleSize = _plugin.Config.Bind(
                "Scale Settings",
                "Minimum Scale",
                0.1f,
                new ConfigDescription(
                    "The minimum allowed scale size.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)
                )
            );

            MaxScaleSize = _plugin.Config.Bind(
                "Scale Settings",
                "Maximum Scale",
                5.0f,
                new ConfigDescription(
                    "The maximum allowed scale size.",
                    new AcceptableValueRange<float>(1.0f, 10.0f)
                )
            );

            ScaleSpeed = _plugin.Config.Bind(
                "Scale Settings",
                "Scale Speed",
                0.1f,
                new ConfigDescription(
                    "How quickly the spray scales when adjusting.",
                    new AcceptableValueRange<float>(0.01f, 1.0f)
                )
            );

            ScalePreviewColor = _plugin.Config.Bind(
                "Scale Settings",
                "Scale Preview Color",
                new Color(0.0f, 1.0f, 0.0f, 0.5f), // Semi-transparent green
                new ConfigDescription("The color of the scale preview.")
            );
            
            AnimateGifs = _plugin.Config.Bind(
                "GIF Settings",
                "Animate GIFs",
                true,
                new ConfigDescription("Enable animation for GIF sprays.")
            );
            
            GifFps = _plugin.Config.Bind(
                "GIF Settings",
                "GIF FPS",
                30,
                new ConfigDescription(
                    "Frames per second for GIF animations. Higher values use more resources.",
                    new AcceptableValueRange<int>(1, 60)
                )
            );

            SprayLifetimeSeconds = _plugin.Config.Bind(
                "Host Settings",
                "Spray Lifetime (Seconds)",
                60f,
                new ConfigDescription(
                    "How long the spray should last. Set to 0 for permanent sprays.",
                    new AcceptableValueRange<float>(0f, 300f)
                )
            );

            MaxSpraysAllowed = _plugin.Config.Bind(
                "Host Settings",
                "Max Sprays",
                10,
                new ConfigDescription(
                    "Maximum number of sprays before the oldest is deleted.",
                    new AcceptableValueRange<int>(1, 100)
                )
            );

            SelectedSprayImage = _plugin.Config.Bind(
                "Spray Settings",
                "Selected Spray Image",
                _plugin._availableImages.FirstOrDefault() ?? "DefaultSpray.png",
                new ConfigDescription(
                    "The image used for spraying.",
                    new AcceptableValueList<string>(_plugin._availableImages.ToArray())
                )
            );

            SelectedSprayImage.SettingChanged += (_, _) =>
            {
                _plugin.LogMessage(LogLevel.Info, "[BiggerSprayMod] Image selection changed. Reloading texture...");
                string path = System.IO.Path.Combine(_plugin._imagesFolderPath, SelectedSprayImage.Value);
                
                // Check if this is a GIF file
                if (path.ToLower().EndsWith(".gif"))
                {
                    _plugin._imageUtils.LoadGifTexture(path);
                }
                else
                {
                    _plugin._cachedSprayTexture = _plugin._imageUtils.LoadTexture(path);
                    _plugin._imageUtils.ClearGifData();
                    
                    if (_plugin._cachedSprayTexture != null)
                    {
                        _plugin._originalImageDimensions = new Vector2(_plugin._cachedSprayTexture.width, _plugin._cachedSprayTexture.height);
                    }
                }
            };
            
            OpenSprayImageFolder = _plugin.Config.Bind(
                "Spray Settings",
                "Open Spray Image Folder",
                false,
                new ConfigDescription("Set to TRUE to open the spray image folder.")
            );
            
            OpenSprayImageFolder.SettingChanged += (_, _) =>
            {
                if (OpenSprayImageFolder.Value)
                {
                    OpenImagesFolder();
                    OpenSprayImageFolder.Value = false; // Reset to false after opening
                }
            };

            RefreshSpraysButton = _plugin.Config.Bind(
                "Spray Settings",
                "Refresh Sprays",
                false,
                new ConfigDescription("Set to TRUE to refresh the list of available sprays.")
            );
        }

        public void UpdateImageListConfig()
        {
            if (_plugin._availableImages.Count == 0)
            {
                _plugin._availableImages.Add("No Images Available");
            }

            // Rebind the selected image config with updated list
            _plugin.Config.Remove(SelectedSprayImage.Definition);
            SelectedSprayImage = _plugin.Config.Bind(
                "Spray Settings",
                "Selected Spray Image",
                _plugin._availableImages.Contains(SelectedSprayImage.Value) ? SelectedSprayImage.Value : _plugin._availableImages[0],
                new ConfigDescription(
                    "The image used for spraying.",
                    new AcceptableValueList<string>(_plugin._availableImages.ToArray())
                )
            );

            SelectedSprayImage.SettingChanged += (_, _) =>
            {
                string path = System.IO.Path.Combine(_plugin._imagesFolderPath, SelectedSprayImage.Value);
                
                // Check if this is a GIF file
                if (path.ToLower().EndsWith(".gif"))
                {
                    _plugin._imageUtils.LoadGifTexture(path);
                }
                else
                {
                    _plugin._cachedSprayTexture = _plugin._imageUtils.LoadTexture(path);
                    _plugin._imageUtils.ClearGifData();
                    
                    if (_plugin._cachedSprayTexture != null)
                    {
                        _plugin._originalImageDimensions = new Vector2(_plugin._cachedSprayTexture.width, _plugin._cachedSprayTexture.height);
                    }
                }
            };
        }

        private void OpenImagesFolder()
        {
            _plugin.LogMessage(LogLevel.Info, "[BiggerSprayMod] Opening images folder...");
            try
            {
                Application.OpenURL(_plugin._imagesFolderPath);
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error opening images folder: {ex.Message}");
            }
        }
    }
} 