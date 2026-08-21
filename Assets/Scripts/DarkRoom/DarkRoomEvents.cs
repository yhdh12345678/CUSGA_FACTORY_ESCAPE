// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class DarkRoomEventChoice
{
    public string Key;
    public string Label;
    public string CostText;
    public bool Enabled;
}

public sealed class DarkRoomEventView
{
    public string Key;
    public string Title;
    public string Text;
    public IReadOnlyList<DarkRoomEventChoice> Choices;
}

public sealed partial class DarkRoomSaveData
{
    public float eventTimer = -1f;
    public string eventArea = string.Empty;
    public string activeEventKey = string.Empty;
    public string activeEventScene = string.Empty;
    public string activeEventResult = string.Empty;
    public float wandererWoodTimer = -1f;
    public int wandererWoodReward;
    public float wandererFurTimer = -1f;
    public int wandererFurReward;
    public bool worldUnlocked;
    public bool worldSeenAll;
    public int mapRevealCount;
    public List<string> perks = new List<string>();
}

public sealed partial class DarkRoomGameState
{
    private const float EventRetryDelay = 90f;
    private static readonly string[] RoomEventKeys =
    {
        "nomad", "noises-outside", "noises-inside", "beggar", "shady-builder",
        "wood-wanderer", "fur-wanderer", "scout", "master", "sick-man"
    };
    private static readonly string[] OutsideEventKeys =
    {
        "ruined-traps", "hut-fire", "sickness", "plague", "beast-attack", "soldier-raid"
    };

    public bool HasActiveEvent => !string.IsNullOrWhiteSpace(data.activeEventKey);
    public float EventTimer => data.eventTimer;
    public IReadOnlyList<string> GetRoomEventCatalogKeys() => RoomEventKeys;

    public IReadOnlyList<string> GetAvailableRoomEventKeys()
    {
        return RoomEventKeys.Where(IsRoomEventAvailable).ToArray();
    }

    public IReadOnlyList<string> GetOutsideEventCatalogKeys() => OutsideEventKeys;

    public IReadOnlyList<string> GetAvailableOutsideEventKeys()
    {
        return OutsideEventKeys.Where(IsOutsideEventAvailable).ToArray();
    }

    public DarkRoomEventView GetCurrentEvent()
    {
        if (!HasActiveEvent) return null;
        return new DarkRoomEventView
        {
            Key = data.activeEventKey,
            Title = GetEventTitle(data.activeEventKey),
            Text = string.IsNullOrWhiteSpace(data.activeEventResult)
                ? GetEventText(data.activeEventKey, data.activeEventScene)
                : $"{GetEventText(data.activeEventKey, data.activeEventScene)}\n结果：{data.activeEventResult}",
            Choices = GetEventChoices(data.activeEventKey, data.activeEventScene)
        };
    }

    public bool TryStartRoomEvent(string key)
    {
        if (HasActiveEvent || !RoomEventKeys.Contains(key) || !IsRoomEventAvailable(key)) return false;
        data.activeEventKey = key;
        data.activeEventScene = "start";
        data.activeEventResult = string.Empty;
        data.eventTimer = -1f;
        Emit(GetEventNotification(key), GetEventAudio(key));
        return true;
    }

    public bool TryStartOutsideEvent(string key)
    {
        if (HasActiveEvent || !OutsideEventKeys.Contains(key) || !IsOutsideEventAvailable(key)) return false;
        data.activeEventKey = key;
        data.activeEventScene = "start";
        data.activeEventResult = string.Empty;
        data.eventTimer = -1f;
        ApplyOutsideEventStart(key);
        Emit(GetEventNotification(key), GetEventAudio(key));
        return true;
    }

    public bool ActivateEventChoice(string choiceKey)
    {
        if (!HasActiveEvent) return false;
        string key = choiceKey.StartsWith("event-choice-", StringComparison.Ordinal)
            ? choiceKey.Substring("event-choice-".Length)
            : choiceKey;
        DarkRoomEventChoice choice = GetEventChoices(data.activeEventKey, data.activeEventScene)
            .FirstOrDefault(item => item.Key == key);
        if (choice == null || !choice.Enabled) return false;
        SpendEventCost(data.activeEventKey, data.activeEventScene, key);
        return ResolveEventChoice(data.activeEventKey, data.activeEventScene, key);
    }

    public void LeaveEventArea()
    {
        data.eventArea = string.Empty;
    }

    private void EnsureEventState()
    {
        data.eventArea ??= string.Empty;
        data.activeEventKey ??= string.Empty;
        data.activeEventScene ??= string.Empty;
        data.activeEventResult ??= string.Empty;
        data.perks ??= new List<string>();
    }

    private void CopyEventSaveData(DarkRoomSaveData save)
    {
        save.eventTimer = data.eventTimer;
        save.eventArea = data.eventArea;
        save.activeEventKey = data.activeEventKey;
        save.activeEventScene = data.activeEventScene;
        save.activeEventResult = data.activeEventResult;
        save.wandererWoodTimer = data.wandererWoodTimer;
        save.wandererWoodReward = data.wandererWoodReward;
        save.wandererFurTimer = data.wandererFurTimer;
        save.wandererFurReward = data.wandererFurReward;
        save.worldUnlocked = data.worldUnlocked;
        save.worldSeenAll = data.worldSeenAll;
        save.mapRevealCount = data.mapRevealCount;
        save.perks = new List<string>(data.perks);
    }

    private void SetEventArea(string area)
    {
        data.eventArea = area;
        if (!HasActiveEvent && data.eventTimer < 0f)
        {
            ScheduleNextEvent();
        }
    }

    private void ScheduleNextEvent()
    {
        data.eventTimer = ((int)Math.Floor(NextRandom() * 3f) + 3) * 60f;
    }

    private float GetEventTimerStep(float current)
    {
        current = GetEarlierTimer(current, data.eventTimer);
        current = GetEarlierTimer(current, data.wandererWoodTimer);
        return GetEarlierTimer(current, data.wandererFurTimer);
    }

    private void DecreaseEventTimers(float seconds)
    {
        if (data.eventTimer >= 0f) data.eventTimer -= seconds;
        if (data.wandererWoodTimer >= 0f) data.wandererWoodTimer -= seconds;
        if (data.wandererFurTimer >= 0f) data.wandererFurTimer -= seconds;
    }

    private bool ProcessEventTimers()
    {
        bool processed = false;
        if (data.wandererWoodTimer >= 0f && data.wandererWoodTimer <= 0f)
        {
            data.wandererWoodTimer = -1f;
            AddStore("wood", data.wandererWoodReward);
            data.wandererWoodReward = 0;
            Emit("神秘流浪者回来了，车上堆满了木头。", null);
            processed = true;
        }
        if (data.wandererFurTimer >= 0f && data.wandererFurTimer <= 0f)
        {
            data.wandererFurTimer = -1f;
            AddStore("fur", data.wandererFurReward);
            data.wandererFurReward = 0;
            Emit("神秘流浪者回来了，车上堆满了毛皮。", null);
            processed = true;
        }
        if (!HasActiveEvent && data.eventTimer >= 0f && data.eventTimer <= 0f)
        {
            if (data.eventArea == "room")
            {
                IReadOnlyList<string> available = GetAvailableRoomEventKeys();
                if (available.Count > 0)
                {
                    int index = Math.Min(available.Count - 1,
                        (int)Math.Floor(NextRandom() * available.Count));
                    TryStartRoomEvent(available[index]);
                }
                else
                {
                    data.eventTimer = EventRetryDelay;
                }
            }
            else if (data.eventArea == "outside")
            {
                IReadOnlyList<string> available = GetAvailableOutsideEventKeys();
                if (available.Count > 0)
                {
                    int index = Math.Min(available.Count - 1,
                        (int)Math.Floor(NextRandom() * available.Count));
                    TryStartOutsideEvent(available[index]);
                }
                else data.eventTimer = EventRetryDelay;
            }
            else
            {
                data.eventTimer = EventRetryDelay;
            }
            processed = true;
        }
        return processed;
    }

    private bool IsRoomEventAvailable(string key)
    {
        return key switch
        {
            "nomad" => GetStore("fur") > 0f,
            "noises-outside" or "noises-inside" or "wood-wanderer" => GetStore("wood") > 0f,
            "beggar" or "fur-wanderer" => GetStore("fur") > 0f,
            "shady-builder" => data.hutCount >= 5 && data.hutCount < 20,
            "scout" or "master" => data.worldUnlocked,
            "sick-man" => GetStore("medicine") > 0f,
            _ => false
        };
    }

    private bool IsOutsideEventAvailable(string key)
    {
        return key switch
        {
            "ruined-traps" => data.trapCount > 0,
            "hut-fire" => data.hutCount > 0 && data.population > 50,
            "sickness" => data.population > 10 && data.population < 50 && GetStore("medicine") > 0f,
            "plague" => data.population > 50 && GetStore("medicine") > 0f,
            "beast-attack" => data.population > 0,
            "soldier-raid" => data.population > 0 && data.cityCleared,
            _ => false
        };
    }

    private static string GetEventTitle(string key)
    {
        return key switch
        {
            "nomad" => "游牧商人",
            "noises-outside" or "noises-inside" => "异响",
            "beggar" => "乞丐",
            "shady-builder" => "可疑的建造者",
            "wood-wanderer" or "fur-wanderer" => "神秘流浪者",
            "scout" => "侦察兵",
            "master" => "大师",
            "sick-man" => "病人",
            "ruined-traps" => "毁坏的陷阱",
            "hut-fire" => "火灾",
            "sickness" => "疾病",
            "plague" => "瘟疫",
            "beast-attack" => "野兽袭击",
            "soldier-raid" => "军队突袭",
            _ => "事件"
        };
    }

    private static string GetEventAudio(string key)
    {
        return key switch
        {
            "nomad" => "event-nomad",
            "noises-outside" => "event-noises-outside",
            "noises-inside" => "event-noises-inside",
            "beggar" => "event-beggar",
            "shady-builder" => "event-shady-builder",
            "wood-wanderer" or "fur-wanderer" => "event-mysterious-wanderer",
            "scout" => "event-scout",
            "master" => "event-wandering-master",
            "sick-man" => "event-sick-man",
            "ruined-traps" => "event-ruined-trap",
            "hut-fire" => "event-hut-fire",
            "sickness" => "event-sickness",
            "plague" => "event-plague",
            "beast-attack" => "event-beast-attack",
            "soldier-raid" => "event-soldier-attack",
            _ => null
        };
    }

    private static string GetEventNotification(string key)
    {
        return key switch
        {
            "nomad" => "一名游牧商人前来交易。",
            "noises-outside" => "墙外传来奇怪的声音。",
            "noises-inside" => "储藏室里似乎有什么东西。",
            "beggar" => "一名乞丐来到这里。",
            "shady-builder" => "一名可疑的建造者路过。",
            "wood-wanderer" or "fur-wanderer" => "一名神秘流浪者来到这里。",
            "scout" => "一名侦察兵借宿一夜。",
            "master" => "一名年迈的流浪者来到这里。",
            "sick-man" => "一个病人蹒跚而来。",
            "ruined-traps" => "一些陷阱被毁坏了。",
            "hut-fire" => "一间小屋燃起大火。",
            "sickness" => "一些村民病倒了。",
            "plague" => "瘟疫正在村落中蔓延。",
            "beast-attack" => "野兽袭击了村民。",
            "soldier-raid" => "军队冲进了村落。",
            _ => "发生了一件事。"
        };
    }

    private static string GetEventText(string key, string scene)
    {
        if (scene == "start")
        {
            return key switch
            {
                "nomad" => "一个游牧商人拖着用粗绳扎紧的破袋走进视野。他不肯说从哪里来，显然也不会久留。",
                "noises-outside" => "墙外传来窸窸窣窣的脚步声。看不清那些影子在做什么。",
                "noises-inside" => "储藏室里传来抓挠声。里面藏着什么东西。",
                "beggar" => "一名乞丐前来，请求一些多余的毛皮，好熬过寒夜。",
                "shady-builder" => "一名可疑的建造者声称，只要三百根木头就能搭一间小屋。",
                "wood-wanderer" => "一个流浪者推着空车到来。他说若让他载走木头，以后会带回更多。建造者并不信任他。",
                "fur-wanderer" => "一个流浪者推着空车到来。她说若让她载走毛皮，以后会带回更多。建造者并不信任她。",
                "scout" => "侦察兵说自己走遍了许多地方，也愿意以合适的价格分享见闻。",
                "master" => "年迈的流浪者温和地微笑，请求在这里借宿一夜。",
                "sick-man" => "一个男人咳嗽着蹒跚而来，恳求得到药物。",
                "ruined-traps" => "几只陷阱被撕得粉碎，巨大的脚印伸向森林深处。",
                "hut-fire" => "烈火吞没了一间小屋，里面的居民没能逃出来。",
                "sickness" => "疾病正在村落中传播，必须立刻决定是否使用药物。",
                "plague" => "可怕的瘟疫迅速蔓延，需要大量药物才能控制。",
                "beast-attack" => "咆哮的野兽从树林涌出。战斗短暂而血腥，村民击退了它们，也付出了代价。",
                "soldier-raid" => "枪声穿过树林，全副武装的人冲进人群。袭击者最终被赶走，地上留下伤亡和弹药。",
                _ => string.Empty
            };
        }
        return (key, scene) switch
        {
            ("noises-outside", "nothing") => "模糊的影子在视野外移动，随后声音停了。",
            ("noises-outside", "stuff") => "门槛外放着一捆树枝，外面裹着粗糙的毛皮。夜色寂静。",
            ("noises-inside", "scales") => "一些木头不见了，地上散落着细小的鳞片。",
            ("noises-inside", "teeth") => "一些木头不见了，地上散落着细小的牙齿。",
            ("noises-inside", "cloth") => "一些木头不见了，地上散落着碎布。",
            ("beggar", "scales") => "乞丐连声道谢，留下一堆细小的鳞片。",
            ("beggar", "teeth") => "乞丐连声道谢，留下一堆细小的牙齿。",
            ("beggar", "cloth") => "乞丐连声道谢，留下一些碎布。",
            ("shady-builder", "steal") => "可疑的建造者带着木头逃走了。",
            ("shady-builder", "build") => "可疑的建造者确实搭好了一间小屋。",
            ("wood-wanderer", "given") => "流浪者推着装满木头的车离开。没人知道他会不会回来。",
            ("fur-wanderer", "given") => "流浪者推着装满毛皮的车离开。没人知道她会不会回来。",
            ("master", "wisdom") => "作为回报，流浪者愿意传授一项经验。",
            ("sick-man", "alloy") => "男人非常感激，留下旅途中捡到的一块奇异金属。",
            ("sick-man", "cells") => "男人非常感激，留下三个发光的盒子。",
            ("sick-man", "scales") => "男人非常感激，把仅有的五片鳞片留了下来。",
            ("sick-man", "nothing") => "男人道过谢，蹒跚着离开了。",
            ("ruined-traps", "nothing") => "脚印很快消失，森林里什么也没有。",
            ("ruined-traps", "catch") => "不远处躺着一头受伤的巨兽。它已经死了，留下大量毛皮、肉和牙齿。",
            ("sickness", "healed") => "疾病及时得到控制，病人逐渐康复。",
            ("sickness", "death") => "疾病蔓延，白天只剩埋葬死者，夜里尽是哭喊。",
            ("plague", "healed") => "瘟疫最终被控制，只有少数人死去。",
            ("plague", "death") => "瘟疫席卷村落，人口几乎被摧毁。",
            _ => string.Empty
        };
    }

    private IReadOnlyList<DarkRoomEventChoice> GetEventChoices(string key, string scene)
    {
        var choices = new List<DarkRoomEventChoice>();
        if (scene != "start")
        {
            if (key == "master" && scene == "wisdom")
            {
                choices.Add(EventChoice("evasive", "学习闪避", null, !HasPerk("evasive")));
                choices.Add(EventChoice("precise", "学习精准", null, !HasPerk("precise")));
                choices.Add(EventChoice("barbarian", "学习蛮力", null, !HasPerk("barbarian")));
                choices.Add(EventChoice("nothing", "什么也不学"));
                return choices;
            }
            choices.Add(EventChoice("leave", data.eventArea == "outside" ? "返回村落" : "返回房间"));
            return choices;
        }

        switch (key)
        {
            case "nomad":
                choices.Add(EventChoice("scales", "购买鳞片", Costs(MakeEventCost("fur", 100))));
                choices.Add(EventChoice("teeth", "购买牙齿", Costs(MakeEventCost("fur", 200))));
                choices.Add(EventChoice("bait", "购买诱饵", Costs(MakeEventCost("fur", 5))));
                choices.Add(EventChoice("compass", "购买罗盘",
                    Costs(MakeEventCost("fur", 300), MakeEventCost("scales", 15), MakeEventCost("teeth", 5)),
                    GetStore("compass") < 1f));
                choices.Add(EventChoice("goodbye", "告别"));
                break;
            case "noises-outside":
            case "noises-inside":
                choices.Add(EventChoice("investigate", "调查"));
                choices.Add(EventChoice("ignore", "忽略"));
                break;
            case "beggar":
                choices.Add(EventChoice("give50", "赠送五十份毛皮", Costs(MakeEventCost("fur", 50))));
                choices.Add(EventChoice("give100", "赠送一百份毛皮", Costs(MakeEventCost("fur", 100))));
                choices.Add(EventChoice("deny", "请他离开"));
                break;
            case "shady-builder":
                choices.Add(EventChoice("build", "支付三百根木头", Costs(MakeEventCost("wood", 300))));
                choices.Add(EventChoice("deny", "告别"));
                break;
            case "wood-wanderer":
                choices.Add(EventChoice("give100", "赠送一百根木头", Costs(MakeEventCost("wood", 100))));
                choices.Add(EventChoice("give500", "赠送五百根木头", Costs(MakeEventCost("wood", 500))));
                choices.Add(EventChoice("deny", "请他离开"));
                break;
            case "fur-wanderer":
                choices.Add(EventChoice("give100", "赠送一百份毛皮", Costs(MakeEventCost("fur", 100))));
                choices.Add(EventChoice("give500", "赠送五百份毛皮", Costs(MakeEventCost("fur", 500))));
                choices.Add(EventChoice("deny", "请她离开"));
                break;
            case "scout":
                choices.Add(EventChoice("map", "购买地图",
                    Costs(MakeEventCost("fur", 200), MakeEventCost("scales", 10)), !data.worldSeenAll));
                choices.Add(EventChoice("learn", "学习侦察",
                    Costs(MakeEventCost("fur", 1000), MakeEventCost("scales", 50), MakeEventCost("teeth", 20)),
                    !HasPerk("scout")));
                choices.Add(EventChoice("leave", "告别"));
                break;
            case "master":
                choices.Add(EventChoice("agree", "提供住处",
                    Costs(MakeEventCost("cured-meat", 100), MakeEventCost("fur", 100), MakeEventCost("torch", 1))));
                choices.Add(EventChoice("deny", "请他离开"));
                break;
            case "sick-man":
                choices.Add(EventChoice("help", "给予一份药物", Costs(MakeEventCost("medicine", 1))));
                choices.Add(EventChoice("ignore", "请他离开"));
                break;
            case "ruined-traps":
                choices.Add(EventChoice("track", "追踪脚印"));
                choices.Add(EventChoice("ignore", "忽略"));
                break;
            case "hut-fire":
            case "beast-attack":
            case "soldier-raid":
                choices.Add(EventChoice("mourn", "返回村落"));
                break;
            case "sickness":
                choices.Add(EventChoice("heal", "使用一份药物", Costs(MakeEventCost("medicine", 1))));
                choices.Add(EventChoice("ignore", "不处理"));
                break;
            case "plague":
                choices.Add(EventChoice("buy", "购买一份药物",
                    Costs(MakeEventCost("scales", 70), MakeEventCost("teeth", 50))));
                choices.Add(EventChoice("heal", "使用五份药物", Costs(MakeEventCost("medicine", 5))));
                choices.Add(EventChoice("ignore", "不处理"));
                break;
        }
        return choices;
    }

    private bool ResolveEventChoice(string eventKey, string scene, string choice)
    {
        if (scene != "start")
        {
            if (eventKey == "master" && scene == "wisdom" && choice != "nothing")
            {
                AddPerk(choice);
            }
            EndEvent();
            return true;
        }

        switch (eventKey)
        {
            case "nomad":
                if (choice == "goodbye") return EndEvent();
                string reward = choice == "compass" ? "compass" : choice;
                AddStore(reward, 1f);
                Emit(choice == "bait" ? "诱饵会让陷阱更有效。" :
                    choice == "compass" ? "旧罗盘凹陷而布满灰尘，但还能使用。" : "交易完成。", null);
                return true;
            case "noises-outside":
                if (choice == "ignore") return EndEvent();
                return EnterEventScene(NextRandom() < 0.3f ? "stuff" : "nothing");
            case "noises-inside":
                if (choice == "ignore") return EndEvent();
                float insideRoll = NextRandom();
                return EnterEventScene(insideRoll < 0.5f ? "scales" : insideRoll < 0.8f ? "teeth" : "cloth");
            case "beggar":
                if (choice == "deny") return EndEvent();
                float beggarRoll = NextRandom();
                string beggarScene = choice == "give50"
                    ? beggarRoll < 0.5f ? "scales" : beggarRoll < 0.8f ? "teeth" : "cloth"
                    : beggarRoll < 0.5f ? "teeth" : beggarRoll < 0.8f ? "scales" : "cloth";
                return EnterEventScene(beggarScene);
            case "shady-builder":
                if (choice == "deny") return EndEvent();
                return EnterEventScene(NextRandom() < 0.6f ? "steal" : "build");
            case "wood-wanderer":
            case "fur-wanderer":
                if (choice == "deny") return EndEvent();
                int amount = choice == "give500" ? 500 : 100;
                float chance = amount == 500 ? 0.3f : 0.5f;
                if (NextRandom() < chance)
                {
                    if (eventKey == "wood-wanderer")
                    {
                        data.wandererWoodTimer = 60f;
                        data.wandererWoodReward = amount * 3;
                    }
                    else
                    {
                        data.wandererFurTimer = 60f;
                        data.wandererFurReward = amount * 3;
                    }
                }
                return EnterEventScene("given");
            case "scout":
                if (choice == "leave") return EndEvent();
                if (choice == "map")
                {
                    RevealRandomWorldRegion();
                    Emit("地图揭示了世界的一部分。", null);
                }
                else
                {
                    AddPerk("scout");
                    Emit("学会了侦察。", null);
                }
                return true;
            case "master":
                if (choice == "deny") return EndEvent();
                return EnterEventScene("wisdom");
            case "sick-man":
                if (choice == "ignore") return EndEvent();
                float sickRoll = NextRandom();
                return EnterEventScene(sickRoll < 0.1f ? "alloy" : sickRoll < 0.3f ? "cells" :
                    sickRoll < 0.5f ? "scales" : "nothing");
            case "ruined-traps":
                if (choice == "ignore") return EndEvent();
                return EnterEventScene(NextRandom() < 0.5f ? "nothing" : "catch");
            case "hut-fire":
            case "beast-attack":
            case "soldier-raid":
                return EndEvent();
            case "sickness":
                if (choice == "heal") return EnterEventScene("healed");
                KillVillagers(1 + (int)Math.Floor(NextRandom() * Math.Max(1, data.population / 2)));
                return EnterEventScene("death");
            case "plague":
                if (choice == "buy")
                {
                    AddStore("medicine", 1);
                    Emit("买到了一份药物。", null);
                    return true;
                }
                if (choice == "heal")
                {
                    KillVillagers(2 + (int)Math.Floor(NextRandom() * 5));
                    return EnterEventScene("healed");
                }
                KillVillagers(10 + (int)Math.Floor(NextRandom() * 80));
                return EnterEventScene("death");
        }
        return false;
    }

    private bool EnterEventScene(string scene)
    {
        data.activeEventScene = scene;
        data.activeEventResult = ApplyEventSceneReward(data.activeEventKey, scene);
        Emit(GetEventText(data.activeEventKey, scene), null);
        return true;
    }

    private string ApplyEventSceneReward(string key, string scene)
    {
        if (key == "noises-outside" && scene == "stuff")
        {
            AddStore("wood", 100);
            AddStore("fur", 10);
            return "获得木头100根、毛皮10份。";
        }
        if (key == "noises-outside" && scene == "nothing")
        {
            return "没有获得或损失物品。";
        }
        if (key == "noises-inside" && (scene == "scales" || scene == "teeth" || scene == "cloth"))
        {
            int woodLost = Math.Max(1, (int)Math.Floor(GetStore("wood") * 0.1f));
            int reward = Math.Max(1, woodLost / 5);
            AddStore("wood", -woodLost);
            AddStore(scene, reward);
            return $"损失木头{woodLost}根，获得{StoreLabel(scene)}{reward}份。";
        }
        if (key == "beggar" && (scene == "scales" || scene == "teeth" || scene == "cloth"))
        {
            AddStore(scene, 20);
            return $"获得{StoreLabel(scene)}20份。";
        }
        if (key == "shady-builder" && scene == "build" && data.hutCount < 20)
        {
            data.hutCount++;
            if (data.populationTimer < 0f) SchedulePopulationIncrease();
            return "新增小屋1间。";
        }
        if (key == "shady-builder" && scene == "steal")
        {
            return "木头已支付，但没有建成小屋。";
        }
        if (key == "sick-man")
        {
            if (scene == "alloy")
            {
                AddStore("alien-alloy", 1);
                return "获得外星合金1块。";
            }
            if (scene == "cells")
            {
                AddStore("energy-cell", 3);
                return "获得能量电池3个。";
            }
            if (scene == "scales")
            {
                AddStore("scales", 5);
                return "获得鳞片5份。";
            }
            return "没有获得物品。";
        }
        if (key == "ruined-traps" && scene == "catch")
        {
            AddStore("fur", 100);
            AddStore("meat", 100);
            AddStore("teeth", 10);
            return "获得毛皮100份、肉100份、牙齿10份。";
        }
        if (key == "ruined-traps" && scene == "nothing")
        {
            return "没有找到猎物，也没有获得物品。";
        }
        return "本次结果没有带来额外物品。";
    }

    private void ApplyOutsideEventStart(string key)
    {
        if (key == "ruined-traps")
        {
            int destroyed = 1 + (int)Math.Floor(NextRandom() * data.trapCount);
            data.trapCount = Math.Max(0, data.trapCount - destroyed);
        }
        else if (key == "hut-fire")
        {
            data.hutCount = Math.Max(0, data.hutCount - 1);
            data.population = Math.Min(data.population, data.hutCount * 4);
            ReduceWorkersToPopulation();
        }
        else if (key == "beast-attack")
        {
            KillVillagers(1 + (int)Math.Floor(NextRandom() * 10));
            AddStore("fur", 100);
            AddStore("meat", 100);
            AddStore("teeth", 10);
        }
        else if (key == "soldier-raid")
        {
            KillVillagers(1 + (int)Math.Floor(NextRandom() * 40));
            AddStore("bullets", 10);
            AddStore("cured-meat", 50);
        }
    }

    private void KillVillagers(int amount)
    {
        data.population = Math.Max(0, data.population - Math.Max(0, amount));
        ReduceWorkersToPopulation();
    }

    private void ReduceWorkersToPopulation()
    {
        while (GetAssignedWorkerCount() > data.population)
        {
            string key = WorkerDefinitions.Select(definition => definition.Key)
                .FirstOrDefault(worker => GetWorkerCount(worker) > 0);
            if (key == null) break;
            SetWorkerCount(key, GetWorkerCount(key) - 1);
        }
    }

    private bool EndEvent()
    {
        data.activeEventKey = string.Empty;
        data.activeEventScene = string.Empty;
        data.activeEventResult = string.Empty;
        ScheduleNextEvent();
        Emit("事件结束。", null);
        return true;
    }

    private void SpendEventCost(string eventKey, string scene, string choice)
    {
        foreach (EventCost cost in GetEventCosts(eventKey, scene, choice))
        {
            AddStore(cost.Key, -cost.Amount);
        }
    }

    private IEnumerable<EventCost> GetEventCosts(string key, string scene, string choice)
    {
        if (scene != "start") return Array.Empty<EventCost>();
        return (key, choice) switch
        {
            ("nomad", "scales") => Costs(MakeEventCost("fur", 100)),
            ("nomad", "teeth") => Costs(MakeEventCost("fur", 200)),
            ("nomad", "bait") => Costs(MakeEventCost("fur", 5)),
            ("nomad", "compass") => Costs(MakeEventCost("fur", 300), MakeEventCost("scales", 15), MakeEventCost("teeth", 5)),
            ("beggar", "give50") => Costs(MakeEventCost("fur", 50)),
            ("beggar", "give100") => Costs(MakeEventCost("fur", 100)),
            ("shady-builder", "build") => Costs(MakeEventCost("wood", 300)),
            ("wood-wanderer", "give100") => Costs(MakeEventCost("wood", 100)),
            ("wood-wanderer", "give500") => Costs(MakeEventCost("wood", 500)),
            ("fur-wanderer", "give100") => Costs(MakeEventCost("fur", 100)),
            ("fur-wanderer", "give500") => Costs(MakeEventCost("fur", 500)),
            ("scout", "map") => Costs(MakeEventCost("fur", 200), MakeEventCost("scales", 10)),
            ("scout", "learn") => Costs(MakeEventCost("fur", 1000), MakeEventCost("scales", 50), MakeEventCost("teeth", 20)),
            ("master", "agree") => Costs(MakeEventCost("cured-meat", 100), MakeEventCost("fur", 100), MakeEventCost("torch", 1)),
            ("sick-man", "help") => Costs(MakeEventCost("medicine", 1)),
            ("sickness", "heal") => Costs(MakeEventCost("medicine", 1)),
            ("plague", "buy") => Costs(MakeEventCost("scales", 70), MakeEventCost("teeth", 50)),
            ("plague", "heal") => Costs(MakeEventCost("medicine", 5)),
            _ => Array.Empty<EventCost>()
        };
    }

    private DarkRoomEventChoice EventChoice(string key, string label,
        EventCost[] costs = null, bool additionallyAvailable = true)
    {
        costs ??= Array.Empty<EventCost>();
        return new DarkRoomEventChoice
        {
            Key = key,
            Label = label,
            CostText = costs.Length == 0 ? string.Empty : string.Join("；",
                costs.Select(cost => $"{StoreLabel(cost.Key)}{FormatAmount(cost.Amount)}")),
            Enabled = additionallyAvailable && costs.All(cost => GetStore(cost.Key) >= cost.Amount)
        };
    }

    private bool HasPerk(string key) => data.perks.Contains(key);

    private void AddPerk(string key)
    {
        if (!HasPerk(key)) data.perks.Add(key);
    }

    private readonly struct EventCost
    {
        public readonly string Key;
        public readonly float Amount;

        public EventCost(string key, float amount)
        {
            Key = key;
            Amount = amount;
        }
    }

    private static EventCost MakeEventCost(string key, float amount) => new EventCost(key, amount);
    private static EventCost[] Costs(params EventCost[] costs) => costs;
}
