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
            // For animated GIFs, we only send the current frame
            Texture2D textureToSend = _plugin._cachedSprayTexture;
            
            if (textureToSend == null)
            {
                _plugin.LogMessage(LogLevel.Error,"[BiggerSprayMod] No valid texture to send over network!");
                return;
            }
            
            // Compress texture to reduce network traffic
            byte[] imageData = textureToSend.EncodeToPNG();
            byte[] compressedData = _plugin._imageUtils.CompressImage(imageData);

            if (compressedData.Length > 500000) // Safety check
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

            if (_plugin._isAnimatedGif)
            {
                _plugin.LogMessage(LogLevel.Info,$"[BiggerSprayMod] Sent GIF frame to network ({compressedData.Length} bytes)");
            }
            else
            {
                _plugin.LogMessage(LogLevel.Info,$"[BiggerSprayMod] Sent spray to network ({compressedData.Length} bytes)");
            }
        }
        catch (Exception ex)
        {
            _plugin.LogMessage(LogLevel.Error,$"[BiggerSprayMod] Error sending spray: {ex.Message}");
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