using System;
using BepInEx.Logging;

namespace BiggerSprayMod.web
{
    /// <summary>
    /// Utility class to handle GIF data serialization for network transmission
    /// </summary>
    public static class GifDataSender
    {
        /// <summary>
        /// Creates a network-safe package containing the GIF URL
        /// </summary>
        public static string CreateGifPackage(string gifName, string gifUrl)
        {
            // For future expansion, we could implement additional packing/compression here
            return gifUrl;
        }
        
        /// <summary>
        /// Extracts the GIF URL from a network package
        /// </summary>
        public static string ExtractGifUrl(string package)
        {
            // Currently we just pass the URL directly
            return package;
        }
        
        /// <summary>
        /// Sanitizes a GIF name to be safe for file system
        /// </summary>
        public static string SanitizeGifName(string gifName)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                gifName = gifName.Replace(c, '_');
            }
            
            return gifName.Trim();
        }
        
        /// <summary>
        /// Validates a GIF URL is acceptable
        /// </summary>
        public static bool ValidateGifUrl(string url, BiggerSprayMod plugin)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                {
                    plugin.LogMessage(LogLevel.Warning, "[BiggerSprayMod] Empty GIF URL");
                    return false;
                }
                
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Invalid GIF URL protocol: {url}");
                    return false;
                }
                
                if (!plugin._webUtils.IsTrustedUrl(url))
                {
                    plugin.LogMessage(LogLevel.Warning, $"[BiggerSprayMod] Untrusted GIF URL: {url}");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                plugin.LogMessage(LogLevel.Error, $"[BiggerSprayMod] Error validating GIF URL: {ex.Message}");
                return false;
            }
        }
    }
} 