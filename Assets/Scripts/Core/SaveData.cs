using System;
using System.IO;
using UnityEngine;

namespace NeonVanguard
{
    /// <summary>跨局持久化的存档数据（最高分 / 累计统计）。</summary>
    [Serializable]
    public class SaveData
    {
        public int BestScore;
        public int BestWave;
        public int TotalKills;
        public int TotalRuns;
        public float BestTime;

        const string FileName = "neonvanguard_save.json";

        [NonSerialized] static SaveData _current;
        [NonSerialized] static bool _loaded;

        public static string FilePath
        {
            get { return Path.Combine(Application.persistentDataPath, FileName); }
        }

        public static SaveData Current
        {
            get
            {
                if (_loaded && _current != null) return _current;
                _loaded = true;
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string json = File.ReadAllText(FilePath);
                        _current = JsonUtility.FromJson<SaveData>(json);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Save] 读取存档失败，使用默认值：" + e.Message);
                }
                if (_current == null) _current = new SaveData();
                return _current;
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] 写入存档失败：" + e.Message);
            }
        }

        /// <summary>一局结束时结算，返回是否刷新了最高分。</summary>
        public bool CommitRun(int score, int wave, int kills, float time)
        {
            TotalRuns++;
            TotalKills += kills;
            bool record = score > BestScore;
            if (record) BestScore = score;
            if (wave > BestWave) BestWave = wave;
            if (time > BestTime) BestTime = time;
            Save();
            return record;
        }
    }

    /// <summary>
    /// 单局运行时数据。GameManager 每局开始调用 Reset()，
    /// 其他系统只读这里的累计值，避免到处互相引用。
    /// </summary>
    public static class RunState
    {
        public static int Wave;
        public static int Score;
        public static int Kills;
        public static float Elapsed;
        public static int Level = 1;
        public static int Xp;
        public static int XpRequired = 8;

        public static void Reset()
        {
            Wave = 0;
            Score = 0;
            Kills = 0;
            Elapsed = 0f;
            Level = 1;
            Xp = 0;
            XpRequired = XpForLevel(1);
        }

        public static int XpForLevel(int level)
        {
            // 经验曲线：前期升得快（快速给到正反馈），后期增长放缓并封顶，
            // 避免高等级时每级需要上千经验、升级感消失。
            int req = Mathf.RoundToInt(8f + 5f * level + 0.4f * level * level);
            return Mathf.Min(req, 600);
        }

        /// <summary>加经验，返回本次升了几级。</summary>
        public static int AddXp(int amount)
        {
            Xp += amount;
            int gained = 0;
            int guard = 0;
            while (Xp >= XpRequired && guard++ < 32)
            {
                Xp -= XpRequired;
                Level++;
                gained++;
                XpRequired = XpForLevel(Level);
            }
            return gained;
        }

        public static void AddScore(int amount)
        {
            Score += amount;
        }
    }
}
