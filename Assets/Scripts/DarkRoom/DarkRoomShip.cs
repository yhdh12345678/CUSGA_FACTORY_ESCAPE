// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

public sealed class DarkRoomSpaceView
{
    public string Layer;
    public string Scene;
    public string Hazard;
}

public sealed partial class DarkRoomSaveData
{
    public int shipHull;
    public int shipThrusters = 1;
    public bool liftoffWarningSeen;
    public bool inSpace;
    public int spaceHull;
    public int spaceAltitude;
    public int spaceHazardLane = 1;
    public bool gameCompleted;
}

public sealed partial class DarkRoomGameState
{
    public int ShipHull => data.shipHull;
    public int ShipThrusters => Math.Max(1, data.shipThrusters);
    public bool CanLiftOff => data.shipFound && data.shipHull > 0 && !data.inSpace;
    public bool InSpace => data.inSpace;
    public int SpaceHull => data.spaceHull;
    public int SpaceAltitude => data.spaceAltitude;
    public bool GameCompleted => data.gameCompleted;

    public bool ReinforceHull()
    {
        if (!data.shipFound || GetStore("alien-alloy") < 1f) return false;
        AddStore("alien-alloy", -1);
        data.shipHull++;
        Emit("外星合金加固了船体。", "reinforce-hull");
        return true;
    }

    public bool UpgradeEngine()
    {
        if (!data.shipFound || GetStore("alien-alloy") < 1f) return false;
        AddStore("alien-alloy", -1);
        data.shipThrusters = Math.Max(1, data.shipThrusters) + 1;
        Emit("引擎推力得到提升。", "upgrade-engine");
        return true;
    }

    public bool BeginLiftoff()
    {
        if (!CanLiftOff) return false;
        data.liftoffWarningSeen = true;
        data.inSpace = true;
        data.spaceHull = data.shipHull;
        data.spaceAltitude = 0;
        data.spaceHazardLane = (int)Math.Floor(NextRandom() * 3f);
        LeaveEventArea();
        Emit("飞船离开地面，开始穿过碎片云。", "lift-off");
        return true;
    }

    public bool AdvanceSpace(string lane)
    {
        if (!data.inSpace || data.gameCompleted) return false;
        int selected = lane == "left" ? 0 : lane == "right" ? 2 : lane == "centre" ? 1 : -1;
        if (selected < 0) return false;
        bool collision = selected == data.spaceHazardLane;
        if (collision && ShipThrusters > 1)
        {
            float lastMomentAvoidance = Math.Min(0.75f, (ShipThrusters - 1) * 0.15f);
            collision = NextRandom() >= lastMomentAvoidance;
        }
        if (collision)
        {
            data.spaceHull--;
            if (data.spaceHull <= 0)
            {
                data.inSpace = false;
                data.spaceHull = 0;
                data.spaceAltitude = 0;
                Emit("船体在碎片撞击中破裂，飞船坠回地面。", "crash");
                return true;
            }
        }
        data.spaceAltitude += 5;
        if (data.spaceAltitude >= 60)
        {
            data.spaceAltitude = 60;
            data.inSpace = false;
            data.gameCompleted = true;
            Emit("飞船冲出碎片云。荒芜世界逐渐缩成身后的微光，漫游者舰队悬在群星之间。", "ending");
            return true;
        }
        data.spaceHazardLane = (int)Math.Floor(NextRandom() * 3f);
        Emit(collision ? $"碎片撞击船体，剩余{data.spaceHull}层。飞船继续上升。" :
            "飞船避开碎片，继续上升。", collision ? $"asteroid-hit-{1 + data.spaceAltitude / 10}" : "space");
        return true;
    }

    public DarkRoomSpaceView GetSpaceView()
    {
        if (!data.inSpace) return null;
        string layer = data.spaceAltitude switch
        {
            < 10 => "对流层",
            < 20 => "平流层",
            < 30 => "中间层",
            < 45 => "热层",
            < 60 => "散逸层",
            _ => "太空"
        };
        string lane = data.spaceHazardLane == 0 ? "左侧" : data.spaceHazardLane == 2 ? "右侧" : "中央";
        return new DarkRoomSpaceView
        {
            Layer = layer,
            Scene = "飞船在逐渐变暗的天空中上升。星光穿过碎片云，船体不断震动。",
            Hazard = $"一块高速碎片正从{lane}航道落下。"
        };
    }

    private void EnsureShipState()
    {
        if (data.shipThrusters <= 0) data.shipThrusters = 1;
        data.spaceHazardLane = Math.Max(0, Math.Min(2, data.spaceHazardLane));
    }

    private void CopyShipSaveData(DarkRoomSaveData save)
    {
        save.shipHull = data.shipHull;
        save.shipThrusters = data.shipThrusters;
        save.liftoffWarningSeen = data.liftoffWarningSeen;
        save.inSpace = data.inSpace;
        save.spaceHull = data.spaceHull;
        save.spaceAltitude = data.spaceAltitude;
        save.spaceHazardLane = data.spaceHazardLane;
        save.gameCompleted = data.gameCompleted;
    }
}
