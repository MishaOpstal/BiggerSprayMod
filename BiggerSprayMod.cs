using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace BiggerSprayMod;

[BepInPlugin("MishaOpstal.BigSprayMod", "Bigger Spray Mod", "1.3.0")]
[BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
public class BiggerSprayMod : BaseUnityPlugin, IOnEventCallback
{
    // Constants
    private const byte SprayEventCode = 42;
    private const byte SettingsRequestEventCode = 43;
    private const byte SettingsResponseEventCode = 44;

    // Config Entries
    private ConfigEntry<KeyCode> _sprayKey;
    private ConfigEntry<KeyCode> _previousSprayKey;
    private ConfigEntry<KeyCode> _nextSprayKey;
    private ConfigEntry<KeyCode> _scaleKey;
    private ConfigEntry<KeyCode> _increaseScaleKey;
    private ConfigEntry<KeyCode> _decreaseScaleKey;
    private ConfigEntry<float> _sprayScale;
    private ConfigEntry<float> _sprayLifetimeSeconds;
    private ConfigEntry<int> _maxSpraysAllowed;
    private ConfigEntry<string> _selectedSprayImage;
    private ConfigEntry<bool> _refreshSpraysButton;
    private ConfigEntry<Color> _scalePreviewColor;
    private ConfigEntry<float> _minScaleSize;
    private ConfigEntry<float> _maxScaleSize;
    private ConfigEntry<float> _scaleSpeed;
    private ConfigEntry<bool> _useScrollWheel;
    private ConfigEntry<bool> _showSprayIfLarge;

    // Runtime
    private float _lastSprayTime = -999f;
    private const float CooldownTime = 0.5f; // Half-second between sprays
    private Texture2D? _cachedSprayTexture;
    private List<string> _availableImages = [];
    private string? _imagesFolderPath;
    private readonly List<GameObject> _spawnedSprays = [];

    // Network Settings (received from host)
    private float _hostSprayLifetime = 60f;
    private int _hostMaxSprays = 10;
    private bool _registeredCallbacks;

    // Scale Preview
    private bool _isScaling = false;
    private float _currentScalePreview;
    private GameObject _scalePreviewObject;
    private Vector2 _originalImageDimensions = new Vector2(1, 1);

    // Static instance for easy access
    public static BiggerSprayMod Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        // Set up paths
        _imagesFolderPath = Path.Combine(Paths.ConfigPath, "BiggerSprayImages");

        // Initialize in correct order
        LoadAvailableImages();
        SetupConfig();
        CreateSprayPrefabs();

        string preloadPath = Path.Combine(_imagesFolderPath, _selectedSprayImage.Value);
        _cachedSprayTexture = LoadTexture(preloadPath);
        if (_cachedSprayTexture != null)
        {
            _originalImageDimensions = new Vector2(_cachedSprayTexture.width, _cachedSprayTexture.height);
        }

        Logger.LogInfo("[BiggerSprayMod] Initialized successfully.");
    }

    private void OnEnable()
    {
        // Register for Photon events when plugin is enabled
        PhotonNetwork.AddCallbackTarget(this);
        _registeredCallbacks = true;
    }

    private void OnDisable()
    {
        // Unregister when disabled
        if (_registeredCallbacks)
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            _registeredCallbacks = false;
        }
    }

    private Material _sprayMaterialTemplate;
    private Material _previewMaterialTemplate;

    private void CreateSprayPrefabs()
    {
        Logger.LogInfo("[BiggerSprayMod] Creating spray materials...");

        // Try finding a build-in shader that works well with transparency
        Shader sprayShader = Shader.Find("Sprites/Default");
        if (sprayShader == null)
        {
            // Fall back to other common shaders
            sprayShader = Shader.Find("Unlit/Texture");

            if (sprayShader == null)
            {
                Logger.LogError("[BiggerSprayMod] Failed to find suitable shader. Sprays may not appear correctly.");
                return;
            }
        }

        _sprayMaterialTemplate = new Material(sprayShader)
        {
            mainTexture = Texture2D.whiteTexture // Default texture
        };

        // Create the preview material (semi-transparent)
        _previewMaterialTemplate = new Material(sprayShader)
        {
            mainTexture = Texture2D.whiteTexture, // Default texture
            color = _scalePreviewColor.Value
        };

        Logger.LogInfo("[BiggerSprayMod] Spray materials ready.");
    }

    private void SetupConfig()
    {
        if (_availableImages == null || _availableImages.Count == 0)
            _availableImages = ["DefaultSpray.png"];

        _sprayKey = Config.Bind(
            "Spray Settings",
            "Spray Key",
            KeyCode.F,
            new ConfigDescription("The key used to spray.")
        );
        
        _previousSprayKey = Config.Bind(
            "Spray Settings",
            "Previous Spray Key",
            KeyCode.Q,
            new ConfigDescription("The key used to select the previous spray image.")
        );
        
        _nextSprayKey = Config.Bind(
            "Spray Settings",
            "Next Spray Key",
            KeyCode.E,
            new ConfigDescription("The key used to select the next spray image.")
        );
        
        _showSprayIfLarge = Config.Bind(
            "Spray Settings",
            "Show spray if it exceeds the size limit locally",
            true,
            new ConfigDescription("Show the spray even if the image is large (Locally).")
        );

        _scaleKey = Config.Bind(
            "Scale Settings",
            "Scale Preview Key",
            KeyCode.LeftAlt,
            new ConfigDescription("Hold this key to preview the scale.")
        );
        
        _increaseScaleKey = Config.Bind(
            "Scale Settings",
            "Increase Scale Key",
            KeyCode.Equals, // + key
            new ConfigDescription("Press this key to increase the spray scale (+ key by default).")
        );
        
        _decreaseScaleKey = Config.Bind(
            "Scale Settings",
            "Decrease Scale Key",
            KeyCode.Minus, // - key
            new ConfigDescription("Press this key to decrease the spray scale (- key by default).")
        );
        
        _useScrollWheel = Config.Bind(
            "Scale Settings",
            "Use Scroll Wheel",
            true,
            new ConfigDescription("Enable scroll wheel to adjust scale while holding the Scale Preview Key.")
        );

        _sprayScale = Config.Bind(
            "Scale Settings",
            "Spray Scale",
            1.0f,
            new ConfigDescription(
                "The size of the spray.",
                new AcceptableValueRange<float>(0.1f, 5.0f)
            )
        );

        _minScaleSize = Config.Bind(
            "Scale Settings",
            "Minimum Scale",
            0.1f,
            new ConfigDescription(
                "The minimum allowed scale size.",
                new AcceptableValueRange<float>(0.1f, 1.0f)
            )
        );

        _maxScaleSize = Config.Bind(
            "Scale Settings",
            "Maximum Scale",
            5.0f,
            new ConfigDescription(
                "The maximum allowed scale size.",
                new AcceptableValueRange<float>(1.0f, 10.0f)
            )
        );

        _scaleSpeed = Config.Bind(
            "Scale Settings",
            "Scale Speed",
            0.1f,
            new ConfigDescription(
                "How quickly the spray scales when adjusting.",
                new AcceptableValueRange<float>(0.01f, 1.0f)
            )
        );

        _scalePreviewColor = Config.Bind(
            "Scale Settings",
            "Scale Preview Color",
            new Color(0.0f, 1.0f, 0.0f, 0.5f), // Semi-transparent green
            new ConfigDescription("The color of the scale preview.")
        );

        _sprayLifetimeSeconds = Config.Bind(
            "Host Settings",
            "Spray Lifetime (Seconds)",
            60f,
            new ConfigDescription(
                "How long the spray should last. Set to 0 for permanent sprays.",
                new AcceptableValueRange<float>(0f, 300f)
            )
        );

        _maxSpraysAllowed = Config.Bind(
            "Host Settings",
            "Max Sprays",
            10,
            new ConfigDescription(
                "Maximum number of sprays before the oldest is deleted.",
                new AcceptableValueRange<int>(1, 100)
            )
        );

        _selectedSprayImage = Config.Bind(
            "Spray Settings",
            "Selected Spray Image",
            _availableImages.FirstOrDefault() ?? "DefaultSpray.png",
            new ConfigDescription(
                "The image used for spraying.",
                new AcceptableValueList<string>(_availableImages.ToArray())
            )
        );

        _selectedSprayImage.SettingChanged += (_, _) =>
        {
            Logger.LogInfo("[BiggerSprayMod] Image selection changed. Reloading texture...");
            string path = Path.Combine(_imagesFolderPath, _selectedSprayImage.Value);
            _cachedSprayTexture = LoadTexture(path);
            if (_cachedSprayTexture != null)
            {
                _originalImageDimensions = new Vector2(_cachedSprayTexture.width, _cachedSprayTexture.height);
            }
        };

        _refreshSpraysButton = Config.Bind(
            "Spray Settings",
            "Refresh Sprays",
            false,
            new ConfigDescription("Set to TRUE to refresh the list of available sprays.")
        );
    }

    private void LoadAvailableImages()
    {
        if (!Directory.Exists(_imagesFolderPath))
        {
            Directory.CreateDirectory(_imagesFolderPath);
            Logger.LogInfo($"[BiggerSprayMod] Created images directory at {_imagesFolderPath}");
        }

        _availableImages = Directory.GetFiles(_imagesFolderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        if (_availableImages.Count == 0)
        {
            // Create a default spray if none exists
            CreateDefaultSpray();
        }
    }

    private void CreateDefaultSpray()
    {
        try
        {
            // Create a simple default spray (a colored square with transparency)
            Texture2D defaultSpray = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[256 * 256];

            // Create a simple shape (circle with gradient)
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(128, 128));
                    float alpha = Mathf.Clamp01(1.0f - (distance / 128f));

                    // Red circle with transparent edges
                    pixels[y * 256 + x] = new Color(1f, 0f, 0f, alpha * alpha);
                }
            }

            defaultSpray.SetPixels(pixels);
            defaultSpray.Apply();

            string defaultPath = Path.Combine(_imagesFolderPath, "DefaultSpray.png");
            File.WriteAllBytes(defaultPath, defaultSpray.EncodeToPNG());

            _availableImages.Add("DefaultSpray.png");
            Logger.LogInfo("[BiggerSprayMod] Created default spray image.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BiggerSprayMod] Failed to create default spray: {ex.Message}");
            _availableImages.Add("No Images Available");
        }
    }

    private void UpdateImageListConfig()
    {
        if (_availableImages.Count == 0)
        {
            _availableImages.Add("No Images Available");
        }

        // Rebind the selected image config with updated list
        Config.Remove(_selectedSprayImage.Definition);
        _selectedSprayImage = Config.Bind(
            "Spray Settings",
            "Selected Spray Image",
            _availableImages.Contains(_selectedSprayImage.Value) ? _selectedSprayImage.Value : _availableImages[0],
            new ConfigDescription(
                "The image used for spraying.",
                new AcceptableValueList<string>(_availableImages.ToArray())
            )
        );

        _selectedSprayImage.SettingChanged += (_, _) =>
        {
            string path = Path.Combine(_imagesFolderPath, _selectedSprayImage.Value);
            _cachedSprayTexture = LoadTexture(path);
            if (_cachedSprayTexture != null)
            {
                _originalImageDimensions = new Vector2(_cachedSprayTexture.width, _cachedSprayTexture.height);
            }
        };
    }

    private void Update()
    {
        // Check if we need to register for Photon events
        if (!_registeredCallbacks && PhotonNetwork.IsConnected)
        {
            PhotonNetwork.AddCallbackTarget(this);
            _registeredCallbacks = true;
            Logger.LogInfo("[BiggerSprayMod] Registered for Photon callbacks.");
        }
        
        // Handle image selection
        if (Input.GetKeyDown(_previousSprayKey.Value))
        {
            int currentIndex = _availableImages.IndexOf(_selectedSprayImage.Value);
            int newIndex = (currentIndex - 1 + _availableImages.Count) % _availableImages.Count;
            _selectedSprayImage.Value = _availableImages[newIndex];
        }
        else if (Input.GetKeyDown(_nextSprayKey.Value))
        {
            int currentIndex = _availableImages.IndexOf(_selectedSprayImage.Value);
            int newIndex = (currentIndex + 1) % _availableImages.Count;
            _selectedSprayImage.Value = _availableImages[newIndex];
        }

        // Handle scaling mode
        if (Input.GetKeyDown(_scaleKey.Value))
        {
            StartScalingMode();
        }
        else if (Input.GetKeyUp(_scaleKey.Value))
        {
            StopScalingMode();
        }

        // Handle key-based scale adjustments
        if (Input.GetKeyDown(_increaseScaleKey.Value))
        {
            AdjustScale(_scaleSpeed.Value);
        }
        else if (Input.GetKeyDown(_decreaseScaleKey.Value))
        {
            AdjustScale(-_scaleSpeed.Value);
        }

        // Handle scroll wheel for scaling
        if (_isScaling && _useScrollWheel.Value)
        {
            float scrollDelta = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                AdjustScale(scrollDelta * _scaleSpeed.Value);
            }
        }

        // Update scaling preview if active
        if (_isScaling)
        {
            UpdateScalingPreview();
        }
        else if (Input.GetKeyDown(_sprayKey.Value))
        {
            TrySpray();
        }

        CheckForRefreshSprays();
    }

    private void AdjustScale(float amount)
    {
        // If in preview mode, adjust the preview
        if (_isScaling)
        {
            _currentScalePreview = Mathf.Clamp(
                _currentScalePreview + amount,
                _minScaleSize.Value,
                _maxScaleSize.Value
            );
            
            // Update the preview if it exists
            if (_scalePreviewObject != null)
            {
                UpdatePreviewScale(_currentScalePreview);
            }
        }
        // Otherwise update the config value directly
        else
        {
            _sprayScale.Value = Mathf.Clamp(
                _sprayScale.Value + amount,
                _minScaleSize.Value,
                _maxScaleSize.Value
            );
        }
    }

    private void StartScalingMode()
    {
        _isScaling = true;
        _currentScalePreview = _sprayScale.Value;
        CreateScalePreview();
    }

    private void StopScalingMode()
    {
        _isScaling = false;
        // Save the new scale value
        _sprayScale.Value = _currentScalePreview;

        // Clean up the preview object
        if (_scalePreviewObject != null)
        {
            Destroy(_scalePreviewObject);
            _scalePreviewObject = null;
        }
    }

    private void CreateScalePreview()
    {
        // Clean up any existing preview
        if (_scalePreviewObject != null)
        {
            Destroy(_scalePreviewObject);
        }

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f))
        {
            // Create the preview quad
            _scalePreviewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _scalePreviewObject.name = "SprayScalePreview";

            // Remove the collider to avoid physics interactions
            Destroy(_scalePreviewObject.GetComponent<Collider>());

            // Position and orient the preview
            Vector3 position = hitInfo.point + hitInfo.normal * 0.01f;
            Quaternion rotation = Quaternion.LookRotation(hitInfo.normal);
            _scalePreviewObject.transform.position = position;
            _scalePreviewObject.transform.rotation = rotation;

            // Apply the current scale to the preview
            UpdatePreviewScale(_currentScalePreview);

            // Create a material with the preview texture and color
            Material previewMaterial = new Material(_previewMaterialTemplate);
            if (_cachedSprayTexture != null)
            {
                previewMaterial.mainTexture = _cachedSprayTexture;
            }

            // Apply the preview color (semi-transparent)
            previewMaterial.color = _scalePreviewColor.Value;

            // Apply shader settings for transparency
            previewMaterial.SetFloat("_Mode", 2); // Fade mode
            previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            previewMaterial.SetInt("_ZWrite", 0);
            previewMaterial.DisableKeyword("_ALPHATEST_ON");
            previewMaterial.EnableKeyword("_ALPHABLEND_ON");
            previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            previewMaterial.renderQueue = 3000;

            // Assign the material to the preview
            _scalePreviewObject.GetComponent<MeshRenderer>().material = previewMaterial;
        }
    }

    private void UpdatePreviewScale(float scale)
    {
        if (_scalePreviewObject == null) return;

        // Calculate aspect ratio to maintain proportions
        Vector2 adjustedScale = CalculateContainedScale(scale);
        _scalePreviewObject.transform.localScale = new Vector3(adjustedScale.x, adjustedScale.y, 1f);
    }

    private void UpdateScalingPreview()
    {
        if (_scalePreviewObject == null)
        {
            CreateScalePreview();
            return;
        }

        // Check if we need to update the position
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f))
        {
            Vector3 position = hitInfo.point + hitInfo.normal * 0.01f;
            Quaternion rotation = Quaternion.LookRotation(hitInfo.normal);
            _scalePreviewObject.transform.position = position;
            _scalePreviewObject.transform.rotation = rotation;
        }
    }

    private Vector2 CalculateContainedScale(float baseScale)
    {
        // Default to square if no texture
        if (_cachedSprayTexture == null)
        {
            return new Vector2(baseScale, baseScale);
        }

        // Get dimensions and calculate aspect ratio
        float width = _originalImageDimensions.x;
        float height = _originalImageDimensions.y;
        float aspectRatio = width / height;

        // Logic to contain the image within a square of size baseScale
        if (width > height)
        {
            // Wider than tall
            return new Vector2(baseScale, baseScale / aspectRatio);
        }
        else
        {
            // Taller than wide or square
            return new Vector2(baseScale * aspectRatio, baseScale);
        }
    }

    private void CheckForRefreshSprays()
    {
        if (_refreshSpraysButton.Value)
        {
            Logger.LogInfo("[BiggerSprayMod] Refreshing sprays list...");

            LoadAvailableImages();
            UpdateImageListConfig();

            // Reset the refresh button back to false
            _refreshSpraysButton.Value = false;

            Logger.LogInfo("[BiggerSprayMod] Sprays list refreshed successfully.");
        }
    }

    private void TrySpray()
    {
        Logger.LogInfo("[BiggerSprayMod] Attempting to spray...");

        // Get settings (either local or from host)
        float localCooldownTime = CooldownTime;
        float localSprayLifetime = PhotonNetwork.IsMasterClient ? _sprayLifetimeSeconds.Value : _hostSprayLifetime;
        int localMaxSprays = PhotonNetwork.IsMasterClient ? _maxSpraysAllowed.Value : _hostMaxSprays;

        // Check if we need to request host settings
        if (PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
        {
            // We already have cached settings from the host or will get them soon
            Logger.LogInfo("[BiggerSprayMod] Using host settings for spray properties.");
            RequestHostSettings();
        }

        if (Time.time - _lastSprayTime < localCooldownTime)
        {
            Logger.LogWarning("[BiggerSprayMod] Spray is on cooldown.");
            return;
        }

        if (_cachedSprayTexture == null)
        {
            Logger.LogError("[BiggerSprayMod] No valid spray texture loaded. Cannot spray.");
            return;
        }

        if (string.IsNullOrEmpty(_selectedSprayImage.Value) || _selectedSprayImage.Value == "No Images Available")
        {
            Logger.LogWarning("[BiggerSprayMod] No valid spray selected.");
            return;
        }
        
        // Check the size on disk of the selected image
        bool sendToNetwork = false;
        string selectedImagePath = Path.Combine(_imagesFolderPath, _selectedSprayImage.Value);
        if (File.Exists(selectedImagePath))
        {
            long fileSize = new FileInfo(selectedImagePath).Length;
            if (fileSize > 5000000) // 5 MB limit
            {
                Logger.LogWarning("[BiggerSprayMod] Selected image is too large to spray.");
                
                if (_showSprayIfLarge.Value)
                {
                    sendToNetwork = true;
                }
                else
                {
                    Logger.LogWarning("[BiggerSprayMod] Not sending large image to network.");
                    return;
                }
            }
            else
            {
                sendToNetwork = true;
            }
        }

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f))
        {
            // Place the spray locally
            Vector3 position = hitInfo.point + hitInfo.normal * 0.01f; // Offset from surface
            Quaternion rotation = Quaternion.LookRotation(hitInfo.normal);
            PlaceSpray(position, rotation, localSprayLifetime, localMaxSprays);

            // Send spray to other players
            if (PhotonNetwork.IsConnected && sendToNetwork)
            {
                SendSprayToNetwork(hitInfo.point, hitInfo.normal);
            }

            _lastSprayTime = Time.time;
        }
        else
        {
            Logger.LogWarning("[BiggerSprayMod] No valid surface detected to spray onto.");
        }
    }

    private void RequestHostSettings()
    {
        if (!PhotonNetwork.IsConnected || PhotonNetwork.IsMasterClient)
            return;

        // Send a request to the host for their settings
        Logger.LogInfo("[BiggerSprayMod] Requesting settings from host...");

        PhotonNetwork.RaiseEvent(
            SettingsRequestEventCode,
            null,
            new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient },
            SendOptions.SendReliable
        );
    }

    private void SendHostSettings()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        Logger.LogInfo("[BiggerSprayMod] Sending host settings to clients...");

        object[] settingsData =
        [
            _sprayLifetimeSeconds.Value,
            _maxSpraysAllowed.Value
        ];

        PhotonNetwork.RaiseEvent(
            SettingsResponseEventCode,
            settingsData,
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable
        );
    }

    private void SendSprayToNetwork(Vector3 hitPoint, Vector3 hitNormal)
    {
        try
        {
            // Compress texture to reduce network traffic
            byte[] imageData = _cachedSprayTexture.EncodeToPNG();
            byte[] compressedData = CompressImage(imageData);

            if (compressedData.Length > 250000) // Safety check
            {
                Logger.LogWarning("[BiggerSprayMod] Spray image too large to send over network!");
                return;
            }

            // Calculate the contained scale dimensions
            Vector2 adjustedScale = CalculateContainedScale(_sprayScale.Value);

            object[] sprayData =
            [
                compressedData,
                hitPoint,
                hitNormal,
                adjustedScale.x,
                adjustedScale.y
            ];

            PhotonNetwork.RaiseEvent(
                SprayEventCode,
                sprayData,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable
            );

            Logger.LogInfo($"[BiggerSprayMod] Sent spray to network ({compressedData.Length} bytes)");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BiggerSprayMod] Error sending spray: {ex.Message}");
        }
    }

    private byte[] CompressImage(byte[] data)
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

    private byte[] DecompressImage(byte[] compressedData)
    {
        using (MemoryStream compressedStream = new MemoryStream(compressedData))
        using (GZipStream gzip = new GZipStream(compressedStream, CompressionMode.Decompress))
        using (MemoryStream resultStream = new MemoryStream())
        {
            gzip.CopyTo(resultStream);
            return resultStream.ToArray();
        }
    }

    public void OnEvent(EventData photonEvent)
    {
        // Handle different event types
        switch (photonEvent.Code)
        {
            case SprayEventCode:
                HandleSprayEvent(photonEvent);
                break;

            case SettingsRequestEventCode:
                if (PhotonNetwork.IsMasterClient)
                {
                    SendHostSettings();
                }

                break;

            case SettingsResponseEventCode:
                HandleSettingsResponse(photonEvent);
                break;
        }
    }

    private void HandleSprayEvent(EventData photonEvent)
    {
        try
        {
            // Extract data
            object[] data = (object[])photonEvent.CustomData;
            byte[] compressedImage = (byte[])data[0];
            Vector3 hitPoint = (Vector3)data[1];
            Vector3 hitNormal = (Vector3)data[2];
            float scaleX = (float)data[3];
            float scaleY = (float)data[4];

            // Decompress image
            byte[] imageData = DecompressImage(compressedImage);

            // Create texture
            Texture2D sprayTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            sprayTexture.LoadImage(imageData);

            // Place spray
            Vector3 position = hitPoint + hitNormal * 0.01f;
            Quaternion rotation = Quaternion.LookRotation(hitNormal);

            // Use host settings and the custom scale dimensions
            PlaceSprayWithCustomScale(position, rotation, sprayTexture,
                new Vector2(scaleX, scaleY), _hostSprayLifetime, _hostMaxSprays);

            Logger.LogInfo("[BiggerSprayMod] Received and placed remote spray.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BiggerSprayMod] Error processing remote spray: {ex.Message}");
        }
    }

    private void HandleSettingsResponse(EventData photonEvent)
    {
        try
        {
            object[] settingsData = (object[])photonEvent.CustomData;
            _hostSprayLifetime = (float)settingsData[0];
            _hostMaxSprays = (int)settingsData[1];

            Logger.LogInfo(
                $"[BiggerSprayMod] Received host settings: Lifetime={_hostSprayLifetime}s, MaxSprays={_hostMaxSprays}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BiggerSprayMod] Error processing settings response: {ex.Message}");
        }
    }

    private Texture2D LoadTexture(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Logger.LogWarning($"[BiggerSprayMod] File not found at {filePath}");
            return null;
        }

        try
        {
            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (texture.LoadImage(fileData))
            {
                Logger.LogInfo(
                    $"[BiggerSprayMod] Successfully loaded texture: {filePath} ({texture.width}x{texture.height})");
                return texture;
            }
            else
            {
                Logger.LogWarning($"[BiggerSprayMod] Failed to load texture from {filePath}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BiggerSprayMod] Error loading texture: {ex.Message}");
            return null;
        }
    }

    private void PlaceSpray(Vector3 position, Quaternion rotation, float lifetime, int maxSprays)
    {
        // Calculate contained scale
        Vector2 adjustedScale = CalculateContainedScale(_sprayScale.Value);
        PlaceSprayWithCustomScale(position, rotation, _cachedSprayTexture, adjustedScale, lifetime, maxSprays);
    }

    private void PlaceSprayWithCustomScale(Vector3 position, Quaternion rotation, Texture2D texture,
        Vector2 scale, float lifetime, int maxSprays)
    {
        if (texture == null)
        {
            Logger.LogWarning("[BiggerSprayMod] Cannot place spray with null texture.");
            return;
        }

        try
        {
            Logger.LogInfo($"[BiggerSprayMod] Creating spray from texture with scale ({scale.x}, {scale.y})...");

            // Create the spray quad
            GameObject spray = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spray.name = "BiggerSpray_Instance";

            // Remove the collider to avoid physics interactions
            UnityEngine.Object.Destroy(spray.GetComponent<Collider>());

            // Position and orient the spray
            spray.transform.position = position;
            spray.transform.rotation = rotation;
            spray.transform.localScale = new Vector3(scale.x, scale.y, 1.0f);

            // Create a material with the spray texture
            Material sprayMaterial = new Material(_sprayMaterialTemplate);
            sprayMaterial.mainTexture = texture;

            // Apply shader settings for transparency
            sprayMaterial.SetFloat("_Mode", 2); // Fade mode
            sprayMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            sprayMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            sprayMaterial.SetInt("_ZWrite", 0);
            sprayMaterial.DisableKeyword("_ALPHATEST_ON");
            sprayMaterial.EnableKeyword("_ALPHABLEND_ON");
            sprayMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            sprayMaterial.renderQueue = 3000;

            // Assign the material to the spray
            spray.GetComponent<MeshRenderer>().material = sprayMaterial;

            // Add to the list of sprays
            _spawnedSprays.Add(spray);

            // Set the lifetime if needed
            if (lifetime > 0)
            {
                UnityEngine.Object.Destroy(spray, lifetime);
            }

            // Handle max sprays limit
            if (_spawnedSprays.Count > maxSprays)
            {
                // Remove the oldest sprays when we exceed the limit
                while (_spawnedSprays.Count > maxSprays)
                {
                    GameObject oldest = _spawnedSprays[0];
                    _spawnedSprays.RemoveAt(0);

                    if (oldest != null)
                    {
                        UnityEngine.Object.Destroy(oldest);
                    }
                }
            }

            Logger.LogInfo("[BiggerSprayMod] Spray placed successfully.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BiggerSprayMod] Error placing spray: {ex.Message}");
        }
    }

    // Clean up when the plugin is destroyed
    private void OnDestroy()
    {
        // Clean up Photon event handling
        if (_registeredCallbacks)
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        // Clean up any remaining sprays
        foreach (GameObject spray in _spawnedSprays)
        {
            if (spray != null)
            {
                UnityEngine.Object.Destroy(spray);
            }
        }

        _spawnedSprays.Clear();

        // Clean up preview object if it exists
        if (_scalePreviewObject != null)
        {
            UnityEngine.Object.Destroy(_scalePreviewObject);
            _scalePreviewObject = null;
        }
    }
}