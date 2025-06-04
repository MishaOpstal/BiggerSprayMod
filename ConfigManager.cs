using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Realtime;

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
        public ConfigEntry<KeyCode> ToggleGifModeKey;
        public ConfigEntry<float> SprayScale;
        public ConfigEntry<float> SprayLifetimeSeconds;
        public ConfigEntry<int> MaxSpraysAllowed;
        public ConfigEntry<string> SelectedSprayImage;
        public ConfigEntry<string> SelectedGifName;
        public ConfigEntry<bool> RefreshSpraysButton;
        public ConfigEntry<bool> RefreshGifsButton;
        public ConfigEntry<bool> OpenGifConfigFolderButton;
        public ConfigEntry<bool> OpenImagesFolderButton;
        public ConfigEntry<Color> ScalePreviewColor;
        public ConfigEntry<float> MinScaleSize;
        public ConfigEntry<float> MaxScaleSize;
        public ConfigEntry<float> ScaleSpeed;
        public ConfigEntry<bool> UseScrollWheel;
        public ConfigEntry<bool> ShowSprayIfLarge;
        public ConfigEntry<float> GifAnimationFps;
        public ConfigEntry<bool> AnimateGifsInWorld;
        public ConfigEntry<bool> myEyesOnly;
        
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

            SprayLifetimeSeconds = _plugin.Config.Bind(
                "Host Settings",
                "Spray Lifetime (Seconds)",
                60f,
                new ConfigDescription(
                    "How long the spray should last. Set to 0 for permanent sprays.",
                    new AcceptableValueRange<float>(0f, 300f)
                )
            );
            
            // Add handler for when spray lifetime is changed
            SprayLifetimeSeconds.SettingChanged += (_, _) =>
            {
                // Only hosts can control spray lifetimes
                if (Photon.Pun.PhotonNetwork.IsMasterClient && Photon.Pun.PhotonNetwork.IsConnected)
                {
                    _plugin.UpdateAllSprayLifetimes();
                }
            };

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
                _plugin._cachedSprayTexture = _plugin._imageUtils.LoadTexture(path);
                if (_plugin._cachedSprayTexture != null)
                {
                    _plugin._originalImageDimensions = new Vector2(_plugin._cachedSprayTexture.width, _plugin._cachedSprayTexture.height);
                }
            };

            RefreshSpraysButton = _plugin.Config.Bind(
                "Spray Settings",
                "Refresh Sprays",
                false,
                new ConfigDescription("Set to TRUE to refresh the list of available sprays.")
            );
            
            SprayKey = _plugin.Config.Bind(
                "Spray Settings",
                "Spray Key",
                KeyCode.F,
                new ConfigDescription("The key used to spray.")
            );
            
            PreviousSprayKey = _plugin.Config.Bind(
                "Spray Settings",
                "Previous Spray/GIF Key",
                KeyCode.LeftArrow,
                new ConfigDescription("The key used to select the previous spray image or GIF.")
            );
            
            NextSprayKey = _plugin.Config.Bind(
                "Spray Settings",
                "Next Spray/GIF Key",
                KeyCode.RightArrow,
                new ConfigDescription("The key used to select the next spray image or GIF.")
            );
            
            ShowSprayIfLarge = _plugin.Config.Bind(
                "Spray Settings",
                "Show spray if it exceeds the size limit locally",
                true,
                new ConfigDescription("Show the spray even if the image is large (Locally).")
            );
            
            OpenImagesFolderButton = _plugin.Config.Bind(
                "Spray Settings",
                "Open Images Folder",
                false,
                new ConfigDescription("Set to TRUE to open the folder containing the spray images.")
            );
            
            myEyesOnly = _plugin.Config.Bind(
                "Spray Settings",
                "My Eyes Only",
                false,
                new ConfigDescription("Enable privacy mode (Don't send sprays over network).")
            );

            ToggleGifModeKey = _plugin.Config.Bind(
                "GIF Settings",
                "Toggle GIF Mode Key",
                KeyCode.G,
                new ConfigDescription("The key used to toggle between regular spray and GIF mode.")
            );
            
            RefreshGifsButton = _plugin.Config.Bind(
                "GIF Settings",
                "Refresh GIFs",
                false,
                new ConfigDescription("Set to TRUE to refresh the list of available GIFs from configuration file.")
            );
            
            OpenGifConfigFolderButton = _plugin.Config.Bind(
                "GIF Settings",
                "Open GIF Config Folder",
                false,
                new ConfigDescription("Set to TRUE to open the folder containing the GIF configuration file.")
            );

            GifAnimationFps = _plugin.Config.Bind(
                "GIF Settings",
                "GIF Animation FPS",
                30.0f,
                new ConfigDescription(
                    "The frames per second rate for GIF animations in the world.",
                    new AcceptableValueRange<float>(1.0f, 60.0f)
                )
            );

            AnimateGifsInWorld = _plugin.Config.Bind(
                "GIF Settings",
                "Animate GIFs In World",
                true,
                new ConfigDescription("When enabled, GIF sprays will be animated in the world. Disable for performance.")
            );

            // Add a handler for the AnimateGifsInWorld setting
            AnimateGifsInWorld.SettingChanged += (_, _) =>
            {
                // When the setting changes, update all existing GIF animators
                _plugin._gifAssetManager.SetAllAnimatorsPaused(!AnimateGifsInWorld.Value);
            };
            
            // Add the GIF selection setting
            SelectedGifName = _plugin.Config.Bind(
                "GIF Settings",
                "Selected GIF",
                _plugin._gifManager.AvailableGifs.Count > 0 ? _plugin._gifManager.AvailableGifs[0] : "No GIFs Available",
                new ConfigDescription(
                    "The GIF used for spraying when in GIF mode.",
                    new AcceptableValueList<string>(_plugin._gifManager.AvailableGifs.ToArray())
                )
            );

            SelectedGifName.SettingChanged += (_, _) =>
            {
                if (_plugin._gifManager.IsGifMode && SelectedGifName.Value != _plugin._gifManager.CurrentGifName)
                {
                    _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] GIF selection changed to: {SelectedGifName.Value}");
                    _plugin._gifManager.SelectGif(SelectedGifName.Value);
                }
            };

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
                _plugin._cachedSprayTexture = _plugin._imageUtils.LoadTexture(path);
                if (_plugin._cachedSprayTexture != null)
                {
                    _plugin._originalImageDimensions = new Vector2(_plugin._cachedSprayTexture.width, _plugin._cachedSprayTexture.height);
                }
            };
        }
    }
} 