using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace Rgdsplus.VerticalWorld
{
    internal static partial class DashboardPages
    {
        private static void DrawExit(SpriteBatch b)
        {
            Label(b,T("退出游戏","Exit game"),40,112,920,48);
            Dashboard.Paragraph(b,T("未保存的当天进度将丢失。睡觉保存后再退出可以保留当天进度。",
                "Unsaved progress from this day will be lost. Sleep to save your day before leaving."),R(48,190,928,112),Ink);
            Button(b,T("退出到标题","Exit to title"),272,346,480,88);
            Button(b,T("退出到桌面","Exit to desktop"),272,478,480,88);
            Label(b,T("返回标题需要重新加载，请稍候","Returning to title reloads the game; please wait"),80,592,864,40,.9f);
            Label(b,T("退出到桌面后返回掌机系统界面","Exit to desktop returns to the handheld menu"),80,642,864,40,.9f);
        }
        private static bool ClickExit(int x,int y)
        {
            bool title=R(272,346,480,88).Contains(x,y), desktop=R(272,478,480,88).Contains(x,y);
            if (!title && !desktop) return true;
            if (Game1.game1.IsSaving || Game1.activeClickableMenu != null || Game1.player.UsingTool || Game1.eventUp || Game1.fadeToBlack || Game1.globalFade)
            { Dashboard.Notice=T("当前操作结束后再退出","Finish the current action before leaving"); return true; }
            if (Game1.options.optionsDirty) Game1.options.SaveDefaultOptions();
            DisplayModeController.GateHeldButtons();
            if (title)
            {
                // The title's native resources can exceed the device's transient
                // GPU budget while the world is still loaded. Ask the launcher
                // to relaunch this session into a fresh title process.
                string session=System.Environment.GetEnvironmentVariable("SDV_SESSION_TOKEN");
                if (string.IsNullOrEmpty(session))
                {
                    Game1.ExitToTitle();
                    return true;
                }
                try { System.IO.File.WriteAllText(session+".return-title",System.Environment.ProcessId.ToString()); }
                catch (System.Exception ex)
                {
                    ModEntry.Write("exit-title-request-failed "+ex.Message);
                    Dashboard.Notice=T("无法返回标题，请稍后重试","Unable to return to title; try again");
                    return true;
                }
                ModEntry.Write("exit-title: requesting fresh title process");
            }
            Game1.quit=true;
            return true;
        }
    }
}
