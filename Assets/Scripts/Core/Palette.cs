using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 全局配色。整体走「暗色舞台 + 霓虹发光体」的赛博街机路线：
    /// 背景压得很暗，所有玩法实体都用高饱和亮色 + 自发光材质，
    /// 这样即使没有任何美术资源，画面也有明确的层次和辨识度。
    /// </summary>
    public static class Palette
    {
        public static readonly Color Bg           = new Color(0.043f, 0.055f, 0.094f);
        public static readonly Color BgGrid       = new Color(0.086f, 0.110f, 0.180f);
        public static readonly Color BgGridMajor  = new Color(0.129f, 0.165f, 0.259f);

        public static readonly Color Player       = new Color(0.31f, 0.96f, 1.00f);
        public static readonly Color PlayerCore   = new Color(0.85f, 1.00f, 1.00f);
        public static readonly Color Shield       = new Color(0.45f, 0.55f, 1.00f);

        public static readonly Color EnemyChaser  = new Color(1.00f, 0.26f, 0.46f);
        public static readonly Color EnemyTank    = new Color(1.00f, 0.58f, 0.15f);
        public static readonly Color EnemyRanged  = new Color(0.72f, 0.40f, 1.00f);
        public static readonly Color EnemyBoss    = new Color(1.00f, 0.10f, 0.35f);

        public static readonly Color Xp           = new Color(0.45f, 1.00f, 0.55f);
        public static readonly Color Health       = new Color(1.00f, 0.35f, 0.45f);
        public static readonly Color Nova         = new Color(0.35f, 0.95f, 1.00f);

        public static readonly Color Warning      = new Color(1.00f, 0.85f, 0.30f);
        public static readonly Color Danger       = new Color(1.00f, 0.30f, 0.30f);
        public static readonly Color Good         = new Color(0.40f, 1.00f, 0.60f);
        public static readonly Color TextDim      = new Color(0.55f, 0.62f, 0.78f);
    }
}
