using System.Collections.Generic;
using UnityEngine;

namespace BiggerSprayMod.web
{
    /// <summary>
    /// Component that handles animating GIF sprays in the world
    /// </summary>
    public class GifSpriteAnimator : MonoBehaviour
    {
        // Animation properties
        private List<Texture2D> _frames = new List<Texture2D>();
        private List<float> _delays = new List<float>();
        private int _currentFrameIndex = 0;
        private float _nextFrameTime = 0f;
        private MeshRenderer _renderer;
        private float _animationFps = 30f; // Default FPS
        
        // Reference tracking
        private string _gifUrl = string.Empty;
        private bool _isInitialized = false;
        private bool _isPaused = false;
        
        /// <summary>
        /// Initialize the animator with a URL, using the asset manager to get frames
        /// </summary>
        public void Initialize(string gifUrl, float fps = 30f)
        {
            if (string.IsNullOrEmpty(gifUrl)) return;
            
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) return;
            
            _gifUrl = gifUrl;
            _animationFps = fps;
            
            // Respect the global animation setting
            _isPaused = !BiggerSprayMod.Instance._configManager.AnimateGifsInWorld.Value;
            
            // Register with the asset manager
            BiggerSprayMod.Instance._gifAssetManager.RegisterAnimator(gifUrl, this);
            
            // Request the asset from the manager
            BiggerSprayMod.Instance._gifAssetManager.GetOrLoadGifAsset(gifUrl, (success, asset) => {
                if (success && asset != null && asset.Frames.Count > 0)
                {
                    RefreshWithAsset(asset.Frames, asset.Delays);
                }
                else
                {
                    // Use default sprite
                    _isInitialized = false;
                }
            });
        }
        
        /// <summary>
        /// Initialize directly with GIF data (for compatibility with previous code)
        /// </summary>
        public void Initialize(WebUtils.GifData gifData, float fps = 30f)
        {
            if (gifData == null || gifData.Frames == null || gifData.Frames.Count == 0) return;
            
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) return;
            
            _animationFps = fps;
            
            // Respect the global animation setting
            _isPaused = !BiggerSprayMod.Instance._configManager.AnimateGifsInWorld.Value;
            
            // Set frames directly (not using asset manager)
            RefreshWithAsset(gifData.Frames, gifData.Delays);
        }
        
        /// <summary>
        /// Refresh the animator with new frames and delays
        /// </summary>
        public void RefreshWithAsset(List<Texture2D> frames, List<float> delays)
        {
            if (frames == null || frames.Count == 0 || _renderer == null) return;
            
            // Store references to the frames and delays
            _frames = frames;
            _delays = delays;
            
            // Reset animation state
            _currentFrameIndex = 0;
            _nextFrameTime = Time.time + GetDelay(_currentFrameIndex);
            _isInitialized = true;
            
            // Set initial frame
            UpdateFrame();
        }
        
        /// <summary>
        /// Pause or resume the animation
        /// </summary>
        public void SetPaused(bool isPaused)
        {
            if (_isPaused == isPaused) return; // No change
            
            _isPaused = isPaused;
            
            if (!_isPaused)
            {
                // When resuming, reset the next frame time so it animates immediately
                _nextFrameTime = Time.time;
            }
        }
        
        private void Update()
        {
            if (!_isInitialized || _frames == null || _frames.Count == 0 || _renderer == null) return;
            
            // Skip animation update if paused
            if (_isPaused) return;

            if (Time.time >= _nextFrameTime)
            {
                // Advance to next frame
                _currentFrameIndex = (_currentFrameIndex + 1) % _frames.Count;
                
                // Set the time for the next frame
                _nextFrameTime = Time.time + GetDelay(_currentFrameIndex);
                
                // Update the texture
                UpdateFrame();
            }
        }
        
        private float GetDelay(int frameIndex)
        {
            // Safe delay calculation with fallbacks
            float delay = 0.033f; // Default ~ 30fps
            
            if (_delays != null && frameIndex >= 0 && frameIndex < _delays.Count)
            {
                delay = _delays[frameIndex];
                if (delay <= 0.001f) // Prevent division by zero or tiny values
                {
                    delay = _animationFps > 0 ? 1f / _animationFps : 0.033f;
                }
            }
            else if (_animationFps > 0)
            {
                delay = 1f / _animationFps;
            }
            
            return delay;
        }
        
        private void UpdateFrame()
        {
            if (!_isInitialized || _frames == null || _currentFrameIndex < 0 || 
                _currentFrameIndex >= _frames.Count || _frames[_currentFrameIndex] == null || 
                _renderer == null) return;
            
            // Check if the renderer is still valid
            try
            {
                _renderer.material.mainTexture = _frames[_currentFrameIndex];
            }
            catch (System.Exception)
            {
                // If we get an error, the renderer or material is likely destroyed
                Cleanup();
            }
        }
        
        private void Cleanup()
        {
            // Don't destroy the textures since they're managed by the asset manager
            _frames = null;
            _delays = null;
            _renderer = null;
            _isInitialized = false;
            
            // Unregister from asset manager if we have a URL
            if (!string.IsNullOrEmpty(_gifUrl))
            {
                BiggerSprayMod.Instance._gifAssetManager.UnregisterAnimator(_gifUrl, this);
            }
            
            // Destroy the component
            Destroy(this);
        }
        
        private void OnDestroy()
        {
            // Unregister from asset manager if we have a URL
            if (!string.IsNullOrEmpty(_gifUrl))
            {
                BiggerSprayMod.Instance._gifAssetManager.UnregisterAnimator(_gifUrl, this);
            }
            
            // Just clear references, don't destroy textures
            _frames = null;
            _delays = null;
            _renderer = null;
            _isInitialized = false;
        }
    }
} 