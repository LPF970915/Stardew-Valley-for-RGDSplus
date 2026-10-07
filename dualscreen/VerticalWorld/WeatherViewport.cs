using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using ViewRectangle = xTile.Dimensions.Rectangle;

namespace Rgdsplus.VerticalWorld
{
    // Weather is screen-space simulation. Its wrap bounds must match the scene, while
    // ordinary updates and menus must retain a single-panel viewport.
    internal static class WeatherViewport
    {
        private static bool Active => VerticalWorldRenderer.Enabled && Game1.game1!=null
            && !Game1.game1.takingMapScreenshot && Game1.gameMode==3
            && Game1.currentLocation!=null && Game1.currentMinigame==null;

        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Game1),nameof(Game1.UpdateViewPort)),
                postfix:new HarmonyMethod(typeof(WeatherViewport),nameof(CameraPostfix)));
            foreach(string name in new[] { "updateWeather", "updateRaindropPosition", "updateRainDropPositionForPlayerMovement",
                "randomizeRainPositions", "randomizeDebrisWeatherPositions", "populateDebrisWeatherArray" })
                Scope(harmony,AccessTools.Method(typeof(Game1),name));
            Scope(harmony,AccessTools.Method(typeof(Game1),nameof(Game1.updateDebrisWeatherForMovement),new[]{typeof(List<WeatherDebris>)}));
            Scope(harmony,AccessTools.Method(typeof(Game1),nameof(Game1.updateDebrisWeatherForMovement),new[]{typeof(List<WeatherDebris>),typeof(int),typeof(float)}));
            Scope(harmony,AccessTools.Method(typeof(WeatherDebris),nameof(WeatherDebris.update),new[]{typeof(bool)}));
            harmony.Patch(AccessTools.Method(typeof(WeatherDebris),nameof(WeatherDebris.draw)),
                prefix:new HarmonyMethod(typeof(WeatherViewport),nameof(DebrisDrawPrefix)),
                finalizer:new HarmonyMethod(typeof(WeatherViewport),nameof(DebrisDrawFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(Game1),nameof(Game1.drawWeather)),
                prefix:new HarmonyMethod(typeof(WeatherViewport),nameof(DrawPrefix)),
                finalizer:new HarmonyMethod(typeof(WeatherViewport),nameof(DrawFinalizer)));
        }
        private static void Scope(Harmony harmony,System.Reflection.MethodInfo method) => harmony.Patch(method,
            prefix:new HarmonyMethod(typeof(WeatherViewport),nameof(BoundsPrefix)),
            finalizer:new HarmonyMethod(typeof(WeatherViewport),nameof(BoundsFinalizer)));

        private static void CameraPostfix()
        {
            if(!Active || Game1.eventUp || Game1.viewportFreeze || VerticalWorldRenderer.DrawingWorld)return;
            var scene=VerticalWorldRenderer.SceneViewport();
            // Keep the previous native camera sample. Both it and the new sample now
            // use the same anchor as DrawWorld, so a stationary player has zero motion.
            Game1.viewport.X=scene.X;Game1.viewport.Y=scene.Y;
        }
        private static void BoundsPrefix(out ViewRectangle? __state)
        {
            __state=null;
            if(!Active)return;
            __state=Game1.viewport;
            Game1.viewport.Width=VerticalWorldRenderer.WorldWidth;
            Game1.viewport.Height=VerticalWorldRenderer.WorldHeight*(DisplayModeController.Expanded?1:2);
        }
        private static Exception BoundsFinalizer(ViewRectangle? __state,Exception __exception)
        {
            if(__state.HasValue)Game1.viewport=__state.Value;
            return __exception;
        }
        private static void DebrisDrawPrefix(WeatherDebris __instance,out Vector2? __state)
        {
            __state=null;
            if(!Active || !VerticalWorldRenderer.IsSecondPass)return;
            __state=__instance.position;
            __instance.position.Y-=VerticalWorldRenderer.WorldHeight;
        }
        private static Exception DebrisDrawFinalizer(WeatherDebris __instance,Vector2? __state,Exception __exception)
        {
            if(__state.HasValue)__instance.position=__state.Value;
            return __exception;
        }
        internal sealed class DrawState
        {
            internal Vector2 Snow;
            internal Vector2[] Rain;
        }
        private static void DrawPrefix(out DrawState __state)
        {
            __state=null;
            if(!Active || !VerticalWorldRenderer.IsSecondPass)return;
            __state=new DrawState { Snow=Game1.snowPos,Rain=new Vector2[Game1.rainDrops.Length] };
            Game1.snowPos.Y-=VerticalWorldRenderer.WorldHeight;
            for(int i=0;i<Game1.rainDrops.Length;i++)
            {
                __state.Rain[i]=Game1.rainDrops[i].position;
                Game1.rainDrops[i].position.Y-=VerticalWorldRenderer.WorldHeight;
            }
        }
        private static Exception DrawFinalizer(DrawState __state,Exception __exception)
        {
            if(__state!=null)
            {
                Game1.snowPos=__state.Snow;
                for(int i=0;i<__state.Rain.Length;i++)Game1.rainDrops[i].position=__state.Rain[i];
            }
            return __exception;
        }
    }
}
