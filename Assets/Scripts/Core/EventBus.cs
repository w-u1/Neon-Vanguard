using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 极简事件总线。让「击杀 / 受伤 / 波次变化」这类信号和具体实现解耦：
    /// UI 只订阅事件，不需要持有敌人或玩家的引用。
    /// </summary>
    public static class EventBus
    {
        static readonly Dictionary<Type, Delegate> Handlers = new Dictionary<Type, Delegate>();

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            var t = typeof(T);
            Delegate d;
            Handlers[t] = Handlers.TryGetValue(t, out d) ? Delegate.Combine(d, handler) : handler;
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var t = typeof(T);
            Delegate d;
            if (!Handlers.TryGetValue(t, out d)) return;
            var result = Delegate.Remove(d, handler);
            if (result == null) Handlers.Remove(t);
            else Handlers[t] = result;
        }

        public static void Publish<T>(T evt) where T : struct
        {
            Delegate d;
            if (!Handlers.TryGetValue(typeof(T), out d)) return;
            var cb = d as Action<T>;
            if (cb == null) return;
            try { cb(evt); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>重开一局时调用，防止旧的订阅者（已销毁的对象）残留。</summary>
        public static void Clear() => Handlers.Clear();
    }

    // ---------------- 事件定义 ----------------

    /// <summary>敌人被击杀。position 用于在其位置生成经验/特效。</summary>
    public struct EnemyKilledEvent
    {
        public Vector3 Position;
        public int Score;
        public bool IsBoss;
        public Color Color;
    }

    /// <summary>波次开始 / 结束。</summary>
    public struct WaveEvent
    {
        public int Wave;
        public int TotalWaves;
        public bool IsBossWave;
    }

    /// <summary>游戏结束。</summary>
    public struct GameOverEvent
    {
        public int Wave;
        public int Score;
        public int Kills;
        public float TimeSurvived;
        public bool NewRecord;
    }

    /// <summary>敌人进入光环/靠近等，用于 UI 提示。</summary>
    public struct ToastEvent
    {
        public string Message;
        public Color Color;
    }
}
