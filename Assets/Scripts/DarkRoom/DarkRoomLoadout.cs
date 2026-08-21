// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

[Serializable]
public sealed class DarkRoomInventoryEntry
{
    public string key;
    public int amount;
}

public sealed class DarkRoomLoadoutItem
{
    public string Key;
    public string Label;
    public float Weight;
    public int Available;
    public int Loaded;
    public bool CanAdd;
    public bool CanRemove;
}

public sealed partial class DarkRoomSaveData
{
    public List<DarkRoomInventoryEntry> outfit = new List<DarkRoomInventoryEntry>();
    public List<DarkRoomInventoryEntry> expeditionInventory = new List<DarkRoomInventoryEntry>();
    public bool expeditionActive;
    public int expeditionX = 30;
    public int expeditionY = 30;
    public int expeditionWater;
    public int expeditionHealth;
    public int expeditionFoodMoves;
    public int expeditionWaterMoves;
}

public sealed partial class DarkRoomGameState
{
    private sealed class LoadoutDefinition
    {
        public string Key;
        public string Label;
        public float Weight;
    }

    private static readonly LoadoutDefinition[] LoadoutDefinitions =
    {
        Loadout("cured-meat", "熏肉", 1f),
        Loadout("torch", "火把", 1f),
        Loadout("bone-spear", "骨矛", 2f),
        Loadout("iron-sword", "铁剑", 3f),
        Loadout("steel-sword", "钢剑", 5f),
        Loadout("rifle", "步枪", 5f),
        Loadout("bullets", "子弹", 0.1f),
        Loadout("medicine", "药物", 1f),
        Loadout("bolas", "套索", 0.5f),
        Loadout("grenade", "手榴弹", 1f),
        Loadout("bayonet", "刺刀", 1f),
        Loadout("laser-rifle", "激光步枪", 5f),
        Loadout("energy-cell", "能量电池", 0.2f),
        Loadout("charm", "护符", 1f)
    };

    public bool CanPrepareExpedition => GetStore("compass") > 0f;
    public bool ExpeditionActive => data.expeditionActive;
    public int CarryingCapacity => GetStore("convoy") > 0f ? 70 : GetStore("wagon") > 0f ? 40 :
        GetStore("rucksack") > 0f ? 20 : 10;
    public float UsedLoadoutSpace => LoadoutDefinitions.Sum(definition =>
        GetEffectiveOutfitAmount(definition.Key) * definition.Weight);
    public float FreeLoadoutSpace => Math.Max(0f, CarryingCapacity - UsedLoadoutSpace);
    public int MaximumWater => GetStore("water-tank") > 0f ? 60 : GetStore("cask") > 0f ? 30 :
        GetStore("waterskin") > 0f ? 20 : 10;
    public int MaximumHealth => GetStore("steel-armour") > 0f ? 45 : GetStore("iron-armour") > 0f ? 25 :
        GetStore("leather-armour") > 0f ? 15 : 10;
    public string ArmourText => GetStore("steel-armour") > 0f ? "钢甲" : GetStore("iron-armour") > 0f ? "铁甲" :
        GetStore("leather-armour") > 0f ? "皮甲" : "无";
    public bool CanEmbark => !data.expeditionActive && GetEffectiveOutfitAmount("cured-meat") > 0;
    public int ExpeditionWater => data.expeditionWater;
    public int ExpeditionHealth => data.expeditionHealth;

    public string GetExpeditionInventoryText()
    {
        string text = string.Join("；", data.expeditionInventory.Where(entry => entry.amount > 0)
            .Select(entry =>
            {
                LoadoutDefinition definition = LoadoutDefinitions.FirstOrDefault(item => item.Key == entry.key);
                return $"{definition?.Label ?? StoreLabel(entry.key)}{entry.amount}";
            }));
        return string.IsNullOrWhiteSpace(text) ? "空" : text;
    }

    public IReadOnlyList<DarkRoomLoadoutItem> GetLoadoutItems()
    {
        return LoadoutDefinitions.Where(definition =>
                GetStore(definition.Key) > 0f || GetEffectiveOutfitAmount(definition.Key) > 0)
            .Select(definition =>
            {
                int loaded = GetEffectiveOutfitAmount(definition.Key);
                int available = Math.Max(0, (int)Math.Floor(GetStore(definition.Key)));
                return new DarkRoomLoadoutItem
                {
                    Key = definition.Key,
                    Label = definition.Label,
                    Weight = definition.Weight,
                    Available = available,
                    Loaded = loaded,
                    CanAdd = loaded < available && FreeLoadoutSpace + 0.0001f >= definition.Weight,
                    CanRemove = loaded > 0
                };
            }).ToArray();
    }

    public bool OpenExpeditionPreparation()
    {
        if (!CanPrepareExpedition) return false;
        data.worldUnlocked = true;
        EnsureWorldGenerated();
        LeaveEventArea();
        Emit("罗盘指向荒芜世界的深处。", "dusty-path");
        return true;
    }

    public bool AdjustLoadout(string key, int amount)
    {
        LoadoutDefinition definition = LoadoutDefinitions.FirstOrDefault(item => item.Key == key);
        if (definition == null || amount == 0 || data.expeditionActive) return false;
        int current = GetEffectiveOutfitAmount(key);
        int available = Math.Max(0, (int)Math.Floor(GetStore(key)));
        int actual;
        if (amount > 0)
        {
            int byWeight = (int)Math.Floor((FreeLoadoutSpace + 0.0001f) / definition.Weight);
            actual = Math.Min(amount, Math.Min(available - current, byWeight));
        }
        else
        {
            actual = -Math.Min(-amount, current);
        }
        if (actual == 0) return false;
        SetOutfitAmount(key, current + actual);
        Emit(actual > 0 ? $"装入了{actual}份{definition.Label}。" :
            $"取出了{-actual}份{definition.Label}。", null);
        return true;
    }

    public bool Embark()
    {
        if (!CanEmbark) return false;
        data.expeditionInventory.Clear();
        foreach (LoadoutDefinition definition in LoadoutDefinitions)
        {
            int amount = GetEffectiveOutfitAmount(definition.Key);
            if (amount <= 0) continue;
            AddStore(definition.Key, -amount);
            data.expeditionInventory.Add(new DarkRoomInventoryEntry { key = definition.Key, amount = amount });
        }
        data.expeditionActive = true;
        data.expeditionX = 30;
        data.expeditionY = 30;
        data.expeditionWater = MaximumWater;
        data.expeditionHealth = MaximumHealth;
        data.expeditionFoodMoves = 0;
        data.expeditionWaterMoves = 0;
        BeginWorldExpedition();
        Emit("踏上了荒芜世界。", "embark");
        return true;
    }

    public bool ReturnHome()
    {
        if (!data.expeditionActive) return false;
        CommitWorldExpedition();
        foreach (DarkRoomInventoryEntry entry in data.expeditionInventory)
        {
            if (entry.amount > 0) AddStore(entry.key, entry.amount);
        }
        data.expeditionInventory.Clear();
        data.expeditionActive = false;
        data.expeditionX = 30;
        data.expeditionY = 30;
        Emit("平安回到了村落。", "dusty-path");
        return true;
    }

    private void EnsureLoadoutState()
    {
        data.outfit ??= new List<DarkRoomInventoryEntry>();
        data.expeditionInventory ??= new List<DarkRoomInventoryEntry>();
        foreach (DarkRoomInventoryEntry entry in data.outfit)
        {
            entry.key ??= string.Empty;
            entry.amount = Math.Max(0, entry.amount);
        }
        foreach (DarkRoomInventoryEntry entry in data.expeditionInventory)
        {
            entry.key ??= string.Empty;
            entry.amount = Math.Max(0, entry.amount);
        }
    }

    private void CopyLoadoutSaveData(DarkRoomSaveData save)
    {
        save.outfit = CopyInventory(data.outfit);
        save.expeditionInventory = CopyInventory(data.expeditionInventory);
        save.expeditionActive = data.expeditionActive;
        save.expeditionX = data.expeditionX;
        save.expeditionY = data.expeditionY;
        save.expeditionWater = data.expeditionWater;
        save.expeditionHealth = data.expeditionHealth;
        save.expeditionFoodMoves = data.expeditionFoodMoves;
        save.expeditionWaterMoves = data.expeditionWaterMoves;
    }

    private int GetEffectiveOutfitAmount(string key)
    {
        DarkRoomInventoryEntry entry = data.outfit.FirstOrDefault(item => item.key == key);
        int planned = entry?.amount ?? 0;
        return Math.Min(planned, Math.Max(0, (int)Math.Floor(GetStore(key))));
    }

    private void SetOutfitAmount(string key, int amount)
    {
        DarkRoomInventoryEntry entry = data.outfit.FirstOrDefault(item => item.key == key);
        if (entry == null)
        {
            entry = new DarkRoomInventoryEntry { key = key };
            data.outfit.Add(entry);
        }
        entry.amount = Math.Max(0, amount);
    }

    private static List<DarkRoomInventoryEntry> CopyInventory(IEnumerable<DarkRoomInventoryEntry> source)
    {
        return source.Select(entry => new DarkRoomInventoryEntry { key = entry.key, amount = entry.amount })
            .ToList();
    }

    private static LoadoutDefinition Loadout(string key, string label, float weight)
    {
        return new LoadoutDefinition { Key = key, Label = label, Weight = weight };
    }
}
