// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

public enum DarkRoomContentCategory
{
    Build,
    Craft,
    Trade
}

public sealed class DarkRoomContentAction
{
    public string Key;
    public string Label;
    public string CostText;
    public bool Enabled;
}

public sealed partial class DarkRoomSaveData
{
    public int tradingPostCount;
    public int tanneryCount;
    public int smokehouseCount;
    public int workshopCount;
    public int steelworksCount;
    public int armouryCount;

    public float curedMeat;
    public float leather;
    public float iron;
    public float coal;
    public float sulphur;
    public float steel;
    public float medicine;
    public float bullets;
    public float energyCell;
    public float bolas;
    public float grenade;
    public float bayonet;
    public float alienAlloy;
    public float torch;
    public float boneSpear;
    public float ironSword;
    public float steelSword;
    public float rifle;
    public float laserRifle;

    public int waterskin;
    public int cask;
    public int waterTank;
    public int rucksack;
    public int wagon;
    public int convoy;
    public int leatherArmour;
    public int ironArmour;
    public int steelArmour;
    public int compass;

    public List<string> seenStores = new List<string>();
    public List<string> discoveredContent = new List<string>();
}

public sealed partial class DarkRoomGameState
{
    private sealed class Cost
    {
        public readonly string Store;
        public readonly float Amount;

        public Cost(string store, float amount)
        {
            Store = store;
            Amount = amount;
        }
    }

    private sealed class Definition
    {
        public string Key;
        public string Label;
        public DarkRoomContentCategory Category;
        public int Maximum;
        public Func<DarkRoomGameState, Cost[]> Costs;
        public string Message;
        public string Audio;
    }

    private static readonly Definition[] ContentDefinitions =
    {
        Building("trap", "陷阱", 10, state => Costs("wood", state.TrapCost), "更多陷阱被放进林地。"),
        Building("cart", "推车", 1, _ => Costs("wood", 30), "摇摇晃晃的推车可以从林地运回更多木头。"),
        Building("hut", "小屋", 20, state => Costs("wood", state.HutCost), "建造者在林地里搭起一间小屋。消息会慢慢传开。"),
        Building("lodge", "猎人小屋", 1, _ => Costs("wood", 200, "fur", 10, "meat", 5), "猎人小屋立在远离村落的林地中。"),
        Building("trading-post", "交易站", 1, _ => Costs("wood", 400, "fur", 100), "游牧者有了摆放货物的地方，也许会多停留一阵。"),
        Building("tannery", "制革坊", 1, _ => Costs("wood", 500, "fur", 50), "制革坊很快在村落边缘建了起来。"),
        Building("smokehouse", "熏肉房", 1, _ => Costs("wood", 600, "meat", 50), "建造者完成了熏肉房，看起来有些饥饿。"),
        Building("workshop", "工坊", 1, _ => Costs("wood", 800, "leather", 100, "scales", 10), "工坊终于准备好了，建造者迫不及待地想开工。"),
        Building("steelworks", "钢铁厂", 1, _ => Costs("wood", 1500, "iron", 100, "coal", 100), "钢铁厂点火，烟霾笼罩了村落。"),
        Building("armoury", "军械库", 1, _ => Costs("wood", 3000, "steel", 100, "sulphur", 50), "军械库完工，旧时代的武器重新出现。"),

        Craft("torch", "火把", 0, _ => Costs("wood", 1, "cloth", 1), "一支驱散黑暗的火把。", "craft"),
        Craft("waterskin", "水袋", 1, _ => Costs("leather", 50), "水袋至少能装下一些水。", "craft-waterskin"),
        Craft("cask", "水桶", 1, _ => Costs("leather", 100, "iron", 20), "水桶能为更远的旅程储存足够的水。", "craft-cask"),
        Craft("water-tank", "水箱", 1, _ => Costs("iron", 100, "steel", 50), "以后再也不用为缺水发愁。", "craft-water-tank"),
        Craft("bone-spear", "骨矛", 0, _ => Costs("wood", 100, "teeth", 5), "骨矛并不精致，但足够锋利。", "craft-bone-spear"),
        Craft("rucksack", "背包", 1, _ => Costs("leather", 200), "更大的背包意味着能在荒野中走得更远。", "craft-rucksack"),
        Craft("wagon", "货车", 1, _ => Costs("wood", 500, "iron", 100), "货车可以装载大量补给。", "craft-wagon"),
        Craft("convoy", "车队", 1, _ => Costs("wood", 1000, "iron", 200, "steel", 100), "车队几乎能运走所有需要的东西。", "craft-convoy"),
        Craft("leather-armour", "皮甲", 1, _ => Costs("leather", 200, "scales", 20), "皮革不算坚固，但总比破布好。", "craft-armour"),
        Craft("iron-armour", "铁甲", 1, _ => Costs("leather", 200, "iron", 100), "铁比皮革更加坚固。", "craft-armour"),
        Craft("steel-armour", "钢甲", 1, _ => Costs("leather", 200, "steel", 100), "钢比铁更加坚固。", "craft-armour"),
        Craft("iron-sword", "铁剑", 0, _ => Costs("wood", 200, "leather", 50, "iron", 20), "锋利的铁剑能在荒野中提供保护。", "craft-weapon"),
        Craft("steel-sword", "钢剑", 0, _ => Costs("wood", 500, "leather", 100, "steel", 20), "钢刃强韧而笔直。", "craft-weapon"),
        Craft("rifle", "步枪", 0, _ => Costs("wood", 200, "steel", 50, "sulphur", 50), "黑火药和子弹，就像旧时代一样。", "craft-rifle"),

        Trade("scales", "鳞片", 0, Costs("fur", 150), "buy-scales"),
        Trade("teeth", "牙齿", 0, Costs("fur", 300), "buy-teeth"),
        Trade("iron", "铁", 0, Costs("fur", 150, "scales", 50), "buy-iron"),
        Trade("coal", "煤", 0, Costs("fur", 200, "teeth", 50), "buy-coal"),
        Trade("steel", "钢", 0, Costs("fur", 300, "scales", 50, "teeth", 50), "buy-steel"),
        Trade("medicine", "药剂", 0, Costs("scales", 50, "teeth", 30), "buy-medicine"),
        Trade("bullets", "子弹", 0, Costs("scales", 10), "buy-bullets"),
        Trade("energy-cell", "能量元件", 0, Costs("scales", 10, "teeth", 10), "buy-energy-cell"),
        Trade("bolas", "套索", 0, Costs("teeth", 10), "buy-bolas"),
        Trade("grenade", "手雷", 0, Costs("scales", 100, "teeth", 50), "buy-grenades"),
        Trade("bayonet", "刺刀", 0, Costs("scales", 500, "teeth", 250), "buy-bayonet"),
        Trade("alien-alloy", "外星合金", 0, Costs("fur", 1500, "scales", 750, "teeth", 300), "buy-alien-alloy"),
        Trade("compass", "指南针", 1, Costs("fur", 400, "scales", 20, "teeth", 10), "buy-compass")
    };

    public int TradingPostCount => data.tradingPostCount;
    public int TanneryCount => data.tanneryCount;
    public int SmokehouseCount => data.smokehouseCount;
    public int WorkshopCount => data.workshopCount;
    public int SteelworksCount => data.steelworksCount;
    public int ArmouryCount => data.armouryCount;
    public float AlienAlloy => data.alienAlloy;
    public bool CanOpenWorkshop => data.builderLevel >= 4;
    public bool CanCraft => data.workshopCount > 0;
    public bool CanTrade => data.tradingPostCount > 0;

    public IReadOnlyList<DarkRoomContentAction> GetContentActions(DarkRoomContentCategory category)
    {
        EnsureContentState();
        return ContentDefinitions
            .Where(definition => definition.Category == category && IsContentVisible(definition))
            .Select(definition => new DarkRoomContentAction
            {
                Key = $"content-{CategoryKey(category)}-{definition.Key}",
                Label = definition.Label,
                CostText = FormatCosts(definition.Costs(this)),
                Enabled = CanActivate(definition)
            })
            .ToArray();
    }

    public bool ActivateContentAction(string actionKey)
    {
        Definition definition = ContentDefinitions.FirstOrDefault(item =>
            actionKey == $"content-{CategoryKey(item.Category)}-{item.Key}");
        if (definition == null || !IsContentVisible(definition) || !CanActivate(definition))
        {
            return false;
        }

        if (definition.Category == DarkRoomContentCategory.Build)
        {
            return definition.Key switch
            {
                "trap" => BuildTrap(),
                "cart" => BuildCart(),
                "hut" => BuildHut(),
                "lodge" => BuildLodge(),
                _ => BuildFixedBuilding(definition)
            };
        }

        Spend(definition.Costs(this));
        AddStore(definition.Key, 1f);
        Discover(definition);
        Emit(definition.Message ?? $"获得了{definition.Label}。", definition.Audio);
        return true;
    }

    public string GetAllStoresText()
    {
        string[] keys =
        {
            "wood", "fur", "meat", "bait", "cured-meat", "leather", "scales", "teeth",
            "cloth", "iron", "coal", "sulphur", "steel", "medicine", "bullets", "energy-cell"
        };
        return string.Join("；", keys.Where(key => GetStore(key) > 0f)
            .Select(key => $"{StoreLabel(key)}{FormatAmount(GetStore(key))}")) + "。";
    }

    private void CopyContentSaveData(DarkRoomSaveData save)
    {
        save.tradingPostCount = data.tradingPostCount;
        save.tanneryCount = data.tanneryCount;
        save.smokehouseCount = data.smokehouseCount;
        save.workshopCount = data.workshopCount;
        save.steelworksCount = data.steelworksCount;
        save.armouryCount = data.armouryCount;
        save.curedMeat = data.curedMeat;
        save.leather = data.leather;
        save.iron = data.iron;
        save.coal = data.coal;
        save.sulphur = data.sulphur;
        save.steel = data.steel;
        save.medicine = data.medicine;
        save.bullets = data.bullets;
        save.energyCell = data.energyCell;
        save.bolas = data.bolas;
        save.grenade = data.grenade;
        save.bayonet = data.bayonet;
        save.alienAlloy = data.alienAlloy;
        save.torch = data.torch;
        save.boneSpear = data.boneSpear;
        save.ironSword = data.ironSword;
        save.steelSword = data.steelSword;
        save.rifle = data.rifle;
        save.laserRifle = data.laserRifle;
        save.waterskin = data.waterskin;
        save.cask = data.cask;
        save.waterTank = data.waterTank;
        save.rucksack = data.rucksack;
        save.wagon = data.wagon;
        save.convoy = data.convoy;
        save.leatherArmour = data.leatherArmour;
        save.ironArmour = data.ironArmour;
        save.steelArmour = data.steelArmour;
        save.compass = data.compass;
        save.seenStores = new List<string>(data.seenStores ?? new List<string>());
        save.discoveredContent = new List<string>(data.discoveredContent ?? new List<string>());
        CopyWorkerSaveData(save);
    }

    private void EnsureContentState()
    {
        data.seenStores ??= new List<string>();
        data.discoveredContent ??= new List<string>();
        string[] keys =
        {
            "wood", "fur", "meat", "bait", "scales", "teeth", "cloth", "charm", "cured-meat",
            "leather", "iron", "coal", "sulphur", "steel", "medicine", "bullets", "energy-cell",
            "bolas", "grenade", "bayonet", "alien-alloy"
        };
        foreach (string key in keys)
        {
            if (GetStore(key) > 0f) MarkStoreSeen(key);
        }
    }

    private bool IsContentVisible(Definition definition)
    {
        if (definition.Category == DarkRoomContentCategory.Build && data.builderLevel < 4) return false;
        if (definition.Category == DarkRoomContentCategory.Craft && data.workshopCount == 0) return false;
        if (definition.Category == DarkRoomContentCategory.Trade)
        {
            if (data.tradingPostCount == 0) return false;
            return definition.Key == "compass" || HasSeenStore(definition.Key);
        }

        int count = GetContentCount(definition);
        if (count > 0 || data.discoveredContent.Contains(ContentId(definition))) return true;
        Cost[] costs = definition.Costs(this);
        Cost wood = costs.FirstOrDefault(cost => cost.Store == "wood");
        if (wood != null && GetStore("wood") < wood.Amount * 0.5f) return false;
        return costs.All(cost => cost.Store == "wood" || HasSeenStore(cost.Store));
    }

    private bool CanActivate(Definition definition)
    {
        if (definition.Maximum > 0 && GetContentCount(definition) >= definition.Maximum) return false;
        return CanAfford(definition.Costs(this));
    }

    private bool BuildFixedBuilding(Definition definition)
    {
        Spend(definition.Costs(this));
        SetBuildingCount(definition.Key, GetBuildingCount(definition.Key) + 1);
        Discover(definition);
        Emit(definition.Message, definition.Audio);
        return true;
    }

    private bool CanAfford(IEnumerable<Cost> costs)
    {
        return costs.All(cost => GetStore(cost.Store) >= cost.Amount);
    }

    private void Spend(IEnumerable<Cost> costs)
    {
        foreach (Cost cost in costs) AddStore(cost.Store, -cost.Amount);
    }

    private void Discover(Definition definition)
    {
        string id = ContentId(definition);
        if (!data.discoveredContent.Contains(id)) data.discoveredContent.Add(id);
    }

    private int GetContentCount(Definition definition)
    {
        return definition.Category == DarkRoomContentCategory.Build
            ? GetBuildingCount(definition.Key)
            : (int)Math.Floor(GetStore(definition.Key));
    }

    private int GetBuildingCount(string key)
    {
        return key switch
        {
            "trap" => data.trapCount,
            "cart" => data.cartCount,
            "hut" => data.hutCount,
            "lodge" => data.lodgeCount,
            "trading-post" => data.tradingPostCount,
            "tannery" => data.tanneryCount,
            "smokehouse" => data.smokehouseCount,
            "workshop" => data.workshopCount,
            "steelworks" => data.steelworksCount,
            "armoury" => data.armouryCount,
            "iron-mine" => data.ironMineCount,
            "coal-mine" => data.coalMineCount,
            "sulphur-mine" => data.sulphurMineCount,
            _ => 0
        };
    }

    private void SetBuildingCount(string key, int value)
    {
        switch (key)
        {
            case "trading-post": data.tradingPostCount = value; break;
            case "tannery": data.tanneryCount = value; break;
            case "smokehouse": data.smokehouseCount = value; break;
            case "workshop": data.workshopCount = value; break;
            case "steelworks": data.steelworksCount = value; break;
            case "armoury": data.armouryCount = value; break;
        }
    }

    private float GetStore(string key)
    {
        return key switch
        {
            "wood" => data.wood,
            "fur" => data.fur,
            "meat" => data.meat,
            "bait" => data.bait,
            "scales" => data.scales,
            "teeth" => data.teeth,
            "cloth" => data.cloth,
            "charm" => data.charm,
            "cured-meat" => data.curedMeat,
            "leather" => data.leather,
            "iron" => data.iron,
            "coal" => data.coal,
            "sulphur" => data.sulphur,
            "steel" => data.steel,
            "medicine" => data.medicine,
            "bullets" => data.bullets,
            "energy-cell" => data.energyCell,
            "bolas" => data.bolas,
            "grenade" => data.grenade,
            "bayonet" => data.bayonet,
            "alien-alloy" => data.alienAlloy,
            "torch" => data.torch,
            "bone-spear" => data.boneSpear,
            "iron-sword" => data.ironSword,
            "steel-sword" => data.steelSword,
            "rifle" => data.rifle,
            "laser-rifle" => data.laserRifle,
            "waterskin" => data.waterskin,
            "cask" => data.cask,
            "water-tank" => data.waterTank,
            "rucksack" => data.rucksack,
            "wagon" => data.wagon,
            "convoy" => data.convoy,
            "leather-armour" => data.leatherArmour,
            "iron-armour" => data.ironArmour,
            "steel-armour" => data.steelArmour,
            "compass" => data.compass,
            _ => 0f
        };
    }

    private void AddStore(string key, float amount)
    {
        switch (key)
        {
            case "wood": data.wood += (int)Math.Round(amount); break;
            case "fur": data.fur += amount; break;
            case "meat": data.meat += amount; break;
            case "bait": data.bait += amount; break;
            case "scales": data.scales += amount; break;
            case "teeth": data.teeth += amount; break;
            case "cloth": data.cloth += amount; break;
            case "charm": data.charm += amount; break;
            case "cured-meat": data.curedMeat += amount; break;
            case "leather": data.leather += amount; break;
            case "iron": data.iron += amount; break;
            case "coal": data.coal += amount; break;
            case "sulphur": data.sulphur += amount; break;
            case "steel": data.steel += amount; break;
            case "medicine": data.medicine += amount; break;
            case "bullets": data.bullets += amount; break;
            case "energy-cell": data.energyCell += amount; break;
            case "bolas": data.bolas += amount; break;
            case "grenade": data.grenade += amount; break;
            case "bayonet": data.bayonet += amount; break;
            case "alien-alloy": data.alienAlloy += amount; break;
            case "torch": data.torch += amount; break;
            case "bone-spear": data.boneSpear += amount; break;
            case "iron-sword": data.ironSword += amount; break;
            case "steel-sword": data.steelSword += amount; break;
            case "rifle": data.rifle += amount; break;
            case "laser-rifle": data.laserRifle += amount; break;
            case "waterskin": data.waterskin += (int)Math.Round(amount); break;
            case "cask": data.cask += (int)Math.Round(amount); break;
            case "water-tank": data.waterTank += (int)Math.Round(amount); break;
            case "rucksack": data.rucksack += (int)Math.Round(amount); break;
            case "wagon": data.wagon += (int)Math.Round(amount); break;
            case "convoy": data.convoy += (int)Math.Round(amount); break;
            case "leather-armour": data.leatherArmour += (int)Math.Round(amount); break;
            case "iron-armour": data.ironArmour += (int)Math.Round(amount); break;
            case "steel-armour": data.steelArmour += (int)Math.Round(amount); break;
            case "compass": data.compass += (int)Math.Round(amount); break;
        }
        if (amount > 0f) MarkStoreSeen(key);
    }

    private void MarkStoreSeen(string key)
    {
        data.seenStores ??= new List<string>();
        if (!data.seenStores.Contains(key)) data.seenStores.Add(key);
    }

    private bool HasSeenStore(string key)
    {
        return GetStore(key) > 0f || (data.seenStores?.Contains(key) ?? false);
    }

    private static Definition Building(string key, string label, int maximum,
        Func<DarkRoomGameState, Cost[]> costs, string message)
    {
        return new Definition
        {
            Key = key, Label = label, Category = DarkRoomContentCategory.Build,
            Maximum = maximum, Costs = costs, Message = message, Audio = "build"
        };
    }

    private static Definition Craft(string key, string label, int maximum,
        Func<DarkRoomGameState, Cost[]> costs, string message, string audio)
    {
        return new Definition
        {
            Key = key, Label = label, Category = DarkRoomContentCategory.Craft,
            Maximum = maximum, Costs = costs, Message = message, Audio = "craft"
        };
    }

    private static Definition Trade(string key, string label, int maximum, Cost[] costs, string audio)
    {
        return new Definition
        {
            Key = key, Label = label, Category = DarkRoomContentCategory.Trade,
            Maximum = maximum, Costs = _ => costs, Message = $"交易获得了{label}。", Audio = "buy"
        };
    }

    private static Cost[] Costs(string firstStore, float firstAmount, params object[] remaining)
    {
        var costs = new List<Cost> { new Cost(firstStore, firstAmount) };
        for (int index = 0; index < remaining.Length; index += 2)
        {
            costs.Add(new Cost((string)remaining[index], Convert.ToSingle(remaining[index + 1])));
        }
        return costs.ToArray();
    }

    private static string ContentId(Definition definition) => $"{CategoryKey(definition.Category)}:{definition.Key}";

    private static string CategoryKey(DarkRoomContentCategory category)
    {
        return category switch
        {
            DarkRoomContentCategory.Build => "build",
            DarkRoomContentCategory.Craft => "craft",
            _ => "trade"
        };
    }

    private static string FormatCosts(IEnumerable<Cost> costs)
    {
        return "消耗" + string.Join("、", costs.Select(cost => $"{FormatAmount(cost.Amount)}{StoreLabel(cost.Store)}"));
    }

    private static string FormatAmount(float amount)
    {
        return Math.Abs(amount - Math.Round(amount)) < 0.001f
            ? ((int)Math.Round(amount)).ToString()
            : amount.ToString("0.0");
    }

    private static string StoreLabel(string key)
    {
        return key switch
        {
            "wood" => "木头", "fur" => "毛皮", "meat" => "肉", "bait" => "诱饵",
            "scales" => "鳞片", "teeth" => "牙齿", "cloth" => "布料", "charm" => "护符",
            "cured-meat" => "熏肉", "leather" => "皮革", "iron" => "铁", "coal" => "煤",
            "sulphur" => "硫磺", "steel" => "钢", "medicine" => "药剂", "bullets" => "子弹",
            "energy-cell" => "能量元件", "bolas" => "套索", "grenade" => "手雷",
            "bayonet" => "刺刀", "alien-alloy" => "外星合金", _ => key
        };
    }
}
