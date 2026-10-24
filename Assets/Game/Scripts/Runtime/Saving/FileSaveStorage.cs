using System;
using System.IO;
using PocketOrbits.Core.Saving;
using UnityEngine;

namespace PocketOrbits.Saving
{
    /// <summary>
    /// Local JSON save file in Application.persistentDataPath. Writes go to a temp file first and are then
    /// swapped in, so a crash or power loss mid-write cannot corrupt the existing save.
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        readonly string _path;

        public FileSaveStorage(string fileName = "pocket_orbits_save.json")
        {
            _path = Path.Combine(Application.persistentDataPath, fileName);
        }

        public string FilePath => _path;

        public string Read()
        {
            if (File.Exists(_path)) return File.ReadAllText(_path);
            // Recover from an interrupted swap.
            string tmp = _path + ".tmp";
            return File.Exists(tmp) ? File.ReadAllText(tmp) : null;
        }

        public void Write(string contents)
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, contents);
            // Keep the previous save as .bak, then move the new file into place. If we are interrupted between
            // the two moves, Read() falls back to the .tmp file, which is complete at this point.
            string bak = _path + ".bak";
            if (File.Exists(_path))
            {
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(_path, bak);
            }
            File.Move(tmp, _path);
        }

        public void Backup(string contents)
        {
            if (contents == null) return;
            string name = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.WriteAllText(name, contents);
            Debug.LogWarning("[PocketOrbits] Save data was unreadable; a copy was kept at " + name);
        }
    }
}
