using System.Reflection;
using System.Runtime.Loader;

string root = Path.GetFullPath(args[0]);
string modPath = Path.Combine(root, "dualscreen/VerticalWorld/bin/Release/net6.0/VerticalWorld.dll");
string gamePath = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "build/analysis/device-game.dll");
string[] search = {
    Path.Combine(root, "port/stardewvalleymainline/tools/SMAPIBundle/game-root/smapi-internal"),
    Path.Combine(root, "port/stardewvalleymainline/tools/SMAPIBundle/game-root"),
    Path.Combine(root, "port/stardewvalleymainline/gamedata")
};
AssemblyLoadContext.Default.Resolving += (context, name) => {
    if (name.Name == "Stardew Valley") return context.LoadFromAssemblyPath(gamePath);
    foreach (var dir in search)
    {
        string file = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(file)) return context.LoadFromAssemblyPath(file);
    }
    return null;
};
Environment.SetEnvironmentVariable("SDV_DUAL_SCREEN_VERTICAL_WORLD", "1");
Environment.SetEnvironmentVariable("SDV_TARGET_FPS", "30");
Environment.SetEnvironmentVariable("SDV_WORLD_ALPHA_DISCARD", "1");
Environment.SetEnvironmentVariable("SDV_TREE_TRIM", "1");
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);
using (var manifest = assembly.GetManifestResourceStream("Rgdsplus.FirstUseMethods.txt")
    ?? throw new Exception("First-use warmup resource missing from packaged DLL"))
using (var reader = new StreamReader(manifest))
{
    string embedded = reader.ReadToEnd().Replace("\r\n", "\n");
    string source = File.ReadAllText(Path.Combine(root, "dualscreen/VerticalWorld/FirstUseMethods.txt")).Replace("\r\n", "\n");
    if (embedded != source) throw new Exception("First-use manifest differs from source");
}
var warmupType = assembly.GetType("Rgdsplus.VerticalWorld.FirstUseWarmup", true);
var warmupEnabled = warmupType.GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic);
foreach (string value in new[] { "1", "0" })
{
    Environment.SetEnvironmentVariable("SDV_FIRST_USE_WARMUP", value);
    if ((bool)warmupEnabled.GetValue(null) != (value == "1"))
        throw new Exception("First-use warmup override is ineffective");
}
Environment.SetEnvironmentVariable("SDV_FIRST_USE_WARMUP", null);
Console.WriteLine("PASS: packaged first-use manifest matches source and warmup can be enabled or disabled.");
var entry = assembly.GetType("Rgdsplus.VerticalWorld.ModEntry", true);
string logPath = Path.Combine(root, "build/patch-install-test.log");
entry.GetField("LogPath", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, logPath);
var renderer = assembly.GetType("Rgdsplus.VerticalWorld.VerticalWorldRenderer", true);
renderer.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
if (!(bool)renderer.GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
    throw new Exception("Patch installation failed: " + File.ReadAllText(logPath));
Console.WriteLine("PASS: all current Harmony patches installed against the exact device game assembly without starting a game.");
var gameAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(gamePath);
var gameType = gameAssembly.GetType("StardewValley.Game1", true);
var optionsType = gameAssembly.GetType("StardewValley.Options", true);
var gameInstance = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(gameType);
var options = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(optionsType);
gameType.GetField("game1").SetValue(null, gameInstance);
gameType.GetField("instanceOptions").SetValue(gameInstance, options);
var uiPatches = assembly.GetType("Rgdsplus.VerticalWorld.UiPatches", true);
foreach (float zoom in new[] { .5f, .75f, 1f, 1.25f, 1.5f })
foreach (float uiScale in new[] { .75f, 1f, 1.25f, 1.5f })
{
    optionsType.GetField("baseZoomLevel").SetValue(options, zoom);
    optionsType.GetField("baseUIScale").SetValue(options, uiScale);
    uiPatches.GetMethod("EnsureViewports", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    var viewport = gameType.GetField("uiViewport").GetValue(null);
    int width = (int)viewport.GetType().GetProperty("Width").GetValue(viewport);
    int height = (int)viewport.GetType().GetProperty("Height").GetValue(viewport);
    if (Math.Abs(width * (uiScale * 1024f / 1366f) - 1024) > 1.5 || Math.Abs(height * (uiScale * 1024f / 1366f) - 768) > 1.5)
        throw new Exception("Patched UI geometry depends on world zoom or exceeds lower panel");
    if (Math.Abs((float)optionsType.GetProperty("zoomLevel").GetValue(options) - zoom) > .001)
        throw new Exception("World zoom getter did not retain setting");
}
Console.WriteLine("PASS: 20 live patched getter/UI-viewport combinations keep UI independent of world zoom.");

const BindingFlags hiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
const BindingFlags hiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
object Blank(Type type) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var cadenceType = assembly.GetType("Rgdsplus.VerticalWorld.DrawCadence", true);
var advance = cadenceType.GetMethod("Advance", hiddenInstance);
object cadence = Activator.CreateInstance(cadenceType, true);
const long updateStep = 166667;
long renderedTicks = 0;
int renderedCount = 0;
for (int tick = 1; tick <= 600; tick++)
{
    object[] call = { tick * updateStep, updateStep, updateStep, 0L };
    if ((bool)advance.Invoke(cadence, call)) { renderedCount++; renderedTicks += (long)call[3]; }
}
Check(renderedCount == 301 && renderedTicks == 600 * updateStep, "30 Hz draw pacing loses animation time or changes its rate");
// A long frame consumes its accumulated time once and skips expired deadlines.
object[] stalled = { 660 * updateStep, 60 * updateStep, updateStep, 0L };
Check((bool)advance.Invoke(cadence, stalled) && (long)stalled[3] == 60 * updateStep, "Long-frame elapsed time is lost");
object[] afterStall = { 661 * updateStep, updateStep, updateStep, 0L };
Check(!(bool)advance.Invoke(cadence, afterStall), "Pacing bursts after a missed deadline");
object[] reset = { updateStep, updateStep, updateStep, 0L };
Check((bool)advance.Invoke(cadence, reset) && (long)reset[3] == updateStep, "Clock reset leaves stale pending time");
Console.WriteLine("PASS: 30 Hz draws preserve elapsed time, skip expired deadlines, and recover from a clock reset.");
var graphicsTypes = Assembly.Load("MonoGame.Framework");
var engineGame = graphicsTypes.GetType("Microsoft.Xna.Framework.Game", true);
var engineClock = graphicsTypes.GetType("Microsoft.Xna.Framework.GameTime", true);
var runner = Blank(gameAssembly.GetType("StardewValley.GameRunner", true));
engineGame.GetField("_targetElapsedTime", hiddenInstance).SetValue(runner, TimeSpan.FromTicks(updateStep));
engineGame.GetProperty("IsFixedTimeStep").SetValue(runner, true);
var clock = Activator.CreateInstance(engineClock);
var clockAlias = clock;
var framePacing = assembly.GetType("Rgdsplus.VerticalWorld.FramePacing", true);
for (int tick=1;tick<=4;tick++)
{
    engineClock.GetProperty("TotalGameTime").SetValue(clock, TimeSpan.FromTicks(tick*updateStep));
    engineClock.GetProperty("ElapsedGameTime").SetValue(clock, TimeSpan.FromTicks(updateStep));
    object[] call = { runner, clock, null };
    bool draw = (bool)framePacing.GetMethod("BeforeDraw", hiddenStatic).Invoke(null, call);
    Check(draw == (tick != 3), "Real pacing prefix did not skip the alternate update");
    if (tick==4) Check(((TimeSpan)engineClock.GetProperty("ElapsedGameTime").GetValue(clockAlias)).Ticks == 2*updateStep,
        "Global clock alias and Draw argument disagree about elapsed time");
    framePacing.GetMethod("AfterDraw", hiddenStatic).Invoke(null, new[] {clock,call[2]});
    Check(((TimeSpan)engineClock.GetProperty("ElapsedGameTime").GetValue(clock)).Ticks == updateStep,
        "Draw finalizer did not restore the engine update clock");
}
Console.WriteLine("PASS: Draw and its clock aliases share elapsed time; the finalizer restores the update clock.");
var stripRectangle = graphicsTypes.GetType("Microsoft.Xna.Framework.Rectangle", true);
var sourceRegion = Activator.CreateInstance(stripRectangle, 16, 8, 48, 96);
var packedPixels = new uint[80 * 120];
for (int y = 8; y < 104; y++) for (int x = 16; x < 64; x++)
    if (y < 70 && x >= 24 && x < 52 || y >= 70 && x >= 36 && x < 40)
        packedPixels[y*80+x] = 0xff135724;
packedPixels[8*80+16] = 1; // Zero alpha with nonzero RGB must still survive.
packedPixels[103*80+63] = 0xff000000;
var trimType = assembly.GetType("Rgdsplus.VerticalWorld.TreeSpriteTrim", true);
var findStrips = trimType.GetMethod("FindStrips", hiddenStatic);
var strips = (Array)findStrips.Invoke(null, new object[] { packedPixels, 80, sourceRegion });
for (int y = 8; y < 104; y++) for (int x = 16; x < 64; x++)
{
    int coverage = 0;
    foreach (var strip in strips)
        if ((bool)stripRectangle.GetMethod("Contains", new[] { typeof(int), typeof(int) }).Invoke(strip, new object[] { x, y })) coverage++;
    Check(coverage <= 1, "Canopy strips overlap and double-blend a pixel");
    Check(packedPixels[y*80+x] == 0 || coverage == 1, "Canopy trimming removes a visible or nonzero-RGB texel");
}
Check(strips.Length > 1 && strips.Length <= 6, "Sparse canopy was not split into bounded strips");
Array.Fill(packedPixels, 0xffffffff);
strips = (Array)findStrips.Invoke(null, new object[] { packedPixels, 80, sourceRegion });
Check(strips.Length == 1 && strips.GetValue(0).Equals(sourceRegion), "Opaque texture should retain one quad");
Console.WriteLine("PASS: canopy cropping retains every nonzero texel, avoids overlaps, and keeps dense rectangles unsplit.");
if (Environment.GetEnvironmentVariable("SDV_WORLD_RENDER_SCALE") == "0.625")
{
    var quality = assembly.GetType("Rgdsplus.VerticalWorld.WorldRenderQuality", true);
    var graphics = Assembly.Load("MonoGame.Framework");
    var rasterTextureType = graphics.GetType("Microsoft.Xna.Framework.Graphics.Texture2D", true);
    var targetType = graphics.GetType("Microsoft.Xna.Framework.Graphics.RenderTarget2D", true);
    var rectangleType = graphics.GetType("Microsoft.Xna.Framework.Rectangle", true);
    object Rect(int x, int y, int w, int h) => Activator.CreateInstance(rectangleType, x, y, w, h);
    var source = quality.GetMethod("Source", hiddenStatic);
    var register = quality.GetMethod("Register", hiddenStatic);
    foreach (int logicalHeight in new[] { 768, 1024, 1536, 2048, 3072 })
    {
        var target = Blank(targetType);
        int physicalHeight = (int)Math.Round(logicalHeight * .625);
        rasterTextureType.GetField("width", hiddenInstance).SetValue(target, 640);
        rasterTextureType.GetField("height", hiddenInstance).SetValue(target, physicalHeight);
        register.Invoke(null, new object[] { target, 1024, logicalHeight });
        var top = source.Invoke(null, new[] { target, Rect(0, 0, 1024, logicalHeight / 2) });
        var bottom = source.Invoke(null, new[] { target, Rect(0, logicalHeight / 2, 1024, logicalHeight / 2) });
        int Value(object rect, string name) => (int)rectangleType.GetProperty(name)?.GetValue(rect)!;
        Check(Value(top, "Bottom") == Value(bottom, "Top") && Value(bottom, "Bottom") == physicalHeight,
            "Reduced raster leaves a seam or loses the bottom edge");
        Check((int)rectangleType.GetField("Width").GetValue(top) == 640, "Reduced source width is incorrect");
        quality.GetMethod("Clear", hiddenStatic).Invoke(null, null);
        var unchanged = source.Invoke(null, new[] { target, Rect(0, 0, 1024, logicalHeight) });
        Check((int)rectangleType.GetField("Width").GetValue(unchanged) == 1024, "Released target still has stale logical geometry");
    }
    Console.WriteLine("PASS: reduced raster source rectangles share exact seams, cover the full texture, and release metadata.");
}
var controller = assembly.GetType("Rgdsplus.VerticalWorld.DisplayModeController", true);
var state = controller.GetField("State", hiddenStatic).GetValue(null);
state.GetType().GetMethod("TryToggle", hiddenInstance).Invoke(state, new object[] { true });
gameType.GetField("_gameMode", hiddenStatic).SetValue(null, (byte)3);
gameType.GetField("instanceGameLocation").SetValue(gameInstance, Blank(gameAssembly.GetType("StardewValley.GameLocation", true)));
Check((bool)controller.GetProperty("Expanded", hiddenStatic).GetValue(null), "Expanded world context did not activate");
Check(!(bool)gameType.GetProperty("IsHudDrawn").GetValue(null), "Hidden HUD is still drawn or accepting native HUD input");

var geometry = assembly.GetType("Rgdsplus.VerticalWorld.LowerPanelGeometry", true);
var tryMap = geometry.GetMethod("TryMap", hiddenStatic);
int Coordinate(object value, string name) => (int)(value.GetType().GetField(name)?.GetValue(value)
    ?? value.GetType().GetProperty(name).GetValue(value));
object[] mapArgs = { 100f, 200f, 1024, 768, null };
Check((bool)tryMap.Invoke(null, mapArgs), "Local lower-panel touch did not map");
var mapped = mapArgs[4];
Check(Coordinate(mapped, "X") == 100 && Coordinate(mapped, "Y") == 200, "Local touch mapping changed coordinates");
mapArgs = new object[] { 1124f, 200f, 1024, 768, null };
Check((bool)tryMap.Invoke(null, mapArgs), "Composite lower-panel touch did not map");
mapped = mapArgs[4];
Check(Coordinate(mapped, "X") == 100 && Coordinate(mapped, "Y") == 200, "Composite touch mapping changed coordinates");
foreach (var uiSize in new[] { (1024, 768), (911, 683), (768, 576) })
{
    mapArgs = new object[] { 512f, 384f, uiSize.Item1, uiSize.Item2, null };
    Check((bool)tryMap.Invoke(null, mapArgs), "Panel-center touch did not map at UI scale " + uiSize);
    mapped = mapArgs[4];
    Check(Math.Abs(Coordinate(mapped, "X") - uiSize.Item1 / 2) <= 1
        && Math.Abs(Coordinate(mapped, "Y") - uiSize.Item2 / 2) <= 1,
        "Panel-center touch drifted from native menu center at UI scale " + uiSize);
    mapArgs = new object[] { 1023f, 767f, uiSize.Item1, uiSize.Item2, null };
    Check((bool)tryMap.Invoke(null, mapArgs), "Bottom-right character-menu option touch did not map");
    mapped = mapArgs[4];
    Check(Coordinate(mapped, "X") == uiSize.Item1 - 1 && Coordinate(mapped, "Y") == uiSize.Item2 - 1,
        "Bottom-right touch escaped native menu coordinate bounds");
}
mapArgs = new object[] { 2048f, 200f, 1024, 768, null };
Check(!(bool)tryMap.Invoke(null, mapArgs), "Out-of-range touch was accepted");
var beforeMode = state.GetType().GetProperty("Mode", hiddenInstance).GetValue(state).ToString();
state.GetType().GetMethod("TryToggle", hiddenInstance).Invoke(state, new object[] { true });
state.GetType().GetMethod("TryToggle", hiddenInstance).Invoke(state, new object[] { true });
var afterMode = state.GetType().GetProperty("Mode", hiddenInstance).GetValue(state).ToString();
Check(beforeMode == afterMode, "Two accepted Select toggles did not restore the display mode");
Console.WriteLine("PASS: Select mode state toggles and local/composite lower-panel touch mapping are stable.");

var dashboard = assembly.GetType("Rgdsplus.VerticalWorld.Dashboard", true);
var preferences = assembly.GetType("Rgdsplus.VerticalWorld.CompanionPreferences", true);
var currentPreferences = preferences.GetField("Current", hiddenStatic).GetValue(null);
Check(Math.Abs((float)preferences.GetProperty("UiScale").GetValue(currentPreferences) - 1.5f) < .001,
    "New installations do not default to 150% UI");
preferences.GetMethod("ApplyUiScale", hiddenStatic).Invoke(null, null);
Check(Math.Abs((float)optionsType.GetField("baseUIScale").GetValue(options) - 1.5f) < .001,
    "Preferred scale was not applied in companion mode");
state.GetType().GetMethod("TryToggle", hiddenInstance).Invoke(state, new object[] { true });
preferences.GetMethod("ApplyUiScale", hiddenStatic).Invoke(null, null);
Check(Math.Abs((float)optionsType.GetField("baseUIScale").GetValue(options) - 1.5f) < .001,
    "Continuous-world mode lost the 150% baseline");
preferences.GetMethod("SetUiScale", hiddenStatic).Invoke(null, new object[] { 1.25f });
preferences.GetMethod("ApplyUiScale", hiddenStatic).Invoke(null, null);
Check(Math.Abs((float)optionsType.GetField("baseUIScale").GetValue(options) - 1.25f) < .001,
    "Applying preferences overwrote an explicit UI scale");
preferences.GetMethod("SetUiScale", hiddenStatic).Invoke(null, new object[] { 1.5f });
state.GetType().GetMethod("TryToggle", hiddenInstance).Invoke(state, new object[] { true });
foreach (float scale in new[] { .75f, 1f, 1.25f, 1.5f })
{
    optionsType.GetField("baseUIScale").SetValue(options, scale);
    uiPatches.GetMethod("EnsureViewports", hiddenStatic).Invoke(null, null);
    var viewport = gameType.GetField("uiViewport").GetValue(null);
    int width = (int)viewport.GetType().GetProperty("Width").GetValue(viewport);
    int height = (int)viewport.GetType().GetProperty("Height").GetValue(viewport);
    for (int active = 0; active < 14; active++)
    {
        dashboard.GetMethod("Reveal", hiddenStatic).Invoke(null, new object[] { active });
        int rightEdge=0, visible=0;
        for(int page=0;page<14;page++)
        {
            var rect=dashboard.GetMethod("NavBounds",hiddenStatic).Invoke(null,new object[]{page});
            if(Coordinate(rect,"Width")==0)continue;
            Check(Coordinate(rect,"X")>=rightEdge && Coordinate(rect,"Right")<=width && Coordinate(rect,"Bottom")<=height*90/768,
                "Three-tab rail overlapped or escaped its header");
            rightEdge=Coordinate(rect,"Right");visible++;
        }
        Check(visible==3,"Rail must show exactly three pages");
        var activeRect=dashboard.GetMethod("NavBounds",hiddenStatic).Invoke(null,new object[]{active});
        Check(Coordinate(activeRect,"Width")>0,"Selected tab is outside visible rail");
    }
    foreach (var region in new[] { ("InventoryBounds", 36), ("ToolbarBounds", 12) })
    {
        for (int slot = 0; slot < region.Item2; slot++)
        {
            var rect = dashboard.GetMethod(region.Item1, hiddenStatic).Invoke(null, new object[] { slot });
            int x = Coordinate(rect, "X"), y = Coordinate(rect, "Y");
            int right = Coordinate(rect, "Right"), bottom = Coordinate(rect, "Bottom");
            Check(x >= 0 && y >= 0 && right <= width && bottom <= height, region.Item1 + " clipped at UI " + scale);
            object[] touch = { (x + right) * 512f / width, (y + bottom) * 384f / height, width, height, null };
            Check((bool)tryMap.Invoke(null, touch), "Slot center touch was rejected");
            var contains = rect.GetType().GetMethod("Contains", new[] { typeof(int), typeof(int) });
            Check((bool)contains.Invoke(rect, new object[] { Coordinate(touch[4], "X"), Coordinate(touch[4], "Y") }), "Slot draw/hit mapping diverged");
            var origin = dashboard.GetMethod("ItemIconOrigin", hiddenStatic).Invoke(null, new[] { rect });
            float originX = (float)origin.GetType().GetField("X").GetValue(origin);
            float originY = (float)origin.GetType().GetField("Y").GetValue(origin);
            float iconScale = (float)dashboard.GetMethod("ItemIconScale", hiddenStatic).Invoke(null, new[] { rect, (object).9f });
            Check(Math.Abs(originX + 32 - (x + right) / 2f) < .001 && Math.Abs(originY + 32 - (y + bottom) / 2f) < .001,
                "Native item center shifted out of its slot at UI " + scale);
            Check(64 * iconScale <= Math.Min(right - x, bottom - y), "Item sprite exceeds slot at UI " + scale);
        }
    }
    for(int row=0;row<2;row++)
    {
        var label=dashboard.GetMethod("HeaderMeterLabel",hiddenStatic).Invoke(null,new object[]{row});
        var bar=dashboard.GetMethod("HeaderMeterBar",hiddenStatic).Invoke(null,new object[]{row});
        Check(Coordinate(label,"Bottom")<Coordinate(bar,"Y") && Coordinate(bar,"Right")<=width*184/1024,
            "Backpack status label or bar overlaps");
    }
    var dashboardPages = assembly.GetType("Rgdsplus.VerticalWorld.DashboardPages", true);
    for (int day = 1; day <= 28; day++)
    {
        var rect = dashboardPages.GetMethod("CalendarBounds", hiddenStatic).Invoke(null, new object[] { day });
        Check(Coordinate(rect, "X") >= 0 && Coordinate(rect, "Y") >= 0
            && Coordinate(rect, "Right") <= width && Coordinate(rect, "Bottom") <= height, "Calendar day clipped at UI " + scale);
    }
}
Console.WriteLine("PASS: 150% default/shared preference, 192 native item centers and three-tab rail/status geometry across 75%-150% UI.");
var shopLayout = assembly.GetType("Rgdsplus.VerticalWorld.ShopMenuLayout", true);
var fitShopScale = shopLayout.GetMethod("FitScale", hiddenStatic);
foreach (float requested in new[] { .56f, .75f, 1f, 1.5f })
{
    float fitted = (float)fitShopScale.Invoke(null, new object[] { requested, 1024, 768, 48 });
    int logicalWidth = (int)Math.Ceiling(1024d / fitted);
    int requiredWidth = 1000 + 48 * 2 + 120 + 24;
    Check(fitted <= requested && logicalWidth >= requiredWidth,
        "Shop menu side controls exceed the lower viewport at requested scale " + requested);
}
Console.WriteLine("PASS: native shop list, currency box, close button, and scroll controls fit the lower viewport at supported UI scales.");
var dashboardMenuType = assembly.GetType("Rgdsplus.VerticalWorld.DashboardMenu", true);
var dashboardMenu = Blank(dashboardMenuType);
dashboard.GetField("ScrollRow", hiddenStatic).SetValue(null, 0);
dashboardMenuType.GetMethod("receiveScrollWheelAction").Invoke(dashboardMenu, new object[] { -1 });
Check((int)dashboard.GetField("ScrollRow", hiddenStatic).GetValue(null) == 0, "Today inventory unexpectedly scrolled");
var persistentPages = assembly.GetType("Rgdsplus.VerticalWorld.DashboardPages", true);
var rowType = persistentPages.GetNestedType("Row", BindingFlags.NonPublic);
var recipeRows = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType));
for (int i = 0; i < 15; i++) recipeRows.Add(Blank(rowType));
var cachedLists = (System.Collections.IDictionary)persistentPages.GetField("lists", hiddenStatic).GetValue(null);
cachedLists["crafting"] = recipeRows;
persistentPages.GetField("refreshPage", hiddenStatic).SetValue(null, 1);
persistentPages.GetField("refreshAt", hiddenStatic).SetValue(null, long.MaxValue);
controller.GetField("panel", hiddenStatic).SetValue(null, dashboardMenu);
controller.GetMethod("OpenPage", hiddenStatic).Invoke(null, new object[] { 1 });
Check(gameType.GetProperty("activeClickableMenu").GetValue(null) == null, "Companion page took native menu ownership");
dashboardMenuType.GetMethod("receiveScrollWheelAction").Invoke(dashboardMenu, new object[] { -1 });
Check((int)dashboard.GetField("ScrollRow", hiddenStatic).GetValue(null) == 1, "Downward wheel did not advance recipes");
dashboardMenuType.GetMethod("receiveScrollWheelAction").Invoke(dashboardMenu, new object[] { 1 });
dashboardMenuType.GetMethod("receiveScrollWheelAction").Invoke(dashboardMenu, new object[] { 1 });
Check((int)dashboard.GetField("ScrollRow", hiddenStatic).GetValue(null) == 0, "Recipe scroll underflowed");
for (int i = 0; i < 10; i++) dashboardMenuType.GetMethod("receiveScrollWheelAction").Invoke(dashboardMenu, new object[] { -1 });
Check((int)dashboard.GetField("ScrollRow", hiddenStatic).GetValue(null) == 9, "Recipe scroll overflowed");
var pausePrefix = uiPatches.GetMethod("PauseMenuPrefix", hiddenStatic);
object[] pauseArgs = { true };
Check(!(bool)pausePrefix.Invoke(null, pauseArgs) && !(bool)pauseArgs[0], "Companion native pause was not blocked");
state.GetType().GetMethod("TryToggle", hiddenInstance).Invoke(state, new object[] { true });
pauseArgs[0] = true;
Check((bool)pausePrefix.Invoke(null, pauseArgs), "Continuous-world native pause was blocked");
state.GetType().GetMethod("TryToggle", hiddenInstance).Invoke(state, new object[] { true });
Environment.SetEnvironmentVariable("SDV_DUAL_UI_TEST_DIR", null);
var testDriver = assembly.GetType("Rgdsplus.VerticalWorld.UiTestDriver", true);
testDriver.GetMethod("Initialize", hiddenStatic).Invoke(null, new object[] { null });
Check(testDriver.GetField("directory", hiddenStatic).GetValue(null) == null, "QA driver enabled without opt-in");
Console.WriteLine("PASS: inventory hit regions, 112 calendar bounds, nonmodal pages, mode-scoped pause guard, recipe scroll bounds, QA disabled by default.");
controller.GetField("MenuScaleLimit", hiddenStatic).SetValue(null, 1024f / 1366f);
uiPatches.GetMethod("EnsureViewports", hiddenStatic).Invoke(null, null);
var wideViewport = gameType.GetField("uiViewport").GetValue(null);
Check((int)wideViewport.GetType().GetProperty("Width").GetValue(wideViewport) >= 1280, "Fixed-width bundle/calendar page would be cropped at 150% UI");
controller.GetField("MenuScaleLimit", hiddenStatic).SetValue(null, float.MaxValue);

var menuType = gameAssembly.GetType("StardewValley.Menus.IClickableMenu", true);
var gameMenuType = gameAssembly.GetType("StardewValley.Menus.GameMenu", true);
var gameMenu = Blank(gameMenuType);
var pages = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(menuType));
pages.Add(Blank(gameAssembly.GetType("StardewValley.Menus.QuestLog", true)));
gameMenuType.GetField("pages").SetValue(gameMenu, pages);
var canSwitch = controller.GetMethod("CanSwitchMenu", hiddenStatic);
Check((bool)canSwitch.Invoke(null, new[] { gameMenu }), "Ready native page could not change layouts");
gameMenuType.GetField("forcePreventClose").SetValue(null, true);
Check(!(bool)canSwitch.Invoke(null, new[] { gameMenu }), "Native close guard was bypassed");
gameMenuType.GetField("forcePreventClose").SetValue(null, false);
Check(!(bool)canSwitch.Invoke(null, new[] { Blank(gameAssembly.GetType("StardewValley.Menus.SaveGameMenu", true)) }), "Save menu could be dismissed by Select");
Check((bool)canSwitch.Invoke(null, new[] { Blank(gameAssembly.GetType("StardewValley.Menus.DialogueBox", true)) }), "Dialogue cannot be preserved during a layout switch");

var route = assembly.GetType("Rgdsplus.VerticalWorld.DialogueRouting", true);
var shouldRouteLegacy = route.GetMethod("ShouldRouteLegacy", hiddenStatic);
Check(!(bool)shouldRouteLegacy.Invoke(null, new object[] { true, Blank(gameAssembly.GetType("StardewValley.Menus.DialogueBox", true)) }),
    "Active DialogueBox was scheduled for a second legacy draw");
Check((bool)shouldRouteLegacy.Invoke(null, new object[] { true, null }),
    "Expanded legacy dialogue without an active menu was suppressed");
var textureType = gameType.GetProperty("uiScreen").PropertyType;
var lowerTarget = Blank(textureType);
var upperTarget = Blank(textureType);
gameType.GetField("_uiScreen", hiddenInstance).SetValue(gameInstance, lowerTarget);
renderer.GetField("upperUi", hiddenStatic).SetValue(null, upperTarget);
route.GetField("routingMenu", hiddenStatic).SetValue(null, true);
var targetArgs = new[] { lowerTarget };
route.GetMethod("TargetPrefix", hiddenStatic).Invoke(null, targetArgs);
Check(ReferenceEquals(targetArgs[0], upperTarget), "Dialogue UI target was not routed to the upper panel");
targetArgs[0] = null;
route.GetMethod("TargetPrefix", hiddenStatic).Invoke(null, targetArgs);
Check(targetArgs[0] == null, "Dialogue routing modified an unrelated render target");
route.GetField("routingMenu", hiddenStatic).SetValue(null, false);
targetArgs[0] = lowerTarget;
route.GetMethod("TargetPrefix", hiddenStatic).Invoke(null, targetArgs);
Check(ReferenceEquals(targetArgs[0], lowerTarget), "Normal menus stopped using the lower UI target");
gameType.GetField("_uiScreen", hiddenInstance).SetValue(gameInstance, null);
renderer.GetField("upperUi", hiddenStatic).SetValue(null, null);
// Uninitialized GPU objects are test sentinels, never submitted to a device or disposed.
GC.SuppressFinalize(lowerTarget); GC.SuppressFinalize(upperTarget);
gameInstance.GetType().GetField("takingMapScreenshot").SetValue(gameInstance, true);
Check(!(bool)controller.GetProperty("Expanded", hiddenStatic).GetValue(null), "Native map screenshot did not bypass dashboard routing");
Console.WriteLine("PASS: three-tab dashboard hit regions, fixed-size menu fit, native close/save guards, scoped dialogue target routing and screenshot bypass.");
gameInstance.GetType().GetField("takingMapScreenshot").SetValue(gameInstance,false);
optionsType.GetField("baseZoomLevel").SetValue(options,1f);
var weather=assembly.GetType("Rgdsplus.VerticalWorld.WeatherViewport",true);
var worldViewportField=gameType.GetField("viewport");
var viewType=worldViewportField.FieldType;
object View(int height)=>Activator.CreateInstance(viewType,new object[]{400,600,1024,height});
var vectorType=gameType.GetField("previousViewportPosition").FieldType;
object Vector(float x,float y)=>Activator.CreateInstance(vectorType,new object[]{x,y});
float Y(object vector)=>(float)vectorType.GetField("Y").GetValue(vector);
var debrisType=gameAssembly.GetType("StardewValley.WeatherDebris",true);
var debris=Blank(debrisType);
gameType.GetField("random").SetValue(null,new Random(1));
var fadeField=gameType.GetField("screenFade",hiddenStatic);
fadeField.SetValue(null,Blank(fadeField.FieldType));
var timeField=gameType.GetField("currentGameTime");
timeField.SetValue(null,Activator.CreateInstance(timeField.FieldType,new object[]{TimeSpan.FromSeconds(1),TimeSpan.FromMilliseconds(16)}));
debrisType.GetField("dy").SetValue(debris,.5f);
debrisType.GetField("animationTimer").SetValue(debris,100000);
var updateDebris=debrisType.GetMethod("update",new[]{typeof(bool)});
if((bool)controller.GetProperty("Expanded",hiddenStatic).GetValue(null))state.GetType().GetMethod("TryToggle",hiddenInstance).Invoke(state,new object[]{true});
worldViewportField.SetValue(null,View(768));
debrisType.GetField("position").SetValue(debris,Vector(300,780));
updateDebris.Invoke(debris,new object[]{true});
Check(Y(debrisType.GetField("position").GetValue(debris))==780.5f,"Petal wrapped at screen seam");
for(int i=0;i<500;i++)updateDebris.Invoke(debris,new object[]{true});
Check(Y(debrisType.GetField("position").GetValue(debris))==1030.5f,"Petal failed to continue falling through lower screen");
Check(Coordinate(worldViewportField.GetValue(null),"Height")==768,"Weather scope leaked into normal viewport");
debrisType.GetField("position").SetValue(debris,Vector(300,1553));
updateDebris.Invoke(debris,new object[]{true});
Check(Y(debrisType.GetField("position").GetValue(debris))==-64,"Petal did not wrap below full scene");
state.GetType().GetMethod("TryToggle",hiddenInstance).Invoke(state,new object[]{true});
debrisType.GetField("position").SetValue(debris,Vector(300,800));
updateDebris.Invoke(debris,new object[]{true});
Check(Y(debrisType.GetField("position").GetValue(debris))==-64,"Companion weather incorrectly extends to dashboard");
object[] scope={null};weather.GetMethod("BoundsPrefix",hiddenStatic).Invoke(null,scope);
var failure=new InvalidOperationException("scope test");
var actual=weather.GetMethod("BoundsFinalizer",hiddenStatic).Invoke(null,new[]{scope[0],failure});
Check(ReferenceEquals(actual,failure)&&Coordinate(worldViewportField.GetValue(null),"Height")==768,"Exception did not restore weather bounds");
gameInstance.GetType().GetField("takingMapScreenshot").SetValue(gameInstance,true);
scope[0]=null;weather.GetMethod("BoundsPrefix",hiddenStatic).Invoke(null,scope);
Check(scope[0]==null,"Map screenshot was changed by weather scopes");
gameInstance.GetType().GetField("takingMapScreenshot").SetValue(gameInstance,false);
Console.WriteLine("PASS: actual WeatherDebris update crosses seam, falls for 500 ticks, wraps at scene bottom, uses single-panel companion bounds and restores viewport on exceptions/screenshots.");

var characterLayout = assembly.GetType("Rgdsplus.VerticalWorld.CharacterCreationLayout", true);
var titleMenuLayout = assembly.GetType("Rgdsplus.VerticalWorld.TitleMenuLayout", true);
Check((bool)titleMenuLayout.GetProperty("Installed", hiddenStatic).GetValue(null),
    "Title menu patches failed to install: " + File.ReadAllText(logPath));
Check((bool)characterLayout.GetProperty("Installed", hiddenStatic).GetValue(null),
    "Optional character creation patches failed to install: " + File.ReadAllText(logPath));
var titleType = gameAssembly.GetType("StardewValley.Menus.TitleMenu", true);
var characterType = gameAssembly.GetType("StardewValley.Menus.CharacterCustomization", true);
var title = Blank(titleType);
var character = Blank(characterType);
var activeMenuField = gameType.GetField("_activeClickableMenu", hiddenStatic);
var subMenuField = titleType.GetField("_subMenu", hiddenStatic);
var previousMenu = activeMenuField.GetValue(null);
var previousSubMenu = subMenuField.GetValue(null);
var previousMode = gameType.GetField("_gameMode", hiddenStatic).GetValue(null);
var previousScale = optionsType.GetField("baseUIScale").GetValue(options);
try
{
    // TitleMenu.Dispose clears menuContent before its subMenu setter runs.
    // The optional layout postfix must be a no-op in that teardown state.
    activeMenuField.SetValue(null, title);
    titleType.GetField("menuContent").SetValue(title, null);
    assembly.GetType("Rgdsplus.VerticalWorld.CharacterCreationLayout", true)
        .GetMethod("AfterSubMenu", hiddenStatic)
        .Invoke(null, new object[] { true });
    Console.WriteLine("PASS: title-menu teardown does not re-enter disposed layout resources.");

    gameType.GetField("_gameMode", hiddenStatic).SetValue(null, (byte)0);
    activeMenuField.SetValue(null, title);
    subMenuField.SetValue(null, character);
    foreach (float scale in new[] { .75f, 1f, 1.5f })
    {
        optionsType.GetField("baseUIScale").SetValue(options, scale);
        uiPatches.GetMethod("EnsureUiViewport", hiddenStatic).Invoke(null, null);
        Check(Math.Abs((float)optionsType.GetProperty("uiScale").GetValue(options) - 1.5f * 1024f / 1366f) < .001,
            "New character creation does not use 150% UI");
        var characterViewport = gameType.GetField("uiViewport").GetValue(null);
        float characterScale = (float)optionsType.GetProperty("uiScale").GetValue(options);
        Check(Math.Abs(Coordinate(characterViewport, "Width") * characterScale - 1024) <= 1.5f
            && Math.Abs(Coordinate(characterViewport, "Height") * characterScale - 768) <= 1.5f,
            "150% character creation changed the physical panel contract");
        Check((float)optionsType.GetField("baseUIScale").GetValue(options) == scale,
            "Character creation overwrote the saved UI preference");
    }
    characterType.GetField("source").SetValue(character,
        Enum.Parse(characterType.GetField("source").FieldType, "Wizard"));
    optionsType.GetField("baseUIScale").SetValue(options, 1f);
    Check(!(bool)characterLayout.GetProperty("Active", hiddenStatic).GetValue(null),
        "New game layout affects existing character customization");
    Check(Math.Abs((float)optionsType.GetProperty("uiScale").GetValue(options) - 1024f / 1366f) < .001,
        "Wizard menu scale was changed");
    foreach (string menuName in new[] { "AdvancedGameOptions", "LoadGameMenu", "CoopMenu", "TitleTextInputMenu" })
    {
        var submenu = Blank(gameAssembly.GetType("StardewValley.Menus." + menuName, true));
        subMenuField.SetValue(null, submenu);
        uiPatches.GetMethod("EnsureUiViewport", hiddenStatic).Invoke(null, null);
        Check(Math.Abs((float)optionsType.GetProperty("uiScale").GetValue(options) - 1.5f * 1024f / 1366f) < .001,
            menuName + " still uses 100% UI");
        if (menuName is "LoadGameMenu" or "CoopMenu")
            Check((int)titleMenuLayout.GetMethod("PageCount", hiddenStatic).Invoke(null, new[] { submenu }) == 2,
                menuName + " page count does not match the two visible rows");
    }
    activeMenuField.SetValue(null, null);
    var worldLoad = Blank(gameAssembly.GetType("StardewValley.Menus.LoadGameMenu", true));
    Check((int)titleMenuLayout.GetMethod("PageCount", hiddenStatic).Invoke(null, new[] { worldLoad }) == 4,
        "Title page sizing changed an in-game load menu");
}
finally
{
    activeMenuField.SetValue(null, previousMenu);
    subMenuField.SetValue(null, previousSubMenu);
    gameType.GetField("_gameMode", hiddenStatic).SetValue(null, previousMode);
    optionsType.GetField("baseUIScale").SetValue(options, previousScale);
}
Console.WriteLine("PASS: character creation patches install, use 150% on the lower panel, preserve preferences and exclude existing-character menus.");

var nativeTouch = assembly.GetType("Rgdsplus.VerticalWorld.NativeTouch", true);
var dispatchField = nativeTouch.GetField("<Dispatching>k__BackingField", hiddenStatic);
bool previousDispatch = (bool)dispatchField.GetValue(null);
try
{
    dispatchField.SetValue(null, true);
    Check(!(bool)optionsType.GetProperty("SnappyMenus").GetValue(options),
        "Touch still requires the gamepad cursor to select native buttons");
    dispatchField.SetValue(null, false);
    object[] pointerArgs = { false };
    Check((bool)nativeTouch.GetMethod("PointerMenus", hiddenStatic).Invoke(null, pointerArgs),
        "Touch override persists outside touch dispatch");
}
finally { dispatchField.SetValue(null, previousDispatch); }
Console.WriteLine("PASS: native touch bypasses gamepad snap gates only during pointer dispatch.");

var dialogueLayout = assembly.GetType("Rgdsplus.VerticalWorld.DialogueLayout", true);
var fitDialogueWidth = dialogueLayout.GetMethod("FitWidth", hiddenStatic);
var savedDialogueMenu = activeMenuField.GetValue(null);
var savedDialogueMode = gameType.GetField("_gameMode", hiddenStatic).GetValue(null);
var savedDialogueScale = optionsType.GetField("baseUIScale").GetValue(options);
try
{
    activeMenuField.SetValue(null, null);
    gameType.GetField("_gameMode", hiddenStatic).SetValue(null, (byte)3);
    foreach (float scale in new[] { .75f, 1f, 1.25f, 1.5f })
    foreach (bool expanded in new[] { false, true })
    {
        optionsType.GetField("baseUIScale").SetValue(options, scale);
        var mode = state.GetType().GetProperty("Mode", hiddenInstance).GetValue(state).ToString();
        if ((mode == "ExpandedHud") != expanded)
            state.GetType().GetMethod("TryToggle", hiddenInstance).Invoke(state, new object[] { true });
        foreach (int original in new[] { 1200, 1240 })
        {
            int width = (int)fitDialogueWidth.Invoke(null, new object[] { original });
            var view = gameType.GetField("uiViewport").GetValue(null);
            int viewportWidth = Coordinate(view, "Width");
            int x = viewportWidth / 2 - width / 2;
            Check(x - 44 >= 0 && x + width + 40 <= viewportWidth,
                "Native dialogue frame exceeds the physical panel at " + scale);
            Check(width - 484 > 0, "Dialogue portrait leaves no text column");
            Check((int)fitDialogueWidth.Invoke(null, new object[] { 300 }) == 300,
                "Short dialogue width was enlarged");
        }
    }
    gameType.GetField("takingMapScreenshot").SetValue(gameInstance, true);
    Check((int)fitDialogueWidth.Invoke(null, new object[] { 1200 }) == 1200,
        "Dialogue fit modifies a native map screenshot");
}
finally
{
    gameType.GetField("takingMapScreenshot").SetValue(gameInstance, false);
    activeMenuField.SetValue(null, savedDialogueMenu);
    gameType.GetField("_gameMode", hiddenStatic).SetValue(null, savedDialogueMode);
    optionsType.GetField("baseUIScale").SetValue(options, savedDialogueScale);
}
Console.WriteLine("PASS: dialogue body, native outer corners and portrait text fit both modes at 75%-150%, without changing short boxes or screenshot mode.");

var letterLayout = assembly.GetType("Rgdsplus.VerticalWorld.LetterLayout", true);
foreach(float scale in new[] {.75f,1f,1.25f,1.5f})
{
    optionsType.GetField("baseUIScale").SetValue(options,scale);
    uiPatches.GetMethod("EnsureViewports",hiddenStatic).Invoke(null,null);
    var vp = gameType.GetField("uiViewport").GetValue(null);
    int vw=(int)vp.GetType().GetProperty("Width").GetValue(vp), vh=(int)vp.GetType().GetProperty("Height").GetValue(vp);
    int lw=(int)letterLayout.GetMethod("FitWidth",hiddenStatic).Invoke(null,new object[]{1280});
    int lh=(int)letterLayout.GetMethod("FitHeight",hiddenStatic).Invoke(null,new object[]{720});
    Check(lw+64<=vw && lh+64<=vh && lw>=800 && lh>=500,"Letter bounds exceed the viewport or become unreadable");
}
Console.WriteLine("PASS: mail geometry fits at all supported UI scales with room for close and attachment controls.");

// Optional integration test uses an installed DDF assembly read-only.
var ddfFile = Directory.EnumerateFiles(Path.Combine(root, "mod"), "DialogueDisplayFramework.dll", SearchOption.AllDirectories).FirstOrDefault();
if (ddfFile != null)
{
    var ddf = AssemblyLoadContext.Default.LoadFromAssemblyPath(ddfFile);
    var adapter = assembly.GetType("Rgdsplus.VerticalWorld.PortraitCompatibility", true);
    adapter.GetMethod("Install", hiddenStatic).Invoke(null, null);
    Check(adapter.GetField("getData", hiddenStatic).GetValue(null) != null, "Optional DDF hooks failed to install");
    var dataType = ddf.GetType("DialogueDisplayFramework.DialogueDisplayData", true);
    object Make(string name) => Activator.CreateInstance(ddf.GetType("DialogueDisplayFramework." + name, true));
    void Put(object item, string field, object value) => item.GetType().GetField(field).SetValue(item, value);
    object Read(object item, string field) => item.GetType().GetField(field).GetValue(item);
    var fit = adapter.GetMethod("FitData", hiddenStatic).MakeGenericMethod(dataType);
    foreach (var tile in new[] { (150, 150), (250, 250), (250, 450) })
    {
        var original = Make("DialogueDisplayData");
        var portrait = Make("PortraitData");
        Put(original, "packName", "Mud.SkimpyPortraitsCP");
        Put(original, "width", 900); Put(original, "xOffset", 508);
        Put(portrait, "w", tile.Item1); Put(portrait, "h", tile.Item2);
        Put(portrait, "xOffset", -900); Put(portrait, "yOffset", -1736);
        Put(original, "portrait", portrait); Put(original, "dialogue", Make("DialogueData"));
        foreach (float scale in new[] { .75f, 1f, 1.25f, 1.5f, .75f })
        {
            optionsType.GetField("baseUIScale").SetValue(options, scale);
            object[] call = { original }; fit.Invoke(null, call);
            var result = call[0]; var p = Read(result, "portrait"); var t = Read(result, "dialogue");
            int w = (int)Read(result, "width"), h = (int)Read(result, "height");
            float s = (float)Read(p, "scale");
            Check((int)Read(p, "xOffset") >= 0 && (int)Read(p, "yOffset") >= 0, "Portrait begins offscreen");
            Check((int)Read(p, "xOffset") + tile.Item1 * s < (int)Read(t, "xOffset"), "Portrait overlaps dialogue text");
            Check((int)Read(p, "yOffset") + tile.Item2 * s <= h - 56, "Portrait overlaps name or is cropped");
            Check((int)Read(t, "xOffset") + (int)Read(t, "width") < w, "Text exceeds dialogue frame");
            Check((int)Read(original, "xOffset") == 508 && (int)Read(portrait, "xOffset") == -900, "DDF source cache mutated");
            Check((int)Read(p, "w") == tile.Item1 && (int)Read(p, "h") == tile.Item2, "Repeated fitting changes tile dimensions");
        }
    }
    Console.WriteLine("PASS: actual DDF optional patches install; square/tall portraits and text fit 75%-150% without mutating DDF source layouts.");
}

var loadingOverlay = assembly.GetType("Rgdsplus.VerticalWorld.LoadingOverlay", true);
var loadProgress = loadingOverlay.GetMethod("ProgressForStage", hiddenStatic);
var keepSaveOverlay = loadingOverlay.GetMethod("ShouldKeepSaveOverlay", hiddenStatic);
var hideOnWorldReady = loadingOverlay.GetMethod("ShouldHideForWorldReady", hiddenStatic);
var titleSettled = loadingOverlay.GetMethod("StartupTitleSettled", hiddenStatic);
var smapiAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(
    Path.Combine(root, "port/stardewvalleymainline/tools/SMAPIBundle/game-root/StardewModdingAPI.dll"));
var loadStageType = smapiAssembly.GetType("StardewModdingAPI.Enums.LoadStage", true);
float LoadProgress(string name) => (float)loadProgress.Invoke(null,
    new[] { Enum.Parse(loadStageType, name) });
Check(LoadProgress("SaveParsed") < LoadProgress("SaveLoadedBasicInfo")
    && LoadProgress("SaveLoadedBasicInfo") < LoadProgress("SaveAddedLocations")
    && LoadProgress("SaveAddedLocations") < LoadProgress("SaveLoadedLocations")
    && LoadProgress("SaveLoadedLocations") < LoadProgress("Preloaded")
    && LoadProgress("Preloaded") < LoadProgress("Ready"),
    "Save-loading progress stages are not monotonic");
Check(LoadProgress("CreatedBasicInfo") < LoadProgress("CreatedInitialLocations")
    && LoadProgress("CreatedInitialLocations") < LoadProgress("CreatedLocations")
    && LoadProgress("CreatedLocations") < LoadProgress("CreatedSaveFile"),
    "New-game progress stages are not monotonic");
Check((bool)keepSaveOverlay.Invoke(null, new object[] { Enum.Parse(loadStageType, "Loaded"), true })
    && (bool)keepSaveOverlay.Invoke(null, new object[] { Enum.Parse(loadStageType, "Ready"), true })
    && !(bool)keepSaveOverlay.Invoke(null, new object[] { Enum.Parse(loadStageType, "Ready"), false }),
    "Save overlay hides before SaveLoaded or remains active outside save loading");
Check(!(bool)hideOnWorldReady.Invoke(null, new object[] { true, true, true, Enum.Parse(loadStageType, "Ready") })
    && !(bool)hideOnWorldReady.Invoke(null, new object[] { true, false, true, Enum.Parse(loadStageType, "CreatedLocations") })
    && !(bool)hideOnWorldReady.Invoke(null, new object[] { false, false, true, Enum.Parse(loadStageType, "Ready") })
    && (bool)hideOnWorldReady.Invoke(null, new object[] { true, false, true, Enum.Parse(loadStageType, "Ready") })
    && (bool)hideOnWorldReady.Invoke(null, new object[] { true, false, false, Enum.Parse(loadStageType, "None") }),
    "Startup/save overlay world-ready hide policy is incorrect");
var beginLoadingTest = loadingOverlay.GetMethod("BeginTest", hiddenStatic);
var onSaveLoaded = loadingOverlay.GetMethod("OnSaveLoaded", BindingFlags.Static | BindingFlags.NonPublic);
var overlayStatus = loadingOverlay.GetMethod("Status", hiddenStatic);
beginLoadingTest.Invoke(null, new object[] { true });
onSaveLoaded.Invoke(null, Array.Empty<object>());
var endedSaveOverlay = overlayStatus.Invoke(null, Array.Empty<object>());
Check(!(bool)endedSaveOverlay.GetType().GetProperty("visible").GetValue(endedSaveOverlay),
    "Save overlay remains visible after SaveLoaded");
Check(!(bool)titleSettled.Invoke(null, new object[] { 1000L, 3499L })
    && (bool)titleSettled.Invoke(null, new object[] { 1000L, 3500L })
    && !(bool)titleSettled.Invoke(null, new object[] { 0L, 100000L }),
    "Startup overlay title settle timing is not enforced");
var detailFactor = assembly.GetType("Rgdsplus.VerticalWorld.PortraitTextureCache", true)
    .GetMethod("DetailDownsampleFactor", hiddenStatic);
int DetailFactor(int width, int height) => (int)detailFactor.Invoke(null, new object[] { width, height });
Check(DetailFactor(600, 600) == 2 && DetailFactor(1000, 1800) == 4
    && DetailFactor(2400, 2400) == 4, "Detailed portrait prefilter factor is incorrect");
Console.WriteLine("PASS: loading status advances through real SMAPI stages; oversized detailed portraits are box-prefiltered before point-sampled rendering.");
