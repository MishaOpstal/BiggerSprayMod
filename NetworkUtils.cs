using System;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Rendering;
using Logger = BepInEx.Logging.Logger;

namespace BiggerSprayMod;

public class NetworkUtils
{
    private BiggerSprayMod _plugin;
    
    // Constants
    public const byte SprayEventCode = 42;
    public const byte SettingsRequestEventCode = 43;
    public const byte SettingsResponseEventCode = 44;
    public const byte GifSprayEventCode = 45;
    public const byte WebGifSprayEventCode = 46;
    
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
                _plugin._sprayUtils.HandleGifSprayEvent(photonEvent);
                break;
                
            case WebGifSprayEventCode:
                HandleWebGifSprayEvent(photonEvent);
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
    
    private void HandleWebGifSprayEvent(EventData photonEvent)
    {
        try
        {
            // Extract web GIF data
            object[] data = (object[])photonEvent.CustomData;
            string gifUrl = (string)data[0];
            string gifName = (string)data[1];
            Vector3 hitPoint = (Vector3)data[2];
            Vector3 hitNormal = (Vector3)data[3];
            float scaleX = (float)data[4];
            float scaleY = (float)data[5];
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Received web GIF spray: {gifName} ({gifUrl})");
            
            // Validate URL
            if (!_plugin._webUtils.IsTrustedUrl(gifUrl))
            {
                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Ignoring untrusted URL: {gifUrl}");
                return;
            }
            
            // Check if already cached locally
            if (_plugin._webUtils.HasCachedGif(gifUrl))
            {
                var gifData = _plugin._webUtils.GetCachedGif(gifUrl);
                PlaceWebGifSpray(gifData, hitPoint, hitNormal, scaleX, scaleY);
            }
            else
            {
                // Need to download the GIF
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Downloading remote GIF: {gifUrl}");
                
                // Create temporary placeholder while downloading
                Vector2 scale = new Vector2(scaleX, scaleY);
                Vector3 position = hitPoint + hitNormal * 0.01f;
                Quaternion rotation = Quaternion.LookRotation(hitNormal);
                
                GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Quad);
                placeholder.name = "BiggerSpray_WebGifPlaceholder";
                
                // Remove collider
                UnityEngine.Object.Destroy(placeholder.GetComponent<Collider>());
                
                // Position it
                placeholder.transform.position = position;
                placeholder.transform.rotation = rotation;
                placeholder.transform.localScale = new Vector3(-scale.x, scale.y, 1.0f);
                
                // Create loading material
                Material placeholderMat = new Material(_plugin._sprayMaterialTemplate);
                placeholderMat.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
                placeholder.GetComponent<MeshRenderer>().material = placeholderMat;
                
                // Add to spray list to manage lifetime
                _plugin._spawnedSprays.Add(placeholder);
                
                // Download the GIF
                _plugin._webUtils.StartGifDownload(gifUrl, (success, gifData) => {
                    // Remove placeholder
                    _plugin._spawnedSprays.Remove(placeholder);
                    UnityEngine.Object.Destroy(placeholder);
                    
                    if (success && gifData != null)
                    {
                        PlaceWebGifSpray(gifData, hitPoint, hitNormal, scaleX, scaleY);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error handling web GIF spray: {ex.Message}");
        }
    }
    
    private void PlaceWebGifSpray(WebUtils.GifData gifData, Vector3 hitPoint, Vector3 hitNormal, float scaleX, float scaleY)
    {
        if (gifData == null || gifData.Frames.Count == 0)
        {
            _plugin.LogMessage(LogLevel.Error, "[BiggerSprayMod] No valid frames for web GIF spray");
            return;
        }
        
        try
        {
            // Create spray object
            Vector3 position = hitPoint + hitNormal * 0.01f;
            Quaternion rotation = Quaternion.LookRotation(hitNormal);
            
            GameObject spray = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spray.name = "BiggerSpray_WebGifInstance";
            
            // Remove collider
            UnityEngine.Object.Destroy(spray.GetComponent<Collider>());
            
            // Position and orient
            spray.transform.position = position;
            spray.transform.rotation = rotation;
            spray.transform.localScale = new Vector3(-scaleX, scaleY, 1.0f);
            
            // Create material with first frame
            Material sprayMat = new Material(_plugin._sprayMaterialTemplate);
            sprayMat.mainTexture = gifData.Frames[0];
            
            // Apply shader settings for transparency
            sprayMat.SetFloat("_Mode", 2); // Fade mode
            sprayMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            sprayMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            sprayMat.SetInt("_ZWrite", 0);
            sprayMat.DisableKeyword("_ALPHATEST_ON");
            sprayMat.EnableKeyword("_ALPHABLEND_ON");
            sprayMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            sprayMat.renderQueue = 3000;
            
            // Assign the material
            spray.GetComponent<MeshRenderer>().material = sprayMat;
            
            // Add GIF component with frames
            GifSprayComponent gifComponent = spray.AddComponent<GifSprayComponent>();
            gifComponent.InitializeWithFrames(gifData.Frames, gifData.Delays);
            
            // Add to the list of sprays
            _plugin._spawnedSprays.Add(spray);
            
            // Set lifetime
            if (_plugin._hostSprayLifetime > 0)
            {
                UnityEngine.Object.Destroy(spray, _plugin._hostSprayLifetime);
            }
            
            // Handle max sprays limit
            if (_plugin._spawnedSprays.Count > _plugin._hostMaxSprays)
            {
                // Remove oldest sprays when exceeding the limit
                while (_plugin._spawnedSprays.Count > _plugin._hostMaxSprays)
                {
                    GameObject oldest = _plugin._spawnedSprays[0];
                    _plugin._spawnedSprays.RemoveAt(0);
                    
                    if (oldest != null)
                    {
                        UnityEngine.Object.Destroy(oldest);
                    }
                }
            }
            
            _plugin.LogMessage(LogLevel.Info, "[BiggerSprayMod] Web GIF spray placed successfully");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error placing web GIF spray: {ex.Message}");
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

    public void SendSprayToNetwork(Vector3 hitPoint, Vector3 hitNormal)
    {
        try
        {
            // Handle web GIFs mode
            if (_plugin._configManager.UseWebGifs.Value)
            {
                SendWebGifToNetwork(hitPoint, hitNormal);
                return;
            }
            
            // Handle animated GIFs differently than static images
            if (_plugin._isAnimatedGif && _plugin._gifFrames.Count > 0)
            {
                SendGifToNetwork(hitPoint, hitNormal);
                return;
            }
            
            // For static images, send as usual
            Texture2D textureToSend = _plugin._cachedSprayTexture;
            
            if (textureToSend == null)
            {
                _plugin.LogMessage(LogLevel.Error,"[BiggerSprayMod] No valid texture to send over network!");
                return;
            }
            
            // Compress texture to reduce network traffic
            byte[] imageData = textureToSend.EncodeToPNG();
            byte[] compressedData = _plugin._imageUtils.CompressImage(imageData);

            if (compressedData.Length > 6000000) // Safety check
            {
                _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Spray image too large to send over network!");
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
                adjustedScale.y
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

    public void SendGifToNetwork(Vector3 hitPoint, Vector3 hitNormal)
    {
        try
        {
            // Prepare GIF data: frames and delays
            int frameCount = _plugin._gifFrames.Count;
            if (frameCount == 0)
            {
                _plugin.LogMessage(LogLevel.Error,"[BiggerSprayMod] No GIF frames to send over network!");
                return;
            }
            
            // Calculate the contained scale dimensions
            Vector2 adjustedScale = _plugin._scalingUtils.CalculateContainedScale(_plugin._configManager.SprayScale.Value);
            
            // Limit frame count for network transmission to reduce size
            int maxNetworkFrames = 50; // Hard limit to 50 frames for network transmission
            if (frameCount > maxNetworkFrames)
            {
                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Limiting GIF to {maxNetworkFrames} frames for network transmission");
                frameCount = maxNetworkFrames;
            }
            
            // Prepare frame data for network transmission
            byte[][] compressedFrames = new byte[frameCount][];
            float[] frameDelays = new float[frameCount];
            
            // Total size check
            long totalSize = 0;
            int actualFrameCount = 0;
            
            // Process each frame
            for (int i = 0; i < frameCount; i++)
            {
                try
                {
                    // Skip null frames for safety
                    if (_plugin._gifFrames[i] == null) continue;
                    
                    // Compress frame
                    byte[] frameData = _plugin._gifFrames[i].EncodeToPNG();
                    byte[] compressedFrame = _plugin._imageUtils.CompressImage(frameData);
                    
                    // Check individual frame size
                    if (compressedFrame.Length > 1000000) // 1MB per frame limit
                    {
                        _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Frame {i} is too large ({compressedFrame.Length} bytes), skipping");
                        continue;
                    }
                    
                    // Check if adding this frame would exceed total size limit
                    if (totalSize + compressedFrame.Length > 3000000) // 3MB total limit
                    {
                        _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] GIF size limit reached at frame {i}, truncating");
                        break;
                    }
                    
                    compressedFrames[actualFrameCount] = compressedFrame;
                    frameDelays[actualFrameCount] = _plugin._gifFrameDelays[i];
                    totalSize += compressedFrame.Length;
                    actualFrameCount++;
                }
                catch (Exception ex)
                {
                    _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error processing frame {i}: {ex.Message}");
                }
            }
            
            // No valid frames
            if (actualFrameCount == 0)
            {
                _plugin.LogMessage(LogLevel.Error, "[BiggerSprayMod] No valid frames to send");
                return;
            }
            
            // Resize arrays if needed
            if (actualFrameCount < frameCount)
            {
                Array.Resize(ref compressedFrames, actualFrameCount);
                Array.Resize(ref frameDelays, actualFrameCount);
            }
            
            // Create GIF spray data packet
            object[] gifSprayData =
            [
                actualFrameCount,       // Number of frames
                compressedFrames,       // Compressed frame data
                frameDelays,            // Frame delay timings
                hitPoint,               // Hit position
                hitNormal,              // Hit normal
                adjustedScale.x,        // Scale X
                adjustedScale.y         // Scale Y
            ];
            
            // Send GIF data to network
            PhotonNetwork.RaiseEvent(
                GifSprayEventCode,
                gifSprayData,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable
            );
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Sent GIF with {actualFrameCount} frames to network ({totalSize} bytes)");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error,$"[BiggerSprayMod] Error sending GIF spray: {ex.Message}");
        }
    }
    
    public void SendWebGifToNetwork(Vector3 hitPoint, Vector3 hitNormal)
    {
        try
        {
            // Get the selected web GIF entry
            GifEntry selectedEntry = _plugin._configManager.GetSelectedWebGifEntry();
            if (selectedEntry == null)
            {
                _plugin.LogMessage(LogLevel.Warning, "[BiggerSprayMod] No web GIF selected to send");
                return;
            }
            
            // Validate URL
            if (!_plugin._webUtils.IsTrustedUrl(selectedEntry.Url))
            {
                _plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Cannot send untrusted URL: {selectedEntry.Url}");
                return;
            }
            
            // Calculate the contained scale dimensions
            Vector2 adjustedScale = _plugin._scalingUtils.CalculateContainedScale(_plugin._configManager.SprayScale.Value);
            
            // Create the data to send
            object[] webGifData =
            [
                selectedEntry.Url,
                selectedEntry.Name,
                hitPoint,
                hitNormal,
                adjustedScale.x,
                adjustedScale.y
            ];
            
            // Send the URL and metadata instead of the actual GIF
            PhotonNetwork.RaiseEvent(
                WebGifSprayEventCode,
                webGifData,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable
            );
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Sent web GIF reference: {selectedEntry.Name} ({selectedEntry.Url})");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error sending web GIF: {ex.Message}");
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
}