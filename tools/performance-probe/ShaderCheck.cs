using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace Rgdsplus.PerformanceProbe;

internal static class ShaderCheck
{
    private static object CheckTreeStrips(GraphicsDevice device)
    {
        var mod = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "VerticalWorld");
        var find = mod.GetType("Rgdsplus.VerticalWorld.TreeSpriteTrim").GetMethod("FindStrips", BindingFlags.NonPublic | BindingFlags.Static);
        using var synthetic = new Texture2D(device, 48, 96);
        var input = new uint[48*96];
        for (int y=3;y<87;y++) for (int x=0;x<48;x++)
            if (y<56 && x>3 && x<44 || y>=56 && x>19 && x<29)
                input[y*48+x]=new Color(64+x*3,40+y*2,80,255).PackedValue;
        synthetic.SetData(input);
        var actual = Game1.content.Load<Texture2D>("TerrainFeatures/tree1_spring");
        using var target = new RenderTarget2D(device, 512, 512, false, SurfaceFormat.Color, DepthFormat.None);
        using var batch = new SpriteBatch(device);
        var baseline = new uint[512*512]; var candidate = new uint[baseline.Length];
        int cases=0, differentPixels=0, supportedCases=0, supportedDifferences=0;
        var details = new List<object>();
        foreach (var texture in new[] { synthetic, actual })
        {
            var pixels = new uint[texture.Width*texture.Height]; texture.GetData(pixels);
            var source = new Rectangle(0,0,48,96);
            var strips = (Rectangle[])find.Invoke(null,new object[] {pixels,texture.Width,source});
            foreach(float scale in new[] {1f, 1.5f, 2f, 4f})
            foreach(var flip in new[] {SpriteEffects.None,SpriteEffects.FlipHorizontally})
            foreach(float opacity in new[] {1f,.4f})
            {
                for(int pass=0;pass<2;pass++)
                {
                    device.SetRenderTarget(target); device.Clear(new Color(41,63,127));
                    batch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp);
                    var position=new Vector2(255,447); var origin=new Vector2(24,96);
                    foreach(var rect in pass==0 ? new[] {source} : strips)
                    {
                        int x=rect.X-source.X,y=rect.Y-source.Y;
                        if(flip==SpriteEffects.FlipHorizontally) x=source.Width-x-rect.Width;
                        batch.Draw(texture,position,rect,Color.White*opacity,0,origin-new Vector2(x,y),scale,flip,.5f);
                    }
                    batch.End(); device.SetRenderTarget(null); target.GetData(pass==0 ? baseline : candidate);
                }
                cases++; int difference=0;
                for(int i=0;i<baseline.Length;i++) if(baseline[i]!=candidate[i]) difference++;
                differentPixels += difference;
                if(scale==4f) { supportedCases++; supportedDifferences+=difference; }
                details.Add(new { texture=texture.Name ?? "synthetic", scale, flip=flip.ToString(), opacity, difference });
            }
        }
        return new { cases, differentPixels, supportedCases, supportedDifferences, passed=supportedDifferences==0, details };
    }

    internal static object Run()
    {
        var device = Game1.graphics.GraphicsDevice;
        var bindings = device.GetRenderTargets();
        var viewport = device.Viewport;
        using var texture = new Texture2D(device, 8, 8);
        var pixels = new Color[64];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.FromNonPremultiplied(i * 31 % 256, i * 59 % 256, i * 83 % 256, i % 8 == 0 ? 0 : i * 17 % 256);
        texture.SetData(pixels);
        using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Color, DepthFormat.None);
        using var batch = new SpriteBatch(device);
        using var effect = new AlphaTestEffect(device) { VertexColorEnabled = true, AlphaFunction = CompareFunction.Greater, ReferenceAlpha = 0 };
        var projection = Matrix.CreateOrthographicOffCenter(0, 128, 128, 0, 0, -1);
        if (device.UseHalfPixelOffset) { projection.M41 += -.5f * projection.M11; projection.M42 += -.5f * projection.M22; }
        effect.Projection = projection;
        var baseline = new Color[128 * 128];
        var candidate = new Color[baseline.Length];
        int maxDifference = 0, differentChannels = 0;
        object treeStrips = null;
        try
        {
            foreach (float opacity in new[] { 1f, .5f, .02f })
            foreach (var transform in new[] { Matrix.Identity, Matrix.CreateScale(.7f) * Matrix.CreateTranslation(12f, 9f, 0) })
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    device.SetRenderTarget(target);
                    device.Clear(new Color(41, 63, 127, 255));
                    effect.World = transform;
                    batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                        null, null, pass == 0 ? null : effect, transform);
                    for (int i = 0; i < 6; i++)
                        batch.Draw(texture, new Vector2(25 + i * 10, 24 + i * 12), null,
                            new Color(220, 180, 100) * opacity, i * .12f, new Vector2(4), 5f, (SpriteEffects)(i % 4), .5f);
                    batch.End();
                    device.SetRenderTarget(null);
                    target.GetData(pass == 0 ? baseline : candidate);
                }
                for (int i = 0; i < baseline.Length; i++)
                {
                    uint a = baseline[i].PackedValue, b = candidate[i].PackedValue;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int delta = Math.Abs((int)((a >> (channel*8)) & 255) - (int)((b >> (channel*8)) & 255));
                        maxDifference = Math.Max(maxDifference, delta);
                        if (delta != 0) differentChannels++;
                    }
                }
            }
            treeStrips = CheckTreeStrips(device);
        }
        finally { device.SetRenderTargets(bindings); device.Viewport = viewport; }
        return new { cases = 6, comparedPixels = baseline.Length * 6, maxDifference, differentChannels, passed = maxDifference <= 1, treeStrips };
    }
}
