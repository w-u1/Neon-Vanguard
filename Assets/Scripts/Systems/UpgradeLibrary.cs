using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonVanguard
{
    public enum Rarity { Common, Rare, Epic }

    /// <summary>
    /// 升级卡。每张卡只做一件事、只改玩家属性表里的数字 ——
    /// 逻辑集中在 PlayerStats，卡片只是"配置 + 一次 Apply"。
    /// </summary>
    public class UpgradeCard
    {
        public string Id;
        public string Name;
        public string Desc;
        public Rarity Rarity;
        public int MaxStacks = 99;
        public int Stacks;
        public Action<Player> Apply;

        public Color RarityColor
        {
            get
            {
                switch (Rarity)
                {
                    case Rarity.Epic: return new Color(1f, 0.62f, 0.20f);
                    case Rarity.Rare: return new Color(0.62f, 0.45f, 1f);
                    default: return new Color(0.55f, 0.72f, 0.90f);
                }
            }
        }

        public string RarityName
        {
            get
            {
                switch (Rarity)
                {
                    case Rarity.Epic: return "史诗";
                    case Rarity.Rare: return "稀有";
                    default: return "普通";
                }
            }
        }
    }

    /// <summary>升级库。所有可选项集中在这里，方便一眼看全 build 的可能性。</summary>
    public static class UpgradeLibrary
    {
        public static List<UpgradeCard> CreateAll()
        {
            var list = new List<UpgradeCard>();

            list.Add(new UpgradeCard
            {
                Id = "dmg",
                Name = "过载弹头",
                Desc = "全武器伤害 +18%",
                Rarity = Rarity.Common,
                Apply = p => p.Stats.DamageMult *= 1.18f
            });

            list.Add(new UpgradeCard
            {
                Id = "rate",
                Name = "高速循环",
                Desc = "射速 +16%",
                Rarity = Rarity.Common,
                Apply = p => p.Stats.FireRateMult *= 1.16f
            });

            list.Add(new UpgradeCard
            {
                Id = "speed",
                Name = "轻量骨架",
                Desc = "移动速度 +8%",
                Rarity = Rarity.Common,
                Apply = p => p.Stats.MoveSpeed *= 1.08f
            });

            list.Add(new UpgradeCard
            {
                Id = "hp",
                Name = "强化装甲",
                Desc = "最大生命 +22 并立即回复 22",
                Rarity = Rarity.Common,
                Apply = p => { p.Stats.MaxHealth += 22f; p.Heal(22f); }
            });

            list.Add(new UpgradeCard
            {
                Id = "crit",
                Name = "弱点洞察",
                Desc = "暴击率 +9%",
                Rarity = Rarity.Common,
                MaxStacks = 8,
                Apply = p => p.Stats.CritChance += 0.09f
            });

            list.Add(new UpgradeCard
            {
                Id = "magnet",
                Name = "磁力场",
                Desc = "经验拾取范围 +40%",
                Rarity = Rarity.Common,
                MaxStacks = 5,
                Apply = p => p.Stats.MagnetRadius *= 1.4f
            });

            list.Add(new UpgradeCard
            {
                Id = "bulletspd",
                Name = "压缩弹道",
                Desc = "子弹速度 +22%，子弹体积 +12%",
                Rarity = Rarity.Common,
                MaxStacks = 6,
                Apply = p => { p.Stats.BulletSpeedMult *= 1.22f; p.Stats.BulletSizeMult *= 1.12f; }
            });

            list.Add(new UpgradeCard
            {
                Id = "pierce",
                Name = "贯穿弹芯",
                Desc = "子弹额外穿透 +1 个敌人",
                Rarity = Rarity.Rare,
                MaxStacks = 5,
                Apply = p => p.Stats.ExtraPierce += 1
            });

            list.Add(new UpgradeCard
            {
                Id = "multi",
                Name = "分裂枪管",
                Desc = "每次射击额外发射 1 发弹丸",
                Rarity = Rarity.Rare,
                MaxStacks = 6,
                Apply = p => p.Stats.ExtraProjectiles += 1
            });

            list.Add(new UpgradeCard
            {
                Id = "regen",
                Name = "纳米修复",
                Desc = "每秒回复 1.2 点生命",
                Rarity = Rarity.Rare,
                MaxStacks = 6,
                Apply = p => p.Stats.RegenPerSecond += 1.2f
            });

            list.Add(new UpgradeCard
            {
                Id = "armor",
                Name = "能量护盾",
                Desc = "受到的伤害 -8%",
                Rarity = Rarity.Rare,
                MaxStacks = 6,
                Apply = p => p.Stats.Armor += 0.08f
            });

            list.Add(new UpgradeCard
            {
                Id = "dashcd",
                Name = "瞬步核心",
                Desc = "冲刺冷却 -15%",
                Rarity = Rarity.Rare,
                MaxStacks = 5,
                Apply = p => p.Stats.DashCooldown *= 0.85f
            });

            list.Add(new UpgradeCard
            {
                Id = "critdmg",
                Name = "致命打击",
                Desc = "暴击伤害 +60%",
                Rarity = Rarity.Rare,
                MaxStacks = 5,
                Apply = p => p.Stats.CritMult += 0.6f
            });

            list.Add(new UpgradeCard
            {
                Id = "dashextra",
                Name = "多重瞬步",
                Desc = "冲刺次数 +1",
                Rarity = Rarity.Epic,
                MaxStacks = 3,
                Apply = p => p.Stats.ExtraDashes += 1
            });

            list.Add(new UpgradeCard
            {
                Id = "dashdmg",
                Name = "撕裂冲刺",
                Desc = "冲刺路径造成 40 点伤害",
                Rarity = Rarity.Epic,
                MaxStacks = 5,
                Apply = p => p.Stats.DashDamage += 40f
            });

            list.Add(new UpgradeCard
            {
                Id = "nova",
                Name = "冲击新星",
                Desc = "冲刺时释放冲击波（半径 6，可叠加）",
                Rarity = Rarity.Epic,
                MaxStacks = 5,
                Apply = p =>
                {
                    p.Stats.NovaRadius = p.Stats.NovaRadius <= 0f ? 6f : p.Stats.NovaRadius + 1.6f;
                    p.Stats.DashDamage += 12f;
                }
            });

            list.Add(new UpgradeCard
            {
                Id = "thorns",
                Name = "灼热力场",
                Desc = "身周持续造成 14 点/秒伤害",
                Rarity = Rarity.Epic,
                MaxStacks = 5,
                Apply = p => p.Stats.ThornsDamage += 14f
            });

            list.Add(new UpgradeCard
            {
                Id = "explosive",
                Name = "爆裂弹药",
                Desc = "子弹有 18% 概率引发爆炸",
                Rarity = Rarity.Epic,
                MaxStacks = 4,
                Apply = p => p.Stats.ExplosiveChance += 0.18f
            });

            list.Add(new UpgradeCard
            {
                Id = "xpgain",
                Name = "数据虹吸",
                Desc = "获得经验 +25%",
                Rarity = Rarity.Rare,
                MaxStacks = 4,
                Apply = p => p.Stats.XpMult += 0.25f
            });

            return list;
        }

        /// <summary>按稀有度权重抽 3 张不重复、且未满层的卡。</summary>
        public static List<UpgradeCard> Draw(List<UpgradeCard> pool, int count)
        {
            var candidates = new List<UpgradeCard>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i].Stacks < pool[i].MaxStacks) candidates.Add(pool[i]);
            }

            var result = new List<UpgradeCard>();
            int guard = 0;
            while (result.Count < count && candidates.Count > 0 && guard++ < 200)
            {
                float total = 0f;
                for (int i = 0; i < candidates.Count; i++) total += Weight(candidates[i].Rarity);
                float r = UnityEngine.Random.value * total;
                int pick = 0;
                for (int i = 0; i < candidates.Count; i++)
                {
                    r -= Weight(candidates[i].Rarity);
                    if (r <= 0f) { pick = i; break; }
                }
                result.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }
            return result;
        }

        static float Weight(Rarity r)
        {
            switch (r)
            {
                case Rarity.Epic: return 0.85f;
                case Rarity.Rare: return 2.0f;
                default: return 4.2f;
            }
        }
    }
}
