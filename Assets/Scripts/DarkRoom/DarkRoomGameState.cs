// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

[Serializable]
public sealed partial class DarkRoomSaveData
{
    public int fireLevel;
    public int temperature;
    public int wood;
    public int builderLevel = -1;
    public bool woodUnlocked;
    public bool seenForest;
    public float fireTimer = -1f;
    public float temperatureTimer = 30f;
    public float builderTimer = -1f;
    public float woodUnlockTimer = -1f;
    public float gatherTimer = -1f;
    public float trapTimer = -1f;
    public float builderIncomeTimer = -1f;
    public float villageIncomeTimer = -1f;
    public float populationTimer = -1f;
    public int trapCount;
    public int cartCount;
    public int hutCount;
    public int lodgeCount;
    public int population;
    public int hunters;
    public int trappers;
    public float fur;
    public float meat;
    public float bait;
    public float scales;
    public float teeth;
    public float cloth;
    public float charm;
    public int randomState = 1;
    public string lastMessage = "房间冰冷刺骨，火堆已经熄灭。";
}

public sealed partial class DarkRoomGameState
{
    public const float FireCoolDelay = 300f;
    public const float TemperatureDelay = 30f;
    public const float BuilderDelay = 30f;
    public const float NeedWoodDelay = 15f;
    public const float GatherDelay = 60f;
    public const float TrapDelay = 90f;
    public const float IncomeDelay = 10f;

    private readonly DarkRoomSaveData data;

    public DarkRoomGameState(DarkRoomSaveData saveData = null)
    {
        data = saveData ?? new DarkRoomSaveData();
        if (data.randomState == 0)
        {
            data.randomState = 1;
        }
        EnsureContentState();
        EnsureEventState();
        EnsureLoadoutState();
        EnsureWorldState();
        EnsureLandmarkState();
        EnsureShipState();
    }

    public event Action Changed;
    public event Action<string> AnnouncementRequested;
    public event Action<string> AudioRequested;

    public int FireLevel => data.fireLevel;
    public int Temperature => data.temperature;
    public int Wood => data.wood;
    public int BuilderLevel => data.builderLevel;
    public bool WoodUnlocked => data.woodUnlocked;
    public bool SeenForest => data.seenForest;
    public string LastMessage => data.lastMessage;
    public int TrapCount => data.trapCount;
    public int CartCount => data.cartCount;
    public int HutCount => data.hutCount;
    public int LodgeCount => data.lodgeCount;
    public int Population => data.population;
    public int Hunters => data.hunters;
    public int Trappers => data.trappers;
    public int Gatherers => Math.Max(0, data.population - GetAssignedWorkerCount());
    public int MaxPopulation => data.hutCount * 4;
    public float Fur => data.fur;
    public float Meat => data.meat;
    public float Bait => data.bait;
    public float Scales => data.scales;
    public float Teeth => data.teeth;
    public float Cloth => data.cloth;
    public float Charm => data.charm;
    public float PopulationTimer => data.populationTimer;
    public float TotalTrapLoot => data.fur + data.meat + data.scales + data.teeth + data.cloth + data.charm;
    public int GatherAmount => data.cartCount > 0 ? 50 : 10;
    public int TrapCost => 10 + data.trapCount * 10;
    public int HutCost => 100 + data.hutCount * 50;

    public bool CanLightFire => data.fireLevel == 0 && (!data.woodUnlocked || data.wood >= 5);
    public bool CanStokeFire => data.fireLevel > 0 && (!data.woodUnlocked || data.wood > 0);
    public bool CanGatherWood => data.woodUnlocked && data.gatherTimer < 0f;
    public bool CanCheckTraps => data.trapCount > 0 && data.trapTimer < 0f;
    public bool CanManageWorkers => data.population > 0 && GetWorkerRoles().Count > 0;
    public bool CanAssignHunter => data.lodgeCount > 0 && Gatherers > 0;
    public bool CanRemoveHunter => data.hunters > 0;
    public bool CanAssignTrapper => data.lodgeCount > 0 && Gatherers > 0;
    public bool CanRemoveTrapper => data.trappers > 0;

    public bool TrapBuildVisible => IsBuildVisible(data.trapCount, TrapCost, true);
    public bool CartBuildVisible => IsBuildVisible(data.cartCount, 30, true);
    public bool HutBuildVisible => IsBuildVisible(data.hutCount, HutCost, true);
    public bool LodgeBuildVisible => IsBuildVisible(data.lodgeCount, 200, data.fur > 0f && data.meat > 0f);
    public bool CanBuildTrap => data.builderLevel >= 4 && data.trapCount < 10 && data.wood >= TrapCost;
    public bool CanBuildCart => data.builderLevel >= 4 && data.cartCount < 1 && data.wood >= 30;
    public bool CanBuildHut => data.builderLevel >= 4 && data.hutCount < 20 && data.wood >= HutCost;
    public bool CanBuildLodge => data.builderLevel >= 4 && data.lodgeCount < 1 &&
        data.wood >= 200 && data.fur >= 10f && data.meat >= 5f;

    public string FireText => data.fireLevel switch
    {
        0 => "熄灭",
        1 => "冒着余烟",
        2 => "微微闪烁",
        3 => "稳定燃烧",
        _ => "熊熊燃烧"
    };

    public string TemperatureText => data.temperature switch
    {
        0 => "冰冷刺骨",
        1 => "寒冷",
        2 => "微凉",
        3 => "温暖",
        _ => "炎热"
    };

    public string BuilderText => data.builderLevel switch
    {
        < 0 => "尚未出现",
        0 => "正在靠近",
        1 => "倒在角落",
        2 => "仍在发抖",
        3 => "呼吸平稳",
        _ => "正在帮忙"
    };

    public string VillageTitle => data.hutCount switch
    {
        0 => "寂静的林地",
        1 => "孤零零的小屋",
        <= 4 => "小小的村落",
        <= 8 => "朴素的村落",
        <= 14 => "庞大的村落",
        _ => "喧闹的村落"
    };

    public DarkRoomSaveData CreateSaveData()
    {
        var save = new DarkRoomSaveData
        {
            fireLevel = data.fireLevel,
            temperature = data.temperature,
            wood = data.wood,
            builderLevel = data.builderLevel,
            woodUnlocked = data.woodUnlocked,
            seenForest = data.seenForest,
            fireTimer = data.fireTimer,
            temperatureTimer = data.temperatureTimer,
            builderTimer = data.builderTimer,
            woodUnlockTimer = data.woodUnlockTimer,
            gatherTimer = data.gatherTimer,
            trapTimer = data.trapTimer,
            builderIncomeTimer = data.builderIncomeTimer,
            villageIncomeTimer = data.villageIncomeTimer,
            populationTimer = data.populationTimer,
            trapCount = data.trapCount,
            cartCount = data.cartCount,
            hutCount = data.hutCount,
            lodgeCount = data.lodgeCount,
            population = data.population,
            hunters = data.hunters,
            trappers = data.trappers,
            fur = data.fur,
            meat = data.meat,
            bait = data.bait,
            scales = data.scales,
            teeth = data.teeth,
            cloth = data.cloth,
            charm = data.charm,
            randomState = data.randomState,
            lastMessage = data.lastMessage
        };
        CopyContentSaveData(save);
        CopyEventSaveData(save);
        CopyLoadoutSaveData(save);
        CopyWorldSaveData(save);
        CopyLandmarkSaveData(save);
        CopyShipSaveData(save);
        return save;
    }

    public bool LightFire()
    {
        if (!CanLightFire)
        {
            Emit("木头不够，生火需要五根木头。", null);
            return false;
        }

        if (data.woodUnlocked)
        {
            data.wood -= 5;
        }
        data.fireLevel = 3;
        data.fireTimer = FireCoolDelay;
        if (data.builderLevel < 0)
        {
            data.builderLevel = 0;
            data.builderTimer = BuilderDelay;
        }
        Emit("火堆燃烧起来，火光越过窗户，照进外面的黑暗。", "light-fire");
        return true;
    }

    public bool StokeFire()
    {
        if (!CanStokeFire)
        {
            Emit("木头已经用完。", null);
            return false;
        }
        if (data.woodUnlocked)
        {
            data.wood--;
        }
        if (data.fireLevel < 4)
        {
            data.fireLevel++;
        }
        data.fireTimer = FireCoolDelay;
        Emit($"添入木头，火堆现在{FireText}。", "stoke-fire");
        return true;
    }

    public bool GatherWood()
    {
        if (!CanGatherWood)
        {
            return false;
        }
        data.wood += GatherAmount;
        data.gatherTimer = GatherDelay;
        Emit($"捡起干枯的灌木和树枝，带回{GatherAmount}根木头。", "gather-wood");
        return true;
    }

    public bool EnterOutside()
    {
        if (!data.woodUnlocked)
        {
            return false;
        }
        if (!data.seenForest)
        {
            data.seenForest = true;
            Emit("天空灰暗，风一直吹个不停。", null);
        }
        SetEventArea("outside");
        return true;
    }

    public bool EnterRoom()
    {
        SetEventArea("room");
        if (data.builderLevel == 3 && data.seenForest)
        {
            data.builderLevel = 4;
            data.builderIncomeTimer = 1f;
            Emit("陌生人站在火边。她说自己能帮忙，也会建造东西。", null);
        }
        return true;
    }

    public bool BuildTrap()
    {
        if (!CanBuildTrap) return false;
        data.wood -= TrapCost;
        data.trapCount++;
        Emit(data.trapCount == 1 ? "建造者做好了第一只陷阱。" : "更多陷阱被放进林地。", "build");
        return true;
    }

    public bool BuildCart()
    {
        if (!CanBuildCart) return false;
        data.wood -= 30;
        data.cartCount = 1;
        Emit("摇摇晃晃的推车可以从林地运回更多木头。", "build");
        return true;
    }

    public bool BuildHut()
    {
        if (!CanBuildHut) return false;
        data.wood -= HutCost;
        data.hutCount++;
        if (data.populationTimer < 0f)
        {
            SchedulePopulationIncrease();
        }
        Emit("建造者在林地里搭起一间小屋。消息会慢慢传开。", "build");
        return true;
    }

    public bool BuildLodge()
    {
        if (!CanBuildLodge) return false;
        data.wood -= 200;
        data.fur -= 10f;
        data.meat -= 5f;
        data.lodgeCount = 1;
        Emit("猎人小屋立在远离村落的林地中。", "build");
        return true;
    }

    public bool AssignHunter(int amount)
    {
        return AssignWorker("hunter", amount);
    }

    public bool AssignTrapper(int amount)
    {
        return AssignWorker("trapper", amount);
    }

    public bool CheckTraps()
    {
        if (!CanCheckTraps)
        {
            return false;
        }
        int baitUsed = Math.Min(data.trapCount, (int)Math.Floor(data.bait));
        int attempts = data.trapCount + baitUsed;
        data.bait -= baitUsed;
        for (int index = 0; index < attempts; index++)
        {
            float roll = NextRandom();
            if (roll < 0.5f) data.fur++;
            else if (roll < 0.75f) data.meat++;
            else if (roll < 0.85f) data.scales++;
            else if (roll < 0.93f) data.teeth++;
            else if (roll < 0.995f) data.cloth++;
            else data.charm++;
        }
        data.trapTimer = TrapDelay;
        Emit($"检查陷阱，共找到{attempts}份猎物。", "check-traps");
        return true;
    }

    public void Advance(float seconds)
    {
        if (seconds <= 0f) return;
        float remaining = seconds;
        while (remaining > 0f)
        {
            float step = GetNextStep(remaining);
            DecreaseActiveTimers(step);
            remaining -= step;
            bool processed = ProcessDueTimers();
            if (step <= 0f && !processed) break;
        }
    }

    private bool IsBuildVisible(int count, int woodCost, bool componentsSeen)
    {
        return data.builderLevel >= 4 && componentsSeen &&
            (count > 0 || data.wood >= woodCost * 0.5f);
    }

    private float GetNextStep(float maximum)
    {
        float next = maximum;
        next = GetEarlierTimer(next, data.fireTimer);
        next = GetEarlierTimer(next, data.temperatureTimer);
        next = GetEarlierTimer(next, data.builderTimer);
        next = GetEarlierTimer(next, data.woodUnlockTimer);
        next = GetEarlierTimer(next, data.gatherTimer);
        next = GetEarlierTimer(next, data.trapTimer);
        next = GetEarlierTimer(next, data.builderIncomeTimer);
        next = GetEarlierTimer(next, data.villageIncomeTimer);
        next = GetEarlierTimer(next, data.populationTimer);
        next = GetEventTimerStep(next);
        return next;
    }

    private static float GetEarlierTimer(float current, float timer)
    {
        return timer >= 0f && timer < current ? timer : current;
    }

    private void DecreaseActiveTimers(float seconds)
    {
        if (data.fireTimer >= 0f) data.fireTimer -= seconds;
        if (data.temperatureTimer >= 0f) data.temperatureTimer -= seconds;
        if (data.builderTimer >= 0f) data.builderTimer -= seconds;
        if (data.woodUnlockTimer >= 0f) data.woodUnlockTimer -= seconds;
        if (data.gatherTimer >= 0f) data.gatherTimer -= seconds;
        if (data.trapTimer >= 0f) data.trapTimer -= seconds;
        if (data.builderIncomeTimer >= 0f) data.builderIncomeTimer -= seconds;
        if (data.villageIncomeTimer >= 0f) data.villageIncomeTimer -= seconds;
        if (data.populationTimer >= 0f) data.populationTimer -= seconds;
        DecreaseEventTimers(seconds);
    }

    private bool ProcessDueTimers()
    {
        bool processed = false;
        if (data.fireLevel > 0 && data.fireTimer <= 0f)
        {
            bool builderStoked = data.fireLevel <= 2 && data.builderLevel > 3 && data.wood > 0;
            if (builderStoked)
            {
                data.wood--;
                data.fireLevel++;
            }
            data.fireLevel--;
            data.fireTimer = data.fireLevel > 0 ? FireCoolDelay : -1f;
            Emit(builderStoked ? "建造者往火堆里添了一根木头。" : $"火势渐弱，火堆现在{FireText}。",
                builderStoked ? "stoke-fire" : null);
            processed = true;
        }

        if (data.temperatureTimer <= 0f)
        {
            data.temperatureTimer = TemperatureDelay;
            int oldTemperature = data.temperature;
            if (data.temperature > data.fireLevel) data.temperature--;
            else if (data.temperature < 4 && data.temperature < data.fireLevel) data.temperature++;
            if (data.temperature != oldTemperature)
            {
                Emit($"房间现在{TemperatureText}。", null);
            }
            processed = true;
        }

        if (data.builderTimer >= 0f && data.builderTimer <= 0f)
        {
            if (data.builderLevel == 0)
            {
                data.builderLevel = 1;
                data.woodUnlockTimer = NeedWoodDelay;
                Emit("一个衣衫褴褛的陌生人跌进门内，倒在角落。", null);
            }
            else if (data.builderLevel < 3 && data.temperature >= 3)
            {
                data.builderLevel++;
                Emit(data.builderLevel == 2
                    ? "陌生人瑟瑟发抖，低声说着无法听清的话。"
                    : "角落里的陌生人不再发抖，呼吸逐渐平稳。", null);
            }
            data.builderTimer = data.builderLevel < 3 ? BuilderDelay : -1f;
            processed = true;
        }

        if (!data.woodUnlocked && data.woodUnlockTimer >= 0f && data.woodUnlockTimer <= 0f)
        {
            data.woodUnlockTimer = -1f;
            data.woodUnlocked = true;
            data.wood = 4;
            Emit("屋外狂风呼啸，木头快要用完了。林地已经可以前往。", null);
            processed = true;
        }

        if (data.gatherTimer >= 0f && data.gatherTimer <= 0f)
        {
            data.gatherTimer = -1f;
            MarkChanged();
            processed = true;
        }
        if (data.trapTimer >= 0f && data.trapTimer <= 0f)
        {
            data.trapTimer = -1f;
            MarkChanged();
            processed = true;
        }

        if (data.builderLevel >= 4 && data.builderIncomeTimer >= 0f && data.builderIncomeTimer <= 0f)
        {
            data.wood += 2;
            data.builderIncomeTimer = IncomeDelay;
            MarkChanged();
            processed = true;
        }

        if (data.population > 0 && data.villageIncomeTimer >= 0f && data.villageIncomeTimer <= 0f)
        {
            CollectVillageIncome();
            data.villageIncomeTimer = IncomeDelay;
            MarkChanged();
            processed = true;
        }

        if (data.hutCount > 0 && data.populationTimer >= 0f && data.populationTimer <= 0f)
        {
            IncreasePopulation();
            processed = true;
        }
        if (ProcessEventTimers()) processed = true;
        return processed;
    }

    private void CollectVillageIncome()
    {
        data.wood += Gatherers;
        CollectAllWorkerIncome();
    }

    private void IncreasePopulation()
    {
        int space = MaxPopulation - data.population;
        if (space > 0)
        {
            int arrivals = (int)Math.Floor(NextRandom() * (space / 2f) + space / 2f);
            if (arrivals == 0) arrivals = 1;
            data.population += arrivals;
            if (data.villageIncomeTimer < 0f) data.villageIncomeTimer = 1f;
            string message = arrivals switch
            {
                1 => "夜里来了一名陌生人。",
                < 5 => "一个饱经风霜的家庭住进了小屋。",
                < 10 => "一小群满身尘土、瘦骨嶙峋的人来到这里。",
                < 30 => "一支车队摇摇晃晃地抵达，带着忧虑和希望。",
                _ => "村落正在兴旺起来，消息确实传开了。"
            };
            SchedulePopulationIncrease();
            Emit(message, null);
            return;
        }
        SchedulePopulationIncrease();
        MarkChanged();
    }

    private void SchedulePopulationIncrease()
    {
        data.populationTimer = ((int)Math.Floor(NextRandom() * 2.5f) * 60f) + 30f;
    }

    private float NextRandom()
    {
        long next = ((long)data.randomState * 1103515245L + 12345L) & 0x7fffffffL;
        data.randomState = (int)next;
        return data.randomState / 2147483648f;
    }

    private void MarkChanged()
    {
        Changed?.Invoke();
    }

    private void Emit(string message, string audioKey)
    {
        data.lastMessage = message;
        if (!string.IsNullOrWhiteSpace(audioKey)) AudioRequested?.Invoke(audioKey);
        AnnouncementRequested?.Invoke(message);
        Changed?.Invoke();
    }
}
