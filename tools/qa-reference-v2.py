"""Reference v2 device acceptance. Uses opt-in in-memory game state, never user saves."""
import importlib.util
import json
import os
import time
from pathlib import Path
import paramiko

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "build/acceptance-reference-v2"
spec = importlib.util.spec_from_file_location("device", ROOT / "tools/qa-device-ui.py")
qa = importlib.util.module_from_spec(spec)
spec.loader.exec_module(qa)


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["preview", "suite"])
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    ssh = paramiko.SSHClient()
    ssh.load_system_host_keys()
    ssh.set_missing_host_key_policy(paramiko.RejectPolicy())
    ssh.connect("[device-host]", username="root", password=os.environ["RGDSPLUS_SSH_PASSWORD"], timeout=10)
    checks = []
    with ssh.open_sftp() as ftp:
        def request(action, **kwargs):
            return qa.request(ftp, action, **kwargs)

        def check(name, condition, state=None):
            checks.append(dict(name=name, passed=bool(condition), state=state))
            print(("PASS " if condition else "FAIL ") + name, flush=True)
            (OUT / "results.json").write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding="utf-8")
            if not condition:
                raise AssertionError(name + ": " + json.dumps(state, ensure_ascii=False))

        def tap(x, y):
            request("touch", x=x, y=y, pressed=True, released=False)
            return request("touch", x=x, y=y, pressed=False, released=True)

        def drag(x, y, dx, dy):
            request("touch", x=x, y=y, pressed=True, released=False)
            for i in range(1, 5):
                state = request("touch", x=x+dx*i/4, y=y+dy*i/4, pressed=False, released=False)
            return request("touch", x=x+dx, y=y+dy, pressed=False, released=True)

        def page(target):
            state = request("status")
            for _ in range(12):
                if state["page"] == target:
                    return state
                state = tap(976, 46)
            raise AssertionError("Could not navigate to " + str(target))

        def shot(name):
            from PIL import Image, ImageStat
            time.sleep(.35)
            state = request("capture")
            path = OUT / (name + ".png")
            ftp.get(qa.QA + "/" + state["screenshot"], str(path))
            with Image.open(path) as image:
                check(name + " renders 1024x768", image.size == (1024,768) and max(ImageStat.Stat(image.convert("RGB")).stddev)>10)
            return path

        def count(state, item_id, quality=None):
            return sum(i["stack"] for i in state["items"] if i and i["id"]==item_id and (quality is None or i["quality"]==quality))

        try:
            state = request("status")
            if not state["worldReady"]:
                request("fixture")
                time.sleep(8)
            if request("status")["mode"] != "ExpandedHud":
                request("toggle")
            time.sleep(12)  # Allow new-farm achievement toasts to leave before visual capture.
            request("language", code="zh")
            request("reset_ui")
            request("dashboard")
            for i in range(12):
                state = page(i)
                check("page %02d uses real navigation without modal" % i, state["page"]==i and state["menu"] is None, {k:state[k] for k in ("page","menu","uiScale")})
                shot("page-%02d-zh-150" % i)
            if args.action == "preview":
                return
            request("dashboard")
            state = tap(568,206)
            check("64px backpack cell selects index 5", state["currentItem"]==5)
            before=request("status")
            state=drag(228,206,748,96)
            check("drag moves whole stack to slot 23", state["items"][23]==before["items"][0] and state["items"][0] is None)
            state=drag(976,302,-748,-96)
            check("reverse drag restores stack", state["items"][0]==before["items"][0] and state["items"][23] is None)
            before=request("status")
            request("touch",x=228,y=206,pressed=True,released=False)
            time.sleep(.85)
            state=request("touch",x=228,y=206,pressed=False,released=True)
            check("hold food consumes once without modal", state["items"][0]["stack"]==before["items"][0]["stack"]-1 and state["menu"] is None)
            time.sleep(3)
            tap(970,466)
            check("next hotbar row",request("status")["currentItem"]//12==1)
            tap(220,466)
            before=request("status")
            state=tap(900,122)
            def inventory(snapshot):
                return sorted((i["id"],i["stack"],i["quality"]) for i in snapshot["items"] if i)
            check("sort preserves every item stack",inventory(before)==inventory(state))
            for capacity in (12,24,36):
                request("capacity",value=capacity)
                shot("backpack-capacity-%d" % capacity)
                if capacity<36:
                    before=request("status");state=tap(228,174+(capacity//12)*96+32)
                    check("locked row cannot select slot "+str(capacity),state["currentItem"]==before["currentItem"])
            request("prepare_icons")
            for language in ("zh","en"):
                request("language",code=language)
                request("prepare_icons")
                for scale in (.75,1,1.25,1.5):
                    request("scale",ui=scale,zoom=.75)
                    page(0)
                    shot("icons-%s-%s"%(language,scale))
                    state=tap(636,206)
                    check("icon and hit stay aligned %s %s"%(language,scale),state["currentItem"]==6)
                    page(1);shot("crafting-%s-%s"%(language,scale))
                    page(10);shot("calendar-%s-%s"%(language,scale))
            request("language",code="zh");request("reset_ui")
            page(0)
            state=drag(760,46,-180,0)
            check("horizontal tab swipe changes page",state["page"]==1 and state["menu"] is None)
            state=drag(350,46,460,0)
            check("long reverse swipe advances only one page",state["page"]==0)
            page(2)
            state=request("observe_history")
            check("history observes today's actual shipping",len(state["history"])==1 and state["history"][0]["shipped"]==875)
            state=request("observe_history")
            check("history repeated observation does not duplicate day",len(state["history"])==1)
            page(7);state=request("status")
            check("fields group ready thirsty and watered crops",state["cropStatusCounts"]==[2,2,2],state["cropStatusCounts"])
            page(1);state=tap(264,122)
            check("storage category filters real recipes",state["recipeCategory"]==1 and "Chest" in state["recipeKeys"] and "Torch" not in state["recipeKeys"])
            tap(770,122);tap(770,122);tap(770,122);tap(770,122)
            state=tap(398,122)
            check("utility category reachable through native arrows",state["recipeCategory"]==6 and "Torch" in state["recipeKeys"] and "Chest" not in state["recipeKeys"])
            for _ in range(4):tap(46,122)
            tap(130,122)
            state=drag(170,570,0,-200)
            check("recipe list scrolls",state["scroll"]>0,state["scroll"])
            list_offset=state["scroll"]
            state=drag(690,440,0,-110)
            check("right detail scroll does not move recipe list",state["scroll"]==list_offset)
            page(4);tap(150,190)
            before=request("status");state=tap(850,673)
            check("quest reward claimed exactly once",state["money"]==before["money"]+500)
            before=state;state=tap(850,673)
            check("second reward tap cannot duplicate money",state["money"]==before["money"])
            request("long_quest");page(4);tap(150,268)
            before=request("status");state=drag(660,440,0,-140)
            check("long Chinese quest scroll independent of list",state["detailScroll"]>0 and state["scroll"]==before["scroll"])
            shot("journal-long-zh")
            before=request("status");state=tap(850,673)
            check("quest cancel needs second tap",state["quests"]==before["quests"])
            state=tap(850,673)
            check("quest cancel applies inline",state["quests"]==before["quests"]-1 and state["menu"] is None)
            page(3);tap(122,128);tap(122,128)
            before=request("status");state=drag(510,370,-180,-110)
            check("map zoom and pan work",state["mapZoom"]==2 and state["mapCenter"]!=before["mapCenter"],state["mapCenter"])
            for _ in range(12):tap(122,128)
            check("map zoom upper bound",request("status")["mapZoom"]==4)
            for _ in range(12):tap(60,128)
            state=drag(510,370,-400,-200)
            check("map zoom and pan lower bound",state["mapZoom"]==1 and state["mapCenter"]==dict(x=.5,y=.5))
            tap(110,678);shot("map-villager-focus")
            page(9);before=request("status");state=drag(500,550,0,-160)
            check("bundles room list scrolls",state["bundleScroll"]>before["bundleScroll"])
            shot("bundles-scroll")
            page(8);state=drag(500,550,0,-200)
            check("machines list scrolls without category replacement",state["scroll"]>0)
            page(10);state=tap(650,220)
            check("calendar selects fifth day",state["calendarDay"]==5 and "旅行货车" in state["calendarEvents"])
            for cooking in (False,True):
                request("prepare_recipe",cooking=cooking);page(1)
                if cooking:tap(900,122)
                before=request("status");state=tap(880,570)
                if cooking:
                    check("cooking consumes egg and seasoning once",count(state,"(O)176")==count(before,"(O)176")-1 and count(state,"(O)917")==count(before,"(O)917")-1 and count(state,"(O)194",2)==count(before,"(O)194",2)+1)
                else:
                    check("craft consumes wood and sap once",count(state,"(O)388")==count(before,"(O)388")-1 and count(state,"(O)92")==count(before,"(O)92")-2 and count(state,"(O)93")==count(before,"(O)93")+1)
                shot("craft-result-"+str(cooking))
            page(11);state=tap(680,270)
            check("real setting changes preference to 145%",abs(state["preferredUiScale"]-1.45)<.001)
            request("toggle");state=request("status")
            check("shared scale in continuous mode",state["mode"]=="SceneHud" and abs(state["uiScale"]-1.45)<.001)
            request("reset_ui");state=request("pause")
            check("continuous mode retains native Start",state["menu"]=="GameMenu")
            shot("continuous-native-menu")
            request("close");request("settle_scene");time.sleep(.5);request("toggle")
            for _ in range(3):
                state=request("pause")
                check("Start remains suppressed in companion",state["menu"] is None)
            page(0);before=request("status");time.sleep(2);state=request("status")
            check("game update continues",state["ticks"]>before["ticks"])
            request("reset_ui");request("language",code="zh");request("dashboard")
            shot("final-backpack-zh-150")
        finally:
            for remote,name in [(qa.GAME+"/Mods/RGDSplus.VerticalWorld/vertical-world.log","device-plugin.log"),("/tmp/sdv-ui-qa-launch.log","device-launch.log")]:
                try:ftp.get(remote,str(OUT/name))
                except OSError:pass
    ssh.close()


if __name__=="__main__":
    main()
