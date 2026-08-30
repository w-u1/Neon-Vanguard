using UnityEngine;

namespace NeonVanguard
{
    public enum EnemyKind { Chaser, Tank, Ranged, Boss, Dasher, Splitter }

    /// <summary>
    /// 敌人定义（数据驱动）。所有敌人都由这张表生成，不含任何预制体。
    /// 这样调平衡只需要改数字，也方便在面试里讲"配置与逻辑分离"。
    /// </summary>
    public class EnemyDef
    {
        public EnemyKind Kind;
        public string Name;

        public float Health;
        public float Speed;
        public float TurnRate = 8f;
        public float ContactDamage;      // 接触伤害（每秒）
        public float Radius = 0.5f;      // 碰撞半径
        public float Mass = 1f;          // 击退抗性，越大越难被推
        public float Armor;              // 0..0.8 减伤

        public int Score;
        public int XpDrop;

        public Color BodyColor;
        public Color AccentColor;
        public PrimitiveType Shape = PrimitiveType.Cube;
        public Vector3 ShapeScale = Vector3.one;

        /// <summary>Resources/Models 下的模型名（Kenney Space Kit）。为空则用程序化几何体。</summary>
        public string ModelName;
        /// <summary>模型缩放到的高度（世界单位）。</summary>
        public float ModelHeight = 1.2f;

        // 远程
        public float PreferredRange = 11f;
        public float FireInterval = 1.9f;
        public float BulletDamage = 9f;
        public float BulletSpeed = 17f;
        public float TelegraphTime = 0.45f;

        // Boss
        public bool IsBoss;
        public float BossBurstInterval = 3.6f;
        public int BossBurstCount = 18;
        public float BossChargeInterval = 6.2f;
        public float BossSummonInterval = 10.5f;

        // 冲锋者：周期性蓄力后高速突进，逼玩家横向闪避
        public float DashInterval = 3.2f;
        public float DashWindup = 0.55f;
        public float DashSpeed = 22f;
        public float DashDuration = 0.45f;

        // 分裂体：死亡后分裂出若干小型单位
        public int SplitCount;
        public float SplitHealthScale = 0.45f;

        // 精英：体型更大、更肉，掉落与分数更高
        public bool IsElite;
        public float EliteScale = 1.45f;
    }

    public static class EnemyCatalog
    {
        /// <summary>追猎者：快、脆、成群出现，是主要的"压力来源"。</summary>
        public static EnemyDef Chaser()
        {
            return new EnemyDef
            {
                Kind = EnemyKind.Chaser,
                Name = "追猎者",
                Health = 26f,
                Speed = 5.2f,
                ContactDamage = 11f,
                Radius = 0.48f,
                Mass = 1f,
                Score = 12,
                XpDrop = 1,
                BodyColor = Palette.EnemyChaser,
                AccentColor = Color.white,
                Shape = PrimitiveType.Cube,
                ShapeScale = new Vector3(0.95f, 0.95f, 0.95f),
                ModelName = "alien",
                ModelHeight = 1.25f
            };
        }

        /// <summary>暴君：慢、肉、接触伤害高，逼玩家拉开距离。</summary>
        public static EnemyDef Tank()
        {
            return new EnemyDef
            {
                Kind = EnemyKind.Tank,
                Name = "暴君",
                Health = 130f,
                Speed = 2.5f,
                ContactDamage = 22f,
                Radius = 0.98f,
                Mass = 4.5f,
                Armor = 0.25f,
                Score = 34,
                XpDrop = 5,
                BodyColor = Palette.EnemyTank,
                AccentColor = new Color(1f, 0.9f, 0.5f),
                Shape = PrimitiveType.Cube,
                ShapeScale = new Vector3(1.75f, 1.5f, 1.75f),
                ModelName = "machine_generator",
                ModelHeight = 2.1f
            };
        }

        /// <summary>织网者：保持距离并射击，逼玩家主动进攻而不是绕圈。</summary>
        public static EnemyDef Ranged()
        {
            return new EnemyDef
            {
                Kind = EnemyKind.Ranged,
                Name = "织网者",
                Health = 42f,
                Speed = 3.6f,
                ContactDamage = 6f,
                Radius = 0.5f,
                Mass = 1.4f,
                Score = 26,
                XpDrop = 3,
                BodyColor = Palette.EnemyRanged,
                AccentColor = new Color(1f, 0.7f, 1f),
                Shape = PrimitiveType.Cylinder,
                ShapeScale = new Vector3(0.95f, 0.7f, 0.95f),
                ModelName = "turret_single",
                ModelHeight = 1.35f,
                PreferredRange = 12f,
                FireInterval = 1.85f,
                BulletDamage = 9f,
                BulletSpeed = 18f,
                TelegraphTime = 0.4f
            };
        }

        /// <summary>首领：三套技能循环，每 5 波出现一次。</summary>
        public static EnemyDef Boss(int wave)
        {
            float scale = 1f + Mathf.Max(0, wave - 5) * 0.16f;
            return new EnemyDef
            {
                Kind = EnemyKind.Boss,
                Name = "深渊主宰",
                Health = 900f * scale,
                Speed = 3.0f,
                ContactDamage = 34f,
                Radius = 2.1f,
                Mass = 60f,
                Armor = 0.22f,
                Score = 420,
                XpDrop = 26,
                BodyColor = Palette.EnemyBoss,
                AccentColor = new Color(1f, 0.6f, 0.2f),
                Shape = PrimitiveType.Cube,
                ShapeScale = new Vector3(3.6f, 2.6f, 3.6f),
                ModelName = "craft_speederD",
                ModelHeight = 3.4f,
                IsBoss = true,
                BossBurstCount = 16 + Mathf.Min(14, wave),
                BossBurstInterval = Mathf.Max(2.2f, 3.8f - wave * 0.06f),
                BossChargeInterval = Mathf.Max(3.4f, 6.4f - wave * 0.08f),
                BossSummonInterval = Mathf.Max(6f, 11f - wave * 0.2f)
            };
        }

        /// <summary>按波次给出敌人属性倍率，保证难度曲线平滑上升。</summary>
        public static void ApplyWaveScaling(EnemyDef def, int wave)
        {
            if (def.IsBoss) return;
            float hpMul = 1f + (wave - 1) * 0.17f + Mathf.Pow(wave, 1.35f) * 0.012f;
            float speedMul = 1f + Mathf.Min(0.55f, (wave - 1) * 0.022f);
            float dmgMul = 1f + (wave - 1) * 0.075f;

            def.Health *= hpMul;
            def.Speed *= speedMul;
            def.ContactDamage *= dmgMul;
            def.BulletDamage *= dmgMul;
            def.XpDrop = Mathf.RoundToInt(def.XpDrop * dmgMul);
            def.Score = Mathf.RoundToInt(def.Score * (1f + (wave - 1) * 0.06f));
        }

        /// <summary>
        /// 把敌人升级为精英：更肉、更大、给更多分与经验。
        /// 精英是"波次里的重点目标"，制造局部压力峰值，避免整波敌人强度平均。
        /// </summary>
        public static void MakeElite(EnemyDef def)
        {
            if (def.IsBoss) return;
            def.IsElite = true;
            def.Health *= 3.2f;
            def.Speed *= 0.88f;
            def.ContactDamage *= 1.45f;
            def.BulletDamage *= 1.3f;
            def.Radius *= def.EliteScale;
            def.Mass *= 3f;
            def.Armor = Mathf.Min(0.6f, def.Armor + 0.12f);
            def.Score = Mathf.RoundToInt(def.Score * 3.4f);
            def.XpDrop = Mathf.RoundToInt(def.XpDrop * 3f);
            def.AccentColor = new Color(1f, 0.85f, 0.35f);
            def.ModelHeight *= def.EliteScale;
        }

        /// <summary>冲锋者：蓄力后高速突进，比追猎者更考验横向走位。</summary>
        public static EnemyDef Dasher()
        {
            return new EnemyDef
            {
                Kind = EnemyKind.Dasher,
                Name = "突进者",
                Health = 38f,
                Speed = 3.4f,
                ContactDamage = 16f,
                Radius = 0.5f,
                Mass = 1.6f,
                Score = 22,
                XpDrop = 2,
                BodyColor = new Color(1f, 0.42f, 0.16f),
                AccentColor = new Color(1f, 0.95f, 0.6f),
                Shape = PrimitiveType.Cube,
                ShapeScale = new Vector3(0.9f, 0.9f, 1.3f),
                // 复用已有的 craft_speederD：它的朝向感强，很适合表现"冲锋"
                ModelName = "craft_speederD",
                ModelHeight = 0.95f,
                DashInterval = 3.2f,
                DashWindup = 0.55f,
                DashSpeed = 23f,
                DashDuration = 0.42f
            };
        }

        /// <summary>分裂体：死亡后炸成几只小型追猎者，清场时要留意补刀。</summary>
        public static EnemyDef Splitter()
        {
            return new EnemyDef
            {
                Kind = EnemyKind.Splitter,
                Name = "孢裂体",
                Health = 62f,
                Speed = 2.7f,
                ContactDamage = 12f,
                Radius = 0.72f,
                Mass = 2.4f,
                Score = 30,
                XpDrop = 3,
                BodyColor = new Color(0.45f, 1f, 0.55f),
                AccentColor = new Color(0.9f, 1f, 0.7f),
                Shape = PrimitiveType.Sphere,
                ShapeScale = new Vector3(1.3f, 1.1f, 1.3f),
                ModelName = "rock_crystals",
                ModelHeight = 1.25f,
                SplitCount = 3,
                SplitHealthScale = 0.4f
            };
        }
    }
}
