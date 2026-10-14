using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    // Optional adapter: the third-party assembly and its content files remain untouched.
    internal static class PortraitCompatibility
    {
        private static MethodInfo getData;
        private static FieldInfo dirtyData;
        private static readonly MethodInfo clone = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static readonly ConditionalWeakTable<object, Fitted> fitted = new();
        private static readonly WeakReference<DialogueBox> renderedBox = new(null);
        private static Rectangle renderedBounds;
        private sealed class Fitted { internal object Data; internal int Width, Height; }

        internal static void Initialize(IModHelper helper)
        {
            PortraitTextureCache.Initialize(helper);
            helper.Events.GameLoop.GameLaunched += (_, _) => Install();
        }

        private static void Install()
        {
            if (!VerticalWorldRenderer.Enabled) return;
            var type = AccessTools.TypeByName("DialogueDisplayFramework.ModEntry");
            if (type == null) return;
            try
            {
                getData = AccessTools.Method(type, "GetDialogueDisplayData", new[] { typeof(Dialogue) });
                dirtyData = AccessTools.Field(type, "dirtyDialogueData");
                if (getData == null || dirtyData == null) throw new MissingMemberException("DDF layout API");
                var harmony = new Harmony("RGDSplus.VerticalWorld.PortraitCompatibility");
                var postfix = AccessTools.Method(typeof(PortraitCompatibility), nameof(FitData)).MakeGenericMethod(getData.ReturnType);
                harmony.Patch(getData, postfix: new HarmonyMethod(postfix));
                harmony.Patch(AccessTools.Constructor(typeof(DialogueBox), new[] { typeof(Dialogue) }),
                    prefix: new HarmonyMethod(typeof(PortraitCompatibility), nameof(NewDialogue)));
                harmony.Patch(AccessTools.Method(typeof(DialogueBox), "checkDialogue"),
                    transpiler: new HarmonyMethod(typeof(PortraitCompatibility), nameof(FitPagination)));
                harmony.Patch(AccessTools.Method(typeof(DialogueBox), "drawPortrait"),
                    prefix: new HarmonyMethod(typeof(PortraitCompatibility), nameof(PreparePortrait)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(PortraitCompatibility), nameof(RestorePortrait)));
                harmony.Patch(AccessTools.Method(typeof(DialogueBox), nameof(DialogueBox.gameWindowSizeChanged)),
                    postfix: new HarmonyMethod(typeof(PortraitCompatibility), nameof(Reflow)) { priority = Priority.Last,
                        after = new[] { "Mangupix.DialogueDisplayFrameworkContinued", "RGDSplus.VerticalWorld" } });
                ModEntry.Write("portrait-compat: DDF layout and pagination installed");
            }
            catch (Exception ex)
            {
                new Harmony("RGDSplus.VerticalWorld.PortraitCompatibility").UnpatchAll("RGDSplus.VerticalWorld.PortraitCompatibility");
                getData = null;
                ModEntry.Write("portrait-compat install failed: " + ex);
            }
        }

        private static void NewDialogue() => dirtyData?.SetValue(null, true);
        private static object Get(object value, string field) => value?.GetType().GetField(field)?.GetValue(value);
        private static int Number(object value, string field, int fallback = 0) => Get(value, field) is int n ? n : fallback;
        private static void Set(object value, string field, object data) => value?.GetType().GetField(field)?.SetValue(value, data);
        private static object Copy(object value) => value == null ? null : clone.Invoke(value, null);

        private static object CopyLayout(object value)
        {
            var result = Copy(value);
            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var child = field.GetValue(value);
                if (child is IList list)
                {
                    var copied = (IList)Activator.CreateInstance(child.GetType());
                    foreach (var item in list) copied.Add(Copy(item));
                    field.SetValue(result, copied);
                }
                else if (child != null && !field.FieldType.IsValueType && field.FieldType != typeof(string))
                    field.SetValue(result, Copy(child));
            }
            return result;
        }

        private static bool Supported(object data) => Get(data, "packName") as string is "Mud.SkimpyPortraitsCP" or "Mud.SkimpyPortraitsCP2" or "RGDSplus.CompatiblePortraits"
            || (data != null && Get(data, "packName") == null && Number(Get(data, "portrait"), "w") == 64 && Number(Get(data, "portrait"), "h") == 64);

        private static void FitData<T>(ref T __result) where T : class
        {
            if (__result == null || !Supported(__result) || !VerticalWorldRenderer.Enabled) return;
            UiPatches.EnsureUiViewport();
            var record = fitted.GetValue(__result, key => new Fitted { Data = CopyLayout(key) });
            int width = DialogueLayout.FitWidth(1200);
            int height = Math.Min(620, Math.Max(192, Game1.uiViewport.Height - 128));
            if (record.Width != width || record.Height != height)
            {
                record.Width = width; record.Height = height;
                Fit(record.Data, width, height);
            }
            __result = (T)record.Data;
        }

        private static void Place(object component, int x, int y)
        {
            Set(component, "xOffset", x); Set(component, "yOffset", y);
            Set(component, "right", false); Set(component, "bottom", false);
        }

        private static void Fit(object data, int width, int height)
        {
            Set(data, "width", width); Set(data, "height", height);
            Set(data, "xOffset", 0); Set(data, "yOffset", 0);
            var portrait = Get(data, "portrait");
            int tileWidth = Math.Max(1, Number(portrait, "w", 64)), tileHeight = Math.Max(1, Number(portrait, "h", 64));
            int column = Math.Min(520, (int)(width * .48f));
            float scale = Math.Min((column - 24f) / tileWidth, (height - 88f) / tileHeight);
            if (tileWidth <= 64 && tileHeight <= 64) scale = Math.Min(4f, scale);
            Place(portrait, (int)((column - tileWidth * scale) / 2), 8 + (int)((height - 88 - tileHeight * scale) / 2));
            Set(portrait, "scale", scale);
            var text = Get(data, "dialogue");
            Place(text, column + 16, 16);
            Set(text, "width", width - column - 40); Set(text, "height", height - 92);
            var name = Get(data, "name");
            Place(name, column / 2, height - 56);
            Set(name, "width", -1); Set(name, "centered", true);
            Place(Get(data, "jewel"), width - 56, height - 60);
            Place(Get(data, "button"), width - 48, height - 48);
            Place(Get(data, "gifts"), column + 16, height - 64);
            var hearts = Get(data, "hearts");
            Place(hearts, column + 16, height - 40);
            Set(hearts, "scale", Math.Min(4f, (width - column - 100f) / (14 * 8)));
            Set(hearts, "heartsPerRow", 14);
            // Native DDF background and divider have fixed PC coordinates.
            foreach (string field in new[] { "images", "dividers" })
                if (Get(data, field) is IList components)
                    foreach (var component in components)
                        if (Get(component, "ID") as string is "DialogueDisplayFramework.Images.PortraitBackground" or "DialogueDisplayFramework.Dividers.PortraitDivider")
                            Set(component, "disabled", true);
        }

        private static object Layout(DialogueBox box)
        {
            if (getData == null || box?.characterDialogue?.speaker == null) return null;
            var data = getData.Invoke(null, new object[] { box.characterDialogue });
            return Supported(data) ? data : null;
        }

        private sealed class PortraitState
        {
            internal Texture2D Override;
            internal object Portrait;
            internal int Width, Height, X, Y, OffsetX, OffsetY;
            internal float Scale;
        }

        private static void PreparePortrait(DialogueBox __instance, out PortraitState __state)
        {
            __state = null;
            var data = Layout(__instance);
            if (data == null) return;
            var portrait = Get(data, "portrait");
            var atlas = __instance.characterDialogue.overridePortrait ?? __instance.characterDialogue.speaker.Portrait;
            if (atlas == null || Get(portrait, "texturePath") != null) return;
            int width = Number(portrait, "w"), height = Number(portrait, "h");
            int x = Number(portrait, "x", -1), y = Number(portrait, "y", -1);
            var rect = x >= 0 && y >= 0 ? new Rectangle(x, y, width, height)
                : Game1.getSourceRectForStandardTileSheet(atlas, __instance.characterDialogue.getPortraitIndex(), width, height);
            if (!atlas.Bounds.Contains(rect)) rect = new Rectangle(0, 0, width, height);
            var detailed = PortraitTextureCache.Get(atlas, rect);
            if (detailed == null) return;
            __state = new PortraitState { Override = __instance.characterDialogue.overridePortrait, Portrait = portrait,
                Width = width, Height = height, X = x, Y = y, Scale = (float)Get(portrait, "scale"),
                OffsetX = Number(portrait, "xOffset"), OffsetY = Number(portrait, "yOffset") };
            __instance.characterDialogue.overridePortrait = detailed;
            var visible = PortraitTextureCache.ActiveBounds;
            int column = Math.Min(520, (int)(__instance.width * .48f));
            float scale = Math.Min((column - 24f) / visible.Width, (__instance.height - 88f) / visible.Height);
            int px = (int)((column - visible.Width * scale) / 2);
            int py = 8 + (int)((__instance.height - 88 - visible.Height * scale) / 2);
            Set(portrait, "w", visible.Width); Set(portrait, "h", visible.Height);
            Set(portrait, "x", visible.X); Set(portrait, "y", visible.Y);
            Set(portrait, "xOffset", px); Set(portrait, "yOffset", py);
            Set(portrait, "scale", scale);
            renderedBox.SetTarget(__instance);
            renderedBounds = new Rectangle(__instance.x + px, __instance.y + py,
                (int)Math.Ceiling(visible.Width * scale), (int)Math.Ceiling(visible.Height * scale));
        }

        private static void RestorePortrait(DialogueBox __instance, PortraitState __state)
        {
            if (__state == null) return;
            __instance.characterDialogue.overridePortrait = __state.Override;
            Set(__state.Portrait, "w", __state.Width); Set(__state.Portrait, "h", __state.Height);
            Set(__state.Portrait, "x", __state.X); Set(__state.Portrait, "y", __state.Y);
            Set(__state.Portrait, "xOffset", __state.OffsetX); Set(__state.Portrait, "yOffset", __state.OffsetY);
            Set(__state.Portrait, "scale", __state.Scale);
        }

        private static string PageRemainder(string text, int width, int height, DialogueBox box)
        {
            var data = Layout(box);
            if (data != null)
            {
                var area = Get(data, "dialogue");
                width = Number(area, "width", width); height = Number(area, "height", height);
            }
            return SpriteText.getSubstringBeyondHeight(text, width, height);
        }

        private static IEnumerable<CodeInstruction> FitPagination(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(SpriteText), nameof(SpriteText.getSubstringBeyondHeight));
            foreach (var code in instructions)
            {
                if (code.Calls(original))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code);
                    yield return CodeInstruction.Call(typeof(PortraitCompatibility), nameof(PageRemainder));
                }
                else yield return code;
            }
        }

        private static void Reflow(DialogueBox __instance)
        {
            var data = Layout(__instance);
            if (data == null || __instance.isQuestion || __instance.characterDialoguesBrokenUp.Count == 0) return;
            var area = Get(data, "dialogue");
            int width = Number(area, "width"), height = Number(area, "height");
            var pages = new List<string>();
            foreach (string pending in __instance.characterDialoguesBrokenUp)
            {
                string remaining = pending;
                while (remaining.Length > 0 && SpriteText.getHeightOfString(remaining, width) > height)
                {
                    // Split by measured glyph height, preserving every character.
                    int low = 1, high = remaining.Length;
                    while (low < high)
                    {
                        int mid = (low + high + 1) / 2;
                        if (SpriteText.getHeightOfString(remaining.Substring(0, mid), width) <= height) low = mid;
                        else high = mid - 1;
                    }
                    int end = low;
                    int space = remaining.LastIndexOf(' ', end - 1, end);
                    if (space > end / 2) end = space + 1;
                    pages.Add(remaining.Substring(0, end));
                    remaining = remaining.Substring(end);
                }
                if (remaining.Length > 0) pages.Add(remaining);
            }
            __instance.characterDialoguesBrokenUp.Clear();
            for (int i = pages.Count - 1; i >= 0; i--) __instance.characterDialoguesBrokenUp.Push(pages[i]);
            if (pages.Count > 1) __instance.dialogueContinuedOnNextPage = true;
            __instance.characterIndexInDialogue = Math.Min(__instance.characterIndexInDialogue, __instance.getCurrentString().Length);
        }

        internal static object Status(DialogueBox box)
        {
            var data = Layout(box);
            if (data == null) return null;
            var portrait = Get(data, "portrait"); var text = Get(data, "dialogue");
            var texture = box.characterDialogue.overridePortrait ?? box.characterDialogue.speaker.Portrait;
            int w = Number(portrait, "w"), h = Number(portrait, "h");
            var source = Game1.getSourceRectForStandardTileSheet(texture, box.characterDialogue.getPortraitIndex(), w, h);
            float scale = (float)Get(portrait, "scale");
            var display = renderedBox.TryGetTarget(out var drawnBox) && ReferenceEquals(drawnBox, box) ? renderedBounds
                : new Rectangle(box.x + Number(portrait, "xOffset"), box.y + Number(portrait, "yOffset"), (int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale));
            return new {
                npc = box.characterDialogue.speaker.Name,
                textureWidth = texture.Width, textureHeight = texture.Height,
                detailedWidth = PortraitTextureCache.ActiveWidth, detailedHeight = PortraitTextureCache.ActiveHeight,
                source = new { x = source.X, y = source.Y, width = w, height = h },
                portrait = new { x = display.X, y = display.Y, width = display.Width, height = display.Height },
                text = new { x = box.x + Number(text, "xOffset"), y = box.y + Number(text, "yOffset"), width = Number(text, "width"), height = Number(text, "height"), renderedHeight = SpriteText.getHeightOfString(box.getCurrentString(), Number(text, "width")) }
            };
        }
    }
}
