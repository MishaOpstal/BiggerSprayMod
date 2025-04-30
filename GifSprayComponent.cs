using UnityEngine;
using System.Collections.Generic;

namespace BiggerSprayMod
{
    /// <summary>
    /// Component to manage GIF animation for individual sprays
    /// </summary>
    public class GifSprayComponent : MonoBehaviour
    {
        public bool IsGif { get; private set; } = false;
        
        // Per-spray GIF data
        public List<Texture2D> Frames { get; private set; } = new List<Texture2D>();
        public List<float> FrameDelays { get; private set; } = new List<float>();
        public int CurrentFrame { get; private set; } = 0;
        public float TimeSinceLastFrame { get; private set; } = 0f;
        
        private MeshRenderer _renderer;
        private BiggerSprayMod _plugin;
        
        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _plugin = BiggerSprayMod.Instance;
        }
        
        public void Initialize(bool isGif)
        {
            IsGif = isGif;
        }
        
        public void InitializeWithFrames(List<Texture2D> frames, List<float> delays)
        {
            IsGif = true;
            Frames = new List<Texture2D>(frames);
            FrameDelays = new List<float>(delays);
            CurrentFrame = 0;
            TimeSinceLastFrame = 0f;
            
            // Set initial frame
            if (Frames.Count > 0 && _renderer != null && _renderer.material != null)
            {
                _renderer.material.mainTexture = Frames[0];
            }
        }
        
        public void UpdateAnimation(float deltaTime, float minFrameTime)
        {
            if (!IsGif || Frames == null || Frames.Count <= 1 || _renderer == null || _renderer.material == null)
                return;
                
            // Safety check for index bounds
            if (CurrentFrame < 0 || CurrentFrame >= Frames.Count || CurrentFrame >= FrameDelays.Count)
            {
                CurrentFrame = 0;
                TimeSinceLastFrame = 0f;
                return;
            }
                
            // Update the time since last frame
            TimeSinceLastFrame += deltaTime;
            
            // Check if it's time to show the next frame
            float frameDelay = FrameDelays[CurrentFrame];
            
            // Use the larger of the two values to prevent too rapid updates
            float effectiveDelay = Mathf.Max(frameDelay, minFrameTime);
            
            if (TimeSinceLastFrame >= effectiveDelay)
            {
                // Move to next frame (with safety check)
                CurrentFrame = (CurrentFrame + 1) % Frames.Count;
                TimeSinceLastFrame = 0f;
                
                // Additional safety check
                if (CurrentFrame >= 0 && CurrentFrame < Frames.Count && Frames[CurrentFrame] != null)
                {
                    // Update the texture
                    _renderer.material.mainTexture = Frames[CurrentFrame];
                }
            }
        }
        
        public void CleanupFrames()
        {
            // Don't destroy the frames as they might be shared
            Frames.Clear();
            FrameDelays.Clear();
        }
    }
} 