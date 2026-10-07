using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Quests;
using StardewValley.TerrainFeatures;
using StardewValley.WorldMaps;

namespace Rgdsplus.VerticalWorld
{
    // Persistent information pages. These never take ownership of activeClickableMenu.
    internal static partial class DashboardPages
    {
        private sealed class Row
        {
            internal string Title, Detail, Group;
            internal int Complete, Required, Status, Count = 1, MinimumDays, MaximumDays;
            internal Item Icon;
            internal NPC Person;
            internal Quest Quest;
            internal CraftingRecipe Recipe;
            internal List<Row> Children;
        }
        private static readonly Color Ink = Dashboard.Ink;
        private static readonly Color Muted = Dashboard.Muted;
        private static readonly Color Fill = new Color(255, 217, 142);
        private static readonly Dictionary<string, List<Row>> lists = new Dictionary<string, List<Row>>();
        private static readonly int[] offsets = new int[12];
        
        private static int calendarDay;
        private static Quest cancelPending;
        private static int refreshPage = -1;
        private static long refreshAt;
        private static Texture2D map;
        internal static string Section => recipeCategory.ToString();
        internal static int CalendarSelection => calendarDay;
        internal static int SettingsSection => settingsOffset;
        internal static float MapZoom => mapZoom;
        internal static Vector2 MapCenter => mapCenter;
        internal static int DetailScroll => detailOffset;
        internal static int GiftScroll => giftOffset;
        internal static int BundleScroll => bundleOffset;
        internal static int RecipeCategory => recipeGroup;
        internal static string[] RecipeKeys => Rows(1).Select(r=>r.Recipe.name).ToArray();
        internal static int[] CropStatusCounts => Enumerable.Range(0,3).Select(status=>Rows(7).Where(r=>r.Status==status).Sum(r=>r.Count)).ToArray();
        internal static int SelectedIndex(int page) => selections[page];
        internal static string CalendarEvents(int day)
        {
            var events = Utility.getAllCharacters().Where(n => n.IsVillager && n.Birthday_Season == Game1.currentSeason && n.Birthday_Day == day)
                .Select(n => n.displayName + T("生日", " birthday")).ToList();
            if (Utility.isFestivalDay(day, Game1.season)) events.Add(Festival(day));
            if (day % 7 == 5 || day % 7 == 0) events.Add(T("旅行货车", "Travelling cart"));
            return events.Count == 0 ? T("无活动", "No events") : string.Join(" | ", events);
        }
        private static string T(string zh, string en) => Dashboard.Text(zh, en);
        private static Rectangle R(int x, int y, int w, int h) => Dashboard.Rect(x, y, w, h);
        internal static int BackpackTextOffset;
        private static int recipeCategory, recipeGroup, categoryStart, detailOffset, detailMax, giftOffset, bundleOffset, mapPeople;
        private static bool showCompleted = true;
        private static readonly int[] selections = new int[12];
        private static Point pointer = new Point(100, 200);
        internal static void Reset()
        {
            lists.Clear(); Array.Clear(offsets,0,offsets.Length); Array.Clear(selections,0,selections.Length);
            cancelPending=null; calendarDay=settingsOffset=recipeCategory=recipeGroup=categoryStart=0;
            BackpackTextOffset=detailOffset=detailMax=giftOffset=bundleOffset=mapPeople=ingredientOffset=0;
            refreshAt=0; refreshPage=-1; map=null; mapZoom=1; mapCenter=new Vector2(.5f,.5f); showCompleted=true;
            focusedPerson=null; requirementOffsets.Clear();
        }
        internal static void Select(int page)
        {
            cancelPending=null; detailOffset=0;
            Dashboard.ScrollRow=offsets[page]; Dashboard.Reveal(page);
        }
        internal static void Pointer(int x,int y) => pointer=Dashboard.Physical(x,y);
        internal static void Scroll(int page,int direction)
        {
            int step=direction<0?1:-1;
            if(page==0) { if(pointer.X>=288&&pointer.Y>=635) BackpackTextOffset=Math.Clamp(BackpackTextOffset+step,0,100); return; }
            if(page==3) { mapPeople=Math.Clamp(mapPeople+step,0,Math.Max(0,People().Count-8)); return; }
            if(page==2) { offsets[2]=Math.Clamp(offsets[2]+step,0,Math.Max(0,Game1.getFarm().getShippingBin(Game1.player).Count-4));Dashboard.ScrollRow=offsets[2];return; }
            if(page==10) return;
            if(page==11) { settingsOffset=Math.Clamp(settingsOffset+step,0,12); return; }
            if(page==1&&pointer.X>=340&&pointer.Y>=332)
            { ingredientOffset=Math.Clamp(ingredientOffset+step,0,Math.Max(0,(Selected(Rows(1),1)?.Recipe?.recipeList.Count??0)-4)); return; }
            if((page==1||page==4)&&pointer.X>=340)
            { detailOffset=Math.Clamp(detailOffset+step,0,detailMax); return; }
            if(page==6&&pointer.X>=420) { giftOffset=Math.Clamp(giftOffset+step,0,Math.Max(0,Gifts(Selected(Rows(6),6)?.Person).Count-6)); return; }
            if(page==9) { bundleOffset=Math.Clamp(bundleOffset+step,0,Math.Max(0,BundleDisplay().Count-3)); return; }
            var rows=Rows(page);
            int visible=page==1||page==4||page==6?6:5;
            offsets[page]=Dashboard.ScrollRow=Math.Clamp(Dashboard.ScrollRow+step,0,Math.Max(0,rows.Count-visible));
        }
        private static Item Object(string id, int count = 1)
        {
            if (string.IsNullOrWhiteSpace(id) || id == "-1") return null;
            return ItemRegistry.Create(ItemRegistry.QualifyItemId(id) ?? "(O)" + id, count, allowNull: true);
        }
        private static void Refresh(int page)
        {
            if (page == refreshPage && Environment.TickCount64 < refreshAt) return;
            refreshPage = page;
            refreshAt = Environment.TickCount64 + 1500;
            lists.Clear();
            foreach (string key in new[] { "machines", "animals", "trees", "crops", "shipping", "quests", "people", "bundles", "crafting", "cooking" })
                lists[key] = new List<Row>();
            if (page == 7 || page == 8) Utility.ForEachLocation(location =>
            {
                foreach (var item in location.objects.Values)
                {
                    if (!item.bigCraftable.Value || item.heldObject.Value == null) continue;
                    var output = item.heldObject.Value;
                    lists["machines"].Add(new Row { Title = item.DisplayName, Icon = output, Group = T("机器", "Machines"),
                        Detail = location.DisplayName + " | " + output.DisplayName + " x" + output.Stack + " | "
                            + (item.readyForHarvest.Value ? T("可收取", "Ready") : item.MinutesUntilReady + T(" 分钟", " min")) });
                }
                foreach (var feature in location.terrainFeatures.Values)
                {
                    if (feature is FruitTree tree)
                    {
                        var fruit = tree.fruit.FirstOrDefault();
                        lists["trees"].Add(new Row { Title = tree.GetDisplayName(), Icon = fruit, Group = T("果树", "Fruit trees"),
                            Detail = location.DisplayName + " | " + T("果实", "Fruit") + " " + tree.fruit.Count
                                + " | " + (tree.daysUntilMature.Value > 0 ? tree.daysUntilMature.Value + T(" 天成熟", " days to mature") : T("已成熟", "Mature")) });
                    }
                    if (feature is HoeDirt soil && soil.crop != null && !soil.crop.dead.Value)
                    {
                        var crop = soil.crop;
                        var harvest = Object(crop.indexOfHarvest.Value);
                        bool ready = crop.currentPhase.Value >= crop.phaseDays.Count - 1 && (!crop.fullyGrown.Value || crop.dayOfCurrentPhase.Value <= 0);
                        int remaining = 0;
                        for (int phase = crop.currentPhase.Value; phase < crop.phaseDays.Count - 1; phase++) remaining += crop.phaseDays[phase];
                        int days=ready?0:crop.fullyGrown.Value?Math.Max(0,crop.dayOfCurrentPhase.Value):Math.Max(0,remaining-crop.dayOfCurrentPhase.Value);
                        lists["crops"].Add(new Row { Title = harvest?.DisplayName ?? T("作物", "Crop"), Icon = harvest, Group = location.DisplayName,
                            Status=ready?0:soil.state.Value==0?1:2,MinimumDays=days,MaximumDays=days });
                    }
                }
                return true;
            });
            if (page == 7 || page == 8) foreach (var animal in Game1.getFarm().getAllFarmAnimals())
                lists["animals"].Add(new Row { Title = animal.displayName + "  " + animal.displayType, Group = T("动物", "Animals"),
                    Icon = Object(animal.currentProduce.Value),
                    Detail = T("好感", "Hearts") + " " + animal.friendshipTowardFarmer.Value / 200 + "/5 | "
                        + (animal.wasPet.Value ? T("已抚摸", "Petted") : T("未抚摸", "Not petted")) + " | "
                        + (animal.isBaby() ? T("幼年", "Baby") : T("成年", "Adult")) });
            if (page == 7 || page == 8) foreach (var item in Game1.getFarm().getShippingBin(Game1.player))
                lists["shipping"].Add(new Row { Title = item.DisplayName, Icon = item, Detail = "x" + item.Stack + " | " + item.sellToStorePrice() + " G" });
            if (page == 4) foreach (var quest in Game1.player.questLog)
            {
                if (quest == null || quest.destroy.Value || !showCompleted && quest.completed.Value) continue;
                quest.reloadObjective();
                lists["quests"].Add(new Row { Title = quest.questTitle, Detail = quest.currentObjective, Quest = quest });
            }
            if (page == 5 || page == 6 || page == 3) foreach (string name in Game1.player.friendshipData.Keys)
            {
                var person = Game1.getCharacterFromName(name);
                if (person == null) continue;
                var friendship = Game1.player.friendshipData[name];
                lists["people"].Add(new Row { Title = person.displayName, Person = person,
                    Detail = T("好感", "Hearts") + " " + friendship.Points / 250 + "/" + (friendship.IsMarried() ? 14 : 10)
                        + " | " + T("本周礼物", "Gifts this week") + " " + friendship.GiftsThisWeek + "/2 | "
                        + (friendship.TalkedToToday ? T("已交谈", "Talked") : T("未交谈", "Not talked")) });
            }
            lists["people"].Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCulture));
            if (page == 9) BuildBundles();
            if (page == 2) lists["progress"] = ProgressRows();
            if (page == 1) foreach (string name in Game1.player.craftingRecipes.Keys)
            {
                var recipe = new CraftingRecipe(name, false);
                lists["crafting"].Add(new Row { Title = recipe.DisplayName, Icon = recipe.createItem(), Recipe = recipe, Detail = Ingredients(recipe) });
            }
            if (page == 1) foreach (string name in Game1.player.cookingRecipes.Keys)
            {
                var recipe = new CraftingRecipe(name, true);
                lists["cooking"].Add(new Row { Title = recipe.DisplayName, Icon = recipe.createItem(), Recipe = recipe, Detail = Ingredients(recipe) });
            }
        }
        private static string Ingredients(CraftingRecipe recipe) => string.Join("  ", recipe.recipeList.Select(pair =>
            recipe.getNameFromIndex(pair.Key) + " x" + pair.Value));
        private static void BuildBundles()
        {
            if (!(Game1.getLocationFromName("CommunityCenter") is CommunityCenter center)) return;
            foreach (var pair in Game1.netWorldState.Value.BundleData)
            {
                var key = pair.Key.Split('/'); var fields = pair.Value.Split('/');
                if (key.Length < 2 || fields.Length < 3 || !int.TryParse(key[1], out int id) || !center.bundles.ContainsKey(id)) continue;
                var flags = center.bundles[id]; var parts = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var missing = new List<Row>(); int done = 0;
                for (int i = 0; i + 2 < parts.Length; i += 3)
                {
                    if (i / 3 < flags.Length && flags[i / 3]) { done++; continue; }
                    int count = int.Parse(parts[i + 1]), quality = int.Parse(parts[i + 2]);
                    var item = Object(parts[i], count);
                    int carried = item == null ? Game1.player.Money : Game1.player.Items.Where(x => x != null && x.QualifiedItemId == item.QualifiedItemId && x.Quality >= quality).Sum(x => x.Stack);
                    if(item!=null)item.Quality=quality;
                    missing.Add(new Row { Title = item?.DisplayName ?? count.ToString("N0") + " G", Icon = item,Complete=carried,Required=count,
                        Detail = "x" + count + " | " + T("品质", "Quality") + " " + quality + " | " + T("持有", "Carried") + " " + carried });
                }
                int required = parts.Length / 3;
                if (fields.Length > 4 && int.TryParse(fields[4], out int need) && need > 0) required = need;
                if (done >= required) missing.Clear();
                lists["bundles"].Add(new Row { Title = fields.Length > 6 && fields[6].Length > 0 ? fields[6] : fields[0],
                    Group = Room(key[0]), Complete = done, Required = required, Detail = Room(key[0]) + " | " + done + "/" + required + " | " + (done >= required ? T("已完成", "Complete") : T("未完成", "Missing")), Children = missing });
            }
        }
        private static string Room(string room) => room switch
        {
            "Crafts Room" => T("工艺室", "Crafts Room"), "Pantry" => T("茶水间", "Pantry"),
            "Fish Tank" => T("鱼缸", "Fish Tank"), "Boiler Room" => T("锅炉房", "Boiler Room"),
            "Bulletin Board" => T("布告栏", "Bulletin Board"), "Vault" => T("金库", "Vault"), "Abandoned Joja Mart" => T("废弃超市", "Abandoned Joja Mart"), _ => room
        };
        private static List<Row> Rows(int page)
        {
            Refresh(page);
            return page switch {
                1 => (recipeCategory==1 ? lists["cooking"] : lists["crafting"]).Where(r=>recipeCategory==1||recipeGroup==0||RecipeGroup(r.Recipe.name)==recipeGroup).ToList(),
                2 => lists["progress"], 3 => lists["people"], 4 => lists["quests"], 5 => lists["people"], 6 => lists["people"],
                7 => lists["crops"].GroupBy(r=>(r.Group,r.Title,r.Status)).Select(group=>new Row { Title=group.First().Title,Group=group.Key.Group,Status=group.Key.Status,Icon=group.First().Icon,Count=group.Count(),MinimumDays=group.Min(r=>r.MinimumDays),MaximumDays=group.Max(r=>r.MaximumDays) }).OrderBy(r=>r.Group).ThenBy(r=>r.Status).ThenBy(r=>r.Title).ToList(),
                8 => lists["machines"].Concat(lists["trees"]).Concat(lists["animals"]).ToList(),
                9 => lists["bundles"], _ => new List<Row>() };
        }
        private static bool CanCook() => Game1.player.HouseUpgradeLevel>0 || Game1.currentLocation.objects.Values.Any(x=>x.QualifiedItemId=="(BC)278");
        private static string RecipeIngredient(CraftingRecipe recipe,string id,int count) => recipe.getNameFromIndex(id)+"  "+Game1.player.Items.Where(item=>item!=null&&CraftingRecipe.ItemMatchesForCrafting(item,id)).Sum(item=>item.Stack)+" / "+count;
        private static string Festival(int day)
        {
            var data=Game1.temporaryContent.Load<Dictionary<string,string>>("Data/Festivals/"+Game1.currentSeason+day);
            return data.TryGetValue("name",out string name)?name:T("节日","Festival");
        }
        private static bool Act(Row row,bool cooking)
        {
            if(row==null || Game1.player.UsingTool || !Game1.player.CanMove) return true;

                if (row.Quest != null)
                {
                    if (row.Quest.completed.Value && row.Quest.moneyReward.Value > 0)
                    {
                        Game1.player.Money += row.Quest.GetMoneyReward();
                        row.Quest.OnMoneyRewardClaimed(); row.Quest.OnLeaveQuestPage();
                        detailOffset=0;
                    }
                    else if (!row.Quest.completed.Value && row.Quest.canBeCancelled.Value)
                    {
                        // A second tap confirms cancellation without opening a modal.
                        if (ReferenceEquals(cancelPending, row.Quest))
                        {
                            row.Quest.accepted.Value = false;
                            if (row.Quest.dailyQuest.Value && row.Quest.dayQuestAccepted.Value == Game1.Date.TotalDays) Game1.player.acceptedDailyQuest.Set(false);
                            Game1.player.questLog.Remove(row.Quest); detailOffset=0; cancelPending = null; Dashboard.Notice = "";
                        }
                        else { cancelPending = row.Quest; Dashboard.Notice = T("再次点击取消任务", "Tap again to cancel quest"); }
                    }
                }
                if (row.Recipe != null && row.Recipe.doesFarmerHaveIngredientsInInventory() && (!cooking || CanCook()) && !Game1.player.UsingTool)
                {
                    var result = row.Recipe.createItem();
                    var seasoning = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("917", 1) };
                    bool useSeasoning = cooking && result.Quality == 0 && CraftingRecipe.DoesFarmerHaveAdditionalIngredientsInInventory(seasoning);
                    if (useSeasoning) result.Quality = 2;
                    if (!Game1.player.couldInventoryAcceptThisItem(result)) { Dashboard.Notice = T("背包已满", "Inventory full"); return true; }
                    row.Recipe.consumeIngredients(null);
                    if (useSeasoning) CraftingRecipe.ConsumeAdditionalIngredients(seasoning, null);
                    Game1.player.addItemToInventoryBool(result);
                    Game1.player.NotifyQuests(quest => quest.OnRecipeCrafted(row.Recipe, result));
                    if (cooking) { Game1.player.cookedRecipe(result.ItemId); Game1.stats.checkForCookingAchievements(); }
                    else { Game1.player.craftingRecipes[row.Recipe.name] += row.Recipe.numberProducedPerCraft; Game1.stats.checkForCraftingAchievements(); }
                    Game1.playSound("coin"); Dashboard.Notice = result.DisplayName;
                }
                refreshAt = 0; return true;
        }
    }
}
