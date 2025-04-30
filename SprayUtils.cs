using System;
using System.IO;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace BiggerSprayMod;

public class SprayUtils
{
    private BiggerSprayMod _plugin;
    
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
        // Calculate contained scale
        Vector2 adjustedScale = _plugin._scalingUtils.CalculateContainedScale(_plugin._configManager.SprayScale.Value);
        PlaceSprayWithCustomScale(position, rotation, _plugin._cachedSprayTexture, adjustedScale, lifetime, maxSprays);
    }

    public void PlaceSprayWithCustomScale(Vector3 position, Quaternion rotation, Texture2D texture,
        Vector2 scale, float lifetime, int maxSprays)
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
            
            // Flip it horizontally
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
            
            // Store GIF data for animation if this is a GIF spray
            if (_plugin._isAnimatedGif)
            {
                // Add component to track this is a GIF spray
                spray.AddComponent<GifSprayComponent>().Initialize(_plugin._gifFrames.Count > 0);
            }

            // Add to the list of sprays
            _plugin._spawnedSprays.Add(spray);

            // Set the lifetime if needed
            if (lifetime > 0)
            {
                UnityEngine.Object.Destroy(spray, lifetime);
            }

            // Handle max sprays limit
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
                _plugin._hostSprayLifetime, 
                _plugin._hostMaxSprays
            );

            _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] Received and placed remote spray.");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error,$"[BiggerSprayMod] Error processing remote spray: {ex.Message}");
        }
    }
    
    public void HandleGifSprayEvent(EventData photonEvent)
    {
        try
        {
            // Extract GIF data
            object[] data = (object[])photonEvent.CustomData;
            int frameCount = (int)data[0];
            byte[][] compressedFrames = (byte[][])data[1];
            float[] frameDelays = (float[])data[2];
            Vector3 hitPoint = (Vector3)data[3];
            Vector3 hitNormal = (Vector3)data[4];
            float scaleX = (float)data[5];
            float scaleY = (float)data[6];
            
            _plugin.LogMessage(LogLevel.Info,$"[BiggerSprayMod] Received GIF with {frameCount} frames");
            
            // Clear any existing GIF data
            _plugin._imageUtils.ClearGifData();
            
            // Process all frames
            for (int i = 0; i < frameCount; i++)
            {
                // Decompress the frame
                byte[] imageData = _plugin._imageUtils.DecompressImage(compressedFrames[i]);
                
                // Create texture for this frame
                Texture2D frameTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                frameTexture.LoadImage(imageData);
                
                // Add to GIF data
                _plugin._gifFrames.Add(frameTexture);
                _plugin._gifFrameDelays.Add(frameDelays[i]);
            }
            
            // Set as animated GIF
            _plugin._isAnimatedGif = true;
            _plugin._currentGifFrame = 0;
            _plugin._gifTimeSinceLastFrame = 0f;
            
            // Use the first frame as the initial texture
            if (_plugin._gifFrames.Count > 0)
            {
                _plugin._cachedSprayTexture = _plugin._gifFrames[0];
                _plugin._originalImageDimensions = new Vector2(
                    _plugin._gifFrames[0].width,
                    _plugin._gifFrames[0].height
                );
                
                // Place the spray with the first frame
                Vector3 position = hitPoint + hitNormal * 0.01f;
                Quaternion rotation = Quaternion.LookRotation(hitNormal);
                
                // Create a local copy of this GIF spray
                GameObject spray = GameObject.CreatePrimitive(PrimitiveType.Quad);
                spray.name = "BiggerSpray_GifInstance";
                
                // Remove the collider to avoid physics interactions
                UnityEngine.Object.Destroy(spray.GetComponent<Collider>());
                
                // Position and orient the spray
                spray.transform.position = position;
                spray.transform.rotation = rotation;
                
                // Apply scale
                spray.transform.localScale = new Vector3(-scaleX, scaleY, 1.0f);
                
                // Create a material with the spray texture
                Material sprayMaterial = new Material(_plugin._sprayMaterialTemplate);
                sprayMaterial.mainTexture = _plugin._gifFrames[0];
                
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
                
                // Add GIF component
                spray.AddComponent<GifSprayComponent>().Initialize(true);
                
                // Add to the list of sprays
                _plugin._spawnedSprays.Add(spray);
                
                // Set the lifetime if needed
                if (_plugin._hostSprayLifetime > 0)
                {
                    UnityEngine.Object.Destroy(spray, _plugin._hostSprayLifetime);
                }
                
                // Handle max sprays limit
                if (_plugin._spawnedSprays.Count > _plugin._hostMaxSprays)
                {
                    // Remove the oldest sprays when we exceed the limit
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
                
                _plugin.LogMessage(LogLevel.Info,"[BiggerSprayMod] GIF spray placed successfully");
            }
            else
            {
                _plugin.LogMessage(LogLevel.Error,"[BiggerSprayMod] No valid GIF frames received");
            }
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error,$"[BiggerSprayMod] Error processing GIF spray: {ex.Message}");
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
        bool sendToNetwork = false;
        string selectedImagePath = Path.Combine(_plugin._imagesFolderPath ?? _plugin.GetPluginPath(), _plugin._configManager.SelectedSprayImage.Value);
        if (File.Exists(selectedImagePath))
        {
            long fileSize = new FileInfo(selectedImagePath).Length;
            if (fileSize > 5000000) // 5 MB limit
            {
                _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Selected image is too large to spray.");
                
                if (_plugin._configManager.ShowSprayIfLarge.Value)
                {
                    sendToNetwork = true;
                }
                else
                {
                    _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] Not sending large image to network.");
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
                _plugin._networkUtils.SendSprayToNetwork(hitInfo.point, hitInfo.normal);
            }

            _plugin._lastSprayTime = Time.time;
        }
        else
        {
            _plugin.LogMessage(LogLevel.Warning,"[BiggerSprayMod] No valid surface detected to spray onto.");
        }
    }
    
    public void UpdateGifAnimations()
    {
        // Only update if we have GIF frames and animation is enabled
        if (!_plugin._isAnimatedGif || !_plugin._configManager.AnimateGifs.Value || _plugin._gifFrames.Count <= 1)
            return;
        
        // Update the time since last frame
        _plugin._gifTimeSinceLastFrame += Time.deltaTime;
        
        // Check if it's time to show the next frame
        float frameDelay = _plugin._gifFrameDelays[_plugin._currentGifFrame];
        
        // If the delay is too small, use a minimum based on the configured FPS
        float minimumFrameTime = 1.0f / _plugin._configManager.GifFps.Value;
        
        // Use the larger of the two values to prevent too rapid updates
        float effectiveDelay = Mathf.Max(frameDelay, minimumFrameTime);
        
        if (_plugin._gifTimeSinceLastFrame >= effectiveDelay)
        {
            // Move to next frame
            _plugin._currentGifFrame = (_plugin._currentGifFrame + 1) % _plugin._gifFrames.Count;
            _plugin._gifTimeSinceLastFrame = 0f;
            
            // Update the cached texture to the new frame
            _plugin._cachedSprayTexture = _plugin._gifFrames[_plugin._currentGifFrame];
            
            // Update all active GIF sprays
            UpdateActiveGifSprayTextures();
        }
    }
    
    private void UpdateActiveGifSprayTextures()
    {
        foreach (GameObject spray in _plugin._spawnedSprays)
        {
            if (spray == null) continue;
            
            // Check if this is a GIF spray
            GifSprayComponent gifComponent = spray.GetComponent<GifSprayComponent>();
            if (gifComponent != null && gifComponent.IsGif)
            {
                // Update the texture on the material
                MeshRenderer renderer = spray.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                {
                    renderer.material.mainTexture = _plugin._gifFrames[_plugin._currentGifFrame];
                }
            }
        }
    }
}