using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class DarkRoomAccessibleItem
{
    public string Key;
    public string Label;
    public string Value;
    public bool IsHeader;
    public bool IsButton;
    public bool IsDisabled;
}

public sealed class DarkRoomApp : MonoBehaviour
{
    public enum Page
    {
        Menu,
        Room,
        Outside,
        Workers,
        WorkerDetail,
        WorkshopMenu,
        ContentList,
        StoryEvent,
        ExpeditionPrep,
        LoadoutDetail,
        World,
        Landmark,
        Ship,
        LiftoffConfirm,
        Space,
        Ending,
        About
    }

    private const string SaveKey = "TextAdventureHouse.ADarkRoom.Save.v1";
    private const string LegacySaveKey = "ADarkRoomResearch.Save.v1";
    private static readonly Color Background = new Color32(18, 18, 18, 255);
    private static readonly Color Panel = new Color32(38, 38, 38, 255);
    private static readonly Color Foreground = new Color32(245, 242, 232, 255);
    private static readonly Color Muted = new Color32(207, 203, 190, 255);
    private static readonly Color ButtonBackground = new Color32(76, 70, 58, 255);
    private static readonly Color DisabledBackground = new Color32(56, 56, 56, 255);

    private readonly Dictionary<string, RectTransform> itemFrames = new Dictionary<string, RectTransform>();
    private readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
    private readonly List<string> buttonOrder = new List<string>();
    private RectTransform content;
    private Font font;
    private AudioSource musicSource;
    private AudioSource soundSource;
    private DarkRoomGameState state;
    private string pendingAnnouncement = string.Empty;
    private string actionFocusKey = string.Empty;
    private DarkRoomContentCategory contentCategory;
    private int contentPage;
    private string selectedWorkerKey = string.Empty;
    private Page eventReturnPage = Page.Room;
    private string eventReturnFocus = string.Empty;
    private string selectedLoadoutKey = string.Empty;
    private int loadoutPage;
    private Action returnToGameMenu;
    private IReadOnlyList<DarkRoomAccessibleItem> presentedItems = Array.Empty<DarkRoomAccessibleItem>();

    public static DarkRoomApp Instance { get; private set; }
    public static bool HasSavedGame => PlayerPrefs.HasKey(SaveKey) || PlayerPrefs.HasKey(LegacySaveKey);
    public Page CurrentPage { get; private set; } = Page.Menu;
    public DarkRoomGameState State => state;
    public string LastPlayedSoundKey { get; private set; } = string.Empty;

    public event Action<string, string, bool> AccessibilityRefreshRequested;

    public static void Launch(Action returnToMenu)
    {
        if (Instance != null)
        {
            return;
        }

        var root = new GameObject(nameof(DarkRoomApp));
        DontDestroyOnLoad(root);
        Instance = root.AddComponent<DarkRoomApp>();
        Instance.returnToGameMenu = returnToMenu;
        root.AddComponent<DarkRoomAccessibility>();
#if UNITY_EDITOR
        root.AddComponent<DarkRoomAccessibilityPreviewBridge>();
#endif
    }

    public static void DeleteSavedGame()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.DeleteKey(LegacySaveKey);
        PlayerPrefs.Save();
    }

    private void Awake()
    {
        Instance = this;
        Application.targetFrameRate = 60;
        font = CreateReadableFont();
        CreateAudio();
        state = LoadState();
        state.Changed += OnStateChanged;
        state.AnnouncementRequested += message => pendingAnnouncement = message;
        state.AudioRequested += PlaySound;
        BuildCanvas();
    }

    private void Start()
    {
        if (state.GameCompleted) ShowEnding();
        else if (state.InSpace) ShowSpace();
        else if (state.ExpeditionActive) ShowWorld();
        else ShowRoom();
    }

    private void Update()
    {
        state.Advance(Time.unscaledDeltaTime);
        if (Input.GetKeyDown(KeyCode.Space)) HandleKeyboardCommand(KeyCode.Space);
        if (Input.GetKeyDown(KeyCode.Escape)) HandleKeyboardCommand(KeyCode.Escape);
        if (Input.GetKeyDown(KeyCode.Home)) HandleKeyboardCommand(KeyCode.Home);
        if (Input.GetKeyDown(KeyCode.End)) HandleKeyboardCommand(KeyCode.End);
        if (Input.GetKeyDown(KeyCode.F5)) HandleKeyboardCommand(KeyCode.F5);
    }

    public bool HandleKeyboardCommand(KeyCode keyCode)
    {
        if (keyCode == KeyCode.Space && EventSystem.current != null)
        {
            Button selected = EventSystem.current.currentSelectedGameObject == null
                ? null
                : EventSystem.current.currentSelectedGameObject.GetComponent<Button>();
            if (selected != null && selected.interactable)
            {
                selected.onClick.Invoke();
                return true;
            }
            return false;
        }
        if (keyCode == KeyCode.Home || keyCode == KeyCode.End)
        {
            Button target = (keyCode == KeyCode.Home ? buttonOrder : buttonOrder.AsEnumerable().Reverse())
                .Select(key => buttons[key]).FirstOrDefault(button => button.interactable);
            if (target == null || EventSystem.current == null) return false;
            EventSystem.current.SetSelectedGameObject(target.gameObject);
            return true;
        }
        if (keyCode == KeyCode.F5)
        {
            string focus = ResolveFocusKey(GetSelectedKey());
            BuildCurrentPage(focus);
            AccessibilityRefreshRequested?.Invoke(focus, "页面已刷新。", true);
            return true;
        }
        if (keyCode == KeyCode.Escape) return GoBack();
        return false;
    }

    private bool GoBack()
    {
        switch (CurrentPage)
        {
            case Page.About: return Activate("back-menu");
            case Page.Room: return Activate("save-progress");
            case Page.Outside: return Activate("back-room");
            case Page.Workers: return Activate("back-outside");
            case Page.WorkerDetail: return Activate("back-workers");
            case Page.WorkshopMenu: return Activate("back-room");
            case Page.ContentList: return Activate("back-workshop");
            case Page.ExpeditionPrep: return Activate("back-room");
            case Page.LoadoutDetail: return Activate("back-loadout");
            case Page.Ship: return Activate("back-room");
            case Page.LiftoffConfirm: return Activate("cancel-liftoff");
            case Page.Ending: return Activate("save-progress");
            case Page.World:
                return state.GetWorldView()?.AtVillage == true && Activate("finish-expedition");
            case Page.Landmark:
                return state.GetLandmarkView()?.Actions.Any(action => action.Key == "leave" && action.Enabled) == true &&
                    Activate("landmark-leave");
            case Page.StoryEvent:
                DarkRoomEventChoice choice = state.GetCurrentEvent()?.Choices.LastOrDefault(item => item.Enabled);
                return choice != null && Activate($"event-choice-{choice.Key}");
            default:
                return false;
        }
    }

    private void OnDestroy()
    {
        if (state != null)
        {
            state.Changed -= OnStateChanged;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && state != null)
        {
            SaveState();
        }
    }

    public IReadOnlyList<DarkRoomAccessibleItem> GetAccessibleItems()
    {
        return presentedItems;
    }

    private IReadOnlyList<DarkRoomAccessibleItem> BuildAccessibleItems()
    {
        var items = new List<DarkRoomAccessibleItem>();
        if (CurrentPage == Page.Menu)
        {
            items.Add(Header("menu-title", "暗黑房间"));
            items.Add(TextItem("menu-summary", "内部研究说明", "公益、内部无障碍研究，不对外发布。"));
            items.Add(ButtonItem("start", "开始游戏"));
            items.Add(ButtonItem("about", "关于与许可"));
            return items;
        }

        if (CurrentPage == Page.About)
        {
            items.Add(Header("about-title", "关于与许可"));
            items.Add(TextItem("about-project", "项目性质", "公益、内部无障碍研究；非官方版本；禁止对外分发。"));
            items.Add(TextItem("about-source", "原作来源", "doublespeakgames/adarkroom，标签1.4，提交7ee96fd。"));
            items.Add(TextItem("about-license", "许可证", "Mozilla Public License 2.0。完整许可证和上游源码随工程保存。"));
            items.Add(ButtonItem("back-menu", "返回主菜单"));
            return items;
        }

        if (CurrentPage == Page.StoryEvent)
        {
            DarkRoomEventView storyEvent = state.GetCurrentEvent();
            if (storyEvent == null)
            {
                items.Add(Header("event-title", "事件"));
                return items;
            }
            items.Add(Header("event-title", storyEvent.Title));
            items.Add(TextItem("event-description", "场景", storyEvent.Text));
            foreach (DarkRoomEventChoice choice in storyEvent.Choices)
            {
                items.Add(ButtonItem($"event-choice-{choice.Key}", choice.Label,
                    !choice.Enabled, choice.CostText));
            }
            return items;
        }

        if (CurrentPage == Page.ExpeditionPrep)
        {
            IReadOnlyList<DarkRoomLoadoutItem> loadout = state.GetLoadoutItems();
            int pageCount = Math.Max(1, (loadout.Count + 4) / 5);
            loadoutPage = Math.Max(0, Math.Min(loadoutPage, pageCount - 1));
            items.Add(Header("path-title", "尘封小径"));
            items.Add(TextItem("path-capacity", "行囊",
                $"已用{state.UsedLoadoutSpace:0.#}/{state.CarryingCapacity}；空余{state.FreeLoadoutSpace:0.#}。"));
            items.Add(TextItem("path-protection", "远征状态",
                $"护甲{state.ArmourText}；生命上限{state.MaximumHealth}；水上限{state.MaximumWater}。{state.GetShipCompassText()}"));
            foreach (DarkRoomLoadoutItem item in loadout.Skip(loadoutPage * 5).Take(5))
            {
                items.Add(ButtonItem($"loadout-open-{item.Key}", item.Label, false,
                    $"装载{item.Loaded}/{item.Available}，重量{item.Weight:0.#}"));
            }
            if (loadoutPage > 0) items.Add(ButtonItem("loadout-previous", "上一页"));
            if (loadoutPage + 1 < pageCount) items.Add(ButtonItem("loadout-next", "下一页"));
            items.Add(ButtonItem("embark", "出发", !state.CanEmbark,
                state.CanEmbark ? string.Empty : "至少装入一份熏肉"));
            items.Add(ButtonItem("back-room", "返回房间"));
            return items;
        }

        if (CurrentPage == Page.LoadoutDetail)
        {
            DarkRoomLoadoutItem item = state.GetLoadoutItems()
                .FirstOrDefault(entry => entry.Key == selectedLoadoutKey);
            if (item == null)
            {
                items.Add(Header("loadout-title", "装载补给"));
                items.Add(ButtonItem("back-loadout", "返回物品列表"));
                return items;
            }
            items.Add(Header("loadout-title", item.Label));
            items.Add(TextItem("loadout-status", "装载状态",
                $"已装载{item.Loaded}；库存{item.Available}；每份重量{item.Weight:0.#}；行囊空余{state.FreeLoadoutSpace:0.#}。"));
            items.Add(ButtonItem("loadout-add-one", "装入一份", !item.CanAdd));
            items.Add(ButtonItem("loadout-add-ten", "装入十份", !item.CanAdd));
            items.Add(ButtonItem("loadout-remove-one", "取出一份", !item.CanRemove));
            items.Add(ButtonItem("loadout-remove-ten", "取出十份", !item.CanRemove));
            items.Add(ButtonItem("back-loadout", "返回物品列表"));
            return items;
        }

        if (CurrentPage == Page.World)
        {
            DarkRoomWorldView world = state.GetWorldView();
            items.Add(Header("world-title", "荒芜世界"));
            if (world == null) return items;
            items.Add(TextItem("world-scene", world.Terrain, world.Scene));
            items.Add(TextItem("world-position", "位置", world.Position));
            items.Add(TextItem("world-compass", "罗盘", world.Compass));
            items.Add(TextItem("world-status", "远征状态",
                $"生命{state.ExpeditionHealth}/{state.MaximumHealth}；水{state.ExpeditionWater}/{state.MaximumWater}；行囊{state.GetExpeditionInventoryText()}。"));
            items.Add(TextItem("world-places", "已发现地点", world.KnownPlaces));
            foreach (DarkRoomWorldDirection direction in world.Directions)
            {
                items.Add(ButtonItem($"world-{direction.Key}", direction.Label,
                    !direction.Enabled, direction.Destination));
            }
            if (world.AtLandmark) items.Add(ButtonItem("inspect-landmark", "调查地点"));
            if (world.AtVillage) items.Add(ButtonItem("finish-expedition", "结束远征"));
            return items;
        }

        if (CurrentPage == Page.Landmark)
        {
            DarkRoomLandmarkView landmark = state.GetLandmarkView();
            items.Add(Header("landmark-title", landmark?.Title ?? "地点"));
            if (landmark == null) return items;
            items.Add(TextItem("landmark-scene", "场景", landmark.Text));
            if (!string.IsNullOrWhiteSpace(landmark.Status))
                items.Add(TextItem("landmark-status", "战斗状态", landmark.Status));
            foreach (DarkRoomLandmarkAction action in landmark.Actions)
            {
                items.Add(ButtonItem($"landmark-{action.Key}", action.Label,
                    !action.Enabled, action.Value));
            }
            return items;
        }

        if (CurrentPage == Page.Ship)
        {
            items.Add(Header("ship-title", "老旧飞船"));
            items.Add(TextItem("ship-scene", "场景",
                "飞船半埋在村落外的尘土中。弯曲船体已经破裂，引擎仍保留着微弱反应。"));
            items.Add(TextItem("ship-status", "飞船状态",
                $"船体{state.ShipHull}；引擎{state.ShipThrusters}；外星合金{FormatStore(state.AlienAlloy)}。"));
            items.Add(ButtonItem("reinforce-hull", "加固船体", state.AlienAlloy < 1f));
            items.Add(ButtonItem("upgrade-engine", "升级引擎", state.AlienAlloy < 1f));
            items.Add(ButtonItem("liftoff", "起飞", !state.CanLiftOff));
            items.Add(ButtonItem("back-room", "返回房间"));
            return items;
        }

        if (CurrentPage == Page.LiftoffConfirm)
        {
            items.Add(Header("liftoff-title", "准备离开"));
            items.Add(TextItem("liftoff-warning", "确认",
                "是时候离开这个地方了。起飞后若船体在碎片云中耗尽，飞船会坠回地面。"));
            items.Add(ButtonItem("confirm-liftoff", "确认起飞"));
            items.Add(ButtonItem("cancel-liftoff", "继续停留"));
            return items;
        }

        if (CurrentPage == Page.Space)
        {
            DarkRoomSpaceView space = state.GetSpaceView();
            items.Add(Header("space-title", space?.Layer ?? "太空"));
            if (space == null) return items;
            items.Add(TextItem("space-scene", "场景", space.Scene));
            items.Add(TextItem("space-status", "飞船状态",
                $"高度{state.SpaceAltitude}/60；船体{state.SpaceHull}/{state.ShipHull}。"));
            items.Add(TextItem("space-hazard", "来袭碎片", space.Hazard));
            items.Add(ButtonItem("space-left", "转向左侧航道"));
            items.Add(ButtonItem("space-centre", "保持中央航道"));
            items.Add(ButtonItem("space-right", "转向右侧航道"));
            return items;
        }

        if (CurrentPage == Page.Ending)
        {
            items.Add(Header("ending-title", "离开荒芜世界"));
            items.Add(TextItem("ending-scene", "终局",
                "飞船冲出碎片云。荒芜世界逐渐缩成身后的微光，漫游者舰队悬在群星之间。漫长的滞留终于结束。"));
            items.Add(TextItem("ending-status", "主线完成", "已经从黑暗房间走到群星之间。"));
            items.Add(ButtonItem("save-progress", "保存进度"));
            return items;
        }

        if (CurrentPage == Page.Outside)
        {
            items.Add(Header("outside-title", state.VillageTitle));
            items.Add(TextItem("outside-stores", "资源",
                $"木头{state.Wood}；毛皮{FormatStore(state.Fur)}；肉{FormatStore(state.Meat)}；诱饵{FormatStore(state.Bait)}。"));
            items.Add(TextItem("outside-village", "聚落",
                $"人口{state.Population}/{state.MaxPopulation}；陷阱{state.TrapCount}；推车{state.CartCount}；小屋{state.HutCount}；猎人小屋{state.LodgeCount}。"));
            items.Add(TextItem("outside-message", "最新消息", state.LastMessage));
            items.Add(ButtonItem("gather-wood", "收集木头", !state.CanGatherWood,
                $"每次获得{state.GatherAmount}根"));
            if (state.TrapCount > 0)
            {
                items.Add(ButtonItem("check-traps", "检查陷阱", !state.CanCheckTraps));
            }
            if (state.CanManageWorkers)
            {
                items.Add(ButtonItem("manage-workers", "分配工人"));
            }
            items.Add(ButtonItem("back-room", "返回房间"));
            return items;
        }

        if (CurrentPage == Page.Workers)
        {
            items.Add(Header("workers-title", "工人分配"));
            items.Add(TextItem("workers-status", "当前人数",
                $"采集者{state.Gatherers}；总人口{state.Population}。采集者每十秒每人获得一根木头。"));
            foreach (DarkRoomWorkerRole role in state.GetWorkerRoles())
            {
                items.Add(ButtonItem($"worker-open-{role.Key}", role.Label, false,
                    $"当前{role.Count}人"));
            }
            items.Add(ButtonItem("back-outside", "返回聚落"));
            return items;
        }

        if (CurrentPage == Page.WorkerDetail)
        {
            DarkRoomWorkerRole role = state.GetWorkerRoles()
                .FirstOrDefault(item => item.Key == selectedWorkerKey);
            if (role == null)
            {
                items.Add(Header("worker-title", "工人分配"));
                items.Add(ButtonItem("back-workers", "返回工种列表"));
                return items;
            }
            items.Add(Header("worker-title", role.Label));
            items.Add(TextItem("worker-status", "当前人数",
                $"{role.Label}{role.Count}人；采集者{state.Gatherers}人。"));
            items.Add(TextItem("worker-income", "生产", role.IncomeText));
            items.Add(ButtonItem("worker-add-one", "分配一人", !role.CanAssign));
            items.Add(ButtonItem("worker-add-ten", "分配十人", !role.CanAssign));
            items.Add(ButtonItem("worker-remove-one", "撤回一人", !role.CanRemove));
            items.Add(ButtonItem("worker-remove-ten", "撤回十人", !role.CanRemove));
            items.Add(ButtonItem("back-workers", "返回工种列表"));
            return items;
        }

        if (CurrentPage == Page.WorkshopMenu)
        {
            items.Add(Header("workshop-title", "建设、制作与交易"));
            items.Add(TextItem("workshop-stores", "库存", state.GetAllStoresText()));
            items.Add(TextItem("workshop-message", "最新消息", state.LastMessage));
            items.Add(ButtonItem("open-build", "建造设施"));
            if (state.CanCraft)
            {
                items.Add(ButtonItem("open-craft", "制作装备"));
            }
            if (state.CanTrade)
            {
                items.Add(ButtonItem("open-trade", "进行交易"));
            }
            items.Add(ButtonItem("back-room", "返回房间"));
            return items;
        }

        if (CurrentPage == Page.ContentList)
        {
            IReadOnlyList<DarkRoomContentAction> actions = state.GetContentActions(contentCategory);
            int pageCount = Math.Max(1, (actions.Count + 4) / 5);
            contentPage = Math.Max(0, Math.Min(contentPage, pageCount - 1));
            string title = contentCategory switch
            {
                DarkRoomContentCategory.Build => "建造设施",
                DarkRoomContentCategory.Craft => "制作装备",
                _ => "交易货物"
            };
            items.Add(Header("content-title", title));
            items.Add(TextItem("content-stores", "库存", state.GetAllStoresText()));
            foreach (DarkRoomContentAction action in actions.Skip(contentPage * 5).Take(5))
            {
                items.Add(ButtonItem(action.Key, action.Label, !action.Enabled, action.CostText));
            }
            if (contentPage > 0)
            {
                items.Add(ButtonItem("content-previous", "上一页"));
            }
            if (contentPage + 1 < pageCount)
            {
                items.Add(ButtonItem("content-next", "下一页"));
            }
            items.Add(ButtonItem("back-workshop", "返回分类"));
            return items;
        }

        items.Add(Header("room-title", state.FireLevel < 2 ? "黑暗房间" : "火光映照的房间"));
        string wood = state.WoodUnlocked ? $"{state.Wood}根" : "尚未发现";
        items.Add(TextItem("room-status", "当前状态",
            $"房间{state.TemperatureText}；火堆{state.FireText}；木头{wood}；陌生人{state.BuilderText}。"));
        items.Add(TextItem("room-message", "最新消息", state.LastMessage));
        if (state.FireLevel == 0)
        {
            items.Add(ButtonItem("light-fire", "生火", !state.CanLightFire,
                state.WoodUnlocked ? "需要五根木头" : "首次生火不消耗木头"));
        }
        else
        {
            items.Add(ButtonItem("stoke-fire", "添柴", !state.CanStokeFire,
                state.WoodUnlocked ? "消耗一根木头" : "目前不消耗木头"));
        }
        if (state.WoodUnlocked)
        {
            items.Add(ButtonItem("go-outside", "前往林地"));
        }
        if (state.CanOpenWorkshop)
        {
            items.Add(ButtonItem("open-workshop", "建设、制作与交易"));
        }
        if (state.CanPrepareExpedition)
        {
            items.Add(ButtonItem("prepare-expedition", "准备远征"));
        }
        if (state.ShipFound)
        {
            items.Add(ButtonItem("show-ship", "查看飞船"));
        }
        items.Add(ButtonItem("save-progress", "保存进度"));
        return items;
    }

    public bool Activate(string key)
    {
        actionFocusKey = key;
        switch (key)
        {
            case "start":
                if (state.GameCompleted) ShowEnding();
                else if (state.InSpace) ShowSpace();
                else if (state.ExpeditionActive) ShowWorld();
                else ShowRoom();
                return true;
            case "about":
                ShowAbout();
                return true;
            case "back-menu":
                ShowMenu();
                return true;
            case "save-progress":
                SaveAndReturnToGameMenu();
                return true;
            case "go-outside":
                return ShowOutside();
            case "back-room":
                ShowRoom();
                return true;
            case "manage-workers":
                ShowWorkers();
                return true;
            case "back-workers":
                ShowWorkers();
                return true;
            case "back-outside":
                return ShowOutside();
            case "open-workshop":
                ShowWorkshopMenu();
                return true;
            case "prepare-expedition":
                return ShowExpeditionPreparation();
            case "back-loadout":
                ShowExpeditionPreparation(false);
                return true;
            case "loadout-previous":
                loadoutPage = Math.Max(0, loadoutPage - 1);
                BuildCurrentPage(GetLoadoutPageFocus());
                AccessibilityRefreshRequested?.Invoke(GetLoadoutPageFocus(), "已显示上一页。", true);
                return true;
            case "loadout-next":
                loadoutPage++;
                BuildCurrentPage(GetLoadoutPageFocus());
                AccessibilityRefreshRequested?.Invoke(GetLoadoutPageFocus(), "已显示下一页。", true);
                return true;
            case "loadout-add-one":
                return state.AdjustLoadout(selectedLoadoutKey, 1);
            case "loadout-add-ten":
                return state.AdjustLoadout(selectedLoadoutKey, 10);
            case "loadout-remove-one":
                return state.AdjustLoadout(selectedLoadoutKey, -1);
            case "loadout-remove-ten":
                return state.AdjustLoadout(selectedLoadoutKey, -10);
            case "embark":
                if (!state.Embark()) return false;
                ShowWorld();
                return true;
            case "world-north":
                return MoveInWorld("north");
            case "world-south":
                return MoveInWorld("south");
            case "world-west":
                return MoveInWorld("west");
            case "world-east":
                return MoveInWorld("east");
            case "finish-expedition":
                return state.FinishExpeditionAtVillage();
            case "inspect-landmark":
                return state.StartCurrentLandmark();
            case "show-ship":
                ShowShip();
                return true;
            case "reinforce-hull":
                return state.ReinforceHull();
            case "upgrade-engine":
                return state.UpgradeEngine();
            case "liftoff":
                ShowLiftoffConfirmation();
                return true;
            case "cancel-liftoff":
                ShowShip();
                return true;
            case "confirm-liftoff":
                if (!state.BeginLiftoff()) return false;
                ShowSpace();
                return true;
            case "space-left":
                return state.AdvanceSpace("left");
            case "space-centre":
                return state.AdvanceSpace("centre");
            case "space-right":
                return state.AdvanceSpace("right");
            case "open-build":
                ShowContentList(DarkRoomContentCategory.Build);
                return true;
            case "open-craft":
                ShowContentList(DarkRoomContentCategory.Craft);
                return true;
            case "open-trade":
                ShowContentList(DarkRoomContentCategory.Trade);
                return true;
            case "back-workshop":
                ShowWorkshopMenu();
                return true;
            case "content-previous":
                contentPage = Math.Max(0, contentPage - 1);
                string previousFocus = GetContentPageFocus();
                BuildCurrentPage(previousFocus);
                AccessibilityRefreshRequested?.Invoke(previousFocus, "已显示上一页。", true);
                return true;
            case "content-next":
                contentPage++;
                string nextFocus = GetContentPageFocus();
                BuildCurrentPage(nextFocus);
                AccessibilityRefreshRequested?.Invoke(nextFocus, "已显示下一页。", true);
                return true;
            case "light-fire":
                return state.LightFire();
            case "stoke-fire":
                return state.StokeFire();
            case "gather-wood":
                return state.GatherWood();
            case "check-traps":
                return state.CheckTraps();
            case "build-trap":
                return state.BuildTrap();
            case "build-cart":
                return state.BuildCart();
            case "build-hut":
                return state.BuildHut();
            case "build-lodge":
                return state.BuildLodge();
            case "worker-add-one":
                return state.AssignWorker(selectedWorkerKey, 1);
            case "worker-add-ten":
                return state.AssignWorker(selectedWorkerKey, 10);
            case "worker-remove-one":
                return state.AssignWorker(selectedWorkerKey, -1);
            case "worker-remove-ten":
                return state.AssignWorker(selectedWorkerKey, -10);
            default:
                if (key.StartsWith("event-choice-", StringComparison.Ordinal))
                {
                    return state.ActivateEventChoice(key);
                }
                if (key.StartsWith("worker-open-", StringComparison.Ordinal))
                {
                    return ShowWorkerDetail(key.Substring("worker-open-".Length));
                }
                if (key.StartsWith("loadout-open-", StringComparison.Ordinal))
                {
                    return ShowLoadoutDetail(key.Substring("loadout-open-".Length));
                }
                if (key.StartsWith("landmark-", StringComparison.Ordinal))
                {
                    return state.ActivateLandmarkAction(key);
                }
                return state.ActivateContentAction(key);
        }
    }

    public bool TryGetScreenFrame(string key, out Rect frame)
    {
        frame = default;
        if (!itemFrames.TryGetValue(key, out RectTransform target) || target == null)
        {
            return false;
        }

        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        frame = Rect.MinMaxRect(min.x, Screen.height - max.y, max.x, Screen.height - min.y);
        return true;
    }

    private void ShowMenu()
    {
        state.LeaveEventArea();
        CurrentPage = Page.Menu;
        BuildCurrentPage("start");
        AccessibilityRefreshRequested?.Invoke("start", string.Empty, true);
    }

    private void ShowRoom()
    {
        CurrentPage = Page.Room;
        state.EnterRoom();
        if (state.HasActiveEvent)
        {
            eventReturnPage = Page.Room;
            eventReturnFocus = state.FireLevel == 0 ? "light-fire" : "stoke-fire";
            ShowStoryEvent(true);
            return;
        }
        UpdateMusic();
        string focus = state.FireLevel == 0 ? "light-fire" : "stoke-fire";
        BuildCurrentPage(focus);
        AccessibilityRefreshRequested?.Invoke(focus, "已进入黑暗房间。", true);
    }

    private bool ShowOutside()
    {
        if (!state.WoodUnlocked)
        {
            return false;
        }
        CurrentPage = Page.Outside;
        state.EnterOutside();
        if (state.HasActiveEvent)
        {
            eventReturnPage = Page.Outside;
            eventReturnFocus = "gather-wood";
            ShowStoryEvent(true);
            return true;
        }
        UpdateMusic();
        BuildCurrentPage("gather-wood");
        AccessibilityRefreshRequested?.Invoke("gather-wood", $"已进入{state.VillageTitle}。", true);
        return true;
    }

    private void ShowWorkers()
    {
        CurrentPage = Page.Workers;
        UpdateMusic();
        string focus = state.GetWorkerRoles().Select(role => $"worker-open-{role.Key}")
            .FirstOrDefault() ?? "back-outside";
        BuildCurrentPage(focus);
        AccessibilityRefreshRequested?.Invoke(focus, "已进入工人分配。", true);
    }

    private bool ShowWorkerDetail(string key)
    {
        DarkRoomWorkerRole role = state.GetWorkerRoles().FirstOrDefault(item => item.Key == key);
        if (role == null)
        {
            return false;
        }
        selectedWorkerKey = key;
        CurrentPage = Page.WorkerDetail;
        string focus = role.CanAssign ? "worker-add-one" : role.CanRemove
            ? "worker-remove-one" : "back-workers";
        BuildCurrentPage(focus);
        AccessibilityRefreshRequested?.Invoke(focus, $"已进入{role.Label}分配。", true);
        return true;
    }

    private void ShowWorkshopMenu()
    {
        CurrentPage = Page.WorkshopMenu;
        BuildCurrentPage("open-build");
        AccessibilityRefreshRequested?.Invoke("open-build", "已进入建设、制作与交易。", true);
    }

    private bool ShowExpeditionPreparation(bool announce = true)
    {
        if (!state.CanPrepareExpedition) return false;
        CurrentPage = Page.ExpeditionPrep;
        if (announce)
        {
            loadoutPage = 0;
            state.OpenExpeditionPreparation();
        }
        string focus = GetLoadoutPageFocus();
        BuildCurrentPage(focus);
        AccessibilityRefreshRequested?.Invoke(focus, announce ? "已进入远征准备。" : string.Empty, true);
        return true;
    }

    private bool ShowLoadoutDetail(string key)
    {
        DarkRoomLoadoutItem item = state.GetLoadoutItems().FirstOrDefault(entry => entry.Key == key);
        if (item == null) return false;
        selectedLoadoutKey = key;
        CurrentPage = Page.LoadoutDetail;
        string focus = item.CanAdd ? "loadout-add-one" : item.CanRemove
            ? "loadout-remove-one" : "back-loadout";
        BuildCurrentPage(focus);
        AccessibilityRefreshRequested?.Invoke(focus, $"已进入{item.Label}装载。", true);
        return true;
    }

    private string GetLoadoutPageFocus()
    {
        return state.GetLoadoutItems().Skip(loadoutPage * 5).Take(5)
            .Select(item => $"loadout-open-{item.Key}").FirstOrDefault() ?? "back-room";
    }

    private void ShowWorld()
    {
        if (state.HasActiveLandmark)
        {
            ShowLandmark(true);
            return;
        }
        CurrentPage = Page.World;
        state.LeaveEventArea();
        UpdateMusic();
        BuildCurrentPage("world-north");
        AccessibilityRefreshRequested?.Invoke("world-scene", "已进入荒芜世界。", true);
    }

    private void ShowLandmark(bool screenChanged)
    {
        CurrentPage = Page.Landmark;
        DarkRoomLandmarkView landmark = state.GetLandmarkView();
        string focus = landmark?.Actions.FirstOrDefault(action => action.Enabled) is DarkRoomLandmarkAction action
            ? $"landmark-{action.Key}" : string.Empty;
        BuildCurrentPage(focus);
        AccessibilityRefreshRequested?.Invoke(focus, pendingAnnouncement, screenChanged);
        actionFocusKey = string.Empty;
        pendingAnnouncement = string.Empty;
    }

    private void ShowShip()
    {
        CurrentPage = Page.Ship;
        state.LeaveEventArea();
        UpdateMusic();
        BuildCurrentPage("reinforce-hull");
        AccessibilityRefreshRequested?.Invoke("ship-scene", "已进入飞船。", true);
    }

    private void ShowLiftoffConfirmation()
    {
        CurrentPage = Page.LiftoffConfirm;
        BuildCurrentPage("confirm-liftoff");
        AccessibilityRefreshRequested?.Invoke("liftoff-warning", "请确认是否起飞。", true);
    }

    private void ShowSpace()
    {
        CurrentPage = Page.Space;
        UpdateMusic();
        BuildCurrentPage("space-left");
        AccessibilityRefreshRequested?.Invoke("space-hazard", "飞船开始上升。", true);
    }

    private void ShowEnding()
    {
        CurrentPage = Page.Ending;
        state.LeaveEventArea();
        UpdateMusic();
        BuildCurrentPage("save-progress");
        AccessibilityRefreshRequested?.Invoke("ending-title", "主线已经完成。", true);
    }

    private void SaveAndReturnToGameMenu()
    {
        SaveState();
        Action callback = returnToGameMenu;
        returnToGameMenu = null;
        Destroy(gameObject);
        callback?.Invoke();
    }

    private bool MoveInWorld(string direction)
    {
        bool moved = state.MoveWorld(direction);
        if (moved && !state.ExpeditionActive && CurrentPage == Page.World) ShowRoom();
        return moved;
    }

    private void ShowContentList(DarkRoomContentCategory category)
    {
        contentCategory = category;
        contentPage = 0;
        CurrentPage = Page.ContentList;
        IReadOnlyList<DarkRoomContentAction> actions = state.GetContentActions(category);
        string focus = actions.FirstOrDefault(action => action.Enabled)?.Key ?? "back-workshop";
        BuildCurrentPage(focus);
        AccessibilityRefreshRequested?.Invoke(focus, "已进入项目列表。", true);
    }

    private string GetContentPageFocus()
    {
        DarkRoomContentAction action = state.GetContentActions(contentCategory)
            .Skip(contentPage * 5)
            .Take(5)
            .FirstOrDefault(item => item.Enabled);
        return action?.Key ?? "back-workshop";
    }

    private void ShowAbout()
    {
        CurrentPage = Page.About;
        BuildCurrentPage("back-menu");
        AccessibilityRefreshRequested?.Invoke("about-title", string.Empty, true);
    }

    private void OnStateChanged()
    {
        SaveState();
        UpdateMusic();
        if (state.HasActiveEvent && CurrentPage != Page.StoryEvent)
        {
            eventReturnPage = CurrentPage;
            eventReturnFocus = GetSelectedKey();
            ShowStoryEvent(true);
            return;
        }
        if (!state.HasActiveEvent && CurrentPage == Page.StoryEvent)
        {
            ReturnFromStoryEvent();
            return;
        }
        if (!state.ExpeditionActive && (CurrentPage == Page.World || CurrentPage == Page.Landmark))
        {
            ShowRoom();
            return;
        }
        if (state.HasActiveLandmark && CurrentPage != Page.Landmark)
        {
            ShowLandmark(true);
            return;
        }
        if (!state.HasActiveLandmark && CurrentPage == Page.Landmark)
        {
            ShowWorld();
            return;
        }
        if (!state.InSpace && CurrentPage == Page.Space)
        {
            if (state.GameCompleted) ShowEnding();
            else ShowShip();
            return;
        }
        if (string.IsNullOrWhiteSpace(actionFocusKey))
        {
            pendingAnnouncement = string.Empty;
            return;
        }

        string preservedFocus = actionFocusKey;
        preservedFocus = ResolveFocusKey(preservedFocus);
        BuildCurrentPage(preservedFocus);
        AccessibilityRefreshRequested?.Invoke(preservedFocus, pendingAnnouncement, false);
        actionFocusKey = string.Empty;
        pendingAnnouncement = string.Empty;
    }

    private void ShowStoryEvent(bool screenChanged)
    {
        CurrentPage = Page.StoryEvent;
        DarkRoomEventView storyEvent = state.GetCurrentEvent();
        string focus = storyEvent?.Choices.FirstOrDefault(choice => choice.Enabled) is DarkRoomEventChoice choice
            ? $"event-choice-{choice.Key}" : string.Empty;
        BuildCurrentPage(focus);
        AccessibilityRefreshRequested?.Invoke(focus, pendingAnnouncement, screenChanged);
        actionFocusKey = string.Empty;
        pendingAnnouncement = string.Empty;
    }

    private void ReturnFromStoryEvent()
    {
        CurrentPage = eventReturnPage == Page.StoryEvent ? Page.Room : eventReturnPage;
        string focus = ResolveFocusKey(eventReturnFocus);
        BuildCurrentPage(focus);
        AccessibilityRefreshRequested?.Invoke(focus, pendingAnnouncement, true);
        actionFocusKey = string.Empty;
        pendingAnnouncement = string.Empty;
    }

    private string ResolveFocusKey(string requestedKey)
    {
        IReadOnlyList<DarkRoomAccessibleItem> items = BuildAccessibleItems();
        DarkRoomAccessibleItem requested = items.FirstOrDefault(item => item.Key == requestedKey);
        if (requested != null && requested.IsButton && !requested.IsDisabled)
        {
            return requestedKey;
        }
        DarkRoomAccessibleItem firstButton = items.FirstOrDefault(item => item.IsButton && !item.IsDisabled);
        return firstButton?.Key ?? string.Empty;
    }

    private void BuildCanvas()
    {
        var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        Image background = canvasObject.AddComponent<Image>();
        background.color = Background;

        var safeArea = new GameObject("SafeArea", typeof(RectTransform), typeof(VerticalLayoutGroup));
        safeArea.transform.SetParent(canvasObject.transform, false);
        content = safeArea.GetComponent<RectTransform>();
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = new Vector2(48f, 48f);
        content.offsetMax = new Vector2(-48f, -48f);
        VerticalLayoutGroup layout = safeArea.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 24f;
        layout.padding = new RectOffset(20, 20, 28, 28);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        if (EventSystem.current == null)
        {
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(transform, false);
        }
    }

    private void BuildCurrentPage(string focusKey)
    {
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }
        itemFrames.Clear();
        buttons.Clear();
        buttonOrder.Clear();

        presentedItems = BuildAccessibleItems();
        IReadOnlyList<DarkRoomAccessibleItem> items = presentedItems;
        foreach (DarkRoomAccessibleItem item in items)
        {
            if (item.IsButton)
            {
                CreateButton(item);
            }
            else
            {
                CreateText(item);
            }
        }

        ConfigureNavigation();
        StartCoroutine(SelectAfterLayout(focusKey));
        actionFocusKey = string.Empty;
    }

    private void CreateText(DarkRoomAccessibleItem item)
    {
        var holder = new GameObject(item.Key, typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        holder.transform.SetParent(content, false);
        Image panel = holder.GetComponent<Image>();
        panel.color = item.IsHeader ? Background : Panel;
        LayoutElement element = holder.GetComponent<LayoutElement>();
        element.minHeight = item.IsHeader ? 112f : 150f;
        element.preferredHeight = item.IsHeader ? 130f : 185f;

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(holder.transform, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(24f, 16f);
        rect.offsetMax = new Vector2(-24f, -16f);
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = item.IsHeader ? 48 : 34;
        text.fontStyle = item.IsHeader ? FontStyle.Bold : FontStyle.Normal;
        text.color = item.IsHeader ? Foreground : Muted;
        text.alignment = item.IsHeader ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.text = string.IsNullOrWhiteSpace(item.Value) ? item.Label : $"{item.Label}\n{item.Value}";
        itemFrames[item.Key] = holder.GetComponent<RectTransform>();
    }

    private void CreateButton(DarkRoomAccessibleItem item)
    {
        var holder = new GameObject(item.Key, typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
        holder.transform.SetParent(content, false);
        LayoutElement element = holder.GetComponent<LayoutElement>();
        element.minHeight = 104f;
        element.preferredHeight = 112f;
        Image image = holder.GetComponent<Image>();
        image.color = item.IsDisabled ? DisabledBackground : ButtonBackground;
        Button button = holder.GetComponent<Button>();
        button.targetGraphic = image;
        button.interactable = !item.IsDisabled;
        string key = item.Key;
        button.onClick.AddListener(() => Activate(key));

        var textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(holder.transform, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(24f, 12f);
        rect.offsetMax = new Vector2(-24f, -12f);
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = 38;
        text.fontStyle = FontStyle.Bold;
        text.color = Foreground;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.text = string.IsNullOrWhiteSpace(item.Value) ? item.Label : $"{item.Label}，{item.Value}";

        buttons[key] = button;
        buttonOrder.Add(key);
        itemFrames[key] = holder.GetComponent<RectTransform>();
    }

    private void ConfigureNavigation()
    {
        List<Button> enabledButtons = buttonOrder
            .Select(key => buttons[key])
            .Where(button => button.interactable)
            .ToList();
        foreach (Button button in buttons.Values)
        {
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnUp = null;
            navigation.selectOnDown = null;
            navigation.selectOnLeft = null;
            navigation.selectOnRight = null;
            button.navigation = navigation;
        }
        for (int index = 0; index < enabledButtons.Count; index++)
        {
            Button current = enabledButtons[index];
            Navigation navigation = current.navigation;
            navigation.selectOnUp = index > 0 ? enabledButtons[index - 1] : null;
            navigation.selectOnDown = index + 1 < enabledButtons.Count ? enabledButtons[index + 1] : null;
            navigation.selectOnLeft = navigation.selectOnUp;
            navigation.selectOnRight = navigation.selectOnDown;
            current.navigation = navigation;
        }
    }

    private IEnumerator SelectAfterLayout(string key)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        if (!string.IsNullOrWhiteSpace(key) && buttons.TryGetValue(key, out Button button) && button.interactable)
        {
            EventSystem.current.SetSelectedGameObject(button.gameObject);
        }
        else
        {
            Button first = buttonOrder.Select(itemKey => buttons[itemKey]).FirstOrDefault(item => item.interactable);
            if (first != null)
            {
                EventSystem.current.SetSelectedGameObject(first.gameObject);
            }
        }
    }

    private string GetSelectedKey()
    {
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
        {
            return string.Empty;
        }
        return EventSystem.current.currentSelectedGameObject.name;
    }

    private void CreateAudio()
    {
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        soundSource = gameObject.AddComponent<AudioSource>();
        soundSource.loop = false;
        soundSource.playOnAwake = false;

        soundManager sharedAudio = soundManager.Instance;
        musicSource.volume = sharedAudio?.musicSource == null ? 0.45f : sharedAudio.musicSource.volume;
        soundSource.volume = sharedAudio?.sfxSource == null ? 0.8f : sharedAudio.sfxSource.volume;
        musicSource.mute = sharedAudio != null && !sharedAudio.IsMusicEnabled;
        soundSource.mute = sharedAudio != null && !sharedAudio.IsSfxEnabled;
    }

    private void PlaySound(string audioKey)
    {
        AudioClip clip = LoadAudio(audioKey);
        if (clip != null)
        {
            LastPlayedSoundKey = audioKey;
            soundSource.Stop();
            soundSource.PlayOneShot(clip);
        }
    }

    private void UpdateMusic()
    {
        string key;
        if (CurrentPage == Page.Space)
        {
            key = "space";
        }
        else if (CurrentPage == Page.Ending)
        {
            key = "ending";
        }
        else if (CurrentPage == Page.Ship || CurrentPage == Page.LiftoffConfirm)
        {
            key = "ship";
        }
        else if (CurrentPage == Page.World || CurrentPage == Page.Landmark)
        {
            key = "world";
        }
        else if (CurrentPage == Page.ExpeditionPrep || CurrentPage == Page.LoadoutDetail)
        {
            key = "dusty-path";
        }
        else if (CurrentPage == Page.Outside || CurrentPage == Page.Workers ||
                 CurrentPage == Page.WorkerDetail)
        {
            key = state.HutCount switch
            {
                0 => "silent-forest",
                1 => "lonely-hut",
                <= 4 => "tiny-village",
                <= 8 => "modest-village",
                <= 14 => "large-village",
                _ => "raucous-village"
            };
        }
        else
        {
            key = state.FireLevel switch
            {
                0 => "fire-dead",
                1 => "fire-smoldering",
                2 => "fire-flickering",
                3 => "fire-burning",
                _ => "fire-roaring"
            };
        }
        AudioClip clip = LoadAudio(key);
        if (clip == null || musicSource.clip == clip)
        {
            return;
        }
        musicSource.clip = clip;
        musicSource.Play();
    }

    private static AudioClip LoadAudio(string key)
    {
        return Resources.Load<AudioClip>($"ThirdParty/ADarkRoom/Audio/{key}");
    }

    private DarkRoomGameState LoadState()
    {
        string key = PlayerPrefs.HasKey(SaveKey)
            ? SaveKey
            : PlayerPrefs.HasKey(LegacySaveKey) ? LegacySaveKey : string.Empty;
        if (string.IsNullOrEmpty(key))
        {
            return new DarkRoomGameState();
        }

        try
        {
            DarkRoomSaveData save = JsonUtility.FromJson<DarkRoomSaveData>(PlayerPrefs.GetString(key));
            return new DarkRoomGameState(save);
        }
        catch (Exception)
        {
            return new DarkRoomGameState();
        }
    }

    private void SaveState()
    {
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(state.CreateSaveData()));
        PlayerPrefs.Save();
    }

    private static Font CreateReadableFont()
    {
        Font embedded = Resources.Load<Font>("Fonts/ADarkRoomAccessibleChinese");
        if (embedded != null)
        {
            return embedded;
        }

        string[] candidates =
        {
            "Noto Sans CJK SC",
            "Noto Sans CJK",
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "PingFang SC",
            "Droid Sans Fallback"
        };
        try
        {
            Font created = Font.CreateDynamicFontFromOSFont(candidates, 40);
            if (created != null)
            {
                return created;
            }
        }
        catch (Exception)
        {
        }
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static string FormatStore(float value)
    {
        return Math.Abs(value - Math.Round(value)) < 0.001f
            ? ((int)Math.Round(value)).ToString()
            : value.ToString("0.0");
    }

    private static DarkRoomAccessibleItem Header(string key, string label)
    {
        return new DarkRoomAccessibleItem { Key = key, Label = label, IsHeader = true };
    }

    private static DarkRoomAccessibleItem TextItem(string key, string label, string value)
    {
        return new DarkRoomAccessibleItem { Key = key, Label = label, Value = value };
    }

    private static DarkRoomAccessibleItem ButtonItem(string key, string label, bool disabled = false, string value = "")
    {
        return new DarkRoomAccessibleItem
        {
            Key = key,
            Label = label,
            Value = value,
            IsButton = true,
            IsDisabled = disabled
        };
    }
}
