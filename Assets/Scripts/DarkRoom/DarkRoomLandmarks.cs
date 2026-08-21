// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class DarkRoomLandmarkAction
{
    public string Key;
    public string Label;
    public string Value;
    public bool Enabled;
}

public sealed class DarkRoomLandmarkView
{
    public string Title;
    public string Text;
    public string Status;
    public bool InCombat;
    public IReadOnlyList<DarkRoomLandmarkAction> Actions;
}

public sealed partial class DarkRoomSaveData
{
    public string activeLandmarkTile = string.Empty;
    public string landmarkStage = string.Empty;
    public string enemyName = string.Empty;
    public int enemyHealth;
    public int enemyMaximumHealth;
    public int enemyDamage;
    public bool enemyStunned;
    public List<string> expeditionDiscoveries = new List<string>();
    public List<string> expeditionUsedOutposts = new List<string>();
    public bool shipFound;
    public bool cityCleared;
}

public sealed partial class DarkRoomGameState
{
    public bool HasActiveLandmark => !string.IsNullOrWhiteSpace(data.activeLandmarkTile);
    public bool InCombat => HasActiveLandmark && data.landmarkStage == "combat";
    public bool CanInspectCurrentLandmark => data.expeditionActive && IsLandmark(
        GetExpeditionTile(data.expeditionX, data.expeditionY));
    public bool ShipFound => data.shipFound;

    public DarkRoomLandmarkView GetLandmarkView()
    {
        if (!HasActiveLandmark) return null;
        char tile = data.activeLandmarkTile[0];
        if (InCombat)
        {
            return new DarkRoomLandmarkView
            {
                Title = TileLabel(tile),
                Text = GetCombatText(tile),
                Status = $"生命{data.expeditionHealth}/{MaximumHealth}；{data.enemyName}生命{data.enemyHealth}/{data.enemyMaximumHealth}。",
                InCombat = true,
                Actions = GetCombatActions()
            };
        }
        return new DarkRoomLandmarkView
        {
            Title = TileLabel(tile),
            Text = GetLandmarkText(tile, data.landmarkStage),
            Status = string.Empty,
            Actions = GetLandmarkActions(tile, data.landmarkStage)
        };
    }

    public bool StartCurrentLandmark()
    {
        if (!CanInspectCurrentLandmark || HasActiveLandmark) return false;
        char tile = GetExpeditionTile(data.expeditionX, data.expeditionY);
        data.activeLandmarkTile = tile.ToString();
        data.landmarkStage = IsCurrentVisited() ? "outpost" : "start";
        Emit(IsCurrentVisited() ? "这里已经清理过，可以安全休整。" : TileDescription(tile),
            LandmarkAudio(tile));
        return true;
    }

    public bool ActivateLandmarkAction(string actionKey)
    {
        if (!HasActiveLandmark) return false;
        string key = actionKey.StartsWith("landmark-", StringComparison.Ordinal)
            ? actionKey.Substring("landmark-".Length) : actionKey;
        DarkRoomLandmarkAction action = GetLandmarkView().Actions.FirstOrDefault(item => item.Key == key);
        if (action == null || !action.Enabled) return false;
        char tile = data.activeLandmarkTile[0];
        if (key.StartsWith("attack-", StringComparison.Ordinal))
            return AttackEnemy(key.Substring("attack-".Length));
        if (key == "medicine") return UseCombatMedicine();
        if (key == "eat") return UseCombatMeat();
        if (key == "leave") return LeaveLandmark();
        if (key == "rest")
        {
            string coordinate = CurrentCoordinate();
            if (!data.expeditionUsedOutposts.Contains(coordinate))
            {
                data.expeditionUsedOutposts.Add(coordinate);
                data.expeditionWater = MaximumWater;
                Emit("水已经补满。", null);
            }
            return LeaveLandmark();
        }
        if (key == "enter") return EnterLandmark(tile);
        if (key == "salvage")
        {
            CompleteLandmark(tile);
            return LeaveLandmark();
        }
        return false;
    }

    private bool EnterLandmark(char tile)
    {
        if ((tile == 'V' || tile == 'I') && GetExpeditionAmount("torch") <= 0) return false;
        if (tile == 'V' || tile == 'I') SetExpeditionAmount("torch", GetExpeditionAmount("torch") - 1);
        switch (tile)
        {
            case 'H':
                float houseRoll = NextRandom();
                if (houseRoll < 0.25f)
                {
                    AddExpeditionLoot("medicine", RandomAmount(2, 5));
                    CompleteLandmark(tile);
                    data.landmarkStage = "cleared";
                    Emit("地板下藏着一些药物。", null);
                    return true;
                }
                if (houseRoll < 0.5f)
                {
                    AddExpeditionLoot("cured-meat", RandomAmount(1, 10));
                    data.expeditionWater = MaximumWater;
                    CompleteLandmark(tile);
                    data.landmarkStage = "cleared";
                    Emit("老井里还有水，屋内也剩下一些补给。", null);
                    return true;
                }
                StartCombat("占屋者", 10, 3);
                break;
            case 'V': StartCombat("洞穴野兽", 10, 4); break;
            case 'O': StartCombat("武装拾荒者", 30, 6); break;
            case 'Y': StartCombat("退伍老兵", 50, 8); break;
            case 'I': StartCombat("野兽母兽", 10, 4); break;
            case 'C': StartCombat("营地主人", 20, 5); break;
            case 'S': StartCombat("军队老兵", 65, 10); break;
            case 'M':
                if (GetExpeditionAmount("charm") <= 0) return false;
                SetExpeditionAmount("charm", GetExpeditionAmount("charm") - 1);
                AddPerk("gastronome");
                CompleteLandmark(tile);
                data.landmarkStage = "cleared";
                Emit("沼泽中的老人收下护符，讲述了如何从食物中恢复更多体力。", null);
                break;
            default:
                CollectPassiveLandmark(tile);
                break;
        }
        return true;
    }

    private void StartCombat(string enemy, int health, int damage)
    {
        data.enemyName = enemy;
        data.enemyHealth = health;
        data.enemyMaximumHealth = health;
        data.enemyDamage = damage;
        data.enemyStunned = false;
        data.landmarkStage = "combat";
        Emit($"{enemy}发动了攻击。", "encounter-tier-1");
    }

    private bool AttackEnemy(string weapon)
    {
        int damage = weapon switch
        {
            "fists" => 1,
            "bone-spear" => 2,
            "iron-sword" => 4,
            "steel-sword" => 6,
            "bayonet" => 8,
            "rifle" => 5,
            "laser-rifle" => 8,
            "grenade" => 15,
            "bolas" => 0,
            _ => 0
        };
        if (weapon == "rifle" && !ConsumeExpeditionItem("bullets", 1)) return false;
        if (weapon == "laser-rifle" && !ConsumeExpeditionItem("energy-cell", 1)) return false;
        if (weapon == "grenade" && !ConsumeExpeditionItem("grenade", 1)) return false;
        if (weapon == "bolas" && !ConsumeExpeditionItem("bolas", 1)) return false;
        if (weapon != "fists" && weapon != "grenade" && weapon != "bolas" &&
            GetExpeditionAmount(weapon) <= 0) return false;
        if (weapon == "bolas") data.enemyStunned = true;
        else
        {
            if (HasPerk("barbarian")) damage = (int)Math.Ceiling(damage * 1.5f);
            data.enemyHealth = Math.Max(0, data.enemyHealth - damage);
        }
        if (data.enemyHealth <= 0)
        {
            WinLandmarkCombat(data.activeLandmarkTile[0]);
            return true;
        }
        if (data.enemyStunned)
        {
            data.enemyStunned = false;
            Emit("敌人被暂时绊住，没有反击。", WeaponAudio(weapon));
            return true;
        }
        float enemyHitChance = HasPerk("evasive") ? 0.7f : 0.8f;
        bool hit = NextRandom() < enemyHitChance;
        if (hit) data.expeditionHealth = Math.Max(0, data.expeditionHealth - data.enemyDamage);
        if (data.expeditionHealth <= 0)
        {
            ClearLandmarkState();
            DieInWorld("伤势过重，世界逐渐远去。");
            return true;
        }
        Emit(hit ? $"攻击命中。{data.enemyName}反击，造成{data.enemyDamage}点伤害。" :
            $"攻击命中。{data.enemyName}的反击落空。", WeaponAudio(weapon));
        return true;
    }

    private void WinLandmarkCombat(char tile)
    {
        if (tile == 'H' || tile == 'V')
        {
            AddExpeditionLoot("cured-meat", RandomAmount(1, 8));
            AddExpeditionLoot("teeth", RandomAmount(1, 5));
        }
        else if (tile == 'O' || tile == 'Y')
        {
            AddExpeditionLoot("bullets", RandomAmount(3, 10));
            AddExpeditionLoot("medicine", 1);
            if (tile == 'Y') AddDiscovery("city");
        }
        else if (tile == 'I') AddExpeditionLoot("teeth", RandomAmount(5, 10));
        else if (tile == 'C') AddExpeditionLoot("iron", RandomAmount(1, 5));
        else if (tile == 'S') AddExpeditionLoot("bullets", RandomAmount(3, 8));
        CompleteLandmark(tile);
        data.landmarkStage = "cleared";
        data.enemyHealth = 0;
        Emit($"{data.enemyName}倒下了，这里已经安全。", null);
    }

    private void CollectPassiveLandmark(char tile)
    {
        if (tile == 'F')
        {
            AddExpeditionLoot("bullets", RandomAmount(5, 20));
            AddExpeditionLoot("energy-cell", RandomAmount(5, 10));
            if (NextRandom() < 0.5f) AddExpeditionLoot("rifle", 1);
        }
        else if (tile == 'B') AddExpeditionLoot("alien-alloy", RandomAmount(1, 3));
        CompleteLandmark(tile);
        data.landmarkStage = "cleared";
        Emit(tile == 'W' ? "飞船仍能修复，也许可以离开这里。" : "搜集了这里还能使用的物资。", null);
    }

    private void CompleteLandmark(char tile)
    {
        string coordinate = CurrentCoordinate();
        if (!data.expeditionVisited.Contains(coordinate)) data.expeditionVisited.Add(coordinate);
        if (tile == 'I') AddDiscovery("iron-mine");
        else if (tile == 'C') AddDiscovery("coal-mine");
        else if (tile == 'S') AddDiscovery("sulphur-mine");
        else if (tile == 'W') AddDiscovery("ship");
    }

    private bool LeaveLandmark()
    {
        ClearLandmarkState();
        Emit("离开了这个地点。", "world");
        return true;
    }

    private void ClearLandmarkState()
    {
        data.activeLandmarkTile = string.Empty;
        data.landmarkStage = string.Empty;
        data.enemyName = string.Empty;
        data.enemyHealth = 0;
        data.enemyMaximumHealth = 0;
        data.enemyDamage = 0;
        data.enemyStunned = false;
    }

    private IReadOnlyList<DarkRoomLandmarkAction> GetLandmarkActions(char tile, string stage)
    {
        if (stage == "outpost")
        {
            bool unused = !data.expeditionUsedOutposts.Contains(CurrentCoordinate());
            return new[]
            {
                LandmarkAction("rest", "补充水", unused),
                LandmarkAction("leave", "离开")
            };
        }
        if (stage == "cleared") return new[] { LandmarkAction("leave", "离开") };
        if (tile == 'F' || tile == 'B' || tile == 'W')
            return new[] { LandmarkAction("salvage", tile == 'W' ? "回收飞船" : "搜集物资"), LandmarkAction("leave", "离开") };
        bool canEnter = (tile != 'V' && tile != 'I' || GetExpeditionAmount("torch") > 0) &&
            (tile != 'M' || GetExpeditionAmount("charm") > 0);
        return new[] { LandmarkAction("enter", tile == 'S' || tile == 'C' ? "进攻" : "进入", canEnter), LandmarkAction("leave", "离开") };
    }

    private IReadOnlyList<DarkRoomLandmarkAction> GetCombatActions()
    {
        var actions = new List<DarkRoomLandmarkAction> { LandmarkAction("attack-fists", "徒手攻击") };
        AddWeapon(actions, "bone-spear", "使用骨矛");
        AddWeapon(actions, "iron-sword", "使用铁剑");
        AddWeapon(actions, "steel-sword", "使用钢剑");
        AddWeapon(actions, "bayonet", "使用刺刀");
        AddWeapon(actions, "rifle", "步枪射击", GetExpeditionAmount("bullets") > 0);
        AddWeapon(actions, "laser-rifle", "激光射击", GetExpeditionAmount("energy-cell") > 0);
        AddWeapon(actions, "grenade", "投掷手榴弹");
        AddWeapon(actions, "bolas", "投掷套索");
        if (GetExpeditionAmount("cured-meat") > 0) actions.Add(LandmarkAction("eat", "食用熏肉"));
        if (GetExpeditionAmount("medicine") > 0) actions.Add(LandmarkAction("medicine", "使用药物"));
        return actions;
    }

    private void AddWeapon(List<DarkRoomLandmarkAction> actions, string key, string label, bool usable = true)
    {
        if (GetExpeditionAmount(key) > 0) actions.Add(LandmarkAction("attack-" + key, label, usable));
    }

    private bool UseCombatMedicine()
    {
        if (!ConsumeExpeditionItem("medicine", 1)) return false;
        data.expeditionHealth = Math.Min(MaximumHealth, data.expeditionHealth + 20);
        Emit("使用药物处理了伤势。", "use-meds");
        return true;
    }

    private bool UseCombatMeat()
    {
        if (!ConsumeExpeditionItem("cured-meat", 1)) return false;
        data.expeditionHealth = Math.Min(MaximumHealth, data.expeditionHealth + (HasPerk("gastronome") ? 16 : 8));
        Emit("吃下熏肉，恢复了一些体力。", "eat-meat");
        return true;
    }

    private bool ConsumeExpeditionItem(string key, int amount)
    {
        int current = GetExpeditionAmount(key);
        if (current < amount) return false;
        SetExpeditionAmount(key, current - amount);
        return true;
    }

    private void AddExpeditionLoot(string key, int amount)
    {
        float weight = LoadoutDefinitions.FirstOrDefault(item => item.Key == key)?.Weight ?? 1f;
        float used = data.expeditionInventory.Sum(entry =>
            entry.amount * (LoadoutDefinitions.FirstOrDefault(item => item.Key == entry.key)?.Weight ?? 1f));
        int fits = (int)Math.Floor((CarryingCapacity - used + 0.0001f) / weight);
        int accepted = Math.Max(0, Math.Min(amount, fits));
        if (accepted > 0) SetExpeditionAmount(key, GetExpeditionAmount(key) + accepted);
    }

    private int RandomAmount(int minimum, int maximum)
    {
        return minimum + (int)Math.Floor(NextRandom() * (maximum - minimum + 1));
    }

    private void AddDiscovery(string key)
    {
        if (!data.expeditionDiscoveries.Contains(key)) data.expeditionDiscoveries.Add(key);
    }

    private bool IsCurrentVisited() => data.expeditionVisited.Contains(CurrentCoordinate());
    private string CurrentCoordinate() => $"{data.expeditionX},{data.expeditionY}";

    private static DarkRoomLandmarkAction LandmarkAction(string key, string label, bool enabled = true)
    {
        return new DarkRoomLandmarkAction { Key = key, Label = label, Enabled = enabled };
    }

    private static string GetLandmarkText(char tile, string stage)
    {
        if (stage == "outpost") return "清理后的地点成为荒野中的安全落脚处。储水设备还可以使用。";
        if (stage == "cleared") return "危险已经解除，散落的物资装进了行囊。";
        return TileDescription(tile);
    }

    private static string GetCombatText(char tile)
    {
        return tile switch
        {
            'I' => "火光照亮矿道，一头庞大野兽扑了过来。",
            'C' => "营火旁的武装首领挡住矿井入口。",
            'S' => "全副武装的老兵守在硫磺矿周围。",
            'Y' => "废墟间传来枪声，一名老兵占据掩体。",
            _ => "敌人挡住了去路。"
        };
    }

    private static string LandmarkAudio(char tile)
    {
        return tile switch
        {
            'I' => "landmark-ironmine", 'C' => "landmark-coalmine", 'S' => "landmark-sulphurmine",
            'H' => "landmark-house", 'V' => "landmark-cave", 'O' => "landmark-town",
            'Y' => "landmark-city", 'W' => "landmark-crashed-ship", 'B' => "landmark-borehole",
            'F' => "landmark-battlefield", 'M' => "landmark-swamp", _ => "world"
        };
    }

    private static string WeaponAudio(string weapon)
    {
        return weapon is "rifle" or "laser-rifle" ? "weapon-ranged-1" :
            weapon is "grenade" or "bolas" ? "weapon-ranged-2" :
            weapon == "fists" ? "weapon-unarmed-1" : "weapon-melee-1";
    }

    private void EnsureLandmarkState()
    {
        data.activeLandmarkTile ??= string.Empty;
        data.landmarkStage ??= string.Empty;
        data.enemyName ??= string.Empty;
        data.expeditionDiscoveries ??= new List<string>();
        data.expeditionUsedOutposts ??= new List<string>();
    }

    private void CopyLandmarkSaveData(DarkRoomSaveData save)
    {
        save.activeLandmarkTile = data.activeLandmarkTile;
        save.landmarkStage = data.landmarkStage;
        save.enemyName = data.enemyName;
        save.enemyHealth = data.enemyHealth;
        save.enemyMaximumHealth = data.enemyMaximumHealth;
        save.enemyDamage = data.enemyDamage;
        save.enemyStunned = data.enemyStunned;
        save.expeditionDiscoveries = new List<string>(data.expeditionDiscoveries);
        save.expeditionUsedOutposts = new List<string>(data.expeditionUsedOutposts);
        save.shipFound = data.shipFound;
        save.cityCleared = data.cityCleared;
    }
}
