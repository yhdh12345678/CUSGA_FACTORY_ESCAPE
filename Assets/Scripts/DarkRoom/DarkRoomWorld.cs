// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

public sealed class DarkRoomWorldDirection
{
    public string Key;
    public string Label;
    public string Destination;
    public bool Enabled;
}

public sealed class DarkRoomWorldView
{
    public string Terrain;
    public string Scene;
    public string Position;
    public string KnownPlaces;
    public string Compass;
    public IReadOnlyList<DarkRoomWorldDirection> Directions;
    public bool AtVillage;
    public bool AtLandmark;
}

public sealed partial class DarkRoomSaveData
{
    public List<string> worldMapRows = new List<string>();
    public List<string> worldMaskRows = new List<string>();
    public List<string> worldVisited = new List<string>();
    public List<string> expeditionMapRows = new List<string>();
    public List<string> expeditionMaskRows = new List<string>();
    public List<string> expeditionVisited = new List<string>();
    public bool expeditionStarving;
    public bool expeditionThirsty;
}

public sealed partial class DarkRoomGameState
{
    public const int WorldRadius = 30;
    private const int WorldSize = WorldRadius * 2 + 1;
    private const int WorldLightRadius = 2;
    private const char VillageTile = 'A';
    private const char ForestTile = ';';
    private const char FieldTile = ',';
    private const char BarrensTile = '.';

    private sealed class LandmarkDefinition
    {
        public char Tile;
        public string Label;
        public int Count;
        public int MinimumRadius;
        public int MaximumRadius;
    }

    private static readonly LandmarkDefinition[] LandmarkDefinitions =
    {
        Landmark('I', "铁矿", 1, 5, 5),
        Landmark('C', "煤矿", 1, 10, 10),
        Landmark('S', "硫磺矿", 1, 20, 20),
        Landmark('H', "废弃老屋", 10, 0, 45),
        Landmark('V', "潮湿洞穴", 5, 3, 10),
        Landmark('O', "废弃城镇", 10, 10, 20),
        Landmark('Y', "城市废墟", 20, 20, 45),
        Landmark('W', "坠毁飞船", 1, 28, 28),
        Landmark('B', "钻孔", 10, 15, 45),
        Landmark('F', "战场", 5, 18, 45),
        Landmark('M', "浑浊沼泽", 1, 15, 45),
        Landmark('X', "毁坏的战舰", 1, 28, 28)
    };

    public DarkRoomWorldView GetWorldView()
    {
        if (!data.expeditionActive || data.expeditionMapRows.Count != WorldSize) return null;
        char tile = GetExpeditionTile(data.expeditionX, data.expeditionY);
        return new DarkRoomWorldView
        {
            Terrain = TileLabel(tile),
            Scene = TileDescription(tile),
            Position = GetWorldPositionText(),
            KnownPlaces = GetKnownPlacesText(),
            Compass = GetShipCompassText(data.expeditionMapRows, data.expeditionX, data.expeditionY),
            Directions = new[]
            {
                WorldDirection("north", "向北", 0, -1),
                WorldDirection("south", "向南", 0, 1),
                WorldDirection("west", "向西", -1, 0),
                WorldDirection("east", "向东", 1, 0)
            },
            AtVillage = tile == VillageTile,
            AtLandmark = IsLandmark(tile)
        };
    }

    public bool MoveWorld(string direction)
    {
        if (!data.expeditionActive) return false;
        int dx = direction == "west" ? -1 : direction == "east" ? 1 : 0;
        int dy = direction == "north" ? -1 : direction == "south" ? 1 : 0;
        if ((dx == 0 && dy == 0) || !IsWorldCoordinate(data.expeditionX + dx, data.expeditionY + dy))
            return false;

        char oldTile = GetExpeditionTile(data.expeditionX, data.expeditionY);
        data.expeditionX += dx;
        data.expeditionY += dy;
        RevealExpeditionMap(data.expeditionX, data.expeditionY, HasPerk("scout") ? 4 : WorldLightRadius);
        char tile = GetExpeditionTile(data.expeditionX, data.expeditionY);
        if (tile == VillageTile)
        {
            ReturnHome();
            return true;
        }
        if (IsLandmark(tile))
        {
            Emit($"来到{TileLabel(tile)}。{TileDescription(tile)}", "world");
            return true;
        }
        if (!UseWorldSupplies()) return true;
        string narration = GetTerrainTransition(oldTile, tile);
        Emit(string.IsNullOrWhiteSpace(narration) ? $"继续穿过{TileLabel(tile)}。" : narration, "world");
        return true;
    }

    public bool FinishExpeditionAtVillage()
    {
        if (!data.expeditionActive || GetExpeditionTile(data.expeditionX, data.expeditionY) != VillageTile)
            return false;
        return ReturnHome();
    }

    public string GetShipCompassText()
    {
        IReadOnlyList<string> rows = data.expeditionActive && data.expeditionMapRows.Count == WorldSize
            ? data.expeditionMapRows : data.worldMapRows;
        int x = data.expeditionActive ? data.expeditionX : WorldRadius;
        int y = data.expeditionActive ? data.expeditionY : WorldRadius;
        return GetShipCompassText(rows, x, y);
    }

    public void RevealRandomWorldRegion()
    {
        EnsureWorldGenerated();
        if (data.worldSeenAll) return;
        var hidden = new List<(int x, int y)>();
        for (int y = 0; y < WorldSize; y++)
        {
            for (int x = 0; x < WorldSize; x++)
            {
                if (data.worldMaskRows[y][x] != '1') hidden.Add((x, y));
            }
        }
        if (hidden.Count == 0)
        {
            data.worldSeenAll = true;
            return;
        }
        int index = Math.Min(hidden.Count - 1, (int)Math.Floor(NextRandom() * hidden.Count));
        RevealRows(data.worldMaskRows, hidden[index].x, hidden[index].y, 5);
        data.worldSeenAll = data.worldMaskRows.All(row => row.All(value => value == '1'));
        data.mapRevealCount++;
    }

    private void EnsureWorldState()
    {
        data.worldMapRows ??= new List<string>();
        data.worldMaskRows ??= new List<string>();
        data.worldVisited ??= new List<string>();
        data.expeditionMapRows ??= new List<string>();
        data.expeditionMaskRows ??= new List<string>();
        data.expeditionVisited ??= new List<string>();
    }

    private void EnsureWorldGenerated()
    {
        if (data.worldMapRows.Count == WorldSize && data.worldMaskRows.Count == WorldSize) return;
        char[][] map = GenerateWorldMap();
        data.worldMapRows = map.Select(row => new string(row)).ToList();
        data.worldMaskRows = Enumerable.Repeat(new string('0', WorldSize), WorldSize).ToList();
        RevealRows(data.worldMaskRows, WorldRadius, WorldRadius, WorldLightRadius);
        data.worldVisited.Clear();
        data.worldSeenAll = false;
    }

    private void BeginWorldExpedition()
    {
        EnsureWorldGenerated();
        data.expeditionMapRows = new List<string>(data.worldMapRows);
        data.expeditionMaskRows = new List<string>(data.worldMaskRows);
        data.expeditionVisited = new List<string>(data.worldVisited);
        data.expeditionDiscoveries.Clear();
        data.expeditionUsedOutposts.Clear();
        data.expeditionStarving = false;
        data.expeditionThirsty = false;
    }

    private void CommitWorldExpedition()
    {
        if (data.expeditionDiscoveries.Contains("iron-mine")) data.ironMineCount = 1;
        if (data.expeditionDiscoveries.Contains("coal-mine")) data.coalMineCount = 1;
        if (data.expeditionDiscoveries.Contains("sulphur-mine")) data.sulphurMineCount = 1;
        if (data.expeditionDiscoveries.Contains("ship")) data.shipFound = true;
        if (data.expeditionDiscoveries.Contains("city")) data.cityCleared = true;
        if (data.expeditionMapRows.Count == WorldSize)
        {
            data.worldMapRows = new List<string>(data.expeditionMapRows);
            data.worldMaskRows = new List<string>(data.expeditionMaskRows);
            data.worldVisited = new List<string>(data.expeditionVisited);
            data.worldSeenAll = data.worldMaskRows.All(row => row.All(value => value == '1'));
        }
        ClearWorldExpedition();
    }

    private void ClearWorldExpedition()
    {
        data.expeditionMapRows.Clear();
        data.expeditionMaskRows.Clear();
        data.expeditionVisited.Clear();
        data.expeditionStarving = false;
        data.expeditionThirsty = false;
        data.expeditionDiscoveries.Clear();
        data.expeditionUsedOutposts.Clear();
        ClearLandmarkState();
    }

    private void CopyWorldSaveData(DarkRoomSaveData save)
    {
        save.worldMapRows = new List<string>(data.worldMapRows);
        save.worldMaskRows = new List<string>(data.worldMaskRows);
        save.worldVisited = new List<string>(data.worldVisited);
        save.expeditionMapRows = new List<string>(data.expeditionMapRows);
        save.expeditionMaskRows = new List<string>(data.expeditionMaskRows);
        save.expeditionVisited = new List<string>(data.expeditionVisited);
        save.expeditionStarving = data.expeditionStarving;
        save.expeditionThirsty = data.expeditionThirsty;
    }

    private char[][] GenerateWorldMap()
    {
        var map = Enumerable.Range(0, WorldSize).Select(_ => new char[WorldSize]).ToArray();
        map[WorldRadius][WorldRadius] = VillageTile;
        for (int radius = 1; radius <= WorldRadius; radius++)
        {
            for (int step = 0; step < radius * 8; step++)
            {
                int x;
                int y;
                if (step < 2 * radius)
                {
                    x = WorldRadius - radius + step;
                    y = WorldRadius - radius;
                }
                else if (step < 4 * radius)
                {
                    x = WorldRadius + radius;
                    y = WorldRadius - 3 * radius + step;
                }
                else if (step < 6 * radius)
                {
                    x = WorldRadius + 5 * radius - step;
                    y = WorldRadius + radius;
                }
                else
                {
                    x = WorldRadius - radius;
                    y = WorldRadius + 7 * radius - step;
                }
                map[y][x] = ChooseTerrain(x, y, map);
            }
        }
        foreach (LandmarkDefinition landmark in LandmarkDefinitions)
        {
            for (int count = 0; count < landmark.Count; count++) PlaceLandmark(map, landmark);
        }
        return map;
    }

    private char ChooseTerrain(int x, int y, char[][] map)
    {
        char[] adjacent =
        {
            y > 0 ? map[y - 1][x] : '\0',
            y < WorldSize - 1 ? map[y + 1][x] : '\0',
            x < WorldSize - 1 ? map[y][x + 1] : '\0',
            x > 0 ? map[y][x - 1] : '\0'
        };
        if (adjacent.Contains(VillageTile)) return ForestTile;
        var chances = new Dictionary<char, float>();
        float nonSticky = 1f;
        foreach (char tile in adjacent.Where(tile => tile != '\0'))
        {
            chances[tile] = chances.TryGetValue(tile, out float value) ? value + 0.5f : 0.5f;
            nonSticky -= 0.5f;
        }
        AddChance(chances, ForestTile, 0.15f * nonSticky);
        AddChance(chances, FieldTile, 0.35f * nonSticky);
        AddChance(chances, BarrensTile, 0.5f * nonSticky);
        float roll = NextRandom();
        float cumulative = 0f;
        foreach (KeyValuePair<char, float> chance in chances.OrderByDescending(item => item.Value))
        {
            cumulative += chance.Value;
            if (roll < cumulative) return chance.Key;
        }
        return BarrensTile;
    }

    private void PlaceLandmark(char[][] map, LandmarkDefinition landmark)
    {
        for (int attempt = 0; attempt < 10000; attempt++)
        {
            int range = Math.Max(0, landmark.MaximumRadius - landmark.MinimumRadius);
            int radius = landmark.MinimumRadius + (range == 0 ? 0 : (int)Math.Floor(NextRandom() * range));
            int xDistance = radius == 0 ? 0 : (int)Math.Floor(NextRandom() * radius);
            int yDistance = radius - xDistance;
            if (NextRandom() < 0.5f) xDistance = -xDistance;
            if (NextRandom() < 0.5f) yDistance = -yDistance;
            int x = Math.Max(0, Math.Min(WorldSize - 1, WorldRadius + xDistance));
            int y = Math.Max(0, Math.Min(WorldSize - 1, WorldRadius + yDistance));
            if (!IsTerrain(map[y][x])) continue;
            map[y][x] = landmark.Tile;
            return;
        }
        throw new InvalidOperationException($"无法放置地标 {landmark.Label}。");
    }

    private bool UseWorldSupplies()
    {
        data.expeditionFoodMoves++;
        data.expeditionWaterMoves++;
        int foodInterval = HasPerk("slow-metabolism") ? 4 : 2;
        if (data.expeditionFoodMoves >= foodInterval)
        {
            data.expeditionFoodMoves = 0;
            int food = GetExpeditionAmount("cured-meat");
            if (food > 0)
            {
                SetExpeditionAmount("cured-meat", food - 1);
                data.expeditionStarving = false;
                data.expeditionHealth = Math.Min(MaximumHealth,
                    data.expeditionHealth + (HasPerk("gastronome") ? 16 : 8));
            }
            else if (!data.expeditionStarving)
            {
                data.expeditionStarving = true;
            }
            else
            {
                DieInWorld("饥饿让世界逐渐远去。");
                return false;
            }
        }
        int waterInterval = HasPerk("desert-rat") ? 2 : 1;
        if (data.expeditionWaterMoves >= waterInterval)
        {
            data.expeditionWaterMoves = 0;
            if (data.expeditionWater > 0)
            {
                data.expeditionWater--;
                data.expeditionThirsty = false;
            }
            else if (!data.expeditionThirsty)
            {
                data.expeditionThirsty = true;
            }
            else
            {
                DieInWorld("难以忍受的口渴让世界逐渐远去。");
                return false;
            }
        }
        return true;
    }

    private void DieInWorld(string message)
    {
        data.expeditionInventory.Clear();
        data.outfit.Clear();
        data.expeditionActive = false;
        data.expeditionX = WorldRadius;
        data.expeditionY = WorldRadius;
        ClearWorldExpedition();
        Emit(message, "death");
    }

    private DarkRoomWorldDirection WorldDirection(string key, string label, int dx, int dy)
    {
        int x = data.expeditionX + dx;
        int y = data.expeditionY + dy;
        bool enabled = IsWorldCoordinate(x, y);
        string destination = !enabled ? "世界边缘" : data.expeditionMaskRows[y][x] == '1'
            ? TileLabel(data.expeditionMapRows[y][x]) : "未探明";
        return new DarkRoomWorldDirection { Key = key, Label = label, Destination = destination, Enabled = enabled };
    }

    private string GetWorldPositionText()
    {
        int dx = data.expeditionX - WorldRadius;
        int dy = data.expeditionY - WorldRadius;
        int distance = Math.Abs(dx) + Math.Abs(dy);
        if (distance == 0) return "位于村落。";
        return $"位于村落{DirectionText(dx, dy)}方，距离{distance}步。";
    }

    private string GetKnownPlacesText()
    {
        var places = new List<(string text, int distance)>();
        for (int y = 0; y < WorldSize; y++)
        {
            for (int x = 0; x < WorldSize; x++)
            {
                char tile = data.expeditionMapRows[y][x];
                if (data.expeditionMaskRows[y][x] != '1' || !IsLandmark(tile)) continue;
                int dx = x - data.expeditionX;
                int dy = y - data.expeditionY;
                int distance = Math.Abs(dx) + Math.Abs(dy);
                string relative = distance == 0 ? "当前位置" : $"{DirectionText(dx, dy)}方{distance}步";
                places.Add(($"{TileLabel(tile)}，{relative}", distance));
            }
        }
        string result = string.Join("；", places.OrderBy(item => item.distance).Take(6).Select(item => item.text));
        return string.IsNullOrWhiteSpace(result) ? "尚未发现地标。" : result + "。";
    }

    private static string GetShipCompassText(IReadOnlyList<string> rows, int currentX, int currentY)
    {
        if (rows == null || rows.Count != WorldSize) return "罗盘尚未找到稳定方向。";
        for (int y = 0; y < rows.Count; y++)
        {
            int x = rows[y].IndexOf('W');
            if (x < 0) continue;
            int dx = x - currentX;
            int dy = y - currentY;
            int distance = Math.Abs(dx) + Math.Abs(dy);
            if (distance == 0) return "罗盘指针已经停住。";
            return $"罗盘指向{DirectionText(dx, dy)}方，目标距离{distance}步。";
        }
        return "罗盘尚未找到稳定方向。";
    }

    private static string DirectionText(int dx, int dy)
    {
        if (dx == 0) return dy < 0 ? "北" : "南";
        if (dy == 0) return dx < 0 ? "西" : "东";
        string vertical = dy < 0 ? "北" : "南";
        string horizontal = dx < 0 ? "西" : "东";
        if (Math.Abs(dx) > Math.Abs(dy) * 2) return horizontal;
        if (Math.Abs(dy) > Math.Abs(dx) * 2) return vertical;
        return vertical + horizontal;
    }

    private char GetExpeditionTile(int x, int y) => data.expeditionMapRows[y][x];

    private void RevealExpeditionMap(int x, int y, int radius)
    {
        RevealRows(data.expeditionMaskRows, x, y, radius);
    }

    private static void RevealRows(List<string> rows, int x, int y, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius + Math.Abs(dx); dy <= radius - Math.Abs(dx); dy++)
            {
                int targetX = x + dx;
                int targetY = y + dy;
                if (!IsWorldCoordinate(targetX, targetY)) continue;
                char[] row = rows[targetY].ToCharArray();
                row[targetX] = '1';
                rows[targetY] = new string(row);
            }
        }
    }

    private int GetExpeditionAmount(string key)
    {
        return data.expeditionInventory.FirstOrDefault(entry => entry.key == key)?.amount ?? 0;
    }

    private void SetExpeditionAmount(string key, int amount)
    {
        DarkRoomInventoryEntry entry = data.expeditionInventory.FirstOrDefault(item => item.key == key);
        if (entry == null)
        {
            entry = new DarkRoomInventoryEntry { key = key };
            data.expeditionInventory.Add(entry);
        }
        entry.amount = Math.Max(0, amount);
    }

    private static bool IsWorldCoordinate(int x, int y) => x >= 0 && y >= 0 && x < WorldSize && y < WorldSize;
    private static bool IsTerrain(char tile) => tile == ForestTile || tile == FieldTile || tile == BarrensTile;
    private static bool IsLandmark(char tile) => LandmarkDefinitions.Any(landmark => landmark.Tile == tile);

    private static string TileLabel(char tile)
    {
        if (tile == VillageTile) return "村落";
        if (tile == ForestTile) return "枯萎森林";
        if (tile == FieldTile) return "干草原";
        if (tile == BarrensTile) return "荒地";
        return LandmarkDefinitions.FirstOrDefault(landmark => landmark.Tile == tile)?.Label ?? "未知地点";
    }

    private static string TileDescription(char tile)
    {
        return tile switch
        {
            VillageTile => "熟悉的小屋与火光就在身边。",
            ForestTile => "扭曲的枯树从尘土中升起，枝桠在头顶交织。",
            FieldTile => "泛黄的干草在风中沙沙作响。",
            BarrensTile => "干裂土地一直延伸到灰暗天际，风卷着尘土。",
            'I' => "废弃矿道通向地下，岩壁上残留着铁矿脉。",
            'C' => "漆黑矿井里散落着煤块。",
            'S' => "刺鼻气味从硫磺矿坑深处涌出。",
            'H' => "一座破败老屋孤零零地立在荒野。",
            'V' => "潮湿洞口没入黑暗。",
            'O' => "低矮建筑沿着空荡街道倾塌。",
            'Y' => "高楼残骸遮住天空，街道通向废墟深处。",
            'W' => "一艘庞大飞船斜插在尘土中，船体已经破裂。",
            'B' => "深不见底的钻孔边缘布满锈蚀设备。",
            'F' => "锈蚀武器和白骨散落在战场上。",
            'M' => "浑浊泥水在枯草间缓慢冒泡。",
            'X' => "一艘遭到重创的战舰横卧在荒野。",
            _ => "风沙遮住了视野。"
        };
    }

    private static string GetTerrainTransition(char oldTile, char newTile)
    {
        if (oldTile == newTile) return string.Empty;
        if (newTile == ForestTile) return "枯树出现在地平线上，干草和尘土逐渐让位于落枝。";
        if (newTile == FieldTile) return "荒地在一片将死的草海前断开，干草随风摇摆。";
        if (newTile == BarrensTile) return "草木渐渐消失，只剩干裂土地和飞扬尘土。";
        return string.Empty;
    }

    private static void AddChance(Dictionary<char, float> chances, char tile, float amount)
    {
        chances[tile] = (chances.TryGetValue(tile, out float value) ? value : 0f) + amount;
    }

    private static LandmarkDefinition Landmark(char tile, string label, int count, int minimum, int maximum)
    {
        return new LandmarkDefinition
        {
            Tile = tile, Label = label, Count = count,
            MinimumRadius = minimum, MaximumRadius = maximum
        };
    }
}
