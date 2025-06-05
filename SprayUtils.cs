using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;
using BiggerSprayMod.web;

namespace BiggerSprayMod;

public class SprayUtils
{
    private BiggerSprayMod _plugin;
    
    // Dictionary to track spray IDs to their corresponding GameObjects
    private Dictionary<string, GameObject> _sprayIdsToGameObjects = new Dictionary<string, GameObject>();
    
    public SprayUtils(BiggerSprayMod plugin)
    {
        _plugin = plugin;
    }
    
    public void CreateSprayPrefabs()
    {
        _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Creating spray materials...");

        // Try finding a build-in shader that works well with transparency
        Shader sprayShader = Shader.Find("Sprites/Default");
        if (sprayShader == null)
        {
            // Fall back to other common shaders
            sprayShader = Shader.Find("Unlit/Texture");

            if (sprayShader == null)
            {
                _plugin.LogMessage(LogLevel.Error,"[BiggerSprayMod] Failed to find suitable shader. Sprays may not appear correctly.");
                return;
            }
        }

        _plugin._sprayMaterialTemplate = new Material(sprayShader)
        {
            mainTexture = Texture2D.whiteTexture // Default texture
        };

        // Create the preview material (semi-transparent)
        _plugin._previewMaterialTemplate = new Material(sprayShader)
        {
            mainTexture = Texture2D.whiteTexture, // Default texture
            color = _plugin._configManager.ScalePreviewColor.Value
        };

        _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Spray materials ready.");
    }
    
    /// <summary>
    /// Register a spray with its unique ID for tracking
    /// </summary>
    public void RegisterSpray(GameObject spray, string sprayId)
    {
        if (spray == null || string.IsNullOrEmpty(sprayId))
            return;
            
        // Add a SprayIdentifier component to the spray
        SprayIdentifier identifier = spray.AddComponent<SprayIdentifier>();
        identifier.SprayId = sprayId;
        
        // Track in our dictionary for fast lookups
        _sprayIdsToGameObjects[sprayId] = spray;
    }
    
    /// <summary>
    /// Remove a spray by its ID
    /// </summary>
    public bool RemoveSprayById(string sprayId)
    {
        if (string.IsNullOrEmpty(sprayId) || !_sprayIdsToGameObjects.TryGetValue(sprayId, out GameObject spray))
            return false;
            
        // Remove from dictionary
        _sprayIdsToGameObjects.Remove(sprayId);
        
        // Remove from the sprays list
        if (_plugin._spawnedSprays.Contains(spray))
        {
            _plugin._spawnedSprays.Remove(spray);
        }
        
        // Destroy the GameObject
        if (spray != null)
        {
            UnityEngine.Object.Destroy(spray);
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Removes all actively tracked sprays
    /// </summary>
    public void RemoveAllSprays()
    {
        _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Removing all sprays...");

        foreach (var spray in _sprayIdsToGameObjects.Values)
        {
            if (spray != null)
            {
                UnityEngine.Object.Destroy(spray);
            }
        }

        _sprayIdsToGameObjects.Clear();
        _plugin._spawnedSprays.Clear();

        _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] All sprays removed.");
    }
    
    public void CreateDefaultSpray()
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

            string defaultPath = Path.Combine(_plugin._imagesFolderPath ?? _plugin.GetPluginPath(), "DefaultSpray.png");
            File.WriteAllBytes(defaultPath, defaultSpray.EncodeToPNG());

            _plugin._availableImages.Add("DefaultSpray.png");
            _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Created default spray image.");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error,$"[BiggerSprayMod] Failed to create default spray: {ex.Message}");
            _plugin._availableImages.Add("No Images Available");
        }
    }
    
    public void CheckForRefreshSprays()
    {
        // Check if the open image folder button was pressed
        if (_plugin._configManager.OpenImagesFolderButton.Value)
        {
            _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Opening image folder...");

            // Open the images folder
            string folderPath = _plugin._imagesFolderPath ?? _plugin.GetPluginPath();
            Application.OpenURL(folderPath);

            // Reset the button back to false
            _plugin._configManager.OpenImagesFolderButton.Value = false;
        }
        // Check if the refresh button was pressed
        if (_plugin._configManager.RefreshSpraysButton.Value)
        {
            _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Refreshing sprays list...");

            _plugin._imageUtils.LoadAvailableImages();
            _plugin._configManager.UpdateImageListConfig();

            // Reset the refresh button back to false
            _plugin._configManager.RefreshSpraysButton.Value = false;

            _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Sprays list refreshed successfully.");
        }
    }
    
    public void PlaceSpray(Vector3 position, Quaternion rotation, float lifetime, int maxSprays)
    {
        // Generate a unique ID for this spray
        string sprayId = Guid.NewGuid().ToString();
        
        // Calculate contained scale
        Vector2 adjustedScale = _plugin._scalingUtils.CalculateContainedScale(_plugin._configManager.SprayScale.Value);
        PlaceSprayWithCustomScale(position, rotation, _plugin._cachedSprayTexture, adjustedScale, sprayId, lifetime, maxSprays);
        
        // If we're the host, schedule this spray for removal
        if (PhotonNetwork.IsMasterClient && PhotonNetwork.IsConnected && lifetime > 0)
        {
            _plugin.StartCoroutine(_plugin.ScheduleSprayRemoval(sprayId, lifetime));
        }
    }

    public void PlaceSprayWithCustomScale(Vector3 position, Quaternion rotation, Texture2D texture,
        Vector2 scale, string sprayId, float lifetime, int maxSprays)
    {
        if (texture == null)
        {
            _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Cannot place spray with null texture.");
            return;
        }

        try
        {
            _plugin.LogMessage(LogLevel.Info,$"[BiggerSprayMod] Creating spray from texture with scale ({scale.x}, {scale.y})...");

            // Create the spray quad
            GameObject spray = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spray.name = "BiggerSpray_Instance";

            // Remove the collider to avoid physics interactions
            UnityEngine.Object.Destroy(spray.GetComponent<Collider>());

            // Position and orient the spray
            spray.transform.position = position;
            spray.transform.rotation = rotation;
            spray.transform.localScale = new Vector3(-scale.x, scale.y, 1.0f);

            // Create a material with the spray texture
            Material sprayMaterial = new Material(_plugin._sprayMaterialTemplate);
            sprayMaterial.mainTexture = texture;

            // Apply shader settings for transparency
            sprayMaterial.SetFloat("_Mode", 2); // Fade mode
            sprayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            sprayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            sprayMaterial.SetInt("_ZWrite", 0);
            sprayMaterial.DisableKeyword("_ALPHATEST_ON");
            sprayMaterial.EnableKeyword("_ALPHABLEND_ON");
            sprayMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            sprayMaterial.renderQueue = 3000;

            // Assign the material to the spray
            spray.GetComponent<MeshRenderer>().material = sprayMaterial;
            
            // Register the spray ID
            RegisterSpray(spray, sprayId);

            // Add to the list of sprays
            _plugin._spawnedSprays.Add(spray);

            // Set the lifetime if needed and we're not in a host-controlled network game
            if (lifetime > 0 && !PhotonNetwork.IsConnected)
            {
                UnityEngine.Object.Destroy(spray, lifetime);
            }

            // Handle max sprays limit if we're not host-controlled
            if (!PhotonNetwork.IsConnected)
            {
                if (_plugin._spawnedSprays.Count > maxSprays)
                {
                    // Remove the oldest sprays when we exceed the limit
                    while (_plugin._spawnedSprays.Count > maxSprays)
                    {
                        GameObject oldest = _plugin._spawnedSprays[0];
                        _plugin._spawnedSprays.RemoveAt(0);

                        if (oldest != null)
                        {
                            UnityEngine.Object.Destroy(oldest);
                        }
                    }
                }
            }

            _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Spray placed successfully.");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error,$"[BiggerSprayMod] Error placing spray: {ex.Message}");
        }
    }
    
    public void HandleSprayEvent(EventData photonEvent)
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
            string sprayId = data.Length > 5 ? (string)data[5] : Guid.NewGuid().ToString(); // Use provided ID or generate one

            // Decompress image
            byte[] imageData = _plugin._imageUtils.DecompressImage(compressedImage);

            // Create texture
            Texture2D sprayTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            sprayTexture.LoadImage(imageData);

            // Place spray
            Vector3 position = hitPoint + hitNormal * 0.01f;
            Quaternion rotation = Quaternion.LookRotation(hitNormal);

            // Use host settings and the custom scale dimensions
            PlaceSprayWithCustomScale(
                position, 
                rotation, 
                sprayTexture,
                new Vector2(scaleX, scaleY),
                sprayId,
                0, // Lifetime is now managed by the host, not by timer
                _plugin._hostMaxSprays
            );

            _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Received and placed remote spray.");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Info,$"[BiggerSprayMod] Error processing remote spray: {ex.Message}");
        }
    }
    
    public void TrySpray()
    {
        _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Attempting to spray...");

        // Get settings (either local or from host)
        float localCooldownTime = BiggerSprayMod.CooldownTime;
        float localSprayLifetime = PhotonNetwork.IsMasterClient ? _plugin._configManager.SprayLifetimeSeconds.Value : _plugin._hostSprayLifetime;
        int localMaxSprays = PhotonNetwork.IsMasterClient ? _plugin._configManager.MaxSpraysAllowed.Value : _plugin._hostMaxSprays;

        // Check if we need to request host settings
        if (PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
        {
            // We already have cached settings from the host or will get them soon
            _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Using host settings for spray properties.");
            _plugin._networkUtils.RequestHostSettings();
        }

        if (Time.time - _plugin._lastSprayTime < localCooldownTime)
        {
            _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Spray is on cooldown.");
            return;
        }

        // Check if we're in GIF mode
        if (_plugin._gifManager.IsGifMode)
        {
            if (string.IsNullOrEmpty(_plugin._gifManager.CurrentGifName) || _plugin._gifManager.CurrentGifName == "No GIFs Available" || _plugin._gifManager.CurrentGifName == "GIFs Disabled - Invalid JSON")
            {
                _plugin.LogMessage(LogLevel.Error,"[BiggerSprayMod] No valid GIF selected.");
                return;
            }
            
            if (_plugin._cachedSprayTexture == null)
            {
                _plugin.LogMessage(LogLevel.Error,"[BiggerSprayMod] No valid GIF texture loaded. Cannot spray.");
                return;
            }
        }
        else // Regular spray mode
        {
            if (_plugin._cachedSprayTexture == null)
            {
                _plugin.LogMessage(LogLevel.Error,"[BiggerSprayMod] No valid spray texture loaded. Cannot spray.");
                return;
            }

            if (string.IsNullOrEmpty(_plugin._configManager.SelectedSprayImage.Value) || _plugin._configManager.SelectedSprayImage.Value == "No Images Available")
            {
                _plugin.LogMessage(LogLevel.Error,"[BiggerSprayMod] No valid spray selected.");
                return;
            }
            
            // Check the size on disk of the selected image
            string selectedImagePath = Path.Combine(_plugin._imagesFolderPath ?? _plugin.GetPluginPath(), _plugin._configManager.SelectedSprayImage.Value);
            if (File.Exists(selectedImagePath))
            {
                long fileSize = new FileInfo(selectedImagePath).Length;
                if (fileSize > 5000000) // 5 MB limit
                {
                    _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Selected image is too large to spray.");
                    
                    if (!_plugin._configManager.ShowSprayIfLarge.Value)
                    {
                        _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Not sending large image to network.");
                        return;
                    }
                }
            }
        }

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f))
        {
            // Generate a unique ID for this spray
            string sprayId = Guid.NewGuid().ToString();
            
            // Place the spray locally
            Vector3 position = hitInfo.point + hitInfo.normal * 0.01f; // Offset from surface
            Quaternion rotation = Quaternion.LookRotation(hitInfo.normal);
            
            // Check if this is a GIF and should be animated
            if (_plugin._gifManager.IsGifMode)
            {
                string gifUrl = _plugin._gifManager.GetGifUrlByName(_plugin._gifManager.CurrentGifName);
                if (!string.IsNullOrEmpty(gifUrl))
                {
                    // Always use the URL-based approach for GIFs to benefit from the asset manager
                    Vector2 adjustedScale = _plugin._scalingUtils.CalculateContainedScale(_plugin._configManager.SprayScale.Value);
                    _plugin._networkUtils.PlaceAnimatedGifSprayByUrl(
                        position, 
                        rotation, 
                        gifUrl, 
                        adjustedScale,
                        sprayId,
                        localSprayLifetime, 
                        localMaxSprays);
                    
                    // Send to network and update time
                    if (PhotonNetwork.IsConnected)
                    {
                        _plugin._networkUtils.SendSprayToNetwork(hitInfo.point, hitInfo.normal, sprayId);
                    }
                    
                    // If we're the host, start the timer to remove this spray
                    if (PhotonNetwork.IsMasterClient && PhotonNetwork.IsConnected && localSprayLifetime > 0)
                    {
                        var coroutine = _plugin.StartCoroutine(_plugin.ScheduleSprayRemoval(sprayId, localSprayLifetime));
                        _plugin._sprayRemovalCoroutines[sprayId] = coroutine;
                    }
                    
                    _plugin._lastSprayTime = Time.time;
                    return;
                }
            }
            
            // Fall back to regular spray placement for non-animated cases
            PlaceSpray(position, rotation, localSprayLifetime, localMaxSprays);

            // Send spray to other players
            if (PhotonNetwork.IsConnected)
            {
                // Checks whether private mode is enabled
                if (!_plugin._configManager.myEyesOnly.Value)
                {
                    // Use the URL-based method for regular sprays
                    if (!_plugin._gifManager.IsGifMode)
                    {
                        _plugin._networkUtils.SendUrlSprayToNetwork(
                            hitInfo.point, 
                            hitInfo.normal, 
                            sprayId, 
                            _plugin._configManager.SelectedSprayImage.Value
                        );
                    }
                    else
                    {
                        // GIFs continue to use the direct method since they already use URLs
                        _plugin._networkUtils.SendSprayToNetwork(hitInfo.point, hitInfo.normal, sprayId);
                    }
                }
                
                // If we're the host, start the timer to remove this spray
                if (PhotonNetwork.IsMasterClient && localSprayLifetime > 0)
                {
                    var coroutine = _plugin.StartCoroutine(_plugin.ScheduleSprayRemoval(sprayId, localSprayLifetime));
                    _plugin._sprayRemovalCoroutines[sprayId] = coroutine;
                }
            }

            _plugin._lastSprayTime = Time.time;
        }
        else
        {
            _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] No valid surface detected to spray onto.");
        }
    }
}

/// <summary>
/// Simple component to identify a spray by ID
/// </summary>
public class SprayIdentifier : MonoBehaviour
{
    public string SprayId { get; set; }
}