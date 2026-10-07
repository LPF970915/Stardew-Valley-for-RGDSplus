using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace Rgdsplus.VerticalWorld
{
    internal sealed class CompanionDay
    {
        public int Day { get; set; }
        public string Season { get; set; }
        public int DayOfMonth { get; set; }
        public int Gold { get; set; }
        public long Shipped { get; set; }
    }
    // Companion observations are stored separately from the game save, as in upstream.
    internal static class CompanionHistory
    {
        private static IModHelper helper;
        private static string key;
        internal static List<CompanionDay> Days = new List<CompanionDay>();
        private static bool Test => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SDV_DUAL_UI_TEST_DIR"));
        internal static void Initialize(IModHelper value)
        {
            helper=value;
            helper.Events.GameLoop.SaveLoaded+=(_,__)=>Load();
            helper.Events.GameLoop.DayStarted+=(_,__)=>Observe();
            helper.Events.GameLoop.DayEnding+=(_,__)=>Observe();
            helper.Events.GameLoop.UpdateTicked+=(_,e)=> { if(e.IsMultipleOf(600))Observe(); };
            helper.Events.GameLoop.ReturnedToTitle+=(_,__)=> {key=null;Days.Clear();};
        }
        private static void Load()
        {
            key="companion-history/"+Game1.uniqueIDForThisGame+"-"+Game1.player.UniqueMultiplayerID+".json";
            try { Days=Test?new List<CompanionDay>():helper.Data.ReadJsonFile<List<CompanionDay>>(key)??new List<CompanionDay>(); }
            catch(Exception ex) { Days=new List<CompanionDay>(); ModEntry.Write("companion-history-read "+ex.Message); }
            Observe();
        }
        internal static void Observe()
        {
            if(!Context.IsWorldReady||!VerticalWorldRenderer.Enabled)return;
            if(key==null)
            {
                if(!Test){Load();return;}
                key="fixture";Days.Clear();
            }
            int date=Game1.Date.TotalDays;
            var last=Days.LastOrDefault();
            bool changed=false;
            if(last==null||last.Day!=date)
            {
                Days.RemoveAll(day=>day.Day>=date);
                last=new CompanionDay { Day=date,Season=Game1.currentSeason,DayOfMonth=Game1.dayOfMonth,Gold=Game1.player.Money };
                Days.Add(last); if(Days.Count>28)Days.RemoveAt(0);changed=true;
            }
            long shipped=Game1.getFarm().getShippingBin(Game1.player).Sum(item=>(long)item.sellToStorePrice()*item.Stack);
            if(last.Shipped!=shipped){last.Shipped=shipped;changed=true;}
            if(changed&&!Test)try { helper.Data.WriteJsonFile(key,Days); }catch(Exception ex){ModEntry.Write("companion-history-write "+ex.Message);}
        }
    }
}
