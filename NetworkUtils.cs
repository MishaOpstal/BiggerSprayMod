using System;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Rendering;
using Logger = BepInEx.Logging.Logger;
using System.Collections;
using UnityEngine.Networking;

namespace BiggerSprayMod;

public class NetworkUtils
{
    private BiggerSprayMod _plugin;
    
    // Constants
    public const byte SprayEventCode = 42;
    public const byte SettingsRequestEventCode = 43;
    public const byte SettingsResponseEventCode = 44;
    public const byte GifSprayEventCode = 45;
    public const byte RemoveSprayEventCode = 46;
    public const byte UrlSprayEventCode = 47;
    
    public NetworkUtils(BiggerSprayMod plugin)
    {
        _plugin = plugin;
    }

    public void OnNetworkEvent(EventData photonEvent)
    {
        // Handle different event types
        switch (photonEvent.Code)
        {
            case SprayEventCode:
                _plugin._sprayUtils.HandleSprayEvent(photonEvent);
                break;

            case GifSprayEventCode:
                HandleGifSprayEvent(photonEvent);
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
                
            case RemoveSprayEventCode:
                HandleRemoveSprayEvent(photonEvent);
                break;
                
            case UrlSprayEventCode:
                HandleUrlSprayEvent(photonEvent);
                break;
        }
    }
    
    public void RequestHostSettings()
    {
        if (!PhotonNetwork.IsConnected || PhotonNetwork.IsMasterClient)
            return;

        // Send a request to the host for their settings
        _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Requesting settings from host...");

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

        _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Sending host settings to clients...");

        object[] settingsData =
        [
            _plugin._configManager.SprayLifetimeSeconds.Value,
            _plugin._configManager.MaxSpraysAllowed.Value
        ];

        PhotonNetwork.RaiseEvent(
            SettingsResponseEventCode,
            settingsData,
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable
        );
    }

    public void SendSprayToNetwork(Vector3 hitPoint, Vector3 hitNormal, string sprayId)
    {
        // Checks whether private mode is enabled
        if (_plugin._configManager.myEyesOnly.Value)
        {
            _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Private mode is enabled, not sending spray over network.");
            return;
        }
        
        try
        {
            // Check if it's a GIF spray
            if (_plugin._gifManager.IsGifMode && !string.IsNullOrEmpty(_plugin._gifManager.CurrentGifName))
            {
                SendGifSprayToNetwork(hitPoint, hitNormal, sprayId);
                return;
            }
            
            // Handle regular spray
            byte[] imageData = _plugin._cachedSprayTexture.EncodeToPNG();
            byte[] compressedData = _plugin._imageUtils.CompressImage(imageData);

            if (compressedData.Length > 1000000) // Safety check
            {
                _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Spray image too large to send over network!" +
                    $" ({compressedData.Length} bytes > 1000000 bytes)");
                return;
            }

            // Calculate the contained scale dimensions
            Vector2 adjustedScale = _plugin._scalingUtils.CalculateContainedScale(_plugin._configManager.SprayScale.Value);

            object[] sprayData =
            [
                compressedData,
                hitPoint,
                hitNormal,
                adjustedScale.x,
                adjustedScale.y,
                sprayId // Include the spray ID
            ];

            PhotonNetwork.RaiseEvent(
                SprayEventCode,
                sprayData,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable
            );

            _plugin.LogMessage(LogLevel.Info,$"[BiggerSprayMod] Sent spray to network ({compressedData.Length} bytes)");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error,$"[BiggerSprayMod] Error sending spray: {ex.Message}");
        }
    }
    
    public void SendGifSprayToNetwork(Vector3 hitPoint, Vector3 hitNormal, string sprayId)
    {
        try
        {
            if (string.IsNullOrEmpty(_plugin._gifManager.CurrentGifName)) return;
            
            string gifUrl = _plugin._gifManager.GetGifUrlByName(_plugin._gifManager.CurrentGifName);
            if (string.IsNullOrEmpty(gifUrl))
            {
                _plugin.LogMessage(LogLevel.Warning, "[BiggerSprayMod] Cannot send GIF spray: URL not found");
                return;
            }
            
            // Calculate the contained scale dimensions
            Vector2 adjustedScale = _plugin._scalingUtils.CalculateContainedScale(_plugin._configManager.SprayScale.Value);
            
            // Send only the URL and position, not the full GIF data
            object[] gifSprayData =
            [
                _plugin._gifManager.CurrentGifName,
                gifUrl,
                hitPoint,
                hitNormal,
                adjustedScale.x,
                adjustedScale.y,
                sprayId // Include the spray ID
            ];
            
            PhotonNetwork.RaiseEvent(
                GifSprayEventCode,
                gifSprayData,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable
            );
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Sent GIF spray to network: {_plugin._gifManager.CurrentGifName}");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error sending GIF spray: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Send a message to all clients to remove a specific spray
    /// Only the host should call this
    /// </summary>
    public void SendRemoveSprayToNetwork(string sprayId)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;
            
        try
        {
            PhotonNetwork.RaiseEvent(
                RemoveSprayEventCode,
                sprayId,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable
            );
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Host sent remove spray command for ID: {sprayId}");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error sending remove spray command: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Handle an incoming request from the host to remove a spray
    /// </summary>
    private void HandleRemoveSprayEvent(EventData photonEvent)
    {
        if (PhotonNetwork.IsMasterClient)
            return; // Host shouldn't process its own removal events
            
        try
        {
            string sprayId = (string)photonEvent.CustomData;
            
            // Try to find and remove the spray with this ID
            bool removed = _plugin._sprayUtils.RemoveSprayById(sprayId);
            
            if (removed)
            {
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Removed spray by host command, ID: {sprayId}");
            }
            else
            {
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Spray with ID {sprayId} not found (may already be removed)");
            }
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error handling remove spray event: {ex.Message}");
        }
    }
    
    private void HandleGifSprayEvent(EventData photonEvent)
    {
        try
        {
            // Extract data
            object[] data = (object[])photonEvent.CustomData;
            string gifName = (string)data[0];
            string gifUrl = (string)data[1];
            Vector3 hitPoint = (Vector3)data[2];
            Vector3 hitNormal = (Vector3)data[3];
            float scaleX = (float)data[4];
            float scaleY = (float)data[5];
            string sprayId = data.Length > 6 ? (string)data[6] : Guid.NewGuid().ToString(); // Use provided ID or generate one
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Received GIF spray: {gifName} ({gifUrl})");
            
            // Position for the spray
            Vector3 position = hitPoint + hitNormal * 0.01f;
            Quaternion rotation = Quaternion.LookRotation(hitNormal);
            
            // Always use the URL-based method to benefit from caching, regardless of animation setting
            PlaceAnimatedGifSprayByUrl(
                position,
                rotation,
                gifUrl,
                new Vector2(scaleX, scaleY),
                sprayId,
                0, // Lifetime is now managed by the host, not by timer
                _plugin._hostMaxSprays
            );
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error processing GIF spray: {ex.Message}");
        }
    }
    
    public void PlaceAnimatedGifSpray(Vector3 position, Quaternion rotation, web.WebUtils.GifData gifData, 
        Vector2 scale, string sprayId, float lifetime, int maxSprays)
    {
        try
        {
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Creating animated GIF spray with {gifData.Frames.Count} frames...");

            // Create the spray quad
            GameObject spray = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spray.name = "BiggerSpray_GifInstance";

            // Remove the collider to avoid physics interactions
            UnityEngine.Object.Destroy(spray.GetComponent<Collider>());

            // Position and orient the spray
            spray.transform.position = position;
            spray.transform.rotation = rotation;
            spray.transform.localScale = new Vector3(-scale.x, scale.y, 1.0f);

            // Create a material with the spray texture
            Material sprayMaterial = new Material(_plugin._sprayMaterialTemplate);
            sprayMaterial.mainTexture = gifData.Frames[0]; // Start with first frame

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

            // Add the animator component and initialize it
            web.GifSpriteAnimator animator = spray.AddComponent<web.GifSpriteAnimator>();
            animator.Initialize(gifData, _plugin._configManager.GifAnimationFps.Value);
            
            // Store the spray ID in the user data
            _plugin._sprayUtils.RegisterSpray(spray, sprayId);

            // Add to the list of sprays
            _plugin._spawnedSprays.Add(spray);

            // Set the lifetime if needed and we're not in a networked game (host managed)
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

            _plugin.LogMessage(LogLevel.Info, "[BiggerSprayMod] Animated GIF spray placed successfully.");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error placing animated GIF spray: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Place a GIF spray by its URL, using the asset manager for caching
    /// </summary>
    public void PlaceAnimatedGifSprayByUrl(Vector3 position, Quaternion rotation, string gifUrl, 
        Vector2 scale, string sprayId, float lifetime, int maxSprays)
    {
        try
        {
            if (string.IsNullOrEmpty(gifUrl))
            {
                _plugin.LogMessage(LogLevel.Error, "[BiggerSprayMod] Cannot place GIF spray: URL is empty");
                return;
            }

            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Creating animated GIF spray from URL: {gifUrl}");

            // Create the spray quad
            GameObject spray = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spray.name = "BiggerSpray_GifInstance";

            // Remove the collider to avoid physics interactions
            UnityEngine.Object.Destroy(spray.GetComponent<Collider>());

            // Position and orient the spray
            spray.transform.position = position;
            spray.transform.rotation = rotation;
            spray.transform.localScale = new Vector3(-scale.x, scale.y, 1.0f);

            // Create a material with a default texture (will be updated by the animator)
            Material sprayMaterial = new Material(_plugin._sprayMaterialTemplate);
            
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

            // Add the animator component and initialize it with the URL
            web.GifSpriteAnimator animator = spray.AddComponent<web.GifSpriteAnimator>();
            
            // Initialize with current FPS setting
            animator.Initialize(gifUrl, _plugin._configManager.GifAnimationFps.Value);
            
            // Ensure the pause state matches the current setting
            animator.SetPaused(!_plugin._configManager.AnimateGifsInWorld.Value);
            
            // Store the spray ID in the user data
            _plugin._sprayUtils.RegisterSpray(spray, sprayId);

            // Add to the list of sprays
            _plugin._spawnedSprays.Add(spray);

            // Set the lifetime if needed and we're not in a networked game (host managed)
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

            string animationStatus = _plugin._configManager.AnimateGifsInWorld.Value ? "animated" : "paused";
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] {animationStatus} GIF spray from URL placed successfully.");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error placing animated GIF spray from URL: {ex.Message}");
        }
    }
    
    private void HandleSettingsResponse(EventData photonEvent)
    {
        try
        {
            object[] settingsData = (object[])photonEvent.CustomData;
            _plugin._hostSprayLifetime = (float)settingsData[0];
            _plugin._hostMaxSprays = (int)settingsData[1];

            _plugin.LogMessage(LogLevel.Info,
                $"[BiggerSprayMod] Received host settings: Lifetime={_plugin._hostSprayLifetime}s, MaxSprays={_plugin._hostMaxSprays}");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error,$"[BiggerSprayMod] Error processing settings response: {ex.Message}");
        }
    }

    private void HandleUrlSprayEvent(EventData photonEvent)
    {
        try
        {
            // Extract data
            object[] data = (object[])photonEvent.CustomData;
            string imageUrl = (string)data[0];
            Vector3 hitPoint = (Vector3)data[1];
            Vector3 hitNormal = (Vector3)data[2];
            float scaleX = (float)data[3];
            float scaleY = (float)data[4];
            string sprayId = data.Length > 5 ? (string)data[5] : Guid.NewGuid().ToString(); // Use provided ID or generate one
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Received spray from tmpfiles.org URL: {imageUrl}");
            
            // Position for the spray
            Vector3 position = hitPoint + hitNormal * 0.01f;
            Quaternion rotation = Quaternion.LookRotation(hitNormal);
            
            // Start downloading the image from the URL
            _plugin.StartCoroutine(DownloadAndPlaceSprayFromUrl(
                imageUrl, 
                position, 
                rotation, 
                new Vector2(scaleX, scaleY),
                sprayId,
                0, // Lifetime is managed by the host
                _plugin._hostMaxSprays
            ));
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error processing URL spray: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Downloads an image from a URL and places it as a spray
    /// </summary>
    private IEnumerator DownloadAndPlaceSprayFromUrl(string url, Vector3 position, Quaternion rotation, 
        Vector2 scale, string sprayId, float lifetime, int maxSprays)
    {
        _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Downloading spray from URL: {url}");
        
        // Use our caching downloader
        yield return _plugin._tmpFilesUploader.DownloadImageWithCacheCoroutine(url, texture => {
            if (texture != null)
            {
                // Place the spray with the downloaded texture
                _plugin._sprayUtils.PlaceSprayWithCustomScale(
                    position,
                    rotation,
                    texture,
                    scale,
                    sprayId,
                    lifetime,
                    maxSprays
                );
                
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Successfully placed spray from URL with cached texture");
            }
            else
            {
                _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Failed to download texture from URL: {url}");
            }
        });
    }

    public void SendUrlSprayToNetwork(Vector3 hitPoint, Vector3 hitNormal, string sprayId, string sprayName)
    {
        // Checks whether private mode is enabled
        if (_plugin._configManager.myEyesOnly.Value)
        {
            _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Private mode is enabled, not sending spray over network.");
            return;
        }
        
        try
        {
            if (_plugin._tmpFilesUploader == null || _plugin._cachedSprayTexture == null)
            {
                _plugin.LogMessage(LogLevel.Error, "[BiggerSprayMod] Cannot send URL spray: Uploader or texture is null");
                return;
            }
            
            // Calculate the contained scale dimensions
            Vector2 adjustedScale = _plugin._scalingUtils.CalculateContainedScale(_plugin._configManager.SprayScale.Value);
            
            // Check if we already have this image cached from a previous upload
            if (_plugin._tmpFilesUploader.HasValidCachedUrl(sprayName))
            {
                // Use the cached URL directly
                string cachedUrl = _plugin._tmpFilesUploader.GetCachedUrl(sprayName);
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Using cached tmpfiles.org URL for {sprayName}: {cachedUrl}");
                
                SendUrlToNetwork(cachedUrl, hitPoint, hitNormal, adjustedScale, sprayId);
                return;
            }
            
            // Convert the texture to PNG for upload
            byte[] imageData = _plugin._cachedSprayTexture.EncodeToPNG();
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Uploading spray to tmpfiles.org for network sharing...");
            
            // Upload the image and get a URL
            _plugin._tmpFilesUploader.UploadImage(
                sprayName, 
                imageData, 
                (success, url) => {
                    if (success && !string.IsNullOrEmpty(url))
                    {
                        // Send the URL to other players
                        SendUrlToNetwork(url, hitPoint, hitNormal, adjustedScale, sprayId);
                    }
                    else
                    {
                        // Fall back to the original compressed data method if upload fails
                        _plugin.LogMessage(LogLevel.Warning, "[BiggerSprayMod] Failed to upload to tmpfiles.org, falling back to direct transfer");
                        SendSprayToNetwork(hitPoint, hitNormal, sprayId);
                    }
                }
            );
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error sending URL spray: {ex.Message}");
            // Fall back to original method
            SendSprayToNetwork(hitPoint, hitNormal, sprayId);
        }
    }
    
    /// <summary>
    /// Sends a spray URL to other players
    /// </summary>
    private void SendUrlToNetwork(string url, Vector3 hitPoint, Vector3 hitNormal, Vector2 scale, string sprayId)
    {
        try
        {
            object[] sprayData =
            [
                url,
                hitPoint,
                hitNormal,
                scale.x,
                scale.y,
                sprayId
            ];

            PhotonNetwork.RaiseEvent(
                UrlSprayEventCode,
                sprayData,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable
            );
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Sent tmpfiles.org URL spray to network: {url}");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error sending URL to network: {ex.Message}");
        }
    }
}