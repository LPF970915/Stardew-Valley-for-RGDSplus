using Mono.Cecil;
using Mono.Cecil.Cil;
using Rgdsplus.VerticalWorld;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
}
foreach (float scale in new[] { .5f, .75f, 1f, 1.25f, 1.5f, 2f })
{
    int w = ScreenGeometry.Width(scale), h = ScreenGeometry.Height(scale);
    Check(Math.Abs(w * scale - 1024) <= scale && Math.Abs(h * scale - 768) <= scale, "Scaled panel coverage");
    foreach (var size in new[] { (640, 512), (4160, 1920), (5120, 4096) })
    foreach (var player in new[] { (0f, 0f), (4096f, 976f), (804.32f, 1488f), ((float)size.Item1 - 64, (float)size.Item2 - 64) })
    {
        int x = ScreenGeometry.CameraX(player.Item1, size.Item1, w);
        int y = ScreenGeometry.CameraY(player.Item2, size.Item2, h);
        int upperOnlyY = ScreenGeometry.CameraY(player.Item2, size.Item2, h, false);
        Check(upperOnlyY >= 0 && upperOnlyY <= Math.Max(0, size.Item2 - h), "Expanded mode uses a single-panel map clamp");
        if (player.Item2 <= size.Item2 - 64)
            Check(player.Item2 + 32 >= upperOnlyY && player.Item2 + 32 <= upperOnlyY + h, "Expanded mode never leaves the player on the hidden lower world");
        Check(x >= 0 && x <= Math.Max(0, size.Item1 - w), "Horizontal map clamp");
        Check(y >= 0 && y <= Math.Max(0, size.Item2 - 2 * h), "Vertical map clamp");
        if (player.Item1 <= size.Item1 - 64 && player.Item2 <= size.Item2 - 64)
            Check(player.Item1 + 32 >= x && player.Item1 + 32 <= x + w && player.Item2 + 32 >= y && player.Item2 + 32 <= y + h * 2, "Player remains in combined visible world");
    }
    Check(ScreenGeometry.CameraX(2500, 8000, w) + w / 2 == 2532, "Zoom stays centered horizontally");
    Check(ScreenGeometry.CameraY(2000, 8000, h) + h / 2 == 2032, "Zoom stays centered on upper panel vertically");
}
Check(ScreenGeometry.CameraX(4096, 5120, 1024) == 3616, "Known-good farm exit camera regression");
Check(ScreenGeometry.CameraY(1488, 1920, 768) == 384, "Known-good BusStop lower-panel regression");
Check(ScreenGeometry.CameraY(1488, 1920, 768, false) == 1136, "Select moves the BusStop player back into the upper panel");
var state = new DisplayModeState();
Check(state.Mode == LowerScreenMode.SceneHud, "Fresh launch preserves accepted scene mode");
for (int i = 0; i < 100; i++)
{
    var before = state.Mode;
    int generation = state.Generation;
    Check(!state.TryToggle(false) && state.Mode == before && state.Generation == generation, "Rejected transitions preserve mode and generation");
    Check(state.TryToggle(true) && state.Mode != before && state.Generation == generation + 1, "Accepted transition changes owner once");
}
state.TryToggle(true);
state.Reset();
Check(state.Mode == LowerScreenMode.SceneHud, "Return to title resets the layout");
Check(ScreenGeometry.Width(float.NaN) == 1024 && ScreenGeometry.Height(0) == 768, "Invalid scale fallback");
foreach (float ui in new[] { .75f, 1f, 1.25f, 1.5f })
{
    float scale = ui * 1024f / 1366f;
    int width = ScreenGeometry.Width(scale), height = ScreenGeometry.Height(scale);
    Check(width >= 896 && height >= 680, "Native inventory minimum fits supported UI scale");
    Check(Math.Abs((height - 88) * scale - (768 - 88 * scale)) < scale, "Toolbar anchored to physical bottom after scaling");
}
if (args.Length == 0) throw new Exception("Pass actual device game assembly path");
using var game = AssemblyDefinition.ReadAssembly(args[0]);
var game1 = game.MainModule.GetType("StardewValley.Game1");
foreach (var name in new[] { "Draw", "Update", "DrawWorld", "ShouldDrawOnBuffer", "renderScreenBuffer", "SetWindowSize", "PushUIMode", "updateViewportForScreenSizeChange", "allocateLightmap", "DrawMenu", "SetRenderTarget", "get_IsHudDrawn", "drawMouseCursor" })
    Check(game1.Methods.Any(m => m.Name == name), "Device patch target " + name);
var resize = game1.Methods.Single(m => m.Name == "SetWindowSize");
Check(resize.Body.Instructions.Count(i => i.Operand is MethodReference m && m.Name == "ApplyChanges" && m.DeclaringType.Name == "GraphicsDeviceManager") == 1, "Unique resize boundary on actual device IL");
int applyIndex = resize.Body.Instructions.ToList().FindIndex(i => i.Operand is MethodReference m && m.Name == "ApplyChanges");
Check(resize.Body.Instructions.Take(applyIndex).Any(i => i.Operand is MethodReference m && m.Name == "PortMasterGetDisplayWidth"), "Physical display override stays before logical boundary");
Check(resize.Body.Instructions.Skip(applyIndex).Any(i => i.Operand is MethodReference m && m.Name == "gameWindowSizeChanged"), "Logical boundary precedes menu resize notifications");
var title = game.MainModule.GetType("StardewValley.Menus.TitleMenu").Methods.Single(m => m.Name == "draw");
var drawCalls = title.Body.Instructions.Where(i => i.Operand is MethodReference m && m.DeclaringType.Name == "SpriteBatch" && m.Name == "Draw").Select(i => (MethodReference)i.Operand).ToList();
Check(drawCalls.Any(m => m.Parameters.Count == 4 && m.Parameters[1].ParameterType.Name == "Rectangle"), "Title background wrapper signature");
Check(drawCalls.Any(m => m.Parameters.Count == 9 && m.Parameters[6].ParameterType.Name == "Single"), "Title logo wrapper signature");
Check(game1.Methods.Count(m => m.Name == "drawDialogueBox" && m.Parameters.Count == 0) == 1, "Legacy dialogue route matches exactly one actual game overload");
Console.WriteLine($"PASS: {checks} geometry, scaling, camera regression and actual-device IL checks.");
