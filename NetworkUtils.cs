using System;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
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
            
            // Prepare frame data for network transmission
            byte[][] compressedFrames = new byte[frameCount][];
            float[] frameDelays = new float[frameCount];
            
            // Total size check
            long totalSize = 0;
            
            // Process each frame
            for (int i = 0; i < frameCount; i++)
            {
                // Compress frame
                byte[] frameData = _plugin._gifFrames[i].EncodeToPNG();
                byte[] compressedFrame = _plugin._imageUtils.CompressImage(frameData);
                compressedFrames[i] = compressedFrame;
                frameDelays[i] = _plugin._gifFrameDelays[i];
                
                totalSize += compressedFrame.Length;
            }
            
            // Check if total size is too big
            if (totalSize > 6000000) // 2MB limit for all frames
            {
                _plugin.LogMessage(LogLevel.Warning,$"[BiggerSprayMod] GIF too large to send over network ({totalSize} bytes)!");
                return;
            }
            
            // Create GIF spray data packet
            object[] gifSprayData =
            [
                frameCount,             // Number of frames
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
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Sent GIF with {frameCount} frames to network ({totalSize} bytes)");
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error,$"[BiggerSprayMod] Error sending GIF spray: {ex.Message}");
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