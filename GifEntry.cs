using System;

namespace BiggerSprayMod
{
    [Serializable]
    public class GifEntry
    {
        public string Name;
        public string Url;
        
        public GifEntry(string name, string url)
        {
            Name = name;
            Url = url;
        }
    }
} 