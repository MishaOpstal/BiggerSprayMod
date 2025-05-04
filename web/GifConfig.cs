using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace BiggerSprayMod.web
{
    [Serializable]
    public class GifEntry
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    [Serializable]
    public class GifConfig
    {
        public List<GifEntry> Gifs { get; set; } = new List<GifEntry>();

        public static GifConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<GifConfig>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BiggerSprayMod] Failed to load GIF config: {ex.Message}");
            }

            return new GifConfig();
        }

        public void Save(string path)
        {
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BiggerSprayMod] Failed to save GIF config: {ex.Message}");
            }
        }

        public static GifConfig CreateDefault()
        {
            return new GifConfig
            {
                Gifs = new List<GifEntry>
                {
                    new GifEntry { Name = "Quack duck", Url = "https://media.tenor.com/fOjhwb3eEqIAAAAi/quack-duck.gif" }
                }
            };
        }
    }
} 