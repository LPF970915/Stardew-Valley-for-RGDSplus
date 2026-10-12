using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Locations;
using StardewValley.WorldMaps;

namespace Rgdsplus.VerticalWorld
{
    // Dedicated reference-page layouts; all coordinates use the 1024x768 canvas.
    internal static partial class DashboardPages
    {
        private static float mapZoom = 1;
        private static Vector2 mapCenter = new Vector2(.5f,.5f);
        private static NPC focusedPerson;
        private static int settingsOffset;
        private static int ingredientOffset;
        private static readonly string[] CategoryZh={"全部","储物","机器","耕种","道路","钓鱼","实用","装饰","其他"};
        private static readonly string[] CategoryEn={"All","Storage","Machines","Farming","Paths","Fishing","Utility","Decor","Other"};
        private static readonly string[] CategoryIcons={"(O)388","(BC)130","(BC)13","(BC)8","(O)322","(O)685","(O)93","(BC)37","(O)390"};
        private static int RecipeGroup(string key)
        {
            string n=key.ToLowerInvariant();
            bool Has(params string[] words)=>words.Any(n.Contains);
            if(Has("chest","storage","dresser"))return 1;
            if(Has("furnace","kiln","tapper","keg","preserves jar","mayonnaise","cheese press","loom","oil maker","seed maker","crystalarium","recycling machine","worm bin","slime egg-press","slime egg press","incubator","lightning rod","bee house","cask","dehydrator","bait maker","fish smoker","geode crusher","bone mill"))return 2;
            if(Has("scarecrow","sprinkler","fertilizer","speed-gro","retaining soil","grass starter","seed","garden pot","deluxe soil"))return 3;
            if(Has("fence","gate","floor","path","stepping stone","walkway"))return 4;
            if(Has("bait","spinner","bobber","tackle","hook","crab pot","fishing"))return 5;
            if(Has("torch","campfire","brazier","bomb","staircase","warp totem","tent kit","farm computer","mini-obelisk","cookout kit","ring"))return 6;
            if(Has("chair","bench","table","sign","statue","flute block","drum block","flower","seasonal plant","jack-o-lantern","stone cairn","tub o' flowers"))return 7;
            return 8;
        }
        private static readonly Dictionary<string,int> requirementOffsets = new Dictionary<string,int>();
        private static void Label(SpriteBatch b,string text,int x,int y,int w,int h=34,float size=1) => Dashboard.Write(b,text,R(x,y,w,h),Ink,size);
        private static void Button(SpriteBatch b,string text,int x,int y,int w,int h=48,bool selected=false) => Dashboard.Button(b,text,R(x,y,w,h),selected);
        private static void Panel(SpriteBatch b,int x,int y,int w,int h,bool selected=false) => Dashboard.Panel(b,R(x,y,w,h),selected?Dashboard.Selected:Fill,Muted);
        private static Row Selected(List<Row> rows,int page)
        {
            if(rows.Count==0) return null;
            selections[page]=Math.Clamp(selections[page],0,rows.Count-1);
            return rows[selections[page]];
        }
        private static void Empty(SpriteBatch b,string text=null) => Label(b,text??T("暂无记录","No entries yet"),44,220,932,60);
        internal static void Draw(SpriteBatch b,int page)
        {
            switch(page)
            {
                case 1: DrawCrafting(b); break;
                case 2: DrawProgress(b); break;
                case 3: DrawMap(b); break;
                case 4: DrawJournal(b); break;
                case 5: DrawRelationships(b); break;
                case 6: DrawGifts(b); break;
                case 7: DrawFarm(b,false); break;
                case 8: DrawFarm(b,true); break;
                case 9: DrawBundles(b); break;
                case 10: DrawCalendar(b); break;
                case 11: DrawSettings(b); break;
                case 12: ModManagement.Draw(b); break;
                case 13: DrawExit(b); break;
            }
        }
        private static void List(SpriteBatch b,List<Row> rows,int page,int width=300)
        {
            int start=offsets[page]=Dashboard.ScrollRow=Math.Clamp(offsets[page],0,Math.Max(0,rows.Count-6));
            for(int i=0;i<6&&start+i<rows.Count;i++)
            {
                var row=rows[start+i]; int y=158+i*78;
                if(page==1||page==4) Dashboard.ButtonSurface(b,R(24,y,width,72),start+i==selections[page]);
                else Panel(b,24,y,width,72,start+i==selections[page]);
                if(row.Icon!=null) Dashboard.DrawItem(b,row.Icon,R(32,y+9,52,52));
                else if(row.Person?.Portrait!=null) b.Draw(row.Person.Portrait,R(32,y+9,52,52),new Rectangle(0,0,64,64),Color.White);
                if(page==4) b.Draw(Game1.mouseCursors,R(36,y+20,28,28),new Rectangle(80,368,16,16),Color.White);
                int left=row.Icon!=null||row.Person!=null?92:page==4?76:36;
                Label(b,row.Title,left,y+8,width-(left-24)-12,32,.92f);
                Dashboard.Write(b,row.Quest!=null?(row.Quest.completed.Value?T("已完成","Completed"):T("进行中","Active")):row.Detail,R(left,y+40,width-(left-24)-12,26),Muted,.84f);
            }
            if(rows.Count==0) Label(b,T("暂无记录","No entries"),36,190,width-24,40);
            Label(b,rows.Count==0?"0 / 0":(start+1)+"–"+Math.Min(rows.Count,start+6)+" / "+rows.Count,36,639,width-24,30,.84f);
            Button(b,"<",24,674,64,34); Button(b,">",width-40,674,64,34);
        }
        private static void DrawCrafting(SpriteBatch b)
        {
            Button(b,"<",24,99,44,46); Button(b,">",748,99,44,46);
            for(int i=0;i<5;i++)
            {
                int category=categoryStart+i,x=76+i*134;
                Dashboard.ButtonSurface(b,R(x,99,128,46),recipeCategory==0&&recipeGroup==category);
                Dashboard.DrawItem(b,Object(CategoryIcons[category]),R(x+4,106,30,30));
                Label(b,T(CategoryZh[category],CategoryEn[category]),x+38,105,86,34,.84f);
            }
            Button(b,recipeCategory==1?T("烹饪 ✓","Cooking"):T("烹饪","Cooking"),804,99,196,46,recipeCategory==1);
            var rows=Rows(1); var row=Selected(rows,1); List(b,rows,1);
            if(row==null) return;
            Panel(b,340,158,660,464);
            Dashboard.DrawItem(b,row.Icon,R(352,170,72,72));
            Label(b,row.Title,438,174,544,38);
            Label(b,row.Recipe.numberProducedPerCraft+" "+T("个 / 次","per craft"),438,213,544,30,.86f);
            var recipe=row.Recipe;
            detailMax=Dashboard.Paragraph(b,recipe.description,R(358,250,624,72),Ink,detailOffset);
            Label(b,T("材料","Ingredients"),358,332,310,34,.9f);
            Label(b,T("持有 / 需求","Carried / required"),712,332,250,34,.84f);
            var ingredients=recipe.recipeList.ToList();
            ingredientOffset=Math.Clamp(ingredientOffset,0,Math.Max(0,ingredients.Count-4));
            for(int i=0;i<4&&ingredientOffset+i<ingredients.Count;i++)
            {
                var pair=ingredients[ingredientOffset+i];int y=374+i*39;
                var icon=Object(recipe.getSpriteIndexFromRawIndex(pair.Key));
                Dashboard.DrawItem(b,icon,R(358,y,32,32));
                Label(b,recipe.getNameFromIndex(pair.Key),400,y,328,32,.85f);
                int have=Game1.player.Items.Where(item=>item!=null&&CraftingRecipe.ItemMatchesForCrafting(item,pair.Key)).Sum(item=>item.Stack);
                Dashboard.Write(b,have+" / "+pair.Value,R(752,y,168,32),have>=pair.Value?new Color(22,122,32):new Color(179,22,30),.86f);
                Check(b,have>=pair.Value,944,y+2);
            }
            bool ready=recipe.doesFarmerHaveIngredientsInInventory()&&(recipeCategory!=1||CanCook());
            Label(b,ready?T("材料齐全","Ready to craft"):recipeCategory==1&&!CanCook()?T("需要厨房或野炊工具","Kitchen or Cookout Kit required"):T("缺少材料","Missing ingredients"),358,539,398,56,.88f);
            Button(b,recipeCategory==1?T("烹饪","Cook"):T("制作","Craft"),782,544,202,58,ready);
            // Result carousel mirrors the selected recipe list and has its own hit targets.
            int first=Math.Clamp(selections[1]-3,0,Math.Max(0,rows.Count-8));
            for(int i=0;i<8&&first+i<rows.Count;i++)
            {
                int x=348+i*81; Panel(b,x,634,74,72,first+i==selections[1]);
                Dashboard.DrawItem(b,rows[first+i].Icon,R(x+5,639,64,62));
            }
        }
        private static void DrawJournal(SpriteBatch b)
        {
            Label(b,T("任务日志","Journal"),32,102,600,40,1.06f);
            Button(b,showCompleted?T("显示已完成","Completed: shown"):T("隐藏已完成","Completed: hidden"),704,99,288,46,showCompleted);
            var rows=Rows(4); var row=Selected(rows,4); List(b,rows,4);
            if(row==null) return;
            Panel(b,340,158,660,550);
            Dashboard.ScrollTitle(b,row.Title,R(392,173,548,58));
            var quest=row.Quest;
            detailMax=Dashboard.Paragraph(b,quest.questDescription+"\n\n"+T("目标","Objective")+"\n"+quest.currentObjective,R(360,245,616,296),Ink,detailOffset);
            Label(b,T("奖励","Reward")+"  "+quest.moneyReward.Value+" G",360,558,600,34);
            Label(b,T("剩余天数","Days left")+"  "+(quest.daysLeft.Value>0?quest.daysLeft.Value.ToString():"—"),360,596,600,32,.9f);
            if(quest.completed.Value&&quest.moneyReward.Value>0) Button(b,T("领取奖励","Claim reward"),748,648,232,48,true);
            else if(!quest.completed.Value&&quest.canBeCancelled.Value) Button(b,ReferenceEquals(cancelPending,quest)?T("确认取消","Confirm cancel"):T("取消任务","Cancel quest"),748,648,232);
            Label(b,Game1.dayOfMonth+" · "+Game1.getTimeOfDayString(Game1.timeOfDay),360,652,360,32,.85f);
        }
        private static void DrawRelationships(SpriteBatch b)
        {
            Label(b,T("村民 / 生日","Villager / birthday"),36,103,280,36);
            Label(b,T("好感度","Friendship"),336,103,400,36);
            Label(b,T("本周赠礼 / 今日交谈","Gifts / talked today"),748,103,236,36,.85f);
            var rows=Rows(5); int start=offsets[5]=Math.Clamp(offsets[5],0,Math.Max(0,rows.Count-5));
            if(rows.Count==0) { Empty(b); return; }
            for(int i=0;i<5&&start+i<rows.Count;i++)
            {
                var row=rows[start+i]; var n=row.Person; var f=Game1.player.friendshipData[n.Name]; int y=158+i*102;
                Panel(b,24,y,976,94);
                b.Draw(n.Portrait,R(36,y+15,64,64),new Rectangle(0,0,64,64),Color.White);
                Label(b,n.displayName,112,y+10,204,34,.92f);
                Label(b,Season(n.Birthday_Season)+" "+n.Birthday_Day,112,y+48,204,30,.85f);
                int max=f.IsMarried()?14:10;
                for(int heart=0;heart<max;heart++)
                    b.Draw(Game1.mouseCursors,R(336+heart%7*50,y+15+heart/7*36,28,24),new Rectangle(heart<f.Points/250?211:218,428,7,6),Color.White);
                Label(b,f.GiftsThisWeek+" / 2",752,y+10,212,32);
                for(int gift=0;gift<3;gift++)
                {
                    int x=752+gift*68;
                    b.Draw(Game1.menuTexture,R(x,y+46,42,38),Game1.getSourceRectForStandardTileSheet(Game1.menuTexture,10),Color.White);
                    Check(b,gift<2?f.GiftsThisWeek>gift:f.TalkedToToday,x+7,y+51);
                }
            }
            Footer(b,5,rows.Count,5);
        }
        private static string Season(string season) => season switch { "spring"=>T("春","Spring"),"summer"=>T("夏","Summer"),"fall"=>T("秋","Fall"),_=>T("冬","Winter") };
        private static void Check(SpriteBatch b,bool value,int x,int y) => b.Draw(Game1.mouseCursors,R(x,y,27,27),new Rectangle(value?236:227,425,9,9),Color.White);
        private static List<Row> People()
        {
            return Game1.player.friendshipData.Keys.Select(name=>Game1.getCharacterFromName(name)).Where(n=>n!=null&&n.IsVillager)
                .OrderBy(n=>n.displayName).Select(n=>new Row { Title=n.displayName,Person=n }).ToList();
        }
        private static List<Row> Gifts(NPC person)
        {
            var result=new List<Row>();
            if(person==null||!Game1.NPCGiftTastes.TryGetValue(person.Name,out string tastes)) return result;
            string[] fields=tastes.Split('/');
            if(fields.Length<2) return result;
            foreach(string id in fields[1].Split(' ',StringSplitOptions.RemoveEmptyEntries).Distinct())
            {
                // Match upstream's explicit loved-item guide; negative category IDs aren't items.
                if(id.StartsWith("-")) continue;
                var item=Object(id);
                if(item==null) continue;
                int carried=Game1.player.Items.Where(i=>i!=null&&i.QualifiedItemId==item.QualifiedItemId).Sum(i=>i.Stack);
                result.Add(new Row { Title=item.DisplayName,Icon=item,Complete=carried,Detail=T("持有","Carried")+" "+carried });
            }
            return result.OrderByDescending(r=>r.Complete>0).ThenBy(r=>r.Title).ToList();
        }
        private static void DrawGifts(SpriteBatch b)
        {
            Label(b,T("村民","Villagers"),36,104,356,36); Label(b,T("最爱礼物 · 背包匹配","Loved gifts · carried items"),438,104,544,36);
            var rows=Rows(6); var row=Selected(rows,6); List(b,rows,6,384);
            Panel(b,420,158,580,550);
            if(row==null) return;
            var gifts=Gifts(row.Person); giftOffset=Math.Clamp(giftOffset,0,Math.Max(0,gifts.Count-6));
            if(gifts.Count==0) Label(b,T("暂无具体物品记录","No explicit loved items"),440,204,536,48);
            for(int i=0;i<6&&giftOffset+i<gifts.Count;i++)
            {
                var gift=gifts[giftOffset+i]; int y=174+i*84;
                Dashboard.DrawItem(b,gift.Icon,R(436,y,64,64));
                Label(b,gift.Title,516,y,450,32); Label(b,gift.Detail,516,y+35,450,30,.88f);
            }
        }
        private static List<Row> ProgressRows()
        {
            var p=Game1.player; int depth=p.deepestMineLevel;
            var museum=Game1.getLocationFromName("ArchaeologyHouse") as LibraryMuseum;
            var rows=new List<Row> {
                new Row { Title=T("矿井","Mines"),Detail=Math.Min(120,depth)+" / 120",Complete=Math.Min(120,depth),Required=120 },
                new Row { Title=T("骷髅洞穴最深层","Skull Cavern best"),Detail=Math.Max(0,depth-120).ToString() },
                new Row { Title=T("博物馆","Museum"),Detail=(museum?.museumPieces.Count()??0)+" / 95",Complete=museum?.museumPieces.Count()??0,Required=95 },
                new Row { Title=T("累计出货种类","Items shipped"),Detail=p.basicShipped.Count().ToString() },
                new Row { Title=T("游玩天数","Days played"),Detail=Game1.stats.DaysPlayed.ToString() },
                new Row { Title=T("累计收入","Total earned"),Detail=p.totalMoneyEarned+" G" }
            };
            if(museum!=null) foreach(var item in p.Items.Where(i=>i!=null&&museum.isItemSuitableForDonation(i)))
                rows.Add(new Row { Title=item.DisplayName,Icon=item,Detail=T("背包中可捐赠给博物馆","Carried: can be donated to museum") });
            foreach(var pair in p.basicShipped.Pairs.OrderByDescending(x=>x.Value))
            {
                var item=Object(pair.Key);
                rows.Add(new Row { Title=item?.DisplayName??pair.Key,Icon=item,Detail=T("累计出货","Shipped")+" ×"+pair.Value });
            }
            return rows;
        }
        private static void DrawProgress(SpriteBatch b)
        {
            var p=Game1.player;
            var museum=Game1.getLocationFromName("ArchaeologyHouse") as LibraryMuseum;
            Label(b,T("矿井与博物馆","Mines and museum"),36,104,456,36);
            Label(b,T("今日出货","Today's shipping"),516,104,464,36);
            Panel(b,24,152,464,438);
            Label(b,T("矿井","Mines")+"  "+Math.Min(120,p.deepestMineLevel)+" / 120",42,168,424,36);
            ProgressBar(b,42,214,420,Math.Min(120,p.deepestMineLevel),120);
            Label(b,T("骷髅洞穴","Skull Cavern")+"  "+Math.Max(0,p.deepestMineLevel-120),42,244,424,34);
            Label(b,T("博物馆","Museum")+"  "+(museum?.museumPieces.Count()??0)+" / 95",42,308,424,36);
            ProgressBar(b,42,354,420,museum?.museumPieces.Count()??0,95);
            Label(b,T("背包中可捐赠","Carried donations"),42,393,420,34,.9f);
            var donations=p.Items.Where(item=>item!=null&&museum?.isItemSuitableForDonation(item)==true).ToList();
            for(int i=0;i<Math.Min(6,donations.Count);i++)Dashboard.DrawItem(b,donations[i],R(42+i*69,434,60,60));
            Label(b,T("累计收入","Total earned")+" "+p.totalMoneyEarned+" G",42,530,420,34,.85f);
            Panel(b,504,152,496,438);
            var shipping=Game1.getFarm().getShippingBin(p);
            long total=shipping.Sum(item=>(long)item.sellToStorePrice()*item.Stack);
            Label(b,total.ToString("N0")+" G · "+shipping.Count+T(" 组"," stacks"),522,166,454,36);
            offsets[2]=Math.Clamp(offsets[2],0,Math.Max(0,shipping.Count-4));
            for(int i=0;i<4&&offsets[2]+i<shipping.Count;i++)
            {
                var item=shipping[offsets[2]+i];int y=222+i*78;
                Dashboard.DrawItem(b,item,R(520,y,56,56));
                Label(b,item.DisplayName,592,y,388,32,.9f);
                Label(b,"×"+item.Stack+" · "+((long)item.sellToStorePrice()*item.Stack)+" G",592,y+33,388,30,.84f);
            }
            if(shipping.Count==0)Label(b,T("出货箱为空","Shipping bin is empty"),524,260,450,44);
            Button(b,"<",516,546,48,34);Button(b,">",936,546,48,34);
            Panel(b,24,602,976,106);
            Label(b,T("近期记录","Recent history"),36,610,192,32,.9f);
            var days=CompanionHistory.Days.TakeLast(7).ToList();
            if(days.Count==0)Label(b,T("从首次连接开始记录","Records begin with the first connection"),242,638,736,38,.85f);
            for(int i=0;i<days.Count;i++)
            {
                var day=days[i];int x=240+i*108;
                Label(b,Season(day.Season)+" "+day.DayOfMonth,x,610,102,30,.84f);
                Label(b,day.Shipped+" G",x,647,102,30,.84f);
            }
            Label(b,T("出货","Shipped"),36,651,176,30,.84f);
        }
        private static void ProgressBar(SpriteBatch b,int x,int y,int width,int have,int required)
        {
            b.Draw(Game1.staminaRect,R(x,y,width,16),new Color(214,164,85));
            b.Draw(Game1.staminaRect,R(x,y,(int)(width*Math.Clamp(have/(float)Math.Max(1,required),0,1)),16),new Color(109,144,51));
        }
        private static void DrawFarm(SpriteBatch b,bool machines)
        {
            if(!machines){DrawFields(b);return;}
            int page=machines?8:7;
            Label(b,machines?T("机器 · 果树 · 动物","Machines · trees · animals"):T("田地 · 按地点查看生长与浇水","Fields · growth and watering by location"),36,104,936,36);
            var rows=Rows(page); int start=offsets[page]=Math.Clamp(offsets[page],0,Math.Max(0,rows.Count-5));
            if(rows.Count==0) { Empty(b); return; }
            for(int i=0;i<5&&start+i<rows.Count;i++)
            {
                var row=rows[start+i]; int y=158+i*102;
                Panel(b,24,y,976,94);
                Dashboard.DrawItem(b,row.Icon,R(36,y+16,60,60));
                Label(b,row.Title,112,y+10,600,34); Label(b,row.Group??"",740,y+10,240,34,.88f);
                Label(b,row.Detail,112,y+49,868,32,.88f);
            }
            Footer(b,page,rows.Count,5);
        }
        private static string FieldStatus(int status) => status==0?T("可收获","Ready"):status==1?T("需要浇水","Needs water"):T("已浇水","Watered");
        private static void DrawFields(SpriteBatch b)
        {
            var rows=Rows(7);
            Color[] colors={new Color(25,114,31),new Color(42,103,168),new Color(71,122,54)};
            Color[] washes={new Color(125,203,101)* (36/255f),new Color(62,140,209)*(34/255f),new Color(169,211,138)*(24/255f)};
            Label(b,T("田地","Fields")+" · "+rows.Sum(r=>r.Count)+T(" 株"," crops"),36,100,450,34);
            for(int s=0;s<3;s++) Dashboard.Write(b,FieldStatus(s)+" "+rows.Where(r=>r.Status==s).Sum(r=>r.Count),R(478+s*174,100,166,34),colors[s],.84f);
            int start=offsets[7]=Math.Clamp(offsets[7],0,Math.Max(0,rows.Count-5));
            if(rows.Count==0){Empty(b);return;}
            for(int i=0;i<5&&start+i<rows.Count;i++)
            {
                var row=rows[start+i];int y=150+i*102;
                bool heading=i==0||rows[start+i-1].Group!=row.Group||rows[start+i-1].Status!=row.Status;
                if(heading)
                {
                    b.Draw(Game1.staminaRect,R(24,y,976,31),washes[row.Status]);
                    Label(b,row.Group,36,y,490,30,.84f);
                    Dashboard.Write(b,FieldStatus(row.Status)+" · "+rows.Where(r=>r.Group==row.Group&&r.Status==row.Status).Sum(r=>r.Count),R(604,y,378,30),colors[row.Status],.84f);
                }
                Dashboard.DrawItem(b,row.Icon,R(36,y+33,56,56));
                Label(b,row.Title+" ×"+row.Count,112,y+39,456,38,.94f);
                string days=row.Status==0?T("可立即收获","Ready to harvest"):(row.MinimumDays==row.MaximumDays?row.MaximumDays.ToString():row.MinimumDays+"–"+row.MaximumDays)+T(" 天后成熟"," days remaining");
                Dashboard.Write(b,days,R(604,y+39,378,38),colors[row.Status],.9f);
                b.Draw(Game1.staminaRect,R(32,y+98,960,1),new Color(169,95,22)*.33f);
            }
            Footer(b,7,rows.Count,5);
        }
        private static void Footer(SpriteBatch b,int page,int count,int visible)
        {
            Button(b,"<",24,674,64,34); Button(b,">",936,674,64,34);
            Label(b,count==0?"0 / 0":(offsets[page]+1)+"–"+Math.Min(count,offsets[page]+visible)+" / "+count,112,675,800,30,.86f);
        }
        private static List<Row> BundleDisplay()
        {
            string[] rooms={Room("Crafts Room"),Room("Pantry"),Room("Fish Tank"),Room("Boiler Room"),Room("Bulletin Board"),Room("Vault"),Room("Abandoned Joja Mart")};
            return Rows(9).OrderBy(r=>Array.IndexOf(rooms,r.Group)).ToList();
        }
        private static void DrawBundles(SpriteBatch b)
        {
            var rows=BundleDisplay(); bundleOffset=Math.Clamp(bundleOffset,0,Math.Max(0,rows.Count-3));
            Label(b,T("社区中心 · 收集追踪","Community Center · bundle tracking"),36,102,944,38);
            if(rows.Count==0) { Empty(b,T("暂无社区中心收集包","No Community Center bundles")); return; }
            for(int i=0;i<3&&bundleOffset+i<rows.Count;i++)
            {
                var row=rows[bundleOffset+i]; int y=152+i*170;
                Panel(b,24,y,976,162);
                int roomDone=rows.Where(r=>r.Group==row.Group).Count(r=>r.Complete>=r.Required), roomTotal=rows.Count(r=>r.Group==row.Group);
                bool roomHeader=i==0||rows[bundleOffset+i-1].Group!=row.Group;
                if(roomHeader)
                {
                    b.Draw(Game1.staminaRect,R(32,y+3,960,35),new Color(255,199,113));
                    Label(b,row.Group+"  "+roomDone+"/"+roomTotal,40,y+3,520,34,.88f);
                    b.Draw(Game1.staminaRect,R(720,y+16,250,8),new Color(190,129,49));
                    b.Draw(Game1.staminaRect,R(720,y+16,roomTotal==0?0:250*roomDone/roomTotal,8),new Color(173,115,35));
                }
                Label(b,row.Title+"  "+row.Complete+"/"+row.Required,40,y+(roomHeader?37:6),928,32,.9f);
                var children=row.Children;
                if(children.Count==0) { Label(b,T("已完成","Completed"),44,y+65,860,42); continue; }
                string key=row.Group+"/"+row.Title;
                int start=requirementOffsets.TryGetValue(key,out int offset)?Math.Clamp(offset,0,Math.Max(0,children.Count-4)):0;
                Button(b,"<",36,y+76,44,56); Button(b,">",944,y+76,44,56);
                for(int j=0;j<4&&start+j<children.Count;j++)
                {
                    var item=children[start+j]; int x=88+j*212;
                    b.Draw(Game1.staminaRect,R(x,y+73,204,82),new Color(255,225,157));
                    Dashboard.DrawItem(b,item.Icon,R(x+3,y+76,42,42));
                    Label(b,item.Title,x+49,y+76,151,34,.84f);
                    Dashboard.Write(b,item.Complete+" / "+item.Required,R(x+4,y+122,152,30),item.Complete>=item.Required?new Color(25,112,31):Muted,.84f);
                    Check(b,item.Complete>=item.Required,x+166,y+122);
                }
            }
            Button(b,"<",24,674,64,34); Button(b,">",936,674,64,34);
            Label(b,(bundleOffset+1)+"–"+Math.Min(rows.Count,bundleOffset+3)+" / "+rows.Count,112,674,800,32,.86f);
        }
        private static Rectangle MapSource()
        {
            float w=300/mapZoom,h=180/mapZoom;
            mapCenter.X=Math.Clamp(mapCenter.X,w/600,1-w/600); mapCenter.Y=Math.Clamp(mapCenter.Y,h/360,1-h/360);
            return new Rectangle((int)(mapCenter.X*300-w/2),(int)(mapCenter.Y*180-h/2),(int)w,(int)h);
        }
        internal static void DragMap(int dx,int dy)
        {
            mapCenter-=new Vector2(dx/(976f*mapZoom),dy/(490f*mapZoom)); MapSource();
        }
        private static void DrawMap(SpriteBatch b)
        {
            map??=Game1.content.Load<Texture2D>("LooseSprites/map");
            var box=Dashboard.Bounds(CompanionLayout.MapCanvas); var source=MapSource();
            b.Draw(map,box,source,Color.White);
            DrawMarker(b,Game1.currentLocation,Game1.player.TilePoint,box,source,Color.Red);
            if(focusedPerson?.currentLocation!=null) DrawMarker(b,focusedPerson.currentLocation,focusedPerson.TilePoint,box,source,Color.Yellow);
            Button(b,"-",32,104,56); Button(b,"+",96,104,56);
            Button(b,mapZoom.ToString("0.0")+"×",160,104,112);
            Label(b,focusedPerson?.displayName??Game1.currentLocation.DisplayName,292,108,684,36);
            Label(b,Game1.player.farmName.Value+" · "+Game1.getTimeOfDayString(Game1.timeOfDay)+" · "+Game1.player.Money.ToString("N0")+" G",36,590,936,32,.88f);
            var people=People(); mapPeople=Math.Clamp(mapPeople,0,Math.Max(0,people.Count-8));
            Button(b,"<",24,653,48,55); Button(b,">",952,653,48,55);
            for(int i=0;i<8&&mapPeople+i<people.Count;i++)
            {
                var person=people[mapPeople+i].Person; int x=88+i*106;
                Panel(b,x,630,98,78,person==focusedPerson);
                b.Draw(person.Portrait,R(x+25,634,48,48),new Rectangle(0,0,64,64),Color.White);
                Label(b,person.displayName,x+4,681,90,26,.84f);
            }
        }
        private static void DrawMarker(SpriteBatch b,GameLocation location,Point tile,Rectangle box,Rectangle source,Color color)
        {
            var position=WorldMapManager.GetPositionData(location,tile);
            if(!position.HasValue) return;
            var pixel=position.Value.GetMapPixelPosition();
            float px=pixel.X/4f,py=pixel.Y/4f;
            if(!source.Contains((int)px,(int)py)) return;
            int size=Math.Max(5,(int)(14*Dashboard.Scale));
            var marker=new Rectangle(box.X+(int)((px-source.X)/source.Width*box.Width)-size/2,box.Y+(int)((py-source.Y)/source.Height*box.Height)-size/2,size,size);
            marker=Rectangle.Intersect(marker,box);
            b.Draw(Game1.staminaRect,marker,Color.White); marker.Inflate(-2,-2);
            if(marker.Width>0&&marker.Height>0)b.Draw(Game1.staminaRect,marker,color);
        }
        internal static Rectangle CalendarBounds(int day) => Dashboard.Bounds(CompanionLayout.Calendar(day));
        private static void DrawCalendar(SpriteBatch b)
        {
            Label(b,Season(Game1.currentSeason)+" · "+T("第 ","Year ")+Game1.year+T(" 年",""),36,100,936,38);
            string[] names={T("周一","Mon"),T("周二","Tue"),T("周三","Wed"),T("周四","Thu"),T("周五","Fri"),T("周六","Sat"),T("周日","Sun")};
            for(int i=0;i<7;i++)Label(b,names[i],36+i*140,136,116,28,.86f);
            var people=Utility.getAllCharacters().Where(n=>n.IsVillager&&n.Birthday_Season==Game1.currentSeason).ToList();
            for(int day=1;day<=28;day++)
            {
                var phys=CompanionLayout.Calendar(day); int x=phys.X,y=phys.Y;
                Panel(b,x,y,136,116,day==(calendarDay==0?Game1.dayOfMonth:calendarDay));
                Label(b,day.ToString(),x+10,y+6,116,30,.92f);
                var npc=people.FirstOrDefault(n=>n.Birthday_Day==day);
                if(npc!=null)
                {
                    b.Draw(npc.Portrait,R(x+10,y+38,42,42),new Rectangle(0,0,64,64),Color.White);
                    Label(b,npc.displayName,x+8,y+82,120,28,.84f);
                }
                else if(Utility.isFestivalDay(day,Game1.season)) Dashboard.Paragraph(b,Festival(day),R(x+10,y+40,116,68),Ink);
                else if(day%7==5||day%7==0) Label(b,T("货车","Cart"),x+10,y+52,116,32,.85f);
                if(day==Game1.dayOfMonth) b.Draw(Game1.staminaRect,R(x+102,y+12,18,18),Color.IndianRed);
            }
            int chosen=calendarDay==0?Game1.dayOfMonth:calendarDay;
            Label(b,chosen+" · "+CalendarEvents(chosen),36,660,940,42,.94f);
        }
        private static string SettingName(int i) => i switch {
            0=>T("世界缩放","World zoom"),1=>T("界面缩放（默认 150%）","UI scale (default 150%)"),
            2=>T("音乐","Music"),3=>T("音效","Sound"),4=>T("手柄吸附","Controller snapping"),5=>T("原生像素边框","Native pixel frames"),
            _=>Dashboard.PageName(i-6) };
        private static void DrawSettings(SpriteBatch b)
        {
            Label(b,T("设置 · 上下滑动查看更多","Settings · scroll for more"),36,104,940,36);
            for(int row=0;row<6;row++)
            {
                int i=settingsOffset+row,y=158+row*84;
                if(i>=18)break;
                Panel(b,24,y,976,76); Label(b,SettingName(i),40,y+20,570,36,.94f);
                if(i<4)
                {
                    float value=i==0?Game1.options.baseZoomLevel:i==1?Game1.options.baseUIScale:i==2?Game1.options.musicVolumeLevel:Game1.options.soundVolumeLevel;
                    Button(b,"-",650,y+12,60,52); Label(b,Math.Round(value*100)+"%",738,y+20,138,36); Button(b,"+",922,y+12,60,52);
                }
                else
                {
                    bool value=i==4?Game1.options.snappyMenus:i==5?CompanionPreferences.Current.NativeFrames:CompanionPreferences.Visible(i-6);
                    Check(b,value,742,y+24); Button(b,value?T("开","On"):T("关","Off"),802,y+12,180,52,value);
                }
            }
            Button(b,"<",24,674,64,34); Button(b,">",936,674,64,34);
            Label(b,(settingsOffset+1)+"–"+Math.Min(18,settingsOffset+6)+" / 18",112,674,800,32,.86f);
        }
        private static bool ClickSettings(int x,int y)
        {
            for(int row=0;row<6;row++)
            {
                int i=settingsOffset+row,top=158+row*84;
                int sign=R(650,top+12,60,52).Contains(x,y)?-1:R(922,top+12,60,52).Contains(x,y)?1:0;
                if(i<4&&sign!=0)
                {
                    switch(i)
                    {
                        case 0: Game1.options.desiredBaseZoomLevel=Game1.options.baseZoomLevel=Math.Clamp(Game1.options.baseZoomLevel+sign*.05f,.5f,1.5f); Game1.forceSnapOnNextViewportUpdate=true; break;
                        case 1: CompanionPreferences.SetUiScale(Game1.options.baseUIScale+sign*.05f); break;
                        case 2: Game1.options.changeSliderOption(1,(int)Math.Round(Math.Clamp(Game1.options.musicVolumeLevel+sign*.1f,0,1)*100)); break;
                        case 3: Game1.options.changeSliderOption(2,(int)Math.Round(Math.Clamp(Game1.options.soundVolumeLevel+sign*.1f,0,1)*100)); break;
                    }
                    Game1.options.optionsDirty=true; UiPatches.EnsureViewports(); VerticalWorldRenderer.InvalidateWorld(); return true;
                }
                if(i>=4&&R(724,top+12,258,52).Contains(x,y))
                {
                    if(i==4) { Game1.options.snappyMenus=!Game1.options.snappyMenus; Game1.options.optionsDirty=true; }
                    else if(i==5) CompanionPreferences.Current.NativeFrames=!CompanionPreferences.Current.NativeFrames;
                    else if(i-6!=0&&i-6!=11) CompanionPreferences.Current.VisiblePages[i-6]=!CompanionPreferences.Visible(i-6);
                    CompanionPreferences.Save(); Dashboard.Reveal(11); return true;
                }
            }
            return true;
        }
        internal static bool Click(int page,int x,int y)
        {
            if(page==0)return Dashboard.ClickBackpack(x,y);
            if(page==12)return ModManagement.Click(x,y);
            if(page==13)return ClickExit(x,y);
            Pointer(x,y);
            if(page==10)
            {
                for(int day=1;day<=28;day++)if(CalendarBounds(day).Contains(x,y)){calendarDay=day;return true;}
                return true;
            }
            if(page==3)
            {
                if(R(32,104,56,48).Contains(x,y))mapZoom=Math.Max(1,mapZoom-.5f);
                else if(R(96,104,56,48).Contains(x,y))mapZoom=Math.Min(4,mapZoom+.5f);
                else if(R(160,104,112,48).Contains(x,y)){mapZoom=1;mapCenter=new Vector2(.5f,.5f);focusedPerson=null;}
                var people=People();
                if(R(24,653,48,55).Contains(x,y))mapPeople=Math.Max(0,mapPeople-1);
                if(R(952,653,48,55).Contains(x,y))mapPeople=Math.Min(Math.Max(0,people.Count-8),mapPeople+1);
                for(int i=0;i<8&&mapPeople+i<people.Count;i++)if(R(88+i*106,630,98,78).Contains(x,y))
                {
                    focusedPerson=people[mapPeople+i].Person;
                    var position=WorldMapManager.GetPositionData(focusedPerson.currentLocation,focusedPerson.TilePoint);
                    if(position.HasValue) { var pixel=position.Value.GetMapPixelPosition();mapCenter=new Vector2(pixel.X/1200f,pixel.Y/720f);mapZoom=Math.Max(2,mapZoom); }
                    else Dashboard.Notice=T("当前位置未标注在地图中","Current location is not on this map");
                }
                return true;
            }
            int arrow=R(24,674,64,34).Contains(x,y)?1:R(page==1||page==4?260:page==6?344:936,674,64,34).Contains(x,y)?-1:0;
            if(arrow!=0)
            {
                pointer=new Point(100,200);
                if(page==11)settingsOffset=Math.Clamp(settingsOffset+(arrow<0?3:-3),0,12);
                else Scroll(page,arrow);
                return true;
            }
            if(page==11)return ClickSettings(x,y);
            if(page==2)
            {
                if(R(516,546,48,34).Contains(x,y))Scroll(2,1);
                if(R(936,546,48,34).Contains(x,y))Scroll(2,-1);
                return true;
            }
            if(page==1)
            {
                if(R(24,99,44,46).Contains(x,y)){categoryStart=Math.Max(0,categoryStart-1);return true;}
                if(R(748,99,44,46).Contains(x,y)){categoryStart=Math.Min(4,categoryStart+1);return true;}
                if(R(804,99,196,46).Contains(x,y))
                {recipeCategory=recipeCategory==1?0:1;offsets[1]=selections[1]=Dashboard.ScrollRow=detailOffset=ingredientOffset=0;return true;}
                for(int i=0;i<5;i++)if(R(76+i*134,99,128,46).Contains(x,y))
                {recipeCategory=0;recipeGroup=categoryStart+i;offsets[1]=selections[1]=Dashboard.ScrollRow=detailOffset=ingredientOffset=0;return true;}
            }
            if(page==4&&R(704,99,288,46).Contains(x,y)){showCompleted=!showCompleted;refreshAt=0;selections[4]=offsets[4]=0;detailOffset=0;return true;}
            var rows=Rows(page); var row=Selected(rows,page);
            if(page==1||page==4||page==6)
            {
                int width=page==6?384:300;
                for(int i=0;i<6&&offsets[page]+i<rows.Count;i++)if(R(24,158+i*78,width,72).Contains(x,y))
                { selections[page]=offsets[page]+i; detailOffset=giftOffset=ingredientOffset=0; cancelPending=null; Dashboard.Notice="";return true; }
                if(page==1)
                {
                    int first=Math.Clamp(selections[1]-3,0,Math.Max(0,rows.Count-8));
                    for(int i=0;i<8&&first+i<rows.Count;i++)if(R(348+i*81,634,74,72).Contains(x,y))
                    { selections[1]=first+i; offsets[1]=Math.Clamp(first+i-2,0,Math.Max(0,rows.Count-6));detailOffset=0;return true; }
                    if(R(782,544,202,58).Contains(x,y))return Act(row,recipeCategory==1);
                }
                if(page==4&&R(748,648,232,48).Contains(x,y))return Act(row,false);
            }
            if(page==9)
            {
                rows=BundleDisplay();
                for(int i=0;i<3&&bundleOffset+i<rows.Count;i++)
                {
                    row=rows[bundleOffset+i];int top=152+i*170;
                    int sign=R(36,top+76,44,56).Contains(x,y)?-1:R(944,top+76,44,56).Contains(x,y)?1:0;
                    if(sign==0)continue;
                    string key=row.Group+"/"+row.Title;requirementOffsets.TryGetValue(key,out int current);
                    requirementOffsets[key]=Math.Clamp(current+sign,0,Math.Max(0,row.Children.Count-4));return true;
                }
            }
            return true;
        }
        internal static bool Swipe(int page,Point start,int direction)
        {
            if(page==1&&start.Y>=99&&start.Y<=145)
            {categoryStart=Math.Clamp(categoryStart+direction,0,4);return true;}
            if(page==9)
            {
                var rows=BundleDisplay();int i=(start.Y-152)/170;
                if(start.Y<198||i<0||i>=3||bundleOffset+i>=rows.Count)return false;
                var row=rows[bundleOffset+i];string key=row.Group+"/"+row.Title;
                requirementOffsets.TryGetValue(key,out int current);
                requirementOffsets[key]=Math.Clamp(current+direction,0,Math.Max(0,row.Children.Count-4));
                return true;
            }
            if(page==1&&start.X>=340&&start.Y>=634&&start.Y<=708)
            {
                var rows=Rows(1);
                selections[1]=Math.Clamp(selections[1]+direction,0,Math.Max(0,rows.Count-1));
                offsets[1]=Dashboard.ScrollRow=Math.Clamp(selections[1]-2,0,Math.Max(0,rows.Count-6));
                detailOffset=0;return true;
            }
            if(page==3&&start.Y>=630)
            { mapPeople=Math.Clamp(mapPeople+direction,0,Math.Max(0,People().Count-8));return true; }
            return false;
        }
    }
}
