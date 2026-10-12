using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Rgdsplus.PerformanceProbe;

public sealed class ModEntry : Mod
{
    private static string directory, label, scene;
    private static FieldInfo fixture;
    private static IInputHelper input;
    private static SButton movementButton;
    private static string pendingWarpTarget;
    private static long pendingWarpAt;
    private static readonly Dictionary<MethodBase, int> indices = new();
    private static readonly List<string> names = new();
    private const int MethodCapacity = 64;
    private static readonly long[] elapsed = new long[MethodCapacity], allocated = new long[MethodCapacity], calls = new long[MethodCapacity];
    private static readonly long[] firstElapsed = new long[MethodCapacity], maxElapsed = new long[MethodCapacity], firstAllocated = new long[MethodCapacity];
    private static readonly double[] frames = new double[20000];
    private static readonly bool[] actionFrames = new bool[20000];
    private static readonly long[] frameAllocated = new long[20000], frameGc0 = new long[20000], frameGc1 = new long[20000], frameGc2 = new long[20000];
    private static readonly long[,] frameMethodTicks = new long[20000, MethodCapacity];
    private static long frameTotalAllocated;
    private static readonly int[] frameCollections = new int[3];
    private static readonly bool tickOnly = Environment.GetEnvironmentVariable("SDV_PERF_TICK_ONLY") == "1";
    private static readonly int[] actionNumbers = new int[20000];
    private static readonly double[] sampleTimes = new double[20000];
    private static readonly double[] frameJitMs = new double[20000];
    private static readonly long[] frameJitMethods = new long[20000];
    private static TimeSpan lastJitTime;
    private static long lastJitMethods;
    private static bool measuring, actionFrame, runActions;
    private static bool drewThisTick;
    private static bool presentationFeedback;
    private static JitTrace jitTrace;
    [DllImport("rgds-presentation-qa")] private static extern int rgds_presentation_init();
    [DllImport("rgds-presentation-qa")] private static extern void rgds_presentation_start();
    [DllImport("rgds-presentation-qa")] private static extern void rgds_presentation_before();
    [DllImport("rgds-presentation-qa")] private static extern void rgds_presentation_after();
    [DllImport("rgds-presentation-qa")] private static extern int rgds_presentation_stop(string path);
    private static int count, active, actions, initialObjects, maxDebris, maxSprites;
    private static long start, lastFrame, globalAllocated, pollAt, actionAt, releaseAt;
    private static readonly int[] collections = new int[3];
    private static readonly List<object> actionEvents = new();
    private static Action installRenderDetail;
    private static bool renderDetailInstalled;
    private static Action installObjectDetail;
    private static bool objectDetailInstalled;
    public struct Timing { public long Start, Allocated; public bool Enabled; }

    public override void Entry(IModHelper helper)
    {
        directory = Environment.GetEnvironmentVariable("SDV_DUAL_PERF_TEST_DIR");
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SDV_DUAL_UI_TEST_DIR"))) return;
        Directory.CreateDirectory(directory);
        if (Environment.GetEnvironmentVariable("SDV_QA_JIT_TRACE") == "1") jitTrace = new JitTrace();
        input = helper.Input;
        helper.Events.GameLoop.GameLaunched += (_, _) => Initialize();
        helper.Events.GameLoop.UpdateTicking += (_, _) =>
        {
            if (measuring && scene == "move" && releaseAt != 0 && Environment.TickCount64 < releaseAt)
                input.Press(movementButton);
        };
        helper.Events.GameLoop.UpdateTicked += (_, _) => Poll();
    }
    private void Initialize()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "VerticalWorld");
        fixture = assembly.GetType("Rgdsplus.VerticalWorld.UiTestDriver").GetField("fixtureActive", BindingFlags.NonPublic | BindingFlags.Static);
        var harmony = new Harmony("RGDSplus.PerformanceProbe");
        if (File.Exists(Path.Combine(directory, "librgds-presentation-qa.so")))
        {
            NativeLibrary.SetDllImportResolver(typeof(ModEntry).Assembly, (name, _, _) => name == "rgds-presentation-qa"
                ? NativeLibrary.Load(Path.Combine(directory, "librgds-presentation-qa.so")) : IntPtr.Zero);
            presentationFeedback = rgds_presentation_init() == 1;
            if (presentationFeedback) harmony.Patch(AccessTools.Method(typeof(Game), "EndDraw"),
                new HarmonyMethod(typeof(ModEntry), nameof(PresentationBefore)), new HarmonyMethod(typeof(ModEntry), nameof(PresentationAfter)));
            File.WriteAllText(Path.Combine(directory, "presentation-ready.json"), JsonSerializer.Serialize(new { presentationFeedback }));
        }
        void Patch(Type type, string name)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => m.Name == name))
            {
                if (names.Count >= elapsed.Length) throw new InvalidOperationException("Performance method capacity exceeded.");
                if (indices.ContainsKey(method)) continue;
                indices.Add(method, names.Count); names.Add(type.Name + "." + name);
                harmony.Patch(method, new HarmonyMethod(typeof(ModEntry), nameof(Begin)), new HarmonyMethod(typeof(ModEntry), nameof(End)));
            }
        }
        Patch(typeof(Game), "Tick");
        harmony.Patch(AccessTools.Method(typeof(GameRunner), "Draw"),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ObserveDraw)));
        if (!tickOnly)
        {
        foreach (string n in new[] { "DoUpdate", "DoDraw", "EndDraw" }) Patch(typeof(Game), n);
        foreach (string n in new[] { "Update", "Draw", "EndDraw" }) Patch(typeof(GameRunner), n);
        foreach (string n in new[] { "Update", "Draw", "DrawWorld", "renderScreenBuffer" }) Patch(typeof(Game1), n);
        Patch(assembly.GetType("Rgdsplus.VerticalWorld.Dashboard"), "Draw");
        foreach (string n in new[] { "Refresh", "Rows" }) Patch(assembly.GetType("Rgdsplus.VerticalWorld.DashboardPages"), n);
        Patch(typeof(MeleeWeapon), "DoDamage");
        Patch(typeof(Axe), "DoFunction"); Patch(typeof(Pickaxe), "DoFunction");
        foreach (var type in new[] { typeof(GameLocation), typeof(TerrainFeature), typeof(Tree), typeof(Grass), typeof(HoeDirt), typeof(StardewValley.Object), typeof(ResourceClump), typeof(Fence), typeof(Bush) })
            Patch(type, "performToolAction");
        installRenderDetail = () =>
        {
            Guard();
            if (measuring || renderDetailInstalled) return;
            Patch(typeof(StardewValley.Object), "draw");
            var graphics = typeof(Microsoft.Xna.Framework.Graphics.Texture2D).Assembly;
            Patch(graphics.GetType("Microsoft.Xna.Framework.Graphics.SpriteBatcher"), "DrawBatch");
            Patch(graphics.GetType("Microsoft.Xna.Framework.Graphics.SpriteBatcher"), "FlushVertexArray");
            Patch(graphics.GetType("Microsoft.Xna.Framework.Graphics.VertexDeclaration"), "Apply");
            Patch(graphics.GetType("Microsoft.Xna.Framework.Graphics.TextureCollection"), "SetTextures");
            Patch(graphics.GetType("Microsoft.Xna.Framework.Graphics.GraphicsDevice"), "ApplyState");
            renderDetailInstalled = true;
        };
        installObjectDetail = () =>
        {
            Guard();
            if (measuring || objectDetailInstalled) return;
            foreach (var n in new[] { "draw", "GetBoundingBoxAt", "isPassable", "IsSprinkler", "GetBaseRadiusForSprinkler" })
                Patch(typeof(StardewValley.Object), n);
            foreach (var n in new[] { "get_QualifiedItemId", "get_ItemId" }) Patch(typeof(Item), n);
            foreach (var n in new[] { "GetDataOrErrorItem", "GetMetadata" }) Patch(typeof(ItemRegistry), n);
            var parsed = typeof(ItemRegistry).Assembly.GetType("StardewValley.ItemTypeDefinitions.ParsedItemData");
            foreach (var n in new[] { "GetTexture", "GetSourceRect" }) Patch(parsed, n);
            Patch(typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch), "Draw");
            objectDetailInstalled = true;
        };
        }
        lastJitTime = JitInfo.GetCompilationTime(true);
        lastJitMethods = JitInfo.GetCompiledMethodCount(true);
        File.WriteAllText(Path.Combine(directory, "ready.json"), JsonSerializer.Serialize(names));
    }
    private static void Begin(MethodBase __originalMethod, out Timing __state)
    {
        __state = new Timing { Enabled = measuring };
        if (!measuring) return;
        __state.Allocated = GC.GetAllocatedBytesForCurrentThread(); __state.Start = Stopwatch.GetTimestamp();
        if (__originalMethod.DeclaringType == typeof(MeleeWeapon) || __originalMethod.DeclaringType == typeof(Axe) || __originalMethod.DeclaringType == typeof(Pickaxe)) actionFrame = true;
    }
    private static void ObserveDraw() { if (measuring) drewThisTick = true; }
    private static void PresentationBefore() => rgds_presentation_before();
    private static void PresentationAfter() => rgds_presentation_after();
    private static void End(MethodBase __originalMethod, Timing __state)
    {
        if (!measuring || !__state.Enabled) return;
        long now = Stopwatch.GetTimestamp(); int i = indices[__originalMethod];
        long duration = now - __state.Start;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - __state.Allocated;
        elapsed[i] += duration;
        allocated[i] += bytes; calls[i]++;
        if (calls[i] == 1) { firstElapsed[i] = duration; firstAllocated[i] = bytes; }
        if (duration > maxElapsed[i]) maxElapsed[i] = duration;
        if (count < frames.Length) frameMethodTicks[count, i] += duration;
        if (names[i] != "Game.Tick") return;
        if (!drewThisTick) return;
        drewThisTick = false;
        TimeSpan jitTime = JitInfo.GetCompilationTime(true);
        long jitMethods = JitInfo.GetCompiledMethodCount(true);
        if (lastFrame != 0 && count < frames.Length)
        {
            sampleTimes[count] = Ms(now - start);
            actionNumbers[count] = actions;
            frameJitMs[count] = (jitTime - lastJitTime).TotalMilliseconds;
            frameJitMethods[count] = jitMethods - lastJitMethods;
            frames[count] = Ms(now - lastFrame); actionFrames[count++] = actionFrame;
            frameAllocated[count - 1] = GC.GetTotalAllocatedBytes(false) - frameTotalAllocated;
            frameGc0[count - 1] = GC.CollectionCount(0) - frameCollections[0];
            frameGc1[count - 1] = GC.CollectionCount(1) - frameCollections[1];
            frameGc2[count - 1] = GC.CollectionCount(2) - frameCollections[2];
            if (GameRunner.instance.IsActive) active++;
        }
        else if (lastFrame == 0)
            for (int m = 0; m < names.Count; m++) frameMethodTicks[0, m] = 0;
        frameTotalAllocated = GC.GetTotalAllocatedBytes(false);
        for (int g = 0; g < 3; g++) frameCollections[g] = GC.CollectionCount(g);
        lastJitTime = jitTime;
        lastJitMethods = jitMethods;
        lastFrame = now; actionFrame = false;
    }
    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    private static void Reply(object value)
    {
        string path = Path.Combine(directory, "result.json");
        File.WriteAllText(path + ".next", JsonSerializer.Serialize(value));
        File.Move(path + ".next", path, true);
    }
    private static void Guard()
    {
        if (fixture?.GetValue(null) is not true || !Context.IsWorldReady) throw new InvalidOperationException("Only the in-memory QA fixture is allowed.");
    }
    private static object Precompile(bool toolChain, bool transitive)
    {
        Guard(); if (measuring) throw new InvalidOperationException("Cannot precompile during measurement.");
        var results = new List<object>();
        long before = Stopwatch.GetTimestamp();
        var targets = new List<(Type, string)> { (typeof(Farmer), "BeginUsingTool"), (typeof(Farmer), "EndUsingTool"),
            (typeof(Tool), "beginUsing"), (typeof(Tool), "endUsing"), (typeof(MeleeWeapon), "beginUsing"),
            (typeof(MeleeWeapon), "DoDamage"), (typeof(Axe), "DoFunction"), (typeof(Pickaxe), "DoFunction"),
            (typeof(Grass), "performToolAction"), (typeof(Tree), "performToolAction"),
            (typeof(StardewValley.Object), "performToolAction"), (typeof(GameLocation), "performToolAction") };
        if (toolChain) targets.AddRange(new[] { (typeof(Farmer), "performBeginUsingTool"), (typeof(Farmer), "performEndUsingTool"),
            (typeof(Farmer), "performFireTool"), (typeof(Farmer), "useTool"),
            (typeof(FarmerSprite), "getAnimationFromIndex"), (typeof(FarmerSprite), "animateOnce"),
            (typeof(FarmerSprite), "doneWithAnimation"), (typeof(FarmerSprite), "currentAnimationTick"),
            (typeof(MeleeWeapon), "DoFunction"), (typeof(MeleeWeapon), "doSwipe"),
            (typeof(Game1), "createRadialDebris"), (typeof(Game1), "createMultipleObjectDebris"),
            (typeof(Game1), "createObjectDebris"), (typeof(Tree), "shake") });
        var queue = new Queue<(MethodBase Method, int Depth)>();
        var seen = new HashSet<MethodBase>();
        foreach (var target in targets)
        {
            foreach (var method in target.Item1.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => m.Name == target.Item2 && !m.IsAbstract && !m.ContainsGenericParameters))
                queue.Enqueue((method, 0));
        }
        while (queue.Count > 0 && seen.Count < 1200)
        {
            var (method, depth) = queue.Dequeue();
            if (method.IsAbstract || method.ContainsGenericParameters || !seen.Add(method)) continue;
            long at = Stopwatch.GetTimestamp();
            string error = null;
            try { RuntimeHelpers.PrepareMethod(method.MethodHandle); }
            catch (Exception ex) { error = ex.GetType().Name; }
            results.Add(new { method = method.DeclaringType.FullName + "." + method.Name, depth, error, ms = Ms(Stopwatch.GetTimestamp() - at) });
            if (!transitive || depth >= 3 || method.GetMethodBody() == null) continue;
            try
            {
                foreach (var instruction in PatchProcessor.GetOriginalInstructions(method))
                    if (instruction.operand is MethodBase child && (child.DeclaringType.Assembly == typeof(Game1).Assembly
                        || child.DeclaringType.Assembly == typeof(Game).Assembly)) queue.Enqueue((child, depth + 1));
            }
            catch (Exception) { /* QA reports prepared methods; unsupported IL is not executed. */ }
        }
        return new { ms = Ms(Stopwatch.GetTimestamp() - before), toolChain, transitive, methods = results };
    }
    private static void Prepare(string value)
    {
        Guard(); if (measuring) throw new InvalidOperationException("Cannot rebuild a measured scene.");
        scene = value; var farm = Game1.getFarm();
        farm.terrainFeatures.Clear(); farm.objects.Clear(); farm.resourceClumps.Clear(); farm.largeTerrainFeatures.Clear(); farm.animals.Clear();
        farm.debris.Clear(); farm.temporarySprites.Clear(); farm.characters.Clear();
        Game1.currentLocation = farm; Game1.player.currentLocation = farm;
        Game1.player.completelyStopAnimatingOrDoingAction(); Game1.player.CanMove = true;
        Game1.player.Position = new Vector2(40 * 64, 24 * 64); Game1.player.Stamina = Game1.player.MaxStamina;
        Game1.timeOfDay = 900; Game1.isRaining = false; Game1.isSnowing = false; Game1.currentSeason = "spring";
        Game1.eventUp = Game1.dialogueUp = Game1.freezeControls = false; Game1.random = new Random(20261008);
        Game1.player.Items[0] = value == "move" || value == "transition" ? null : value == "axe" ? new Axe() : value == "pickaxe" ? new Pickaxe() : new MeleeWeapon("47");
        Game1.player.CurrentToolIndex = 0;
        for (int y = 14; y <= 44; y++) for (int x = 28; x <= 57; x++)
        {
            if (x == 40 && y == 24 || value == "sparse" || value == "data" || value == "move" || value == "transition") continue;
            var tile = new Vector2(x, y);
            if (value == "axe" || value == "mixed" && x % 4 == 0 && y % 4 == 0) farm.terrainFeatures[tile] = new Tree("1", 5);
            else if (value == "pickaxe" || value == "mixed" && (x + y) % 9 == 0)
            {
                // The (Vector2, string) constructor creates a BIG craftable.
                // Use the item registry so this is a real, breakable stone.
                var stone = (StardewValley.Object)ItemRegistry.Create("(O)343");
                if (stone.bigCraftable.Value || !stone.IsBreakableStone() || ItemRegistry.GetDataOrErrorItem(stone.QualifiedItemId).IsErrorItem)
                    throw new InvalidOperationException("Invalid stone performance fixture.");
                stone.TileLocation = tile; stone.MinutesUntilReady = 2; farm.objects[tile] = stone;
            }
            else farm.terrainFeatures[tile] = new Grass(1, 4);
        }
        if (value == "data") for (int y = 0; y < 12; y++) for (int x = 0; x < 60; x++)
        {
            var tile = new Vector2(x, y); var crop = new Crop("472", x, y, farm);
            if ((x + y) % 3 == 0) crop.currentPhase.Value = crop.phaseDays.Count - 1;
            farm.terrainFeatures[tile] = new HoeDirt((x + y) % 2, crop);
            var machine = (StardewValley.Object)ItemRegistry.Create("(BC)12");
            machine.TileLocation = tile; machine.heldObject.Value = (StardewValley.Object)ItemRegistry.Create("(O)334");
            machine.MinutesUntilReady = (x + y) * 30; machine.readyForHarvest.Value = (x + y) % 3 == 0; farm.objects[tile] = machine;
        }
        if (value == "transition")
        {
            // Keep the synthetic fixture's unplayed story events out of the transition benchmark.
            foreach (string location in new[] { "Farm", "FarmHouse" })
                foreach (string key in Game1.content.Load<Dictionary<string, string>>("Data\\Events\\" + location).Keys)
                    Game1.player.eventsSeen.Add(key.Split('/')[0]);
            foreach (var location in Game1.locations) location.currentEvent = null;
            Game1.activeClickableMenu = null;
            Game1.eventUp = Game1.dialogueUp = Game1.messagePause = Game1.freezeControls = false;
            Game1.warpFarmer("FarmHouse", 9, 8, 2);
        }
    }
    private static void Action()
    {
        if (Game1.player.UsingTool) return;
        if (scene == "move")
        {
            actions++; actionFrame = true;
            actionEvents.Add(new { action = actions, phase = "move-start", timeMs = Ms(Stopwatch.GetTimestamp() - start), x = Game1.player.Position.X, y = Game1.player.Position.Y });
            movementButton = Game1.options.moveDownButton[0].ToSButton();
            releaseAt = Environment.TickCount64 + 500;
            return;
        }
        if (scene == "transition")
        {
            if (pendingWarpTarget != null) return;
            actions++; actionFrame = true;
            actionEvents.Add(new { action = actions, phase = "warp-start", timeMs = Ms(Stopwatch.GetTimestamp() - start), location = Game1.currentLocation.Name });
            pendingWarpTarget = Game1.currentLocation.Name == "FarmHouse" ? "Farm" : "FarmHouse";
            pendingWarpAt = Environment.TickCount64;
            if (pendingWarpTarget == "Farm") Game1.warpFarmer("Farm", 64, 15, 2);
            else Game1.warpFarmer("FarmHouse", 9, 8, 2);
            return;
        }
        int n = scene == "axe" ? actions / 12 : scene == "pickaxe" ? actions / 2 : actions;
        int x = 29 + n % 27, y = 16 + n / 27 * 3;
        if (y > 43) { runActions = false; return; }
        actions++; actionFrame = true; Game1.player.Position = new Vector2(x * 64, y * 64); Game1.player.FacingDirection = 2;
        Game1.player.lastClick = new Vector2(x * 64 + 32, (y + 1) * 64 + 32);
        Game1.player.BeginUsingTool(); releaseAt = Environment.TickCount64 + 150;
    }
    private static void Poll()
    {
        if (fixture?.GetValue(null) is true)
        {
            Game1.timeOfDay = 900;
            if (measuring)
            {
                // Engine warp is asynchronous; observe the destination on later ticks.
                if (pendingWarpTarget != null && Game1.currentLocation.Name == pendingWarpTarget && Environment.TickCount64 - pendingWarpAt >= 500)
                {
                    actionEvents.Add(new { action = actions, phase = "warp-end", timeMs = Ms(Stopwatch.GetTimestamp() - start), location = Game1.currentLocation.Name,
                        menu = Game1.activeClickableMenu?.GetType().Name, eventUp = Game1.eventUp, freezeControls = Game1.freezeControls });
                    pendingWarpTarget = null;
                }
                maxDebris = Math.Max(maxDebris, Game1.currentLocation.debris.Count); maxSprites = Math.Max(maxSprites, Game1.currentLocation.temporarySprites.Count);
                if (releaseAt != 0 && Environment.TickCount64 >= releaseAt)
                {
                    if (scene == "move")
                    {
                        Game1.player.Halt();
                        actionEvents.Add(new { action = actions, phase = "move-stop", timeMs = Ms(Stopwatch.GetTimestamp() - start), x = Game1.player.Position.X, y = Game1.player.Position.Y });
                    }
                    else if (Game1.player.CurrentTool is not MeleeWeapon) Game1.player.EndUsingTool();
                    releaseAt = 0;
                }
            }
        }
        if (measuring && runActions && Environment.TickCount64 >= actionAt) { actionAt = Environment.TickCount64 + (scene == "move" || scene == "transition" ? 3000 : 1000); Action(); }
        if (Environment.TickCount64 < pollAt) return; pollAt = Environment.TickCount64 + 200;
        string path = Path.Combine(directory, "command.json"); if (!File.Exists(path)) return;
        string id = "invalid";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path)); File.Delete(path);
            var data = doc.RootElement; id = data.GetProperty("id").GetString(); Guard(); object result;
            switch (data.GetProperty("action").GetString())
            {
                case "shader_check": Guard(); if (measuring) throw new InvalidOperationException("Stop measurement first"); result = ShaderCheck.Run(); break;
                case "tree_band":
                    Guard(); if (measuring) throw new InvalidOperationException("Stop measurement first");
                    int band = data.GetProperty("height").GetInt32();
                    if (band != 8 && band != 16 && band != 32) throw new InvalidOperationException("Unsupported tree band");
                    var trim = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "VerticalWorld").GetType("Rgdsplus.VerticalWorld.TreeSpriteTrim");
                    trim.GetField("StripHeight", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, band);
                    trim.GetMethod("Clear", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                    result = new { height = band }; break;
                case "precompile": result = Precompile(data.TryGetProperty("toolChain", out var toolChain) && toolChain.GetBoolean(), data.TryGetProperty("transitive", out var transitive) && transitive.GetBoolean()); break;
                case "render_detail": installRenderDetail(); result = new { renderDetailInstalled }; break;
                case "object_detail": installObjectDetail(); result = new { objectDetailInstalled }; break;
                case "prepare": Prepare(data.GetProperty("scene").GetString()); result = new { scene, terrain = Game1.getFarm().terrainFeatures.Count(), objects = Game1.getFarm().objects.Count() }; break;
                case "start":
                    label = data.GetProperty("label").GetString(); Array.Clear(elapsed); Array.Clear(allocated); Array.Clear(calls);
                    count = active = actions = maxDebris = maxSprites = 0; lastFrame = releaseAt = 0; drewThisTick = false;
                    pendingWarpTarget = null;
                    actionEvents.Clear();
                    Array.Clear(firstElapsed); Array.Clear(maxElapsed); Array.Clear(firstAllocated);
                    Array.Clear(frameAllocated); Array.Clear(frameGc0); Array.Clear(frameGc1); Array.Clear(frameGc2); Array.Clear(frameMethodTicks);
                    initialObjects = Game1.getFarm().terrainFeatures.Count() + Game1.getFarm().objects.Count();
                    for (int i = 0; i < 3; i++) collections[i] = GC.CollectionCount(i);
                    globalAllocated = GC.GetTotalAllocatedBytes(false); runActions = data.GetProperty("actions").GetBoolean();
                    if (presentationFeedback) rgds_presentation_start();
                    jitTrace?.Start();
                    frameTotalAllocated = globalAllocated;
                    for (int i = 0; i < 3; i++) frameCollections[i] = collections[i];
                    actionAt = Environment.TickCount64 + 1000; start = Stopwatch.GetTimestamp(); measuring = true; result = new { started = label }; break;
                case "stop": result = Stop(); break;
                default: throw new InvalidOperationException("Unknown performance command.");
            }
            Reply(new { id, error = (string)null, result });
        }
        catch (Exception ex) { measuring = false; Reply(new { id, error = ex.ToString() }); }
    }
    private static object Distribution(double[] values)
    {
        if (values.Length == 0) return null; Array.Sort(values);
        return new { n = values.Length, mean = values.Average(), p50 = values[(int)((values.Length - 1) * .5)], p95 = values[(int)((values.Length - 1) * .95)], p99 = values[(int)((values.Length - 1) * .99)], max = values[^1], over33 = values.Count(v => v > 33.333), over50 = values.Count(v => v > 50) };
    }
    private static object Stop()
    {
        jitTrace?.Stop(Path.Combine(directory, label + "-jit.json"));
        measuring = runActions = false; double seconds = Ms(Stopwatch.GetTimestamp() - start) / 1000;
        long bytes = GC.GetTotalAllocatedBytes(false) - globalAllocated;
        int removed = initialObjects - Game1.getFarm().terrainFeatures.Count() - Game1.getFarm().objects.Count();
        var gc = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collections[i]).ToArray();
        var boundaries = Enumerable.Range(0, names.Count).Select(i => new { name = names[i], calls = calls[i], totalMs = Ms(elapsed[i]), msPerFrame = Ms(elapsed[i]) / Math.Max(1, count), firstMs = Ms(firstElapsed[i]), maxMs = Ms(maxElapsed[i]), allocatedBytes = allocated[i], firstAllocatedBytes = firstAllocated[i] }).ToArray();
        int presented = presentationFeedback ? rgds_presentation_stop(Path.Combine(directory, label + "-presentation.csv")) : 0;
        var result = new { label, scene, tickOnly, presentationFeedback, presented, seconds, frames = count, activeFrames = active, fps = count / seconds, frameMs = Distribution(frames.Take(count).ToArray()), actionFrameMs = Distribution(Enumerable.Range(0, count).Where(i => actionFrames[i]).Select(i => frames[i]).ToArray()), gc, allocatedBytes = bytes, allocatedMiBPerSecond = bytes / 1048576.0 / seconds, managedMiB = GC.GetTotalMemory(false) / 1048576.0, actions, removed, maxDebris, maxSprites, boundaries, actionEvents, zoom = Game1.options.zoomLevel, uiScale = Game1.options.uiScale, menu = Game1.activeClickableMenu?.GetType().Name };
        File.WriteAllText(Path.Combine(directory, label + ".json"), JsonSerializer.Serialize(result));
        using var writer = new StreamWriter(Path.Combine(directory, label + "-frames.csv"));
        writer.WriteLine("index,frame_ms,action,allocated_bytes,gc0,gc1,gc2,game_update_ms,game_draw_ms,world_ms,melee_ms,grass_ms,axe_ms,pickaxe_ms,elapsed_ms,action_number,jit_ms,jit_methods");
        int MethodIndex(string name) => names.IndexOf(name);
        int gameUpdate = MethodIndex("Game1.Update"), gameDraw = MethodIndex("Game1.Draw"), world = MethodIndex("Game1.DrawWorld");
        int melee = MethodIndex("MeleeWeapon.DoDamage"), grass = MethodIndex("Grass.performToolAction"), axe = MethodIndex("Axe.DoFunction"), pickaxe = MethodIndex("Pickaxe.DoFunction");
        double T(int frame, int method) => method < 0 ? 0 : Ms(frameMethodTicks[frame, method]);
        for (int i = 0; i < count; i++) writer.WriteLine(i + "," + frames[i].ToString("F4", CultureInfo.InvariantCulture) + "," + (actionFrames[i] ? 1 : 0) + "," + frameAllocated[i] + "," + frameGc0[i] + "," + frameGc1[i] + "," + frameGc2[i] + "," + T(i, gameUpdate).ToString("F4", CultureInfo.InvariantCulture) + "," + T(i, gameDraw).ToString("F4", CultureInfo.InvariantCulture) + "," + T(i, world).ToString("F4", CultureInfo.InvariantCulture) + "," + T(i, melee).ToString("F4", CultureInfo.InvariantCulture) + "," + T(i, grass).ToString("F4", CultureInfo.InvariantCulture) + "," + T(i, axe).ToString("F4", CultureInfo.InvariantCulture) + "," + T(i, pickaxe).ToString("F4", CultureInfo.InvariantCulture) + "," + sampleTimes[i].ToString("F4", CultureInfo.InvariantCulture) + "," + actionNumbers[i] + "," + frameJitMs[i].ToString("F4", CultureInfo.InvariantCulture) + "," + frameJitMethods[i]);
        using var methodsWriter = new StreamWriter(Path.Combine(directory, label + "-methods.csv"));
        methodsWriter.WriteLine("index," + string.Join(",", names.Select((name, i) => i + ":" + name)));
        for (int i = 0; i < count; i++) methodsWriter.WriteLine(i + "," + string.Join(",", Enumerable.Range(0, names.Count).Select(m => T(i, m).ToString("F4", CultureInfo.InvariantCulture))));
        return result;
    }
}
