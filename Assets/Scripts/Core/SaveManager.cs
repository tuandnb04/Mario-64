using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public class CourseCoinRecord
{
    public string courseName;
    public int bestCoins;
}

[Serializable]
public class Sm64SaveData
{
    public int totalStars;
    public List<string> collectedStarIds = new();
    public List<CourseCoinRecord> courseCoinRecords = new();
    public List<string> bowserKeysCollected = new();
    public float musicVolume = 0.75f;
    public float sfxVolume = 0.90f;
    public string lastCourseName = "PeachCastleHub";
    public long timestamp;
}

public interface ISaveable
{
    void PopulateSaveData(Sm64SaveData data);
    void LoadFromSaveData(Sm64SaveData data);
}

/// <summary>
/// Persistent Save/Load Manager for Super Mario 64 Remake.
/// Uses atomic temp-swap file I/O in Application.persistentDataPath with XOR obfuscation.
/// </summary>
public class SaveManager : MonoBehaviour
{
    private const string SaveFileName = "sm64_remake_save.json";
    private const string EncryptedHeader = "SM64E1:";
    private const string ObfuscationKey = "PeachCastle1996_SM64Remake_Key";

    private static SaveManager instance;
    public static SaveManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<SaveManager>();
            }
            if (instance == null)
            {
                var go = new GameObject("SaveManager");
                instance = go.AddComponent<SaveManager>();
            }
            instance.EnsureLoaded();
            return instance;
        }
    }

    public static bool HasInstance => instance != null;

    private readonly List<ISaveable> _saveables = new();
    private Sm64SaveData _data = new();
    private bool _loaded;
    private string _overrideSavePath;

    public string SaveFilePath => !string.IsNullOrEmpty(_overrideSavePath)
        ? _overrideSavePath
        : Path.Combine(Application.persistentDataPath, SaveFileName);

    public int TotalStarsCollected
    {
        get
        {
            EnsureLoaded();
            return _data.collectedStarIds != null ? _data.collectedStarIds.Count : _data.totalStars;
        }
    }

    public float MusicVolume
    {
        get
        {
            EnsureLoaded();
            return Mathf.Clamp01(_data.musicVolume);
        }
    }

    public float SfxVolume
    {
        get
        {
            EnsureLoaded();
            return Mathf.Clamp01(_data.sfxVolume);
        }
    }

    public string LastCourseName
    {
        get
        {
            EnsureLoaded();
            return string.IsNullOrEmpty(_data.lastCourseName) ? "PeachCastleHub" : _data.lastCourseName;
        }
    }

    public Sm64SaveData CurrentData
    {
        get
        {
            EnsureLoaded();
            return _data;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
        }
        EnsureLoaded();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public void SetSavePathOverride(string customFilePath, bool reload = true)
    {
        _overrideSavePath = customFilePath;
        _loaded = false;
        if (reload)
        {
            LoadFromDisk();
        }
    }

    public void RegisterSaveable(ISaveable saveable)
    {
        if (saveable == null || _saveables.Contains(saveable)) return;
        _saveables.Add(saveable);
        EnsureLoaded();
        saveable.LoadFromSaveData(_data);
    }

    public void UnregisterSaveable(ISaveable saveable)
    {
        if (saveable == null) return;
        _saveables.Remove(saveable);
    }

    public static string BuildStarId(string courseName, int actIndex)
    {
        var safeCourse = string.IsNullOrEmpty(courseName) ? "UnknownCourse" : courseName.Trim();
        return $"{safeCourse}_Star_{Mathf.Max(1, actIndex)}";
    }

    public bool RecordStarCollected(string courseName, int actIndex, bool autoSave = true)
    {
        EnsureLoaded();
        _data.collectedStarIds ??= new List<string>();
        var id = BuildStarId(courseName, actIndex);
        if (_data.collectedStarIds.Contains(id))
        {
            return false;
        }
        _data.collectedStarIds.Add(id);
        _data.totalStars = _data.collectedStarIds.Count;
        _data.lastCourseName = string.IsNullOrEmpty(courseName) ? _data.lastCourseName : courseName;
        if (autoSave) SaveToDisk();
        return true;
    }

    public bool IsStarCollected(string courseName, int actIndex)
    {
        EnsureLoaded();
        if (_data.collectedStarIds == null) return false;
        return _data.collectedStarIds.Contains(BuildStarId(courseName, actIndex));
    }

    public int GetCourseStarCount(string courseName)
    {
        EnsureLoaded();
        if (_data.collectedStarIds == null || string.IsNullOrEmpty(courseName)) return 0;
        var prefix = courseName.Trim() + "_Star_";
        var count = 0;
        for (var i = 0; i < _data.collectedStarIds.Count; i++)
        {
            if (_data.collectedStarIds[i] != null && _data.collectedStarIds[i].StartsWith(prefix, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    public void RecordCourseCoins(string courseName, int coins, bool autoSave = true)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(courseName)) return;
        _data.courseCoinRecords ??= new List<CourseCoinRecord>();

        var clamped = Mathf.Max(0, coins);
        for (var i = 0; i < _data.courseCoinRecords.Count; i++)
        {
            var rec = _data.courseCoinRecords[i];
            if (rec == null || !string.Equals(rec.courseName, courseName, StringComparison.Ordinal)) continue;
            if (clamped > rec.bestCoins)
            {
                rec.bestCoins = clamped;
                if (autoSave) SaveToDisk();
            }
            return;
        }

        _data.courseCoinRecords.Add(new CourseCoinRecord
        {
            courseName = courseName,
            bestCoins = clamped
        });
        if (autoSave) SaveToDisk();
    }

    public int GetBestCourseCoins(string courseName)
    {
        EnsureLoaded();
        if (_data.courseCoinRecords == null || string.IsNullOrEmpty(courseName)) return 0;
        for (var i = 0; i < _data.courseCoinRecords.Count; i++)
        {
            var rec = _data.courseCoinRecords[i];
            if (rec != null && string.Equals(rec.courseName, courseName, StringComparison.Ordinal))
                return Mathf.Max(0, rec.bestCoins);
        }
        return 0;
    }

    public void RecordBowserKey(string keyId, bool autoSave = true)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(keyId)) return;
        _data.bowserKeysCollected ??= new List<string>();
        if (_data.bowserKeysCollected.Contains(keyId)) return;
        _data.bowserKeysCollected.Add(keyId);
        if (autoSave) SaveToDisk();
    }

    public bool HasBowserKey(string keyId)
    {
        EnsureLoaded();
        return !string.IsNullOrEmpty(keyId) &&
               _data.bowserKeysCollected != null &&
               _data.bowserKeysCollected.Contains(keyId);
    }

    public void RecordAudioSettings(float musicVol, float sfxVol, bool autoSave = true)
    {
        EnsureLoaded();
        _data.musicVolume = Mathf.Clamp01(musicVol);
        _data.sfxVolume = Mathf.Clamp01(sfxVol);
        if (autoSave) SaveToDisk();
    }

    public void RecordLastCourse(string courseName, bool autoSave = false)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(courseName)) return;
        _data.lastCourseName = courseName;
        if (autoSave) SaveToDisk();
    }

    public void SaveToDisk()
    {
        EnsureLoaded();
        for (var i = 0; i < _saveables.Count; i++)
        {
            _saveables[i]?.PopulateSaveData(_data);
        }

        _data.collectedStarIds ??= new List<string>();
        _data.courseCoinRecords ??= new List<CourseCoinRecord>();
        _data.bowserKeysCollected ??= new List<string>();
        _data.totalStars = _data.collectedStarIds.Count;
        _data.timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var json = JsonUtility.ToJson(_data, true);
        var payload = EncryptString(json, ObfuscationKey);

        var path = SaveFilePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmpPath = path + ".tmp";
        var bakPath = path + ".bak";

        try
        {
            File.WriteAllText(tmpPath, payload, Encoding.UTF8);
            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
            }
            if (File.Exists(path))
            {
                File.Move(path, bakPath);
            }
            File.Move(tmpPath, path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SaveManager] Failed to save '{path}': {ex.Message}");
        }
    }

    public void LoadFromDisk()
    {
        _loaded = true;
        var path = SaveFilePath;
        if (!File.Exists(path))
        {
            var bakPath = path + ".bak";
            if (!File.Exists(bakPath))
            {
                _data = new Sm64SaveData();
                NotifySaveablesLoaded();
                return;
            }
            path = bakPath;
        }

        try
        {
            var raw = File.ReadAllText(path, Encoding.UTF8);
            var json = DecryptString(raw, ObfuscationKey);
            var loaded = JsonUtility.FromJson<Sm64SaveData>(json);
            _data = loaded ?? new Sm64SaveData();
            _data.collectedStarIds ??= new List<string>();
            _data.courseCoinRecords ??= new List<CourseCoinRecord>();
            _data.bowserKeysCollected ??= new List<string>();
            _data.totalStars = _data.collectedStarIds.Count;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SaveManager] Failed to load '{path}', starting fresh save: {ex.Message}");
            _data = new Sm64SaveData();
        }

        NotifySaveablesLoaded();
    }

    public void ResetSaveData(bool deleteFile = true)
    {
        _data = new Sm64SaveData();
        _loaded = true;
        if (deleteFile)
        {
            TryDeleteFile(SaveFilePath);
            TryDeleteFile(SaveFilePath + ".tmp");
            TryDeleteFile(SaveFilePath + ".bak");
        }
        NotifySaveablesLoaded();
    }

    private void EnsureLoaded()
    {
        if (!_loaded)
        {
            LoadFromDisk();
        }
    }

    private void NotifySaveablesLoaded()
    {
        for (var i = 0; i < _saveables.Count; i++)
        {
            _saveables[i]?.LoadFromSaveData(_data);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // Ignore cleanup exceptions
        }
    }

    public static string EncryptString(string plainText, string key)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var dataBytes = Encoding.UTF8.GetBytes(plainText);
        var keyBytes = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(key) ? ObfuscationKey : key);
        XorInPlace(dataBytes, keyBytes);
        return EncryptedHeader + Convert.ToBase64String(dataBytes);
    }

    public static string DecryptString(string cipherText, string key)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;
        var trimmed = cipherText.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return trimmed;
        }
        if (!trimmed.StartsWith(EncryptedHeader, StringComparison.Ordinal))
        {
            return trimmed;
        }

        var b64 = trimmed.Substring(EncryptedHeader.Length);
        var dataBytes = Convert.FromBase64String(b64);
        var keyBytes = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(key) ? ObfuscationKey : key);
        XorInPlace(dataBytes, keyBytes);
        return Encoding.UTF8.GetString(dataBytes);
    }

    private static void XorInPlace(byte[] data, byte[] key)
    {
        var keyLen = key.Length;
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(data[i] ^ key[i % keyLen]);
        }
    }
}
