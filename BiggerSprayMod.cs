using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace BiggerSprayMod;

[BepInPlugin("MishaOpstal.BigSprayMod", "Bigger Spray Mod", "1.5.0")]
[BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
public class BiggerSprayMod : BaseUnityPlugin, IOnEventCallback
{
    // Runtime state
    public float _lastSprayTime = -999f;
    public const float CooldownTime = 0.5f; // Half-second between sprays
    public Texture2D? _cachedSprayTexture;
    public List<string> _availableImages = [];
    public string? _imagesFolderPath;
    public readonly List<GameObject> _spawnedSprays = [];
    public Material _sprayMaterialTemplate;
    public Material _previewMaterialTemplate;
    
    // GIF Animation
    public bool _isAnimatedGif;
    public List<Texture2D> _gifFrames = [];
    public List<float> _gifFrameDelays = [];
    public float _gifTimeSinceLastFrame;
    public int _currentGifFrame;

    // Network Settings (received from host)
    public float _hostSprayLifetime = 60f;
    public int _hostMaxSprays = 10;
    public bool _registeredCallbacks;

    // Scale Preview
    public bool _isScaling = false;
    public float _currentScalePreview;
    public GameObject _scalePreviewObject;
    public Vector2 _originalImageDimensions = new Vector2(1, 1);
    
    // Managers & Utility classes
    public ConfigManager _configManager;
    public InputManager _inputManager;
    public ScalingUtils _scalingUtils;
    public ImageUtils _imageUtils;
    public SprayUtils _sprayUtils;
    public NetworkUtils _networkUtils;

    // Static instance for easy access
    public static BiggerSprayMod Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        
        // Initialize managers and utility classes
        _configManager = new ConfigManager(this);
        _inputManager = new InputManager(this);
        _scalingUtils = new ScalingUtils(this);
        _imageUtils = new ImageUtils(this);
        _sprayUtils = new SprayUtils(this);
        _networkUtils = new NetworkUtils(this);

        // Set up paths
        _imagesFolderPath = Path.Combine(Paths.ConfigPath, "BiggerSprayImages");

        // Initialize in correct order
        _imageUtils.LoadAvailableImages();
        _configManager.Initialize();
        _sprayUtils.CreateSprayPrefabs();

        string preloadPath = Path.Combine(_imagesFolderPath, _configManager.SelectedSprayImage.Value);
        
        // Check if this is a GIF file
        if (preloadPath.ToLower().EndsWith(".gif"))
        {
            _imageUtils.LoadGifTexture(preloadPath);
        }
        else
        {
            _cachedSprayTexture = _imageUtils.LoadTexture(preloadPath);
            if (_cachedSprayTexture != null)
            {
                _originalImageDimensions = new Vector2(_cachedSprayTexture.width, _cachedSprayTexture.height);
            }
        }

        Logger.LogInfo("[BiggerSprayMod] Initialized successfully.");
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
        
        // Clean up all GIF frames
        _imageUtils.ClearGifData();
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
        
        // Process inputs through the input manager
        _inputManager.ProcessInputs();

        // Check for refresh sprays button
        _sprayUtils.CheckForRefreshSprays();
        
        // Update animated GIFs if enabled
        if (_isAnimatedGif && _configManager.AnimateGifs.Value && _gifFrames.Count > 0)
        {
            _sprayUtils.UpdateGifAnimations();
        }
    }
    
    public void OnEvent(EventData photonEvent)
    {
        // Delegate to network event handling
        _networkUtils.OnNetworkEvent(photonEvent);
    }
    
    public void LogMessage(Enum messageType, string message)
    {
        switch (messageType)
        {
            case LogLevel.Debug:
                Logger.LogDebug(message);
                break;
            case LogLevel.Info:
                Logger.LogInfo(message);
                break;
            case LogLevel.Warning:
                Logger.LogWarning(message);
                break;
            case LogLevel.Error:
                Logger.LogError(message);
                break;
            default:
                Logger.LogInfo(message);
                break;
        }
    }
    
    public string GetPluginPath()
    {
        // Get a plugin path of DLL
        string pluginPath = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrEmpty(pluginPath))
        {
            Logger.LogError("[BiggerSprayMod] Failed to get plugin path.");
            return string.Empty;
        }
        
        return pluginPath;
    }
}