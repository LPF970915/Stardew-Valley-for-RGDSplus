using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace Rgdsplus.VerticalWorld
{
    // Opt-in device QA only. The normal launcher never enables this channel.
    internal static class UiTestDriver
    {
        private static string directory;
        private static string capture;
        private static string captureId;
        private static long nextPoll;
        private static bool fixtureActive;
        private static long ticks;
        private static long fixtureSettlesUntil;
        private static int presentationRequests;

        // The firmware's Weston extension accepts per-surface metadata. Apply only
        // in an explicitly enabled QA session, without changing the compositor.
        private static void PresentOnBothPanels()
        {
            var window = Game1.game1.Window;
            window.Title = "flags=stay-on-top";
            window.Title = "attrs=pos:0,0";
            window.Title = "requests=activate";
            window.Title = "Stardew Valley";
        }

        internal static void Initialize(IModHelper helper)
        {
            directory = Environment.GetEnvironmentVariable("SDV_DUAL_UI_TEST_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            helper.Events.GameLoop.UpdateTicked += (_, __) => Poll();
            ModEntry.Write("ui-test-enabled directory=" + directory);
        }

        private static void Poll()
        {
            ticks++;
            if (presentationRequests < 8 && ticks % 60 == 0)
            {
                PresentOnBothPanels();
                presentationRequests++;
            }
            if (fixtureActive && Game1.timeOfDay >= 1800) Game1.timeOfDay = 900;
            if (fixtureActive && Environment.TickCount64 < fixtureSettlesUntil)
            {
                if (Game1.activeClickableMenu is DialogueBox) Game1.activeClickableMenu = null;
                foreach (var location in Game1.locations) location.currentEvent = null;
                Game1.eventUp = Game1.dialogueUp = Game1.messagePause = Game1.freezeControls = false;
                Game1.fadeToBlack = Game1.globalFade = false;
                Game1.fadeToBlackAlpha = 0;
                Game1.pauseTime = 0;
                Game1.messageAfterPause = "";
                Game1.player.CanMove = true;
            }
            if (Environment.TickCount64 < nextPoll || capture != null) return;
            nextPoll = Environment.TickCount64 + 100;
            string path = Path.Combine(directory, "command.json");
            if (!File.Exists(path)) return;
            string id = "invalid";
            try
            {
                using var command = JsonDocument.Parse(File.ReadAllText(path));
                File.Delete(path);
                var data = command.RootElement;
                id = data.GetProperty("id").GetString();
                switch (data.GetProperty("action").GetString())
                {
                    case "status": break;
                    case "present": PresentOnBothPanels(); break;
                    case "fixture":
                        if (Context.IsWorldReady) throw new InvalidOperationException("Already in a world.");
                        Game1.game1.loadForNewGame();
                        Game1.player.Name = "RGDS QA";
                        Game1.player.farmName.Value = "Pixel Farm";
                        Game1.player.MaxItems = 36;
                        while (Game1.player.Items.Count < 36) Game1.player.Items.Add(null);
                        Game1.dayOfMonth = 8;
                        Game1.year = 2;
                        Game1.timeOfDay = 900;
                        Game1.stats.DaysPlayed = 40;
                        Game1.hasStartedDay = true;
                        Game1.gameMode = 3;
                        Game1.activeClickableMenu = null;
                        Game1.eventUp = Game1.dialogueUp = false;
                        Game1.fadeToBlack = Game1.globalFade = false;
                        Game1.fadeToBlackAlpha = 0;
                        Game1.currentLocation = Game1.getFarm();
                        Game1.player.currentLocation = Game1.currentLocation;
                        Game1.player.Position = new Vector2(64 * 64, 15 * 64);
                        Game1.player.Money = 1234567;
                        Game1.player.CanMove = true;
                        for (int i = 0; i < 12; i++)
                            Game1.player.Items[i] = ItemRegistry.Create("(O)" + new[] { "24", "388", "390", "372", "18", "20", "22", "80", "92", "334", "335", "336" }[i], 10 + i);
                        foreach (string recipe in CraftingRecipe.cookingRecipes.Keys)
                            Game1.player.cookingRecipes[recipe] = 1;
                        foreach (string recipe in CraftingRecipe.craftingRecipes.Keys)
                            Game1.player.craftingRecipes[recipe] = 0;
                        foreach (string name in new[] { "Abigail", "Robin", "Lewis", "Pierre", "Linus", "Demetrius", "Shane", "Sam" })
                            Game1.player.friendshipData[name] = new Friendship(1250);
                        var farm = Game1.getFarm();
                        for (int i = 0; i < 8; i++)
                        {
                            var machine = (StardewValley.Object)ItemRegistry.Create("(BC)12");
                            machine.heldObject.Value = (StardewValley.Object)ItemRegistry.Create("(O)334");
                            machine.MinutesUntilReady = i * 30;
                            machine.readyForHarvest.Value = i == 0;
                            farm.objects[new Vector2(12 + i, 12)] = machine;
                        }
                        farm.getShippingBin(Game1.player).Add(ItemRegistry.Create("(O)24", 25));
                        for (int i = 0; i < 6; i++)
                        {
                            var animal = new FarmAnimal("White Chicken", 900000 + i, Game1.player.UniqueMultiplayerID);
                            animal.Name = "QA " + (i + 1);
                            animal.age.Value = 10;
                            animal.wasPet.Value = i % 2 == 0;
                            animal.currentProduce.Value = i % 2 == 0 ? "176" : null;
                            farm.animals[animal.myID.Value] = animal;
                        }
                        for (int i = 0; i < 6; i++)
                        {
                            var tree = new StardewValley.TerrainFeatures.FruitTree("628", 4);
                            tree.daysUntilMature.Value = 0;
                            tree.fruit.Add(ItemRegistry.Create("(O)613"));
                            farm.terrainFeatures[new Vector2(12 + i * 2, 20)] = tree;
                            var planted=new Crop("472",12+i,24,farm);
                            if(i>=4){planted.currentPhase.Value=planted.phaseDays.Count-1;planted.dayOfCurrentPhase.Value=0;}
                            farm.terrainFeatures[new Vector2(12 + i, 24)] = new StardewValley.TerrainFeatures.HoeDirt(i % 2, planted);
                        }
                        Game1.player.questLog.Clear();
                        for (int i = 0; i < 7; i++)
                        {
                            var quest = new StardewValley.Quests.Quest();
                            quest.questTitle = "QA " + (i + 1);
                            quest.questDescription = "Help the farm prepare for the new season.";
                            quest.currentObjective = "Collect 10 wood for the farm.";
                            quest.canBeCancelled.Value = true;
                            quest.accepted.Value = true;
                            quest.moneyReward.Value = 500;
                            quest.completed.Value = i == 0;
                            Game1.player.questLog.Add(quest);
                        }
                        fixtureActive = true;
                        fixtureSettlesUntil = Environment.TickCount64 + 45000;
                        // The fixture bypasses the save loader and its SMAPI readiness timer.
                        typeof(Context).GetProperty(nameof(Context.IsWorldReady)).GetSetMethod(true).Invoke(null, new object[] { true });
                        CompanionPreferences.ApplyUiScale();
                        UiPatches.EnsureViewports();
                        break;
                    case "load":
                        if (Context.IsWorldReady) throw new InvalidOperationException("Already in a world.");
                        SaveGame.Load(data.GetProperty("save").GetString()); break;
                    case "toggle": DisplayModeController.TestToggle(); break;
                    case "observe_history":
                        if(!fixtureActive)throw new InvalidOperationException("In-memory fixture required.");
                        CompanionHistory.Observe();break;
                    case "prepare_recipe":
                        if (!fixtureActive) throw new InvalidOperationException("In-memory fixture required.");
                        bool cooking = data.GetProperty("cooking").GetBoolean();
                        var recipes = cooking ? Game1.player.cookingRecipes : Game1.player.craftingRecipes;
                        recipes.Clear();
                        recipes[cooking ? "Fried Egg" : "Torch"] = 0;
                        Game1.player.Items[25] = ItemRegistry.Create("(O)176", 4);
                        Game1.player.Items[26] = ItemRegistry.Create("(O)917", 2);
                        Game1.player.Items[27] = ItemRegistry.Create("(O)388", 20);
                        Game1.player.Items[28] = ItemRegistry.Create("(O)92", 20);
                        Game1.player.HouseUpgradeLevel = 1;
                        DashboardPages.Reset();
                        break;
                    case "prepare_icons":
                        if (!fixtureActive) throw new InvalidOperationException("In-memory fixture required.");
                        string[] iconIds = { "(O)24", "(O)388", "(O)390", "(O)176", "(BC)12", "(W)0", "(T)WateringCan", "(T)Axe", "(H)0", "(R)516", "(O)335", "(O)336" };
                        for (int i = 0; i < iconIds.Length; i++)
                            Game1.player.Items[i] = ItemRegistry.Create(iconIds[i], i < 4 ? 10 + i : 1, i == 3 ? 4 : 0);
                        Game1.player.CurrentToolIndex = 0;
                        break;
                    case "capacity":
                        if (!fixtureActive) throw new InvalidOperationException("In-memory fixture required.");
                        int capacity = data.GetProperty("value").GetInt32();
                        if (capacity != 12 && capacity != 24 && capacity != 36) throw new ArgumentOutOfRangeException("capacity");
                        Game1.player.MaxItems = capacity;
                        Game1.player.CurrentToolIndex = Math.Min(Game1.player.CurrentToolIndex, capacity - 1);
                        break;
                    case "long_quest":
                        if (!fixtureActive) throw new InvalidOperationException("In-memory fixture required.");
                        Game1.player.questLog[1].questDescription = string.Join("\n", Enumerable.Repeat("请收集木材和石头，帮助村民修缮农场。这里的长任务说明应该保持可读字号，可以独立滚动。", 12));
                        Game1.player.questLog[1].questTitle = "春季农场修缮计划";
                        DashboardPages.Reset();
                        break;
                    case "settle_scene":
                        if (!fixtureActive) throw new InvalidOperationException("In-memory fixture required.");
                        fixtureSettlesUntil = Environment.TickCount64 + 45000;
                        Game1.displayHUD = true;
                        Game1.panMode = false;
                        break;
                    case "reset_ui":
                        if (!fixtureActive) throw new InvalidOperationException("In-memory fixture required.");
                        CompanionPreferences.SetUiScale(1.5f);
                        break;
                    case "pause":
                        if (Game1.CanShowPauseMenu()) Game1.activeClickableMenu = new GameMenu();
                        break;
                    case "dashboard":
                        if (!Context.IsWorldReady || !DisplayModeController.Expanded)
                            throw new InvalidOperationException("Expanded world required.");
                        if (Game1.activeClickableMenu != null && !Game1.activeClickableMenu.readyToClose())
                            throw new InvalidOperationException("Menu is holding an item or cannot close.");
                        DisplayModeController.TestDashboard(); break;
                    case "touch":
                        DisplayModeController.TestTouch(data.GetProperty("x").GetSingle(), data.GetProperty("y").GetSingle(),
                            data.GetProperty("pressed").GetBoolean(), data.GetProperty("released").GetBoolean()); break;
                    case "close":
                        if (Game1.activeClickableMenu?.readyToClose() == false)
                            throw new InvalidOperationException("Menu cannot close.");
                        Game1.activeClickableMenu?.exitThisMenu(false); break;
                    case "scale":
                        float ui = data.GetProperty("ui").GetSingle(), zoom = data.GetProperty("zoom").GetSingle();
                        if (ui < .75f || ui > 1.5f || zoom < .5f || zoom > 1.5f)
                            throw new ArgumentOutOfRangeException("scale");
                        Game1.options.singlePlayerDesiredUIScale = Game1.options.localCoopDesiredUIScale = Game1.options.baseUIScale = ui;
                        Game1.options.baseZoomLevel = zoom;
                        UiPatches.EnsureViewports();
                        Game1.activeClickableMenu?.gameWindowSizeChanged(Rectangle.Empty, Rectangle.Empty); break;
                    case "language":
                        LocalizedContentManager.CurrentLanguageCode = data.GetProperty("code").GetString() == "zh"
                            ? LocalizedContentManager.LanguageCode.zh : LocalizedContentManager.LanguageCode.en;
                        Dashboard.Reset(); break;
                    case "capture":
                        capture = "lower-" + Guid.NewGuid().ToString("N") + ".png";
                        captureId = id;
                        return;
                    default: throw new InvalidOperationException("Unknown test action.");
                }
                Reply(id, null, null);
            }
            catch (Exception ex) { Reply(id, ex.ToString(), null); }
        }

        internal static void AfterPresent()
        {
            if (capture == null) return;
            try
            {
                var device = Game1.game1.GraphicsDevice;
                var previous = device.GetRenderTargets();
                var viewport = device.Viewport;
                using var target = new RenderTarget2D(device, 1024, 768, false, SurfaceFormat.Color,
                    DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                try
                {
                    device.SetRenderTarget(target);
                    device.Clear(Color.Transparent);
                    var batch = Game1.spriteBatch;
                    if (DisplayModeController.Expanded)
                    {
                        var transform = Matrix.CreateScale(1024f / Game1.uiViewport.Width, 768f / Game1.uiViewport.Height, 1);
                        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null, null, transform);
                        if (Game1.activeClickableMenu == null || Game1.activeClickableMenu is DialogueBox)
                            Dashboard.Draw(batch, DisplayModeController.SelectedPage, false);
                        else Dashboard.DrawBackdrop(batch);
                        batch.End();
                    }
                    else
                    {
                        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
                        // The dual-world renderer no longer stores the world in Game1.screen.
                        var field = typeof(VerticalWorldRenderer).GetField(VerticalWorldRenderer.TwoPass ? "lower" : "world", BindingFlags.Static | BindingFlags.NonPublic);
                        var scene = field.GetValue(null) as RenderTarget2D;
                        if (scene != null && !scene.IsDisposed)
                            batch.Draw(scene, new Rectangle(0, 0, 1024, 768), new Rectangle(0,
                                scene.Height > VerticalWorldRenderer.WorldHeight ? VerticalWorldRenderer.WorldHeight : 0,
                                scene.Width, Math.Min(scene.Height, VerticalWorldRenderer.WorldHeight)), Color.White);
                        else if (Game1.game1.screen != null) batch.Draw(Game1.game1.screen, new Rectangle(0, 0, 1024, 768), Color.White);
                        batch.End();
                    }
                    batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
                    if (Game1.game1.uiScreen != null) batch.Draw(Game1.game1.uiScreen, new Rectangle(0, 0, 1024, 768), Color.White);
                    batch.End();
                    device.SetRenderTarget(null);
                    using var stream = File.Create(Path.Combine(directory, capture));
                    target.SaveAsPng(stream, 1024, 768);
                    Reply(captureId, null, capture);
                }
                finally { device.SetRenderTargets(previous); device.Viewport = viewport; }
            }
            catch (Exception ex) { Reply(captureId, ex.ToString(), null); }
            finally { capture = captureId = null; }
        }

        private static void Reply(string id, string error, string screenshot)
        {
            var menu = Game1.activeClickableMenu;
            var result = new
            {
                id, error, screenshot, worldReady = Context.IsWorldReady, gameMode = Game1.gameMode, ticks,
                eventUp = Game1.eventUp, dialogueUp = Game1.dialogueUp, fade = Game1.fadeToBlack || Game1.globalFade,
                mode = DisplayModeController.State.Mode.ToString(), menu = menu?.GetType().Name,
                tab = (menu as GameMenu)?.currentTab, uiWidth = Game1.uiViewport.Width, uiHeight = Game1.uiViewport.Height,
                uiScale = Game1.options?.baseUIScale, zoom = Game1.options?.baseZoomLevel,
                preferredUiScale = CompanionPreferences.Current.UiScale,
                hudDrawn = Game1.IsHudDrawn, displayHud = Game1.displayHUD, panMode = Game1.panMode,
                freezeControls = Game1.freezeControls, hostPaused = Game1.HostPaused,
                hudMenus = Game1.onScreenMenus.Select(item => item.GetType().Name).ToArray(),
                currentItem = Game1.player?.CurrentToolIndex, scroll = Dashboard.ScrollRow,
                page = DisplayModeController.Expanded ? (int?)DisplayModeController.SelectedPage : null,
                notice = Dashboard.Notice, section = DashboardPages.Section, quests = Game1.player?.questLog.Count, money = Game1.player?.Money,
                calendarDay = DashboardPages.CalendarSelection, settingsSection = DashboardPages.SettingsSection,
                mapZoom = DashboardPages.MapZoom, mapCenter = new { x = DashboardPages.MapCenter.X, y = DashboardPages.MapCenter.Y },
                detailScroll = DashboardPages.DetailScroll, giftScroll = DashboardPages.GiftScroll, bundleScroll = DashboardPages.BundleScroll,
                selectedIndex = DisplayModeController.Expanded ? DashboardPages.SelectedIndex(DisplayModeController.SelectedPage) : 0,
                calendarEvents = DisplayModeController.Expanded && DisplayModeController.SelectedPage == 10
                    ? DashboardPages.CalendarEvents(DashboardPages.CalendarSelection == 0 ? Game1.dayOfMonth : DashboardPages.CalendarSelection) : null,
                nativeFrames = CompanionPreferences.Current.NativeFrames, visiblePages = CompanionPreferences.Current.VisiblePages,
                recipeCategory = DashboardPages.RecipeCategory,
                recipeKeys = DisplayModeController.Expanded && DisplayModeController.SelectedPage==1 ? DashboardPages.RecipeKeys : null,
                cropStatusCounts = DisplayModeController.Expanded && DisplayModeController.SelectedPage==7 ? DashboardPages.CropStatusCounts : null,
                history = CompanionHistory.Days.Select(day=>new { day=day.Day,shipped=day.Shipped }).ToArray(),
                items = Game1.player?.Items.Select(item => item == null ? null : new { id = item.QualifiedItemId, stack = item.Stack, quality = item.Quality }).ToArray(),
                craftCounts = Game1.player?.craftingRecipes.Keys.ToDictionary(key => key, key => Game1.player.craftingRecipes[key]),
                cookedCounts = Game1.player?.recipesCooked.Keys.ToDictionary(key => key, key => Game1.player.recipesCooked[key]),
                language = LocalizedContentManager.CurrentLanguageCode.ToString(),
                bounds = menu == null ? null : new { x = menu.xPositionOnScreen, y = menu.yPositionOnScreen, width = menu.width, height = menu.height }
            };
            string path = Path.Combine(directory, "result.json");
            File.WriteAllText(path + ".next", JsonSerializer.Serialize(result));
            File.Move(path + ".next", path, true);
        }
    }
}
