// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class DarkRoomWorkerRole
{
    public string Key;
    public string Label;
    public string IncomeText;
    public int Count;
    public bool CanAssign;
    public bool CanRemove;
}

public sealed partial class DarkRoomSaveData
{
    public int ironMineCount;
    public int coalMineCount;
    public int sulphurMineCount;
    public int tanners;
    public int charcutiers;
    public int ironMiners;
    public int coalMiners;
    public int sulphurMiners;
    public int steelworkers;
    public int armourers;
}

public sealed partial class DarkRoomGameState
{
    private sealed class WorkerDefinition
    {
        public string Key;
        public string Label;
        public string IncomeText;
        public Func<DarkRoomGameState, bool> Unlocked;
    }

    private static readonly WorkerDefinition[] WorkerDefinitions =
    {
        Worker("hunter", "猎人", "每十秒获得0.5份毛皮和0.5份肉", state => state.data.lodgeCount > 0),
        Worker("trapper", "陷阱师", "每十秒消耗1份肉，制作1份诱饵", state => state.data.lodgeCount > 0),
        Worker("tanner", "制革师", "每十秒消耗5份毛皮，制作1份皮革", state => state.data.tanneryCount > 0),
        Worker("charcutier", "熏肉师", "每十秒消耗5份肉和5根木头，制作1份熏肉", state => state.data.smokehouseCount > 0),
        Worker("iron-miner", "铁矿工", "每十秒消耗1份熏肉，开采1份铁", state => state.data.ironMineCount > 0),
        Worker("coal-miner", "煤矿工", "每十秒消耗1份熏肉，开采1份煤", state => state.data.coalMineCount > 0),
        Worker("sulphur-miner", "硫磺矿工", "每十秒消耗1份熏肉，开采1份硫磺", state => state.data.sulphurMineCount > 0),
        Worker("steelworker", "炼钢工", "每十秒消耗1份铁和1份煤，制作1份钢", state => state.data.steelworksCount > 0),
        Worker("armourer", "军械师", "每十秒消耗1份钢和1份硫磺，制作1颗子弹", state => state.data.armouryCount > 0)
    };

    public IReadOnlyList<DarkRoomWorkerRole> GetWorkerRoles()
    {
        return WorkerDefinitions.Where(definition => definition.Unlocked(this))
            .Select(definition => new DarkRoomWorkerRole
            {
                Key = definition.Key,
                Label = definition.Label,
                IncomeText = definition.IncomeText,
                Count = GetWorkerCount(definition.Key),
                CanAssign = Gatherers > 0,
                CanRemove = GetWorkerCount(definition.Key) > 0
            })
            .ToArray();
    }

    public bool AssignWorker(string key, int amount)
    {
        WorkerDefinition definition = WorkerDefinitions.FirstOrDefault(item => item.Key == key);
        if (definition == null || !definition.Unlocked(this) || amount == 0) return false;
        int current = GetWorkerCount(key);
        int actual = amount > 0 ? Math.Min(amount, Gatherers) : -Math.Min(-amount, current);
        if (actual == 0) return false;
        SetWorkerCount(key, current + actual);
        if (data.villageIncomeTimer < 0f) data.villageIncomeTimer = 1f;
        Emit(actual > 0
            ? $"分配了{actual}名{definition.Label}。"
            : $"撤回了{-actual}名{definition.Label}。", null);
        return true;
    }

    private int GetAssignedWorkerCount()
    {
        return WorkerDefinitions.Sum(definition => GetWorkerCount(definition.Key));
    }

    private void CollectAllWorkerIncome()
    {
        int hunters = data.hunters;
        if (hunters > 0)
        {
            AddStore("fur", hunters * 0.5f);
            AddStore("meat", hunters * 0.5f);
        }
        ApplyWorkerIncome(data.trappers, new[] { Pair("meat", 1) }, Pair("bait", 1));
        ApplyWorkerIncome(data.tanners, new[] { Pair("fur", 5) }, Pair("leather", 1));
        ApplyWorkerIncome(data.charcutiers,
            new[] { Pair("meat", 5), Pair("wood", 5) }, Pair("cured-meat", 1));
        ApplyWorkerIncome(data.ironMiners, new[] { Pair("cured-meat", 1) }, Pair("iron", 1));
        ApplyWorkerIncome(data.coalMiners, new[] { Pair("cured-meat", 1) }, Pair("coal", 1));
        ApplyWorkerIncome(data.sulphurMiners, new[] { Pair("cured-meat", 1) }, Pair("sulphur", 1));
        ApplyWorkerIncome(data.steelworkers,
            new[] { Pair("iron", 1), Pair("coal", 1) }, Pair("steel", 1));
        ApplyWorkerIncome(data.armourers,
            new[] { Pair("steel", 1), Pair("sulphur", 1) }, Pair("bullets", 1));
    }

    private void ApplyWorkerIncome(int count, StorePair[] costs, StorePair output)
    {
        if (count <= 0 || costs.Any(cost => GetStore(cost.Key) < cost.Amount * count)) return;
        foreach (StorePair cost in costs) AddStore(cost.Key, -cost.Amount * count);
        AddStore(output.Key, output.Amount * count);
    }

    private int GetWorkerCount(string key)
    {
        return key switch
        {
            "hunter" => data.hunters,
            "trapper" => data.trappers,
            "tanner" => data.tanners,
            "charcutier" => data.charcutiers,
            "iron-miner" => data.ironMiners,
            "coal-miner" => data.coalMiners,
            "sulphur-miner" => data.sulphurMiners,
            "steelworker" => data.steelworkers,
            "armourer" => data.armourers,
            _ => 0
        };
    }

    private void SetWorkerCount(string key, int value)
    {
        switch (key)
        {
            case "hunter": data.hunters = value; break;
            case "trapper": data.trappers = value; break;
            case "tanner": data.tanners = value; break;
            case "charcutier": data.charcutiers = value; break;
            case "iron-miner": data.ironMiners = value; break;
            case "coal-miner": data.coalMiners = value; break;
            case "sulphur-miner": data.sulphurMiners = value; break;
            case "steelworker": data.steelworkers = value; break;
            case "armourer": data.armourers = value; break;
        }
    }

    private void CopyWorkerSaveData(DarkRoomSaveData save)
    {
        save.ironMineCount = data.ironMineCount;
        save.coalMineCount = data.coalMineCount;
        save.sulphurMineCount = data.sulphurMineCount;
        save.tanners = data.tanners;
        save.charcutiers = data.charcutiers;
        save.ironMiners = data.ironMiners;
        save.coalMiners = data.coalMiners;
        save.sulphurMiners = data.sulphurMiners;
        save.steelworkers = data.steelworkers;
        save.armourers = data.armourers;
    }

    private readonly struct StorePair
    {
        public readonly string Key;
        public readonly float Amount;

        public StorePair(string key, float amount)
        {
            Key = key;
            Amount = amount;
        }
    }

    private static StorePair Pair(string key, float amount) => new StorePair(key, amount);

    private static WorkerDefinition Worker(string key, string label, string income,
        Func<DarkRoomGameState, bool> unlocked)
    {
        return new WorkerDefinition { Key = key, Label = label, IncomeText = income, Unlocked = unlocked };
    }
}
