using System;
using System.Linq;
using System.Text;
using RaidPrototype.Content;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using NVec = RaidPrototype.Vec2;

namespace RaidPrototype
{
    public sealed class Entry : IMod
    {
        private GameObject driver;
        public void Init(ModManifest manifest) { DefaultContent.EnsureRegistered(); }
        public void OnEnable()
        {
            if(driver != null)return;
            ContentBridge.Start();
            driver=new GameObject("RaidPrototype Driver");
            UnityEngine.Object.DontDestroyOnLoad(driver);
            driver.AddComponent<RaidDriver>();
        }
        public void OnDisable() { ContentBridge.Stop();if(driver!=null){driver.SetActive(false);UnityEngine.Object.Destroy(driver);} driver=null; }
    }
    public sealed class RaidDriver : MonoBehaviour
    {
        private GameObject canvasObject, launcher, panel, preparation, resultPanel;
        private MainMenuUIController menu;
        private RaidGraphic field;
        private TMP_FontAsset font;
        private TextMeshProUGUI status, inventory, result, help, heading, pauseNotice, intro;
        private GameObject weaponA, weaponB;
        private GameObject mapPicker;
        private TextMeshProUGUI mapPickerTitle;
        private readonly GameObject[] mapChoices=new GameObject[4];
        private int mapPage;
        private readonly string[] displayedMapIds=new string[4];
        private string mapListSignature;
        private Image weaponIcon, lootIcon;
        private string selectedMapId=DefaultContent.DefaultMapId;
        private WeaponDefinition selectedWeaponA, selectedWeaponB;
        private static string Name(string zh,string en)=>Chinese&&!string.IsNullOrWhiteSpace(zh)?zh:en;
        private RaidModel model;
        private MapDefinition runMap;
        private WeaponDefinition[] runLoadout;
        private bool opened, started, paused, focus=true;
        private EventSystem capturedEventSystem;
        private bool priorNavigation;
        private GameObject priorSelection;
        private float flash;
        private float contentRefreshTimer;
        private float fontRefreshTimer;
        private string fontLocale;
        private bool IsMenu => menu != null && menu.gameObject.activeInHierarchy && menu.gameObject.scene == SceneManager.GetActiveScene();
        private static bool Chinese { get { var c=LocHelper.GetCurrentLocaleCode(); return c!=null&&c.StartsWith("zh",StringComparison.OrdinalIgnoreCase); } }
        private static string L(string zh,string en)=>Chinese?zh:en;
        private void Update()
        {
            try
            {
                contentRefreshTimer-=Mathf.Clamp(Time.unscaledDeltaTime,0,.1f);
                if(contentRefreshTimer<=0){ContentBridge.Refresh();contentRefreshTimer=1f;if(opened && !started && mapPicker!=null && mapPicker.activeSelf && mapListSignature!=string.Join("|",ContentRegistry.Maps.OrderBy(m=>m.Id,StringComparer.Ordinal).Select(m=>m.Id)))DrawMapPicker();}
                var current=MainMenuUIController.Instance;
                bool valid=current!=null&&current.gameObject.activeInHierarchy&&current.gameObject.scene==SceneManager.GetActiveScene();
                if(!valid) { if(canvasObject!=null)Cleanup(); return; }
                if(menu!=current || canvasObject==null)
                {
                    Cleanup();menu=current;font=ResolveFont();
                    if(font==null)return; // Locale fonts may be loaded after the main-menu controller.
                    CreateInterface();
                }
                fontRefreshTimer-=Mathf.Clamp(Time.unscaledDeltaTime,0,.1f);
                if(fontRefreshTimer<=0 || fontLocale!=LocHelper.GetCurrentLocaleCode()){RefreshFont();fontRefreshTimer=1f;}
                if(!opened) return;
                if(Input.GetKeyDown(KeyCode.Escape)) { if(mapPicker!=null && mapPicker.activeSelf)mapPicker.SetActive(false);else if(started && model.State==RaidState.Playing) paused=!paused; else Close(); }
                if(!opened)return;
                if(started && Input.GetKeyDown(KeyCode.B)) paused=!paused;
                if(started && model.State==RaidState.Playing && !paused && focus)
                {
                    if(Input.GetKeyDown(KeyCode.Alpha1))model.SelectWeapon(0);
                    if(Input.GetKeyDown(KeyCode.Alpha2) && model.Loadout.Count>1)model.SelectWeapon(1);
                    float x=(Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0);
                    float y=(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0);
                    float dt=Mathf.Clamp(Time.unscaledDeltaTime,0,.05f);
                    model.Update(dt,new NVec(x,y));
                    if(Input.GetKeyDown(KeyCode.E))model.Interact();
                    if(field.MouseWorld(Input.mousePosition,out var target))
                    {
                        field.Aim=new Vector2(target.X,target.Y);
                        bool trigger=model.CurrentWeapon.Automatic?Input.GetMouseButton(0):Input.GetMouseButtonDown(0);
                        if(trigger && model.Fire(target)) flash=.07f;
                    }
                    flash=Mathf.Max(0,flash-dt);
                }
                DrawStatus();
            }
            catch(Exception ex)
            {
                Debug.LogError("[RaidPrototype] Closed after error; no real items or saves involved: "+ex);
                Cleanup();
                enabled=false;
            }
        }
        private TMP_FontAsset ResolveFont()
        {
            var locale=LocHelper.GetCurrentLocaleCode() ?? "";
            var loader=FontLoader.Instance;
            // The menu logo uses Tektur and cannot display CJK. Never use title.font for body UI.
            if(locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase))return loader!=null?loader.simplifiedChineseFont:null;
            if(locale.StartsWith("ja",StringComparison.OrdinalIgnoreCase))return loader!=null?loader.japaneseFont:null;
            if(locale.StartsWith("ko",StringComparison.OrdinalIgnoreCase))return loader!=null?loader.koreanFont:null;
            if(loader!=null && loader.notoSans!=null)return loader.notoSans;
            if(menu!=null && menu.body!=null && menu.body.font!=null)return menu.body.font;
            if(menu!=null && menu.versionTMP!=null && menu.versionTMP.font!=null)return menu.versionTMP.font;
            return TMP_Settings.defaultFontAsset;
        }
        private void RefreshFont()
        {
            var resolved=ResolveFont();if(resolved==null || canvasObject==null)return;
            font=resolved;fontLocale=LocHelper.GetCurrentLocaleCode();
            foreach(var text in canvasObject.GetComponentsInChildren<TextMeshProUGUI>(true))if(text.font!=font)text.font=font;
        }
        private void OnApplicationFocus(bool value){focus=value;if(!value && opened && started)paused=true;}
        private void OnDisable()=>Cleanup();
        private void OnDestroy()=>Cleanup();
        private void CreateInterface()
        {
            // A separate overlay, available ONLY in the main menu. No gameplay scene interception.
            font=ResolveFont();fontLocale=LocHelper.GetCurrentLocaleCode();
            canvasObject=new GameObject("RaidPrototype Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform,false);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=950;
            var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,800);scaler.matchWidthOrHeight=.5f;
            launcher=Button(canvasObject.transform,L("搜打撤原型（独立沙盒）","Raid Prototype — Sandbox"),Open);
            var lr=launcher.GetComponent<RectTransform>();lr.anchorMin=lr.anchorMax=new Vector2(1,0);lr.pivot=new Vector2(1,0);lr.anchoredPosition=new Vector2(-20,20);lr.sizeDelta=new Vector2(300,48);
            panel=Panel(canvasObject.transform,"Raid",new Color32(15,20,27,255)); Stretch(panel.GetComponent<RectTransform>(),Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            heading=Text(panel.transform,L("废站搜打撤 / 战斗原型","DERELICT RAID / COMBAT PROTOTYPE"),24);Area(heading.rectTransform,0,.91f,.68f,1,20,8);
            var close=Button(panel.transform,L("退出沙盒","Close sandbox"),Close);Area(close.GetComponent<RectTransform>(),.82f,.92f,.985f,.985f);
            var board=new GameObject("Arena",typeof(RectTransform),typeof(CanvasRenderer),typeof(RaidGraphic));board.transform.SetParent(panel.transform,false);
            field=board.GetComponent<RaidGraphic>();field.raycastTarget=true;Area(field.rectTransform,.015f,.12f,.77f,.9f);
            status=Text(panel.transform,"",17);Area(status.rectTransform,.79f,.59f,.985f,.9f);
            inventory=Text(panel.transform,"",15);Area(inventory.rectTransform,.79f,.29f,.985f,.56f);
            var wi=Panel(panel.transform,"WeaponIcon",Color.white);weaponIcon=wi.GetComponent<Image>();weaponIcon.raycastTarget=false;weaponIcon.preserveAspect=true;Area(weaponIcon.rectTransform,.94f,.83f,.98f,.9f);weaponIcon.enabled=false;
            var li=Panel(panel.transform,"LootIcon",Color.white);lootIcon=li.GetComponent<Image>();lootIcon.raycastTarget=false;lootIcon.preserveAspect=true;Area(lootIcon.rectTransform,.94f,.58f,.98f,.65f);lootIcon.enabled=false;
            help=Text(panel.transform,L("WASD 移动 · 鼠标瞄准 · 左键射击 · E 开箱 · 1/2 切枪 · B/Esc 暂停\n撤离区域：停留至本图倒计时结束。所有物品仅本局有效，不读取或写入真实存档。","WASD move · Mouse aim · LMB shoot · E loot · 1/2 switch · B/Esc pause\nExtraction zone: wait for this map's countdown. All loot is temporary. No save or real inventory access."),16);Area(help.rectTransform,.02f,.015f,.98f,.115f);
            var pause=Button(panel.transform,L("暂停 / 背包","Pause / bag"),()=>{if(started && model.State==RaidState.Playing)paused=!paused;});Area(pause.GetComponent<RectTransform>(),.79f,.19f,.985f,.26f);
            pauseNotice=Text(panel.transform,"",30);Area(pauseNotice.rectTransform,.2f,.42f,.68f,.58f);pauseNotice.alignment=TextAlignmentOptions.Center;
            preparation=Panel(panel.transform,"Preparation",new Color32(20,29,38,250));Area(preparation.GetComponent<RectTransform>(),.13f,.2f,.73f,.82f);
            intro=Text(preparation.transform,"",18);Area(intro.rectTransform,.05f,.38f,.95f,.95f);
            var nextMap=Button(preparation.transform,L("选择地图 / 内容包","Choose map / content pack"),ShowMapPicker);Area(nextMap.GetComponent<RectTransform>(),.06f,.29f,.94f,.38f);
            weaponA=Button(preparation.transform,"Weapon 1",()=>StartRaidByIndex(0));Area(weaponA.GetComponent<RectTransform>(),.06f,.16f,.94f,.26f);
            weaponB=Button(preparation.transform,"Weapon 2",()=>StartRaidByIndex(1));Area(weaponB.GetComponent<RectTransform>(),.06f,.03f,.94f,.13f);
            resultPanel=Panel(panel.transform,"Result",new Color32(20,29,38,250));Area(resultPanel.GetComponent<RectTransform>(),.16f,.24f,.72f,.77f);
            result=Text(resultPanel.transform,"",24);Area(result.rectTransform,.06f,.35f,.94f,.94f);
            var again=Button(resultPanel.transform,L("返回准备界面","Back to preparation"),Prepare);Area(again.GetComponent<RectTransform>(),.06f,.07f,.94f,.25f);
            mapPicker=Panel(panel.transform,"MapPicker",new Color32(20,29,38,255));Area(mapPicker.GetComponent<RectTransform>(),.1f,.18f,.76f,.87f);
            mapPickerTitle=Text(mapPicker.transform,"",20);Area(mapPickerTitle.rectTransform,.05f,.83f,.95f,.98f);
            for(int i=0;i<mapChoices.Length;i++){int slot=i;mapChoices[i]=Button(mapPicker.transform,"Map",()=>SelectMapSlot(slot));Area(mapChoices[i].GetComponent<RectTransform>(),.05f,.64f-i*.145f,.95f,.77f-i*.145f);}
            var prev=Button(mapPicker.transform,L("上一页","Previous page"),()=>MoveMapPage(-1));Area(prev.GetComponent<RectTransform>(),.05f,.02f,.33f,.14f);
            var next=Button(mapPicker.transform,L("下一页","Next page"),()=>MoveMapPage(1));Area(next.GetComponent<RectTransform>(),.36f,.02f,.64f,.14f);
            var back=Button(mapPicker.transform,L("返回准备","Back to preparation"),()=>mapPicker.SetActive(false));Area(back.GetComponent<RectTransform>(),.67f,.02f,.95f,.14f);
            mapPicker.SetActive(false);resultPanel.SetActive(false);panel.SetActive(false);
            Debug.Log("[RaidPrototype] Main-menu sandbox entry created. No game/save APIs used.");
        }
        private void Open()
        {
            if(!IsMenu || EventSystem.current==null || field==null || panel==null || opened)return;
            // Do not call NewGame, LoadGame, travel, Time.timeScale or any store/inventory API.
            capturedEventSystem=EventSystem.current;
            priorNavigation=capturedEventSystem.sendNavigationEvents;
            priorSelection=capturedEventSystem.currentSelectedGameObject;
            capturedEventSystem.sendNavigationEvents=false;
            opened=true;launcher.SetActive(false);panel.SetActive(true);Prepare();capturedEventSystem.SetSelectedGameObject(null);
        }
        private void Prepare()
        {
            if(!opened || !IsMenu || field==null)return;
            try{model=new RaidModel(selectedMapId);}catch(System.Collections.Generic.KeyNotFoundException){selectedMapId=DefaultContent.DefaultMapId;DefaultContent.EnsureRegistered();model=new RaidModel(selectedMapId);}
            field.Model=model;started=false;paused=false;flash=0;
            runMap=model.Map;runLoadout=model.Loadout.ToArray();var map=runMap;var loadout=runLoadout;
            selectedWeaponA=loadout[0];selectedWeaponB=loadout.Length>1?loadout[1]:null;
            var available=ContentRegistry.Maps.OrderBy(m=>m.Id,StringComparer.Ordinal).ToArray();int number=Array.FindIndex(available,m=>m.Id==selectedMapId)+1;
            intro.text=L("已加载地图 ","Loaded maps: ")+available.Length+"  ·  "+number+" / "+available.Length+"\n"+Name(map.NameZh,map.NameEn)+"\n"+map.Id+"\n\n"+map.Width+" × "+map.Height+L(" 场地 · "," arena · ")+model.Enemies.Count+L(" 名敌人 · "," enemies · ")+model.Boxes.Count+L(" 个箱子"," crates")+"\n"+(map.HasExtraction?L("到达绿色区域并停留完成撤离。","Reach the green zone and wait to extract."):L("战斗沙盒地图，没有撤离目标。","Combat sandbox — no extraction objective."))+"\n\n"+L("点击下方选择地图，再选择起始武器。\n仅内存模拟，1/2切换本图武器。","Choose a map below, then a starting weapon.\nMemory-only run; 1/2 switches weapons.");
            weaponA.GetComponentInChildren<TextMeshProUGUI>(true).text=Name(selectedWeaponA.NameZh,selectedWeaponA.NameEn);
            weaponB.SetActive(selectedWeaponB!=null);if(selectedWeaponB!=null)weaponB.GetComponentInChildren<TextMeshProUGUI>(true).text=Name(selectedWeaponB.NameZh,selectedWeaponB.NameEn);
            ApplyTheme();mapPicker.SetActive(false);preparation.SetActive(true);resultPanel.SetActive(false);DrawStatus();
        }
        private void StartRaid(Weapon weapon)=>StartRaidByIndex(weapon==Weapon.Rifle?1:0);
        private void StartRaidByIndex(int index)
        {
            if(!opened || !IsMenu || field==null)return;
            if(!ContentRegistry.Maps.Any(m=>m.Id==selectedMapId)){Prepare();return;}
            model=new RaidModel(selectedMapId);model.SelectWeapon(index);field.Model=model;runMap=model.Map;runLoadout=model.Loadout.ToArray();
            started=true;paused=false;flash=0;preparation.SetActive(false);resultPanel.SetActive(false);EventSystem.current?.SetSelectedGameObject(null);DrawStatus();
        }
        private void ShowMapPicker()
        {
            if(!opened || started || !IsMenu || mapPicker==null)return;
            ContentBridge.Refresh();mapPage=0;mapPicker.SetActive(true);DrawMapPicker();
            Debug.Log("[RaidPrototype] Map picker opened: "+string.Join(", ",ContentRegistry.Maps.Select(m=>m.Id)));
            EventSystem.current?.SetSelectedGameObject(null);
        }
        private void DrawMapPicker()
        {
            var maps=ContentRegistry.Maps.OrderBy(m=>m.Id,StringComparer.Ordinal).ToArray();
            int pages=Math.Max(1,(maps.Length+3)/4);mapPage=Math.Max(0,Math.Min(mapPage,pages-1));
            mapPickerTitle.text=L("选择地图 · 已加载 ","Choose map · Loaded ")+maps.Length+L(" 张地图"," map(s)")+"  ·  "+(mapPage+1)+" / "+pages+"\n"+string.Join("; ",ContentRegistry.Packs.Select(p=>p.Id+": "+p.Maps.Length+L("图"," map(s)")));
            for(int i=0;i<mapChoices.Length;i++)
            {
                int index=mapPage*4+i;bool exists=index<maps.Length;mapChoices[i].SetActive(exists);displayedMapIds[i]=exists?maps[index].Id:null;
                if(exists)mapChoices[i].GetComponentInChildren<TextMeshProUGUI>(true).text=Name(maps[index].NameZh,maps[index].NameEn)+"\n"+maps[index].Id;
            }
            mapListSignature=string.Join("|",maps.Select(m=>m.Id));
        }
        private void MoveMapPage(int direction)
        {
            if(!opened || started || mapPicker==null || !mapPicker.activeSelf)return;
            mapPage+=Math.Sign(direction);DrawMapPicker();
        }
        private void SelectMapSlot(int slot)
        {
            if(slot<0 || slot>=displayedMapIds.Length || displayedMapIds[slot]==null)return;
            SelectMap(displayedMapIds[slot]);
        }
        private void SelectMap(string id)
        {
            if(!opened || started || !IsMenu)return;
            selectedMapId=ContentRegistry.Maps.Any(m=>m.Id==id)?id:DefaultContent.DefaultMapId;
            if(mapPicker!=null)mapPicker.SetActive(false);
            Prepare();Debug.Log("[RaidPrototype] Selected map: "+selectedMapId);
            EventSystem.current?.SetSelectedGameObject(null);
        }
        private void CycleMap()
        {
            if(started || !opened)return;
            var maps=ContentRegistry.Maps.ToArray();if(maps.Length==0)return;
            int index=Array.FindIndex(maps,m=>m.Id==selectedMapId);selectedMapId=maps[(index+1)%maps.Length].Id;Prepare();
        }
        private void ApplyTheme()
        {
            var skin=model.Skin;
            foreach(var image in panel.GetComponentsInChildren<Image>(true))
                if(image.GetComponent<RaidSpriteView>()==null && image.gameObject.name!="RaidBackground")image.color=VisualBridge.Color(image.GetComponent<Button>()!=null?skin.UIButton:skin.UIBackground);
            // A skin may use translucent panels, but the sandbox root must fully cover the host menu.
            var rootColor=VisualBridge.Color(skin.UIBackground);rootColor.a=255;panel.GetComponent<Image>().color=rootColor;
            foreach(var text in panel.GetComponentsInChildren<TextMeshProUGUI>(true))text.color=VisualBridge.Color(skin.UIText);
            heading.text=L("俯视射击框架 / ","TOP-DOWN FRAMEWORK / ")+Name(model.Map.NameZh,model.Map.NameEn);
        }
        private void ReleaseInput()
        {
            if(capturedEventSystem!=null)
            {
                capturedEventSystem.SetSelectedGameObject(null);
                capturedEventSystem.sendNavigationEvents=priorNavigation;
                if(priorSelection!=null && priorSelection.activeInHierarchy)capturedEventSystem.SetSelectedGameObject(priorSelection);
            }
            capturedEventSystem=null;priorSelection=null;
        }
        private void Close()
        {
            ReleaseInput();
            opened=false;started=false;paused=false;model=null;runMap=null;runLoadout=null;selectedWeaponA=null;selectedWeaponB=null;
            if(field!=null){field.Model=null;field.Refresh();}if(panel!=null)panel.SetActive(false);if(launcher!=null)launcher.SetActive(IsMenu);
        }
        private void DrawStatus()
        {
            if(model==null)return;
            var weapon=model.CurrentWeapon;var map=runMap ?? model.Map;
            weaponIcon.sprite=VisualBridge.Find(weapon.SpriteKey);weaponIcon.enabled=weaponIcon.sprite!=null;weaponIcon.color=Color.white;
            var lootEntries=model.Inventory;var firstItem=lootEntries.Count>0?model.GetItem(lootEntries[0].ItemId):null;
            string itemKey=firstItem?.SpriteKey;if(string.IsNullOrEmpty(itemKey))itemKey=model.Skin.ItemKey;
            lootIcon.sprite=VisualBridge.Find(itemKey);lootIcon.enabled=lootIcon.sprite!=null && firstItem!=null;lootIcon.color=Color.white;
            status.text=L("生命 ","HP ")+Mathf.CeilToInt(model.PlayerHealth)+" / "+RaidModel.PlayerMaxHealth+"\n\n"+Name(weapon.NameZh,weapon.NameEn)+"\n"+L("虚拟武器 · 无限弹药","Virtual · Infinite ammo")+"\n\n"+L("存活敌人 ","Enemies ")+Alive()+"\n"+(map.HasExtraction?L("撤离 ","Extract ")+model.ExtractionProgress.ToString("0.0")+" / "+map.ExtractionSeconds+" s":L("无撤离目标","No extraction objective"));
            var bag=new StringBuilder(L("本局虚拟背包\n\n","TEMPORARY BAG\n\n"));
            foreach(var loot in model.Inventory){var item=model.GetItem(loot.ItemId);bag.Append(Name(item.NameZh,item.NameEn)).Append(" × ").Append(loot.Count).Append('\n');}
            if(model.LootCount==0)bag.Append(L("空\n","Empty\n"));
            bag.Append('\n').Append(L("内容包物资，不进入当铺库存。","Content-pack loot; never added to the shop."));inventory.text=bag.ToString();
            pauseNotice.text=started&&paused&&model.State==RaidState.Playing?L("已暂停\n按 B 或 Esc 继续","PAUSED\nB / Esc to resume"):"";
            if(started&&model.State!=RaidState.Playing)
            {
                resultPanel.SetActive(true);
                result.text=(model.State==RaidState.Extracted?L("撤离成功","EXTRACTED"):L("行动失败","RAID FAILED"))+"\n\n"+L("搜获：","Found: ")+model.LootCount+L(" 件虚拟物资"," virtual item(s)")+"\n\n"+L("没有真实收益或装备损失。\n返回准备后清空本局数据。","No real rewards or equipment loss.\nReturning clears this run.");
            }
            field.ShotFlash=flash;field.Refresh();
        }
        private int Alive(){int n=0;foreach(var e in model.Enemies)if(e.Alive)n++;return n;}
        private void Cleanup()
        {
            if(canvasObject!=null)canvasObject.SetActive(false);
            ReleaseInput();
            opened=false;started=false;paused=false;flash=0;model=null;field=null;menu=null;
            if(canvasObject!=null)UnityEngine.Object.Destroy(canvasObject);canvasObject=null;launcher=null;panel=null;
        }
        private static GameObject Panel(Transform parent,string name,Color32 color)
        {var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);go.GetComponent<Image>().color=color;return go;}
        private TextMeshProUGUI Text(Transform parent,string value,float size)
        {var go=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);var t=go.GetComponent<TextMeshProUGUI>();t.font=font;t.text=value;t.fontSize=size;t.color=new Color32(218,227,233,255);t.raycastTarget=false;t.richText=false;t.enableWordWrapping=true;return t;}
        private GameObject Button(Transform parent,string value,UnityEngine.Events.UnityAction action)
        {var go=Panel(parent,value,new Color32(49,75,91,255));var b=go.AddComponent<Button>();b.targetGraphic=go.GetComponent<Image>();var nav=b.navigation;nav.mode=Navigation.Mode.None;b.navigation=nav;b.onClick.AddListener(action);var text=Text(go.transform,value,18);text.alignment=TextAlignmentOptions.Center;Stretch(text.rectTransform,Vector2.zero,Vector2.one,new Vector2(6,3),new Vector2(-6,-3));return go;}
        private static void Stretch(RectTransform r,Vector2 min,Vector2 max,Vector2 offsetMin,Vector2 offsetMax){r.anchorMin=min;r.anchorMax=max;r.offsetMin=offsetMin;r.offsetMax=offsetMax;}
        private static void Area(RectTransform r,float x0,float y0,float x1,float y1,float padX=0,float padY=0)=>Stretch(r,new Vector2(x0,y0),new Vector2(x1,y1),new Vector2(padX,padY),new Vector2(-padX,-padY));
    }
}
