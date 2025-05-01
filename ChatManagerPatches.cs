using System;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;
using BiggerSprayMod.web;
using HarmonyLib;
using UnityEngine;

namespace BiggerSprayMod
{
    [HarmonyPatch(typeof(ChatManager))]
    public class ChatManagerPatches
    {
        [HarmonyPatch("MessageSend")]
        [HarmonyPrefix]
        private static bool MessageSendPreFix(ChatManager __instance, bool _possessed)
        {
            // Check for GIF spray command
            if (Regex.IsMatch(__instance.chatMessage, @"^\/addgifspray\s+(.+)$"))
            {
                // Extract GIF name from command
                Match match = Regex.Match(__instance.chatMessage, @"^\/addgifspray\s+(.+)$");
                if (match.Success && match.Groups.Count > 1)
                {
                    string gifName = match.Groups[1].Value.Trim();
                    
                    // Don't allow empty names
                    if (string.IsNullOrWhiteSpace(gifName))
                    {
                        // Change the message to indicate an error
                        __instance.chatMessage = "GIF name cannot be empty!";
                        return true;
                    }
                    
                    // Sanitize the GIF name
                    gifName = GifDataSender.SanitizeGifName(gifName);
                    
                    // Get URL from clipboard
                    string clipboardText = GUIUtility.systemCopyBuffer;
                    
                    if (string.IsNullOrWhiteSpace(clipboardText) || !clipboardText.StartsWith("http"))
                    {
                        // Change the message to indicate an error
                        __instance.chatMessage = "Invalid URL in clipboard!";
                        return true;
                    }
                    
                    // Validate URL with WebUtils
                    bool isValid = BiggerSprayMod.Instance._webUtils.IsTrustedUrl(clipboardText);
                    
                    if (!isValid)
                    {
                        // Change the message to indicate an error
                        __instance.chatMessage = "URL is not trusted!";
                        return true;
                    }
                    
                    // Add GIF to config
                    bool success = AddGifToConfig(gifName, clipboardText);
                    
                    if (success)
                    {
                        // Refresh the GIF list
                        BiggerSprayMod.Instance._gifManager.RefreshGifList();
                        __instance.chatMessage = $"Added GIF: {gifName}";
                    }
                    else
                    {
                        // Change the message to indicate an error
                        __instance.chatMessage = "Failed to add GIF!";
                    }
                    
                    return true;
                }
            }
            
            // Check for regular spray command
            if (Regex.IsMatch(__instance.chatMessage, @"^\/addspray\s+(.+)$"))
            {
                // Extract spray name from command
                Match match = Regex.Match(__instance.chatMessage, @"^\/addspray\s+(.+)$");
                if (match.Success && match.Groups.Count > 1)
                {
                    string sprayName = match.Groups[1].Value.Trim();
                    
                    // Don't allow empty names
                    if (string.IsNullOrWhiteSpace(sprayName))
                    {
                        // Change the message to indicate an error
                        __instance.chatMessage = "Spray name cannot be empty!";
                        return true;
                    }
                    
                    // Sanitize the spray name
                    sprayName = GifDataSender.SanitizeGifName(sprayName); // Reuse the same sanitization method
                    
                    // Get URL from clipboard
                    string clipboardText = GUIUtility.systemCopyBuffer;
                    
                    if (string.IsNullOrWhiteSpace(clipboardText) || !clipboardText.StartsWith("http"))
                    {
                        // Change the message to indicate an error
                        __instance.chatMessage = "Invalid URL in clipboard!";
                        return true;
                    }
                    
                    // Validate URL with WebUtils
                    bool isValid = BiggerSprayMod.Instance._webUtils.IsTrustedUrl(clipboardText);
                    
                    if (!isValid)
                    {
                        // Change the message to indicate an error
                        __instance.chatMessage = "URL is not trusted!";
                        return true;
                    }
                    
                    // Download and save the spray image
                    BiggerSprayMod.Instance.StartCoroutine(
                        BiggerSprayMod.Instance._webUtils.DownloadImageCoroutine(
                            clipboardText, 
                            sprayName, 
                            (success) => {
                                if (success)
                                {
                                    // Refresh the spray images list
                                    BiggerSprayMod.Instance._imageUtils.LoadAvailableImages();
                                    BiggerSprayMod.Instance._configManager.UpdateImageListConfig();
                                    BiggerSprayMod.Instance.LogMessage(BepInEx.Logging.LogLevel.Info, $"[BiggerSprayMod] Added spray image: {sprayName}");
                                }
                                else
                                {
                                    BiggerSprayMod.Instance.LogMessage(BepInEx.Logging.LogLevel.Error, $"[BiggerSprayMod] Failed to add spray image: {sprayName}");
                                }
                            }
                        )
                    );
                    
                    // Change the message to indicate success
                    __instance.chatMessage = $"Downloading spray: {sprayName}...";
                    return true;
                }
            }

            return true;
        }
        
        private static bool AddGifToConfig(string gifName, string gifUrl)
        {
            try
            {
                string configPath = Path.Combine(Paths.ConfigPath, "BiggerSprayGifs.json");
                
                // Load existing config or create new one
                GifConfig config;
                if (File.Exists(configPath))
                {
                    config = GifConfig.Load(configPath);
                }
                else
                {
                    config = GifConfig.CreateDefault();
                }
                
                // Check if a GIF with this name already exists
                bool exists = false;
                foreach (var gif in config.Gifs)
                {
                    if (gif.Name.Equals(gifName, StringComparison.OrdinalIgnoreCase))
                    {
                        gif.Url = gifUrl; // Update URL if name exists
                        exists = true;
                        break;
                    }
                }
                
                // Add new entry if it doesn't exist
                if (!exists)
                {
                    config.Gifs.Add(new GifEntry { Name = gifName, Url = gifUrl });
                }
                
                // Save the config
                config.Save(configPath);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}