using UnityEngine;

namespace NeonVanguard
{
    public enum WeaponKind { Pistol, Shotgun, Railgun }

    /// <summary>
    /// 玩家属性集合。所有升级都只是改这里的数字，
    /// 好处是数值来源唯一、方便调参，也方便在工程师面试里讲"数据驱动"。
    /// </summary>
    [System.Serializable]
    public class PlayerStats
    {
        // 移动
        public float MoveSpeed = 11.5f;
        public float DashSpeed = 44f;
        public float DashDuration = 0.16f;
        public float DashCooldown = 1.1f;
        public int ExtraDashes = 0;

        // 生存
        public float MaxHealth = 100f;
        public float RegenPerSecond = 0f;
        public float Armor = 0f;               // 减伤百分比 0..0.6

        // 输出
        public float DamageMult = 1f;
        public float FireRateMult = 1f;
        public float CritChance = 0.06f;
        public float CritMult = 2.2f;
        public int ExtraProjectiles = 0;       // 额外弹丸数（霰弹/散射类）
        public int ExtraPierce = 0;
        public float BulletSpeedMult = 1f;
        public float BulletSizeMult = 1f;

        // 特殊机制
        public float NovaRadius = 0f;          // 冲刺时的新星半径（0 表示未解锁）
        public float DashDamage = 0f;          // 冲刺路径伤害
        public float ThornsDamage = 0f;        // 近身反伤光环每秒伤害
        public float MagnetRadius = 4f;      // 经验拾取半径（太小会导致大量经验球白白过期）
        public float XpMult = 1f;
        public float NovaCooldown = 0f;        // 新星内置冷却
        public float ExplosiveChance = 0f;     // 子弹爆炸概率

        public void Clamp()
        {
            Armor = Mathf.Clamp(Armor, 0f, 0.6f);
            CritChance = Mathf.Clamp01(CritChance);
            MoveSpeed = Mathf.Clamp(MoveSpeed, 4f, 26f);
            FireRateMult = Mathf.Clamp(FireRateMult, 0.25f, 4f);
        }
    }

    /// <summary>单把武器的静态配置。</summary>
    public class WeaponDef
    {
        public WeaponKind Kind;
        public string Name;
        public float Cooldown;        // 基础射击间隔（秒）
        public float Damage;          // 单发/单弹丸伤害
        public int Pellets;           // 每次开火弹丸数
        public float SpreadDeg;       // 散布角度
        public float Speed;           // 弹速
        public float Life;            // 存活时间
        public int Pierce;            // 可穿透敌人数（不含首次命中）
        public float Size;            // 子弹尺寸
        public float Knockback;
        public Color Tint;
        public bool AutoFire;         // 是否按住即连发
        public float Recoil;          // 后坐力（给屏幕和玩家一个反向位移）
    }

    public static class WeaponLibrary
    {
        public static readonly WeaponDef Pistol = new WeaponDef
        {
            Kind = WeaponKind.Pistol,
            Name = "脉冲手枪",
            Cooldown = 0.135f,
            Damage = 11f,
            Pellets = 1,
            SpreadDeg = 3.2f,
            Speed = 46f,
            Life = 1.0f,
            Pierce = 0,
            Size = 0.19f,
            Knockback = 5.5f,
            Tint = new Color(0.55f, 0.98f, 1f),
            AutoFire = true,
            Recoil = 0.6f
        };

        public static readonly WeaponDef Shotgun = new WeaponDef
        {
            Kind = WeaponKind.Shotgun,
            Name = "碎裂霰弹",
            Cooldown = 0.56f,
            Damage = 7.5f,
            Pellets = 7,
            SpreadDeg = 26f,
            Speed = 34f,
            Life = 0.36f,
            Pierce = 0,
            Size = 0.2f,
            Knockback = 13f,
            Tint = new Color(1f, 0.78f, 0.30f),
            AutoFire = true,
            Recoil = 3.2f
        };

        public static readonly WeaponDef Railgun = new WeaponDef
        {
            Kind = WeaponKind.Railgun,
            Name = "轨道炮",
            Cooldown = 0.95f,
            Damage = 46f,
            Pellets = 1,
            SpreadDeg = 0f,
            Speed = 78f,
            Life = 1.1f,
            Pierce = 6,
            Size = 0.34f,
            Knockback = 9f,
            Tint = new Color(0.72f, 0.45f, 1f),
            AutoFire = true,
            Recoil = 4.5f
        };

        public static WeaponDef Get(WeaponKind k)
        {
            switch (k)
            {
                case WeaponKind.Shotgun: return Shotgun;
                case WeaponKind.Railgun: return Railgun;
                default: return Pistol;
            }
        }

        public static readonly WeaponKind[] Order = { WeaponKind.Pistol, WeaponKind.Shotgun, WeaponKind.Railgun };
    }
}
