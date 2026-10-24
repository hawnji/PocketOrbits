using System;
using System.Collections.Generic;
using PocketOrbits.Core.Levels;
using UnityEngine;

namespace PocketOrbits.Levels
{
    /// <summary>Loads level JSON TextAssets from Resources/Levels and drops (and logs) any that fail validation.</summary>
    public static class LevelLoader
    {
        public const string ResourcePath = "Levels";

        public static LevelCatalog LoadCatalog()
        {
            var levels = new List<LevelDefinition>();
            foreach (var asset in Resources.LoadAll<TextAsset>(ResourcePath))
            {
                try
                {
                    var level = LevelJson.Parse(asset.text, asset.name);
                    var report = LevelValidator.Validate(level);
                    if (!report.Ok)
                    {
                        Debug.LogError("[PocketOrbits] Level " + asset.name + " is invalid and was skipped:\n" + report);
                        continue;
                    }
                    levels.Add(level);
                }
                catch (FormatException e)
                {
                    Debug.LogError("[PocketOrbits] Could not parse level " + asset.name + ": " + e.Message);
                }
            }
            if (levels.Count == 0) Debug.LogError("[PocketOrbits] No levels found in Resources/" + ResourcePath);
            return new LevelCatalog(levels);
        }
    }
}
