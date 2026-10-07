using System;
using StardewModdingAPI;
using StardewValley;

namespace Rgdsplus.VerticalWorld
{
    internal sealed class CompanionPreferences
    {
        public bool NativeFrames { get; set; } = true;
        public float UiScale { get; set; } = 1.5f;
        public int LayoutVersion { get; set; }
        public bool[] VisiblePages { get; set; } = new bool[12];
        private static IModHelper helper;
        internal static CompanionPreferences Current = new CompanionPreferences();
        public CompanionPreferences() => Array.Fill(VisiblePages, true);
        internal static void Initialize(IModHelper modHelper)
        {
            helper = modHelper;
            Current = helper.ReadConfig<CompanionPreferences>();
            // Old page indices describe Today/Farm/Skills; they cannot be reused for v2.
            if (Current.LayoutVersion < 2)
            {
                Current.VisiblePages = new CompanionPreferences().VisiblePages;
                Current.UiScale = 1.5f;
                Current.LayoutVersion = 2;
                Save();
            }
            if (Current.VisiblePages == null || Current.VisiblePages.Length != 12)
                Current.VisiblePages = new CompanionPreferences().VisiblePages;
            Current.VisiblePages[0] = Current.VisiblePages[11] = true;
            Current.UiScale = ValidUiScale(Current.UiScale);
            helper.Events.GameLoop.GameLaunched += (_, __) => ApplyUiScale();
            helper.Events.GameLoop.SaveLoaded += (_, __) => ApplyUiScale();
            helper.Events.GameLoop.DayStarted += (_, __) => ApplyUiScale();
        }
        internal static float ValidUiScale(float scale) => float.IsFinite(scale) ? Math.Clamp(scale, .75f, 1.5f) : 1.5f;
        internal static void ApplyUiScale()
        {
            if (!VerticalWorldRenderer.Enabled || Game1.options == null) return;
            Game1.options.singlePlayerDesiredUIScale = Game1.options.localCoopDesiredUIScale = Game1.options.baseUIScale = Current.UiScale;
            UiPatches.EnsureViewports();
        }
        internal static void SetUiScale(float scale)
        {
            Current.UiScale = ValidUiScale(scale);
            ApplyUiScale();
            Save();
        }
        internal static void RememberUiScale(float scale)
        {
            if (!VerticalWorldRenderer.Enabled || !Context.IsWorldReady || helper == null || !float.IsFinite(scale) || scale < .75f || scale > 1.5f) return;
            if (Math.Abs(Current.UiScale - scale) < .001f) return;
            Current.UiScale = scale;
            Save();
        }
        internal static bool Visible(int page) => Current.VisiblePages[page];
        internal static void Save() => helper?.WriteConfig(Current);
    }
}
