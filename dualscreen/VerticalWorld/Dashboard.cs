using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace Rgdsplus.VerticalWorld
{
    internal static class Dashboard
    {
        // Reference v2 palette; textures below are read from the user's PC game atlas.
        internal static readonly Color Ink = new Color(50,27,15), Muted = new Color(112,69,31), Card = new Color(255,217,142), Selected = new Color(255,180,65);
        private static readonly int[] PageIconTiles = { 0,4,1,3,5,2,2,3,4,5,2,6 };
        private static readonly string[] ChinesePages = { "背包", "制作", "进度", "地图", "任务", "关系", "送礼指南", "田地", "机器", "社区中心", "日历", "设置" };
        private static readonly string[] EnglishPages = { "Backpack", "Crafting", "Progress", "Map", "Journal", "Relationships", "Gift Guide", "Fields", "Machines", "Bundles", "Calendar", "Settings" };
        internal const int PageCount = 12;
        internal static string Notice = "";
        internal static int ScrollRow;
        private static int railStart;
        internal static string Text(string zh, string en) => LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.zh ? zh : en;
        internal static string PageName(int i) => Text(ChinesePages[i], EnglishPages[i]);
        internal static float Scale => Math.Min(Game1.uiViewport.Width / 1024f, Game1.uiViewport.Height / 768f);
        internal static Rectangle Rect(int x,int y,int w,int h) => Bounds(new Rectangle(x,y,w,h));
        internal static Rectangle Bounds(Rectangle box) => CompanionLayout.Scale(box,Game1.uiViewport.Width,Game1.uiViewport.Height);
        internal static Point Physical(int x,int y) => new Point((int)Math.Round(x*1024f/Game1.uiViewport.Width),(int)Math.Round(y*768f/Game1.uiViewport.Height));
        private static List<int> VisiblePages => Enumerable.Range(0,PageCount).Where(CompanionPreferences.Visible).ToList();
        internal static void Reveal(int page)
        {
            var pages = VisiblePages;
            int pos = pages.IndexOf(page);
            railStart = Math.Clamp(railStart,0,Math.Max(0,pages.Count-3));
            if(pos<railStart) railStart=Math.Max(0,pos);
            if(pos>=railStart+3) railStart=pos-2;
        }
        internal static int Adjacent(int page,int direction)
        {
            var pages=VisiblePages;
            return pages[(Math.Max(0,pages.IndexOf(page))+direction+pages.Count)%pages.Count];
        }
        internal static Rectangle NavBounds(int index)
        {
            int slot=VisiblePages.IndexOf(index)-railStart;
            return slot>=0 && slot<3 ? Bounds(CompanionLayout.Tab(slot)) : Rectangle.Empty;
        }
        internal static Rectangle InventoryBounds(int index) => Bounds(CompanionLayout.Inventory(index));
        internal static Rectangle ToolbarBounds(int index) => InventoryBounds(index);
        internal static Rectangle HeaderMeterLabel(int row) => Rect(30,482+row*62,140,28);
        internal static Rectangle HeaderMeterBar(int row) => Rect(30,514+row*62,140,16);
        internal static Vector2 ItemIconOrigin(Rectangle box) => new Vector2(box.X+box.Width/2f-32,box.Y+box.Height/2f-32);
        internal static float ItemIconScale(Rectangle box,float fill) => Math.Min(box.Width,box.Height)*fill/64f;
        internal static int HitItem(int x,int y,bool backpack)
        {
            if(backpack) for(int i=0;i<Math.Min(36,Game1.player.MaxItems);i++) if(InventoryBounds(i).Contains(x,y)) return i;
            return -1;
        }
        internal static void Reset() { Notice=""; ScrollRow=railStart=0; DashboardPages.Reset(); }
        internal static void RefreshSummary() { }
        internal static void DrawBackdrop(SpriteBatch b)
        {
            b.Draw(Game1.staminaRect,new Rectangle(0,0,Game1.uiViewport.Width,Game1.uiViewport.Height),new Color(69,35,15));
            NineSlice(b,Game1.menuTexture,new Rectangle(0,256,60,60),20,Rect(4,4,1016,760),12);
        }
        internal static void Draw(SpriteBatch b,int page,bool focused)
        {
            DrawBackdrop(b); Reveal(page);
            Button(b,"<",Bounds(CompanionLayout.PreviousPage)); Button(b,">",Bounds(CompanionLayout.NextPage));
            for(int i=0;i<PageCount;i++)
            {
                var box=NavBounds(i); if(box.IsEmpty)continue;
                ButtonSurface(b,box,page==i);
                b.Draw(Game1.mouseCursors,new Rectangle(box.X+(int)(14*Scale),box.Y+(int)(14*Scale),(int)(32*Scale),(int)(32*Scale)),new Rectangle(PageIconTiles[i]*16,368,16,16),Color.White);
                Write(b,PageName(i),new Rectangle(box.X+(int)(58*Scale),box.Y,box.Width-(int)(70*Scale),box.Height),Ink,.96f);
            }
            Panel(b,Bounds(CompanionLayout.Content),Card,Muted);
            if(page==0) DrawBackpack(b); else DashboardPages.Draw(b,page);
            if(!string.IsNullOrEmpty(Notice))Write(b,Notice,Rect(24,726,976,30),Muted,.86f);
            else
            {
                int season=Game1.currentSeason switch { "spring"=>0,"summer"=>1,"fall"=>2,_=>3 };
                b.Draw(Game1.mouseCursors,Rect(26,733,24,16),new Rectangle(406,441+season*8,12,8),Color.White);
                Write(b,Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)+" · "+Game1.dayOfMonth+" · "+Text("第 "+Game1.year+" 年","Year "+Game1.year),Rect(62,726,660,30),Muted,.84f);
                Write(b,Game1.getTimeOfDayString(Game1.timeOfDay),Rect(804,726,196,30),Muted,.84f);
            }
        }
        private static void DrawBackpack(SpriteBatch b)
        {
            var p=Game1.player;
            Panel(b,Rect(24,98,160,610),new Color(255,215,135),Muted);
            Write(b,p.farmName.Value,Rect(32,106,144,34),Ink,.86f);
            bool previous=FarmerRenderer.isDrawingForUI;
            try
            {
                FarmerRenderer.isDrawingForUI=true;
                // Use an independent animation frame: never freeze the live farmer's sprite.
                p.FarmerRenderer.draw(b,new FarmerSprite.AnimationFrame(0,32000),0,new Rectangle(0,0,16,32),
                    new Vector2(64,278)*Scale,Vector2.Zero,.97f,2,Color.White,0,1.05f*Scale,p);
            }
            finally { FarmerRenderer.isDrawingForUI=previous; }
            string season=Game1.currentSeason switch { "spring"=>Text("春","Spring"),"summer"=>Text("夏","Summer"),"fall"=>Text("秋","Fall"),_=>Text("冬","Winter") };
            int seasonIndex=Game1.currentSeason switch { "spring"=>0,"summer"=>1,"fall"=>2,_=>3 };
            b.Draw(Game1.mouseCursors,Rect(32,153,24,16),new Rectangle(406,441+seasonIndex*8,12,8),Color.White);
            Write(b,season+" "+Game1.dayOfMonth+Text(" 日",""),Rect(62,145,114,30),Ink,.84f);
            b.Draw(Game1.mouseCursors,Rect(32,194,24,16),new Rectangle(317+12*(Game1.weatherIcon==999?0:Game1.weatherIcon),421,12,8),Color.White);
            Write(b,Game1.getTimeOfDayString(Game1.timeOfDay),Rect(62,186,114,30),Ink,.84f);
            Write(b,p.Money.ToString("N0")+" G",Rect(32,223,144,40),Ink,.84f);
            Write(b,p.Name,Rect(32,437,144,32),Ink,.88f);
            Meter(b,0,p.Stamina,p.MaxStamina,new Color(106,162,70),Text("体力","EP"));
            Meter(b,1,p.health,p.maxHealth,new Color(199,82,67),Text("生命","HP"));
            Write(b,Text("每日运气","Daily luck"),Rect(32,615,144,30),Muted,.86f);
            Write(b,p.team.sharedDailyLuck.Value.ToString("+0.000;-0.000;0.000"),Rect(32,652,144,30),Ink);
            b.Draw(Game1.mouseCursors,Rect(200,102,32,32),new Rectangle(0,368,16,16),Color.White);
            Write(b,Text("背包","Backpack")+"  "+p.Items.Take(p.MaxItems).Count(item=>item!=null)+" / "+p.MaxItems,Rect(242,102,570,38),Ink,1.05f);
            Button(b,Text("整理","Sort"),Bounds(CompanionLayout.Sort));
            for(int i=0;i<Math.Min(36,p.MaxItems);i++)
            {
                var box=InventoryBounds(i);
                b.Draw(Game1.menuTexture,box,Game1.getSourceRectForStandardTileSheet(Game1.menuTexture,i==p.CurrentToolIndex?56:10),Color.White);
                if(i<p.Items.Count) { DrawItem(b,p.Items[i],box,.86f); DrawItemStatus(b,p.Items[i],box); }
            }
            for(int row=0;row<Math.Min(3,(p.MaxItems+11)/12);row++)
                Write(b,Text("第 "+(row+1)+" 行","Row "+(row+1))+(p.CurrentToolIndex/12==row?Text(" · 快捷栏"," · Hotbar"):""),Rect(200,144+row*96,800,27),Muted,.84f);
            for(int row=(p.MaxItems+11)/12;row<3;row++)
            {
                ButtonSurface(b,Rect(196,174+row*96,812,64),false);
                Write(b,Text("升级背包以解锁第 "+(row+1)+" 行","Upgrade backpack to unlock row "+(row+1)),Rect(212,188+row*96,780,36),Ink,.9f);
            }
            Button(b,"<",Bounds(CompanionLayout.PreviousRow)); Button(b,">",Bounds(CompanionLayout.NextRow));
            Write(b,Text("快捷栏","Hotbar")+" "+(p.CurrentToolIndex/12+1)+" / "+Math.Max(1,p.MaxItems/12),Rect(272,448,648,36),Ink,.9f);
            Panel(b,Rect(196,500,812,76),new Color(255,207,126),Muted);
            string[] names={Text("耕种","Farming"),Text("采矿","Mining"),Text("觅食","Foraging"),Text("钓鱼","Fishing"),Text("战斗","Combat")};
            int[] levels={p.FarmingLevel,p.MiningLevel,p.ForagingLevel,p.FishingLevel,p.CombatLevel};
            for(int i=0;i<5;i++)
            {
                int x=212+i*158;
                Write(b,names[i],Rect(x,503,145,32),Ink,.84f);
                Write(b,levels[i].ToString(),Rect(x,538,38,28),Ink,.86f);
                b.Draw(Game1.staminaRect,Rect(x+45,548,88,10),Muted);
                b.Draw(Game1.staminaRect,Rect(x+45,548,(int)(88*Math.Clamp(levels[i]/10f,0,1)),10),new Color(108,151,73));
            }
            Panel(b,Rect(196,584,812,124),new Color(255,207,126),Muted);
            var item=p.CurrentItem;
            DrawItem(b,item,Rect(208,609,64,64));
            Write(b,item?.DisplayName??Text("选择一个物品","Select an item"),Rect(288,590,410,34),Ink);
            Write(b,item==null?"":item.getCategoryName()+" · "+item.sellToStorePrice()+" G",Rect(706,591,286,30),Muted,.86f);
            Paragraph(b,item?.getDescription()??Text("点选切换物品；拖动换位；长按食物食用。","Tap to select, drag to move, hold food to eat."),Rect(288,635,704,64),Ink,DashboardPages.BackpackTextOffset);
        }
        internal static bool ClickBackpack(int x,int y)
        {
            if(Game1.player.UsingTool||!Game1.player.CanMove) return false;
            if(Bounds(CompanionLayout.Sort).Contains(x,y))
            {
                Item current=Game1.player.CurrentItem;
                Item[] sorted=Game1.player.Items.Where(i=>i!=null).OrderBy(i=>i.Category).ThenBy(i=>i.DisplayName).ThenBy(i=>i.Quality).ToArray();
                for(int i=0;i<Game1.player.Items.Count;i++) Game1.player.Items[i]=i<sorted.Length?sorted[i]:null;
                Game1.player.CurrentToolIndex=Math.Max(0,Array.IndexOf(sorted,current)); Game1.playSound("Ship"); return true;
            }
            int direction=Bounds(CompanionLayout.PreviousRow).Contains(x,y)?-1:Bounds(CompanionLayout.NextRow).Contains(x,y)?1:0;
            if(direction==0) return false;
            int rows=Math.Max(1,Game1.player.MaxItems/12);
            Game1.player.CurrentToolIndex=((Game1.player.CurrentToolIndex/12+direction+rows)%rows)*12+Game1.player.CurrentToolIndex%12; return true;
        }
        internal static void DrawItem(SpriteBatch b,Item item,Rectangle box,float fill=.9f)
        {
            if(item==null) return;
            item.drawInMenu(b,ItemIconOrigin(box),ItemIconScale(box,fill),1f,1f,StackDrawType.Hide,Color.White,false);
            float ds=Math.Min(box.Width,box.Height)/64f*2.5f; int pad=Math.Max(1,(int)(2*Scale));
            if(item.Stack>1&&item.maximumStackSize()>1&&!item.IsRecipe)
                Utility.drawTinyDigits(item.Stack,b,new Vector2(box.Right-pad-Utility.getWidthOfTinyDigitString(item.Stack,ds),box.Bottom-pad-7*ds),ds,1f,Color.White);
            if(item.Quality>0)
            {
                var src=item.Quality<4?new Rectangle(338+(item.Quality-1)*8,400,8,8):new Rectangle(346,392,8,8);
                int size=Math.Max(1,(int)(Math.Min(box.Width,box.Height)*.24f));
                b.Draw(Game1.mouseCursors,new Rectangle(box.Left+pad,box.Bottom-pad-size,size,size),src,Color.White);
            }
        }
        private static void DrawItemStatus(SpriteBatch b,Item item,Rectangle box)
        {
            float value; Color color=Color.CornflowerBlue;
            if(item is WateringCan can)value=can.IsBottomless?1:Math.Clamp(can.WaterLeft/(float)Math.Max(1,can.waterCanMax),0,1);
            else if(item is MeleeWeapon weapon)
            {
                int cooldown=weapon.type.Value switch { MeleeWeapon.dagger=>MeleeWeapon.daggerCooldown,MeleeWeapon.club=>MeleeWeapon.clubCooldown,MeleeWeapon.stabbingSword=>MeleeWeapon.attackSwordCooldown,_=>MeleeWeapon.defenseCooldown };
                if(cooldown<=0)return;
                value=Math.Clamp(cooldown/(float)(weapon.type.Value==MeleeWeapon.club?MeleeWeapon.clubCooldownTime:weapon.type.Value==MeleeWeapon.dagger?MeleeWeapon.daggerCooldownTime:MeleeWeapon.defenseCooldownTime),0,1);
                color=new Color(194,93,75);
            }
            else return;
            var bar=new Rectangle(box.X+5,box.Bottom-7,Math.Max(1,box.Width-10),4);
            b.Draw(Game1.staminaRect,bar,Muted);
            bar.Width=(int)(bar.Width*value);
            if(bar.Width>0)b.Draw(Game1.staminaRect,bar,color);
        }
        internal static void Panel(SpriteBatch b,Rectangle box,Color fill,Color border)
        {
            // Reference pages use thin dividers and warm washes, not repeated window chrome.
            b.Draw(Game1.staminaRect,box,new Color(190,129,49));
            int line=Math.Max(1,(int)Scale); box.Inflate(-line,-line);
            if(box.Width>0&&box.Height>0)b.Draw(Game1.staminaRect,box,fill);
        }
        internal static void NineSlice(SpriteBatch b,Texture2D texture,Rectangle source,int inset,Rectangle box,int physicalBorder)
        {
            int edge=Math.Max(1,Math.Min((int)(physicalBorder*Scale),Math.Min(box.Width,box.Height)/2));
            int[] sx={source.X,source.X+inset,source.Right-inset}, sy={source.Y,source.Y+inset,source.Bottom-inset};
            int[] sw={inset,source.Width-inset*2,inset}, sh={inset,source.Height-inset*2,inset};
            int[] dx={box.X,box.X+edge,box.Right-edge},dy={box.Y,box.Y+edge,box.Bottom-edge};
            int[] dw={edge,box.Width-edge*2,edge},dh={edge,box.Height-edge*2,edge};
            for(int y=0;y<3;y++)for(int x=0;x<3;x++)
                if(dw[x]>0&&dh[y]>0)b.Draw(texture,new Rectangle(dx[x],dy[y],dw[x],dh[y]),new Rectangle(sx[x],sy[y],sw[x],sh[y]),Color.White);
        }
        internal static void ButtonSurface(SpriteBatch b,Rectangle box,bool selected)
        {
            if(CompanionPreferences.Current.NativeFrames)
                NineSlice(b,Game1.mouseCursors,new Rectangle(432,439,9,9),3,box,6);
            else Panel(b,box,new Color(255,169,31),Muted);
            if(selected)
            {
                var highlight=box; highlight.Inflate(-(int)(5*Scale),-(int)(5*Scale));
                b.Draw(Game1.staminaRect,highlight,new Color(255,227,148)*.28f);
                b.Draw(Game1.staminaRect,new Rectangle(box.X+(int)(8*Scale),box.Bottom-(int)(7*Scale),box.Width-(int)(16*Scale),Math.Max(2,(int)(3*Scale))),Ink);
            }
        }
        internal static void ScrollTitle(SpriteBatch b,string value,Rectangle box)
        {
            // ui:scroll has 12px horizontal insets and no vertical inset.
            const int inset=12; int edge=Math.Min((int)(20*Scale),box.Width/2);
            b.Draw(Game1.mouseCursors,new Rectangle(box.X,box.Y,edge,box.Height),new Rectangle(325,318,inset,18),Color.White);
            b.Draw(Game1.mouseCursors,new Rectangle(box.X+edge,box.Y,box.Width-edge*2,box.Height),new Rectangle(337,318,1,18),Color.White);
            b.Draw(Game1.mouseCursors,new Rectangle(box.Right-edge,box.Y,edge,box.Height),new Rectangle(338,318,inset,18),Color.White);
            box.Inflate(-edge,-(int)(7*Scale)); Write(b,value,box,Ink);
        }
        // Never shrink long strings: preserve readable type and truncate only single-line labels.
        internal static void Write(SpriteBatch b,string value,Rectangle box,Color color,float scale=1f)
        {
            if(string.IsNullOrEmpty(value)||box.Width<=0||box.Height<=0) return;
            scale=Math.Max(.84f,scale)*1.2f*Scale;
            value=value.Replace("\r","").Replace("\n"," ");
            if(Game1.smallFont.MeasureString(value).X*scale>box.Width)
            {
                while(value.Length>0&&Game1.smallFont.MeasureString(value+"…").X*scale>box.Width) value=value.Substring(0,value.Length-1);
                value+="…";
            }
            var size=Game1.smallFont.MeasureString(value)*scale;
            b.DrawString(Game1.smallFont,value,new Vector2(box.X,box.Y+Math.Max(0,(box.Height-size.Y)/2)),color,0,Vector2.Zero,scale,SpriteEffects.None,1f);
        }
        internal static int Paragraph(SpriteBatch b,string value,Rectangle box,Color color,int offset=0)
        {
            float scale=1.12f*Scale;
            string[] lines=Game1.parseText(value??"",Game1.smallFont,(int)(box.Width/scale)).Replace("\r","").Split('\n');
            int height=Math.Max(1,(int)Math.Ceiling(Game1.smallFont.LineSpacing*scale)), visible=Math.Max(1,box.Height/height);
            offset=Math.Clamp(offset,0,Math.Max(0,lines.Length-visible));
            for(int i=0;i<visible&&offset+i<lines.Length;i++)
                b.DrawString(Game1.smallFont,lines[offset+i],new Vector2(box.X,box.Y+i*height),color,0,Vector2.Zero,scale,SpriteEffects.None,1f);
            return Math.Max(0,lines.Length-visible);
        }
        internal static void Button(SpriteBatch b,string text,Rectangle box,bool selected=false)
        {
            ButtonSurface(b,box,selected);
            if(text=="<"||text==">")
            {
                int w=Math.Min((int)(28*Scale),box.Width-(int)(12*Scale)),h=Math.Min((int)(26*Scale),box.Height-(int)(8*Scale));
                b.Draw(Game1.mouseCursors,new Rectangle(box.Center.X-w/2,box.Center.Y-h/2,w,h),new Rectangle(text=="<"?352:365,495,12,11),Color.White);
                return;
            }
            if(text==Text("整理","Sort"))
            {
                b.Draw(Game1.mouseCursors,new Rectangle(box.X+(int)(12*Scale),box.Center.Y-(int)(16*Scale),(int)(32*Scale),(int)(32*Scale)),new Rectangle(162,440,16,16),Color.White);
                box.X+=(int)(44*Scale);box.Width-=(int)(44*Scale);
            }
            box.Inflate(-(int)(12*Scale),-(int)(4*Scale)); Write(b,text,box,Ink,.96f);
        }
        private static void Meter(SpriteBatch b,int row,float current,float maximum,Color color,string label)
        {
            Write(b,label,Rect(30,482+row*62,48,28),Ink,.84f);
            int numberX=LocalizedContentManager.CurrentLanguageCode==LocalizedContentManager.LanguageCode.zh?80:68;
            Write(b,(int)Math.Max(0,current)+"/"+(int)maximum,Rect(numberX,482+row*62,176-numberX,28),Ink,.84f);
            var box=HeaderMeterBar(row); b.Draw(Game1.staminaRect,box,Muted); box.Inflate(-2,-2);
            box.Width=(int)(box.Width*Math.Clamp(current/Math.Max(1,maximum),0,1)); if(box.Width>0)b.Draw(Game1.staminaRect,box,color);
        }
    }
}
