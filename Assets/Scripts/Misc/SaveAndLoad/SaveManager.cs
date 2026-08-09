using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public static class SaveManager
{
    private static readonly string saveFolder = Application.persistentDataPath + "/GameData";

    public static bool Exists(string profileName)
    {
        if (!TryGetPath(profileName, out string path))
        {
            return false;
        }

        return File.Exists(path) || File.Exists(path + ".bak") || File.Exists(path + ".tmp");
    }

    /// <summary>
    /// 删除配置文件
    /// </summary>
    /// <param name="profileName">配置文件名称</param>
    public static void Delete(string profileName)
    {
        if (!TryGetPath(profileName, out string path))
        {
            Debug.LogWarning($"Invalid save profile name: {profileName}");
            return;
        }

        bool deleted = false;
        foreach (string candidate in new[] { path, path + ".tmp", path + ".bak" })
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            File.Delete(candidate);
            deleted = true;
        }

        Debug.Log(deleted
            ? $"Successfully Delete {path}"
            : $"Save Profile {profileName} does not exist");
    }

    /// <summary>
    /// 导出所保存的Json数据
    /// </summary>
    /// <param name="profileName">配置文件名称</param>
    /// <returns>导出的数据</returns>
    public static SaveProfile<T> Load<T>(string profileName) where T : SaveProfileData
    {
        if (!TryLoad(profileName, out SaveProfile<T> save))
            throw new Exception($"Save Profile {profileName} does not exist");

        Debug.Log($"Successfully Load {GetPath(profileName)}");
        return save;
    }

    public static bool TryLoad<T>(string profileName, out SaveProfile<T> save) where T : SaveProfileData
    {
        IReadOnlyList<SaveProfile<T>> candidates = LoadCandidates<T>(profileName);
        if (candidates.Count > 0)
        {
            save = candidates[0];
            return true;
        }

        save = null;
        return false;
    }

    public static IReadOnlyList<SaveProfile<T>> LoadCandidates<T>(string profileName) where T : SaveProfileData
    {
        if (!TryGetPath(profileName, out string path))
        {
            return Array.Empty<SaveProfile<T>>();
        }

        var saves = new List<SaveProfile<T>>(2);
        foreach (string candidate in new[] { path, path + ".bak", path + ".tmp" })
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                SaveProfile<T> save = JsonConvert.DeserializeObject<SaveProfile<T>>(File.ReadAllText(candidate));
                if (save?.saveData != null)
                {
                    saves.Add(save);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to load save profile {candidate}: {exception.Message}");
            }
        }

        return saves;
    }

    /// <summary>
    /// 保存配置文件
    /// </summary>
    /// <param name="save">保存的数据</param>
    public static void Save<T>(SaveProfile<T> save) where T : SaveProfileData
    {
        string path = GetPath(save.profileName);
        if (File.Exists(path))
            throw new Exception($"Save Profile {save.profileName} already exists!");

        var jsonString = JsonConvert.SerializeObject(save,Formatting.Indented,
            new JsonSerializerSettings{ReferenceLoopHandling = ReferenceLoopHandling.Ignore});

        // 当存储路径不存在时
        if (!Directory.Exists(saveFolder))
            Directory.CreateDirectory(saveFolder);

        File.WriteAllText(path, jsonString);
    }

    public static void SaveOrReplace<T>(SaveProfile<T> save) where T : SaveProfileData
    {
        if (!Directory.Exists(saveFolder))
            Directory.CreateDirectory(saveFolder);

        string path = GetPath(save.profileName);
        string temporaryPath = path + ".tmp";
        string backupPath = path + ".bak";
        string jsonString = JsonConvert.SerializeObject(save, Formatting.Indented,
            new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

        File.WriteAllText(temporaryPath, jsonString);
        if (File.Exists(path))
        {
            File.Replace(temporaryPath, path, backupPath);
        }
        else
        {
            File.Move(temporaryPath, path);
        }
    }

    private static string GetPath(string profileName)
    {
        if (!TryGetPath(profileName, out string path))
        {
            throw new ArgumentException("Save profile name must be a file name without path separators.", nameof(profileName));
        }

        return path;
    }

    private static bool TryGetPath(string profileName, out string path)
    {
        if (string.IsNullOrWhiteSpace(profileName) || profileName == "." || profileName == ".." ||
            profileName.IndexOf('/') >= 0 || profileName.IndexOf('\\') >= 0)
        {
            path = null;
            return false;
        }

        path = Path.Combine(saveFolder, profileName);
        return true;
    }
}
