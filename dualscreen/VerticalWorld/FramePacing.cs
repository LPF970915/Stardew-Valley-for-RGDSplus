using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;

namespace Rgdsplus.VerticalWorld
{
    // Keep the engine's 60 Hz fixed updates; only omit alternate render passes.
    internal static class FramePacing
    {
        internal static bool Enabled { get; } = Environment.GetEnvironmentVariable("SDV_TARGET_FPS") == "30";
        private static readonly DrawCadence cadence = new();

        internal static void Install(Harmony harmony)
        {
            if (!Enabled) return;
            harmony.Patch(AccessTools.Method(typeof(Game), "DoDraw"),
                prefix: new HarmonyMethod(typeof(FramePacing), nameof(BeforeDraw)),
                finalizer: new HarmonyMethod(typeof(FramePacing), nameof(AfterDraw)));
        }

        private static bool BeforeDraw(Game __instance, GameTime gameTime, out TimeSpan? __state)
        {
            __state = null;
            if (__instance is not GameRunner || !__instance.IsFixedTimeStep) return true;
            if (!cadence.Advance(gameTime.TotalGameTime.Ticks, gameTime.ElapsedGameTime.Ticks,
                    __instance.TargetElapsedTime.Ticks, out long elapsed)) return false;
            // Game1.currentGameTime aliases the engine clock. Draw-time UI
            // timers must see the same accumulated time as the Draw argument.
            // Restore it even on failure so update timing remains untouched.
            __state = gameTime.ElapsedGameTime;
            gameTime.ElapsedGameTime = TimeSpan.FromTicks(elapsed);
            return true;
        }

        private static void AfterDraw(GameTime gameTime, TimeSpan? __state)
        {
            if (__state.HasValue) gameTime.ElapsedGameTime = __state.Value;
        }
    }

    internal sealed class DrawCadence
    {
        private long next, pending, previous = -1, step;

        internal bool Advance(long total, long elapsed, long updateStep, out long drawElapsed)
        {
            if (total < previous || updateStep != step)
            {
                next = pending = 0;
                step = updateStep;
            }
            previous = total;
            pending += elapsed;
            drawElapsed = 0;
            if (total < next) return false;
            long interval = Math.Max(1, updateStep) * 2;
            // Skip missed deadlines after a stall instead of issuing a burst
            // of catch-up frames; logic catch-up remains owned by Game.Tick.
            next = (total / interval + 1) * interval;
            drawElapsed = pending;
            pending = 0;
            return true;
        }
    }
}
