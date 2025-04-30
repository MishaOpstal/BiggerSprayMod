using BepInEx.Logging;
using UnityEngine;

namespace BiggerSprayMod
{
    public class InputManager
    {
        private readonly BiggerSprayMod _plugin;
        
        public InputManager(BiggerSprayMod plugin)
        {
            _plugin = plugin;
        }
        
        private bool _webGifInitialized = false;
        
        public void ProcessInputs()
        {
            // First time using web GIFs, try to load it
            if (_plugin._configManager.UseWebGifs.Value && !_webGifInitialized)
            {
                _webGifInitialized = true;
                TryLoadWebGif();
            }
            
            // Handle image selection
            if (Input.GetKeyDown(_plugin._configManager.PreviousSprayKey.Value))
            {
                if (_plugin._configManager.UseWebGifs.Value)
                {
                    _plugin._configManager.SelectPreviousWebGif();
                    TryLoadWebGif();
                }
                else
                {
                    SelectPreviousSpray();
                }
            }
            else if (Input.GetKeyDown(_plugin._configManager.NextSprayKey.Value))
            {
                if (_plugin._configManager.UseWebGifs.Value)
                {
                    _plugin._configManager.SelectNextWebGif();
                    TryLoadWebGif();
                }
                else
                {
                    SelectNextSpray();
                }
            }
            
            // Handle cycling between local and web GIFs
            if (Input.GetKeyDown(_plugin._configManager.CycleGifTypeKey.Value))
            {
                _plugin._configManager.UseWebGifs.Value = !_plugin._configManager.UseWebGifs.Value;
                _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Switched to {(_plugin._configManager.UseWebGifs.Value ? "web" : "local")} GIFs");
                
                if (_plugin._configManager.UseWebGifs.Value)
                {
                    TryLoadWebGif();
                }
                else
                {
                    // Switch back to local GIF/image
                    string path = System.IO.Path.Combine(_plugin._imagesFolderPath, _plugin._configManager.SelectedSprayImage.Value);
                    if (path.ToLower().EndsWith(".gif"))
                    {
                        _plugin._imageUtils.LoadGifTexture(path);
                    }
                    else
                    {
                        _plugin._cachedSprayTexture = _plugin._imageUtils.LoadTexture(path);
                        _plugin._imageUtils.ClearGifData();
                        
                        if (_plugin._cachedSprayTexture != null)
                        {
                            _plugin._originalImageDimensions = new Vector2(_plugin._cachedSprayTexture.width, _plugin._cachedSprayTexture.height);
                        }
                    }
                }
            }

            // Handle scaling mode
            if (Input.GetKeyDown(_plugin._configManager.ScaleKey.Value))
            {
                ScalingUtils.StartScalingMode();
            }
            else if (Input.GetKeyUp(_plugin._configManager.ScaleKey.Value))
            {
                ScalingUtils.StopScalingMode();
            }

            // Handle key-based scale adjustments
            if (Input.GetKeyDown(_plugin._configManager.IncreaseScaleKey.Value))
            {
                _plugin._scalingUtils.AdjustScale(_plugin._configManager.ScaleSpeed.Value);
            }
            else if (Input.GetKeyDown(_plugin._configManager.DecreaseScaleKey.Value))
            {
                _plugin._scalingUtils.AdjustScale(-_plugin._configManager.ScaleSpeed.Value);
            }

            // Handle scroll wheel for scaling
            if (_plugin._isScaling && _plugin._configManager.UseScrollWheel.Value)
            {
                float scrollDelta = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scrollDelta) > 0.01f)
                {
                    _plugin._scalingUtils.AdjustScale(scrollDelta * _plugin._configManager.ScaleSpeed.Value);
                }
            }

            // Update scaling preview if active
            if (_plugin._isScaling)
            {
                _plugin._scalingUtils.UpdateScalingPreview();
            }
            else if (Input.GetKeyDown(_plugin._configManager.SprayKey.Value))
            {
                _plugin._sprayUtils.TrySpray();
            }
        }
        
        public void TryLoadWebGif()
        {
            GifEntry selectedEntry = _plugin._configManager.GetSelectedWebGifEntry();
            if (selectedEntry == null)
            {
                _plugin.LogMessage(LogLevel.Warning, "[BiggerSprayMod] No web GIF selected");
                return;
            }
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Loading web GIF: {selectedEntry.Name} ({selectedEntry.Url})");
            
            // Check if already cached
            if (_plugin._webUtils.HasCachedGif(selectedEntry.Url))
            {
                var gifData = _plugin._webUtils.GetCachedGif(selectedEntry.Url);
                ApplyWebGifData(gifData);
            }
            else
            {
                // Start download
                _plugin._webUtils.StartGifDownload(selectedEntry.Url, (success, gifData) => {
                    if (success && gifData != null)
                    {
                        ApplyWebGifData(gifData);
                    }
                });
            }
        }
        
        private void ApplyWebGifData(WebUtils.GifData gifData)
        {
            if (gifData == null || gifData.Frames.Count == 0)
                return;
                
            // Apply the web GIF data
            _plugin._imageUtils.ClearGifData();
            
            // Set the main GIF data
            _plugin._isAnimatedGif = true;
            _plugin._gifFrames.AddRange(gifData.Frames);
            _plugin._gifFrameDelays.AddRange(gifData.Delays);
            _plugin._currentGifFrame = 0;
            _plugin._gifTimeSinceLastFrame = 0f;
            
            // Set the cached texture to the first frame
            _plugin._cachedSprayTexture = gifData.Frames[0];
            _plugin._originalImageDimensions = new Vector2(
                gifData.Frames[0].width,
                gifData.Frames[0].height
            );
            
            _plugin.LogMessage(LogLevel.Info, $"[BiggerSprayMod] Applied web GIF with {gifData.Frames.Count} frames");
        }
        
        private void SelectPreviousSpray()
        {
            int currentIndex = _plugin._availableImages.IndexOf(_plugin._configManager.SelectedSprayImage.Value);
            int newIndex = (currentIndex - 1 + _plugin._availableImages.Count) % _plugin._availableImages.Count;
            _plugin._configManager.SelectedSprayImage.Value = _plugin._availableImages[newIndex];
        }
        
        private void SelectNextSpray()
        {
            int currentIndex = _plugin._availableImages.IndexOf(_plugin._configManager.SelectedSprayImage.Value);
            int newIndex = (currentIndex + 1) % _plugin._availableImages.Count;
            _plugin._configManager.SelectedSprayImage.Value = _plugin._availableImages[newIndex];
        }
    }
} 