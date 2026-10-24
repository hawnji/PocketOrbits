using System;
using System.Collections.Generic;
using PocketOrbits.Core.Utilities;
using J = PocketOrbits.Core.Utilities.MiniJson;

namespace PocketOrbits.Core.Saving
{
    public sealed class LevelRecord
    {
        public bool Completed;
        public int BestStars;
        public int BestMoves = int.MaxValue;
        public int Attempts;
    }

    public sealed class GameSettings
    {
        public float MusicVolume = 0.6f;
        public float SfxVolume = 0.8f;
        public bool Haptics = true;
        public bool HighContrast;
        /// <summary>UI text scale multiplier (0.8 - 1.6).</summary>
        public float TextScale = 1f;
        /// <summary>Visual effect intensity (0 = minimal, 1 = full).</summary>
        public float EffectsIntensity = 1f;
        public bool TrajectoryPreview = true;
        /// <summary>0 = battery saver (30 fps, minimal effects), 1 = balanced, 2 = high.</summary>
        public int Quality = 1;

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        public void Sanitize()
        {
            MusicVolume = Clamp01(MusicVolume);
            SfxVolume = Clamp01(SfxVolume);
            EffectsIntensity = Clamp01(EffectsIntensity);
            if (float.IsNaN(TextScale)) TextScale = 1f;
            TextScale = Math.Max(0.8f, Math.Min(1.6f, TextScale));
            if (Quality < 0) Quality = 0;
            if (Quality > 2) Quality = 2;
        }

        static float Clamp01(float v) => float.IsNaN(v) ? 0.5f : v < 0 ? 0 : v > 1 ? 1 : v;
    }

    /// <summary>Everything persisted locally. Versioned; see <see cref="SaveSerializer"/>.</summary>
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public readonly Dictionary<string, LevelRecord> Levels = new Dictionary<string, LevelRecord>();
        public GameSettings Settings = new GameSettings();
        public string LastPlayedLevel;
        public int TotalLaunches;

        public LevelRecord Get(string levelId)
        {
            if (!Levels.TryGetValue(levelId, out var r))
            {
                r = new LevelRecord();
                Levels[levelId] = r;
            }
            return r;
        }

        public int StarsFor(string levelId) => Levels.TryGetValue(levelId, out var r) ? r.BestStars : 0;
        public bool IsCompleted(string levelId) => Levels.TryGetValue(levelId, out var r) && r.Completed;
    }

    public enum LoadStatus { Loaded = 0, NoSaveFound = 1, CorruptReset = 2, MigratedFromOlderVersion = 3, NewerVersionReset = 4 }

    public static class SaveSerializer
    {
        public static string ToJson(SaveData data)
        {
            var levels = new Dictionary<string, object>();
            foreach (var kv in data.Levels)
            {
                levels[kv.Key] = new Dictionary<string, object>
                {
                    { "completed", kv.Value.Completed },
                    { "bestStars", kv.Value.BestStars },
                    { "bestMoves", kv.Value.BestMoves == int.MaxValue ? -1 : kv.Value.BestMoves },
                    { "attempts", kv.Value.Attempts },
                };
            }
            var s = data.Settings;
            var root = new Dictionary<string, object>
            {
                { "version", data.Version },
                { "levels", levels },
                {
                    "settings", new Dictionary<string, object>
                    {
                        { "musicVolume", (double)s.MusicVolume },
                        { "sfxVolume", (double)s.SfxVolume },
                        { "haptics", s.Haptics },
                        { "highContrast", s.HighContrast },
                        { "textScale", (double)s.TextScale },
                        { "effectsIntensity", (double)s.EffectsIntensity },
                        { "trajectoryPreview", s.TrajectoryPreview },
                        { "quality", s.Quality },
                    }
                },
                { "lastPlayedLevel", data.LastPlayedLevel },
                { "totalLaunches", data.TotalLaunches },
            };
            return MiniJson.Serialize(root, pretty: false);
        }

        /// <summary>
        /// Never throws. Missing, corrupt or unknown-future data yields a fresh default save with a status
        /// telling the caller what happened (so it can keep a backup of the bad file).
        /// </summary>
        public static SaveData FromJson(string json, out LoadStatus status)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                status = LoadStatus.NoSaveFound;
                return new SaveData();
            }
            try
            {
                var root = J.Obj(J.Parse(json), "save");
                int version = J.Int(root, "version", 0);
                if (version > SaveData.CurrentVersion)
                {
                    status = LoadStatus.NewerVersionReset;
                    return new SaveData();
                }
                var data = new SaveData();
                if (root.TryGetValue("levels", out object lo) && lo != null)
                {
                    foreach (var kv in J.Obj(lo, "levels"))
                    {
                        var r = J.Obj(kv.Value, "level record");
                        int bestMoves = J.Int(r, "bestMoves", -1);
                        data.Levels[kv.Key] = new LevelRecord
                        {
                            Completed = J.Bool(r, "completed", false),
                            BestStars = Math.Max(0, Math.Min(3, J.Int(r, "bestStars", 0))),
                            BestMoves = bestMoves < 0 ? int.MaxValue : bestMoves,
                            Attempts = Math.Max(0, J.Int(r, "attempts", 0)),
                        };
                        // A record with stars is by definition completed.
                        if (data.Levels[kv.Key].BestStars > 0) data.Levels[kv.Key].Completed = true;
                    }
                }
                if (root.TryGetValue("settings", out object so) && so != null)
                {
                    var o = J.Obj(so, "settings");
                    var s = data.Settings;
                    s.MusicVolume = (float)J.Num(o, "musicVolume", s.MusicVolume);
                    s.SfxVolume = (float)J.Num(o, "sfxVolume", s.SfxVolume);
                    s.Haptics = J.Bool(o, "haptics", s.Haptics);
                    s.HighContrast = J.Bool(o, "highContrast", s.HighContrast);
                    s.TextScale = (float)J.Num(o, "textScale", s.TextScale);
                    s.EffectsIntensity = (float)J.Num(o, "effectsIntensity", s.EffectsIntensity);
                    s.TrajectoryPreview = J.Bool(o, "trajectoryPreview", s.TrajectoryPreview);
                    s.Quality = J.Int(o, "quality", s.Quality);
                    s.Sanitize();
                }
                data.LastPlayedLevel = J.Str(root, "lastPlayedLevel", null);
                data.TotalLaunches = Math.Max(0, J.Int(root, "totalLaunches", 0));
                status = version < SaveData.CurrentVersion ? LoadStatus.MigratedFromOlderVersion : LoadStatus.Loaded;
                data.Version = SaveData.CurrentVersion;
                return data;
            }
            catch (Exception)
            {
                status = LoadStatus.CorruptReset;
                return new SaveData();
            }
        }
    }

    /// <summary>Platform storage abstraction (file system in Unity, memory in tests).</summary>
    public interface ISaveStorage
    {
        /// <summary>Returns null when nothing is stored.</summary>
        string Read();
        void Write(string contents);
        /// <summary>Keeps a copy of unreadable data for diagnostics.</summary>
        void Backup(string contents);
    }

    public sealed class MemorySaveStorage : ISaveStorage
    {
        public string Contents;
        public string BackupContents;
        public int Writes;
        public string Read() => Contents;
        public void Write(string contents) { Contents = contents; Writes++; }
        public void Backup(string contents) { BackupContents = contents; }
    }

    /// <summary>Loads and saves <see cref="SaveData"/> through an <see cref="ISaveStorage"/>.</summary>
    public sealed class SaveService
    {
        readonly ISaveStorage _storage;
        public SaveData Data { get; private set; }
        public LoadStatus LastLoadStatus { get; private set; }

        public SaveService(ISaveStorage storage)
        {
            _storage = storage;
            Data = new SaveData();
        }

        public SaveData Load()
        {
            string raw = null;
            try { raw = _storage.Read(); }
            catch (Exception) { raw = null; }
            Data = SaveSerializer.FromJson(raw, out var status);
            LastLoadStatus = status;
            if (status == LoadStatus.CorruptReset || status == LoadStatus.NewerVersionReset)
            {
                try { _storage.Backup(raw); } catch (Exception) { /* best effort */ }
            }
            return Data;
        }

        public bool Save()
        {
            try
            {
                _storage.Write(SaveSerializer.ToJson(Data));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
