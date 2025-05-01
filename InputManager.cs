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
        
        public void ProcessInputs()
        {
            // Handle GIF mode toggle
            if (Input.GetKeyDown(_plugin._configManager.ToggleGifModeKey.Value))
            {
                _plugin._gifManager.ToggleGifMode();
            }
            
            // Handle image selection based on current mode
            if (_plugin._gifManager.IsGifMode)
            {
                // GIF mode navigation
                if (Input.GetKeyDown(_plugin._configManager.PreviousSprayKey.Value))
                {
                    _plugin._gifManager.SelectPreviousGif();
                    // Sync the config setting
                    if (!string.IsNullOrEmpty(_plugin._gifManager.CurrentGifName))
                    {
                        _plugin._configManager.SelectedGifName.Value = _plugin._gifManager.CurrentGifName;
                    }
                }
                else if (Input.GetKeyDown(_plugin._configManager.NextSprayKey.Value))
                {
                    _plugin._gifManager.SelectNextGif();
                    // Sync the config setting
                    if (!string.IsNullOrEmpty(_plugin._gifManager.CurrentGifName))
                    {
                        _plugin._configManager.SelectedGifName.Value = _plugin._gifManager.CurrentGifName;
                    }
                }
            }
            else
            {
                // Regular image navigation
                if (Input.GetKeyDown(_plugin._configManager.PreviousSprayKey.Value))
                {
                    SelectPreviousSpray();
                }
                else if (Input.GetKeyDown(_plugin._configManager.NextSprayKey.Value))
                {
                    SelectNextSpray();
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