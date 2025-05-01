using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BiggerSprayMod.web;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using HarmonyLib;

namespace BiggerSprayMod;

[BepInPlugin("MishaOpstal.BigSprayMod", "Bigger Spray Mod", "1.6.0")]
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

    // Network Settings (received from host)
    public float _hostSprayLifetime = 60f;
    public int _hostMaxSprays = 10;
    public bool _registeredCallbacks;
    
    // Track spray expiration for host-controlled removal
    private Dictionary<string, float> _sprayExpirationTimes = new Dictionary<string, float>();
    public Dictionary<string, Coroutine> _sprayRemovalCoroutines = new Dictionary<string, Coroutine>();
    private float _lastHostCleanupTime = 0f;
    private const float HostCleanupInterval = 1f; // Check once per second

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
    public WebUtils _webUtils;
    public GifManager _gifManager;
    public GifAssetManager _gifAssetManager;

    // Static instance for easy access
    public static BiggerSprayMod Instance { get; private set; }
    internal Harmony? Harmony { get; set; }

    private void Awake()
    {
        Instance = this;
        
        // Initialize managers and utility classes
        _configManager = new ConfigManager(this);
        _inputManager = new InputManager(this);
        _scalingUtils = new ScalingUtils(this);
        _imageUtils = new ImageUtils(this);
        _sprayUtils = new SprayUtils(this);
        _webUtils = new WebUtils(this);
        _networkUtils = new NetworkUtils(this);
        _gifManager = new GifManager(this, _webUtils);
        _gifAssetManager = new GifAssetManager(this, _webUtils);

        // Set up paths
        _imagesFolderPath = Path.Combine(Paths.ConfigPath, "BiggerSprayImages");

        // Initialize in correct order
        _imageUtils.LoadAvailableImages();
        _configManager.Initialize();
        _sprayUtils.CreateSprayPrefabs();

        string preloadPath = Path.Combine(_imagesFolderPath, _configManager.SelectedSprayImage.Value);
        _cachedSprayTexture = _imageUtils.LoadTexture(preloadPath);
        if (_cachedSprayTexture != null)
        {
            _originalImageDimensions = new Vector2(_cachedSprayTexture.width, _cachedSprayTexture.height);
        }
        
        Patch();

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
        
        // Make sure to dispose all GIFs properly
        if (_webUtils != null)
        {
            _webUtils.DisposeAllGifs();
        }
        
        // Clean up the asset manager
        if (_gifAssetManager != null)
        {
            _gifAssetManager.Dispose();
        }
        
        // Release cached texture
        if (_cachedSprayTexture != null)
        {
            _cachedSprayTexture = null;
        }
        
        Logger.LogInfo("[BiggerSprayMod] Plugin resources cleaned up on destroy");
    }
    
    internal void Patch()
    {
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();
    }

    internal void Unpatch()
    {
        Harmony?.UnpatchSelf();
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
        
        // Check for refresh GIFs button
        CheckForRefreshGifs();
        
        // Update GIF animation if in GIF mode
        _gifManager.Update();
        
        // Periodically clean up old cached GIFs
        if (Time.frameCount % 3600 == 0) // Every ~1 minute at 60 FPS
        {
            // Use the asset manager for cleanup
            _gifAssetManager.CleanupOldAssets();
        }
        
        // If we're the host, periodically check for sprays that need to be removed
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.IsConnected && 
            Time.time > _lastHostCleanupTime + HostCleanupInterval)
        {
            UpdateHostSprayManagement();
            _lastHostCleanupTime = Time.time;
        }
    }
    
    /// <summary>
    /// Schedule a spray for removal after the specified lifetime
    /// </summary>
    public IEnumerator ScheduleSprayRemoval(string sprayId, float lifetime)
    {
        if (string.IsNullOrEmpty(sprayId) || lifetime <= 0f)
            yield break;
            
        // Store the expiration time
        _sprayExpirationTimes[sprayId] = Time.time + lifetime;
        
        // Wait for the lifetime
        yield return new WaitForSeconds(lifetime);
        
        // Remove locally
        bool removed = _sprayUtils.RemoveSprayById(sprayId);
        
        // Send removal notification to all clients
        if (removed && PhotonNetwork.IsConnected && PhotonNetwork.IsMasterClient)
        {
            _networkUtils.SendRemoveSprayToNetwork(sprayId);
            _sprayExpirationTimes.Remove(sprayId);
            
            // Remove from coroutines dictionary as well
            if (_sprayRemovalCoroutines.ContainsKey(sprayId))
            {
                _sprayRemovalCoroutines.Remove(sprayId);
            }
            
            LogMessage(LogLevel.Info, $"[BiggerSprayMod] Host removed spray {sprayId} and notified clients");
        }
    }
    
    /// <summary>
    /// Update host spray management - check expiration times and max spray limit
    /// </summary>
    private void UpdateHostSprayManagement()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.IsConnected)
            return;
            
        // Check for expired sprays
        float currentTime = Time.time;
        List<string> expiredSprays = new List<string>();
        
        foreach (var entry in _sprayExpirationTimes)
        {
            if (currentTime >= entry.Value)
            {
                expiredSprays.Add(entry.Key);
            }
        }
        
        // Remove expired sprays
        foreach (string sprayId in expiredSprays)
        {
            // If we have a coroutine for this spray, stop it
            if (_sprayRemovalCoroutines.TryGetValue(sprayId, out var coroutine))
            {
                if (coroutine != null)
                {
                    StopCoroutine(coroutine);
                }
                _sprayRemovalCoroutines.Remove(sprayId);
            }
            
            bool removed = _sprayUtils.RemoveSprayById(sprayId);
            if (removed)
            {
                _networkUtils.SendRemoveSprayToNetwork(sprayId);
                LogMessage(LogLevel.Info, $"[BiggerSprayMod] Host removed expired spray {sprayId}");
            }
            _sprayExpirationTimes.Remove(sprayId);
        }
        
        // Enforce max spray limit
        EnforceMaxSprays();
    }
    
    /// <summary>
    /// Enforce the maximum number of sprays allowed by removing oldest
    /// </summary>
    private void EnforceMaxSprays()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.IsConnected)
            return;
            
        int maxSprays = _configManager.MaxSpraysAllowed.Value;
        
        // If we're under the limit, nothing to do
        if (_spawnedSprays.Count <= maxSprays)
            return;
            
        // Need to remove oldest sprays
        int sprayCountToRemove = _spawnedSprays.Count - maxSprays;
        
        for (int i = 0; i < sprayCountToRemove && i < _spawnedSprays.Count; i++)
        {
            GameObject oldest = _spawnedSprays[0];
            
            if (oldest != null)
            {
                // Get the spray ID
                SprayIdentifier identifier = oldest.GetComponent<SprayIdentifier>();
                if (identifier != null && !string.IsNullOrEmpty(identifier.SprayId))
                {
                    string sprayId = identifier.SprayId;
                    
                    // If we have a coroutine for this spray, stop it
                    if (_sprayRemovalCoroutines.TryGetValue(sprayId, out var coroutine))
                    {
                        if (coroutine != null)
                        {
                            StopCoroutine(coroutine);
                        }
                        _sprayRemovalCoroutines.Remove(sprayId);
                    }
                    
                    // Remove from dictionary and notify clients
                    _sprayUtils.RemoveSprayById(sprayId);
                    _networkUtils.SendRemoveSprayToNetwork(sprayId);
                    _sprayExpirationTimes.Remove(sprayId);
                    
                    LogMessage(LogLevel.Info, $"[BiggerSprayMod] Host removed oldest spray {sprayId} to enforce limit");
                }
                else
                {
                    // No ID, just remove directly
                    _spawnedSprays.Remove(oldest);
                    UnityEngine.Object.Destroy(oldest);
                }
            }
            else
            {
                // Null reference, just remove it
                _spawnedSprays.RemoveAt(0);
            }
        }
    }
    
    private void CheckForRefreshGifs()
    {
        if (_configManager.RefreshGifsButton.Value)
        {
            LogMessage(LogLevel.Info, "[BiggerSprayMod] Refreshing GIFs list...");
            
            _gifManager.RefreshGifList();
            
            // Reset the refresh button back to false
            _configManager.RefreshGifsButton.Value = false;
            
            LogMessage(LogLevel.Info, "[BiggerSprayMod] GIFs list refreshed successfully.");
        }
        
        if (_configManager.OpenGifConfigFolderButton.Value)
        {
            _gifManager.OpenGifConfigFolder();
            
            // Reset the button back to false
            _configManager.OpenGifConfigFolderButton.Value = false;
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

    public void UpdateAllSprayLifetimes()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.IsConnected)
            return;
            
        float newLifetime = _configManager.SprayLifetimeSeconds.Value;
        float currentTime = Time.time;
        Dictionary<string, float> updatedExpirationTimes = new Dictionary<string, float>();
        
        LogMessage(LogLevel.Info, $"[BiggerSprayMod] Host updating all spray lifetimes to {newLifetime} seconds");
        
        // If we're setting to permanent, just clear all expiration times
        if (newLifetime <= 0)
        {
            // Stop all active removal coroutines
            foreach (var coroutine in _sprayRemovalCoroutines.Values)
            {
                if (coroutine != null)
                {
                    StopCoroutine(coroutine);
                }
            }
            
            _sprayRemovalCoroutines.Clear();
            _sprayExpirationTimes.Clear();
            LogMessage(LogLevel.Info, "[BiggerSprayMod] All sprays set to permanent (no expiration)");
            return;
        }
        
        // Update all existing sprays by creating new expiration times for them
        foreach (var spray in _spawnedSprays)
        {
            if (spray == null) continue;
            
            SprayIdentifier identifier = spray.GetComponent<SprayIdentifier>();
            if (identifier == null || string.IsNullOrEmpty(identifier.SprayId)) continue;
            
            string sprayId = identifier.SprayId;
            
            // Cancel any existing coroutine for this spray
            if (_sprayRemovalCoroutines.TryGetValue(sprayId, out var existingCoroutine))
            {
                if (existingCoroutine != null)
                {
                    StopCoroutine(existingCoroutine);
                }
            }
            
            // Set the new expiration time for this spray
            updatedExpirationTimes[sprayId] = currentTime + newLifetime;
            
            // Start a new coroutine with the updated lifetime and store the reference
            _sprayRemovalCoroutines[sprayId] = StartCoroutine(ScheduleSprayRemoval(sprayId, newLifetime));
        }
        
        // Replace the old expiration times dictionary
        _sprayExpirationTimes = updatedExpirationTimes;
        
        LogMessage(LogLevel.Info, $"[BiggerSprayMod] Updated {updatedExpirationTimes.Count} spray lifetimes");
    }
}