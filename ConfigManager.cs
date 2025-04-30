using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using System.IO;
using BepInEx;

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
        public ConfigEntry<string> GifUrlsJson;
        public ConfigEntry<string> SelectedWebGif;
        public ConfigEntry<KeyCode> CycleGifTypeKey;
        public ConfigEntry<bool> UseWebGifs;
        public ConfigEntry<bool> OpenWebGifFolder;
        public ConfigEntry<bool> RefreshWebGifsButton;
        
        // Parsed GIF entries
        public List<GifEntry> WebGifEntries = new List<GifEntry>();
        private string _gifJsonFilePath;
        
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
                KeyCode.LeftArrow,
                new ConfigDescription("The key used to select the previous spray image.")
            );
            
            NextSprayKey = _plugin.Config.Bind(
                "Spray Settings",
                "Next Spray Key",
                KeyCode.RightArrow,
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

            CycleGifTypeKey = _plugin.Config.Bind(
                "GIF Settings",
                "Cycle GIF Type Key",
                KeyCode.G,
                new ConfigDescription("Press this key to cycle between local GIFs and web GIFs.")
            );
            
            UseWebGifs = _plugin.Config.Bind(
                "GIF Settings",
                "Use Web GIFs",
                false,
                new ConfigDescription("Enable using GIFs from web URLs instead of local files.")
            );
            
            OpenWebGifFolder = _plugin.Config.Bind(
                "GIF Settings",
                "Open Web GIF Folder",
                false,
                new ConfigDescription("Set to TRUE to open the Web GIF configuration folder.")
            );
            
            OpenWebGifFolder.SettingChanged += (_, _) =>
            {
                if (OpenWebGifFolder.Value)
                {
                    OpenWebGifsFolder();
                    OpenWebGifFolder.Value = false; // Reset to false after opening
                }
            };
            
            RefreshWebGifsButton = _plugin.Config.Bind(
                "GIF Settings",
                "Refresh Web GIFs",
                false,
                new ConfigDescription("Set to TRUE to refresh the list of Web GIFs.")
            );
            
            RefreshWebGifsButton.SettingChanged += (_, _) =>
            {
                if (RefreshWebGifsButton.Value)
                {
                    LoadGifEntriesFromFile();
                    RefreshWebGifsButton.Value = false; // Reset to false after refreshing
                }
            };
            
            // The JSON stored in the config is kept for backward compatibility but isn't used anymore
            GifUrlsJson = _plugin.Config.Bind(
                "GIF Settings (Legacy)",
                "GIF URLs (JSON)",
                "[]",
                new ConfigDescription("This setting is deprecated. GIFs are now stored in an external file for easier editing.")
            );
            
            SelectedWebGif = _plugin.Config.Bind(
                "GIF Settings",
                "Selected Web GIF",
                "Party Parrot",
                new ConfigDescription("The currently selected web GIF to use.")
            );
            
            // Set up the GIF JSON file path
            string gifConfigFolder = Path.Combine(BepInEx.Paths.ConfigPath, "BiggerSprayGifs");
            if (!Directory.Exists(gifConfigFolder))
            {
                Directory.CreateDirectory(gifConfigFolder);
            }
            _gifJsonFilePath = Path.Combine(gifConfigFolder, "webgifs.json");
            
            // Initialize default GIFs file if it doesn't exist
            if (!File.Exists(_gifJsonFilePath))
            {
                CreateDefaultGifJsonFile();
            }
            
            // Load the GIF entries
            LoadGifEntriesFromFile();
        }

        private void CreateDefaultGifJsonFile()
        {
            try
            {
                var defaultGifs = new List<GifEntry>
                {
                    new GifEntry("Party Parrot", "https://media.tenor.com/GdulfMz1EgAAAAAd/party-parrot.gif"),
                    new GifEntry("Deal With It", "https://media.tenor.com/jCk4BeXlOVQAAAAd/deal-with-it-sunglasses.gif"),
                    new GifEntry("Supa Hot", "https://media.tenor.com/cH1mlP9UQp8AAAAd/boom-roasted.gif"),
                    new GifEntry("Mind Blown", "https://media.tenor.com/Aqu6oXkbhQEAAAAd/mind-blown-keanu-reeves.gif")
                };
                
                string json = JsonConvert.SerializeObject(defaultGifs, Formatting.Indented);
                File.WriteAllText(_gifJsonFilePath, json);
                
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Created default Web GIF configuration at {_gifJsonFilePath}");
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error creating default GIF JSON file: {ex.Message}");
            }
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

        private void OpenWebGifsFolder()
        {
            string folderPath = Path.GetDirectoryName(_gifJsonFilePath);
            
            _plugin.LogMessage(LogLevel.Info, "[BiggerSprayMod] Opening Web GIFs folder...");
            try
            {
                Application.OpenURL(folderPath);
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error opening Web GIFs folder: {ex.Message}");
            }
        }

        private void LoadGifEntriesFromFile()
        {
            try
            {
                WebGifEntries.Clear();
                
                if (!File.Exists(_gifJsonFilePath))
                {
                    _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] GIF JSON file not found at {_gifJsonFilePath}");
                    CreateDefaultGifJsonFile();
                }
                
                string json = File.ReadAllText(_gifJsonFilePath);
                var entries = JsonConvert.DeserializeObject<List<GifEntry>>(json);
                
                if (entries != null)
                {
                    // If WebUtils is not yet initialized, just add all entries
                    if (_plugin._webUtils == null)
                    {
                        WebGifEntries.AddRange(entries);
                        _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Loaded {WebGifEntries.Count} web GIF entries (URL validation will be done later)");
                    }
                    else
                    {
                        // Only add entries with trusted URLs
                        foreach (var entry in entries)
                        {
                            if (_plugin._webUtils.IsTrustedUrl(entry.Url))
                            {
                                WebGifEntries.Add(entry);
                            }
                            else
                            {
                                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Skipping untrusted URL: {entry.Url}");
                            }
                        }
                        
                        _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Loaded {WebGifEntries.Count} web GIF entries");
                    }
                    
                    // Update GIF selection dropdown
                    UpdateWebGifSelectionDropdown();
                    
                    // Update the selected web GIF entry
                    UpdateSelectedWebGifEntry();
                }
            }
            catch (Exception ex)
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error loading GIF entries from file: {ex.Message}");
                
                // Create default entries if parse failed
                WebGifEntries = new List<GifEntry>
                {
                    new GifEntry("Party Parrot", "https://media.tenor.com/GdulfMz1EgAAAAAd/party-parrot.gif")
                };
                
                // Also recreate the default file
                CreateDefaultGifJsonFile();
            }
        }
        
        private void UpdateWebGifSelectionDropdown()
        {
            if (WebGifEntries.Count == 0)
                return;
                
            // Get all GIF names for the dropdown
            string[] gifNames = WebGifEntries.Select(entry => entry.Name).ToArray();
            
            // Remove existing entry and recreate it with the updated list
            _plugin.Config.Remove(SelectedWebGif.Definition);
            SelectedWebGif = _plugin.Config.Bind(
                "GIF Settings",
                "Selected Web GIF",
                WebGifEntries.FirstOrDefault()?.Name ?? "Party Parrot",
                new ConfigDescription(
                    "The web GIF to use.",
                    new AcceptableValueList<string>(gifNames)
                )
            );
            
            // Re-add the setting changed event
            SelectedWebGif.SettingChanged += (_, _) =>
            {
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Selected web GIF changed to: {SelectedWebGif.Value}");
                // If we're using web GIFs, trigger a reload
                if (_plugin._configManager.UseWebGifs.Value && _plugin._inputManager != null)
                {
                    _plugin._inputManager.TryLoadWebGif();
                }
            };
        }
        
        public void UpdateSelectedWebGifEntry()
        {
            // Make sure we have at least one entry
            if (WebGifEntries.Count == 0)
            {
                _plugin.LogMessage(LogLevel.Warning, "[BiggerSprayMod] No valid web GIF entries found");
                return;
            }
            
            // Check if the selected entry exists
            var selectedEntry = WebGifEntries.FirstOrDefault(e => 
                e.Name.Equals(SelectedWebGif.Value, StringComparison.OrdinalIgnoreCase));
            
            if (selectedEntry == null)
            {
                // If not found, use the first entry
                SelectedWebGif.Value = WebGifEntries[0].Name;
            }
        }
        
        public GifEntry GetSelectedWebGifEntry()
        {
            if (WebGifEntries.Count == 0)
                return null;
                
            var entry = WebGifEntries.FirstOrDefault(e => 
                e.Name.Equals(SelectedWebGif.Value, StringComparison.OrdinalIgnoreCase));
                
            return entry ?? WebGifEntries[0];
        }
        
        public void SelectNextWebGif()
        {
            if (WebGifEntries.Count == 0)
                return;
                
            int currentIndex = -1;
            for (int i = 0; i < WebGifEntries.Count; i++)
            {
                if (WebGifEntries[i].Name.Equals(SelectedWebGif.Value, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }
            
            int nextIndex = (currentIndex + 1) % WebGifEntries.Count;
            SelectedWebGif.Value = WebGifEntries[nextIndex].Name;
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Selected web GIF: {SelectedWebGif.Value}");
        }
        
        public void SelectPreviousWebGif()
        {
            if (WebGifEntries.Count == 0)
                return;
                
            int currentIndex = -1;
            for (int i = 0; i < WebGifEntries.Count; i++)
            {
                if (WebGifEntries[i].Name.Equals(SelectedWebGif.Value, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }
            
            int prevIndex = (currentIndex - 1 + WebGifEntries.Count) % WebGifEntries.Count;
            SelectedWebGif.Value = WebGifEntries[prevIndex].Name;
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Selected web GIF: {SelectedWebGif.Value}");
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

        // For backward compatibility
        private void LoadGifEntriesFromJson()
        {
            // This method is kept for backward compatibility
            // Actually load from file now
            LoadGifEntriesFromFile();
        }
    }
} 