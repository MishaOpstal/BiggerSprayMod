using UnityEngine;

namespace BiggerSprayMod
{
    /// <summary>
    /// Simple component to mark a spray as a GIF animation
    /// </summary>
    public class GifSprayComponent : MonoBehaviour
    {
        public bool IsGif { get; private set; } = false;
        
        public void Initialize(bool isGif)
        {
            IsGif = isGif;
        }
    }
} 