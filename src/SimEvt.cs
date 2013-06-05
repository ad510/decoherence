﻿// Copyright (c) 2013 Andrew Downing
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

/* This file contains:
 * the different types of simulation events (including user commands) that can be applied at a specific time in the game
 * the base class for the simulation events
 * a list data type that specializes in storing simulation events
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Decoherence
{
    /// <summary>
    /// base class for simulation events
    /// </summary>
    public abstract class SimEvt
    {
        public long time;

        public abstract void apply(Sim g);
    }

    /// <summary>
    /// base class for unit commands
    /// </summary>
    public abstract class CmdEvt : SimEvt
    {
        public long timeCmd; // time is latest simulation time when command is given, timeCmd is when event takes place (may be in past)
        public int[] units;

        protected CmdEvt(long timeVal, long timeCmdVal, int[] unitsVal)
        {
            time = timeVal;
            timeCmd = timeCmdVal;
            units = unitsVal;
        }

        public override void apply(Sim g)
        {
            g.cmdHistory.add(this); // copy event to command history list (it should've already been popped from event list)
        }
    }

    public enum Formation : byte { Tight, Loose, Ring };

    /// <summary>
    /// command to move unit(s)
    /// </summary>
    public class MoveCmdEvt : CmdEvt
    {
        public FP.Vector pos; // where to move to
        public Formation formation;

        public MoveCmdEvt(long timeVal, long timeCmdVal, int[] unitsVal, FP.Vector posVal, Formation formationVal)
            : base(timeVal, timeCmdVal, unitsVal)
        {
            pos = posVal;
            formation = formationVal;
        }

        public override void apply(Sim g)
        {
            FP.Vector curPos, goal, rows = new FP.Vector(), offset;
            long spacing = 0;
            int count = 0, i = 0;
            base.apply(g);
            // count number of units able to move
            foreach (int unit in units)
            {
                if (unitCanMove(g, unit))
                {
                    count++;
                    if (formation == Formation.Tight && g.unitT[g.u[unit].type].tightFormationSpacing > spacing) spacing = g.unitT[g.u[unit].type].tightFormationSpacing;
                }
            }
            if (count == 0) return;
            // calculate spacing
            // (if tight formation, then spacing was already calculated above)
            // TODO: loose formation should be triangular and not use sqrt
            if (formation == Formation.Loose)
            {
                spacing = FP.mul(g.visRadius, FP.fromDouble(Math.Sqrt(2))) >> FP.Precision << FP.Precision;
            }
            else if (formation == Formation.Ring)
            {
                spacing = (g.visRadius * 2 >> FP.Precision) - 1 << FP.Precision;
            }
            if (formation == Formation.Tight || formation == Formation.Loose)
            {
                rows.x = (int)Math.Ceiling(Math.Sqrt(count)); // TODO: don't use sqrt
                rows.y = (count - 1) / rows.x + 1;
                offset = (rows - new FP.Vector(1, 1)) * spacing / 2;
            }
            else if (formation == Formation.Ring)
            {
                offset.x = (count == 1) ? 0 : FP.div(spacing / 2, FP.fromDouble(Math.Sin(Math.PI / count))); // TODO: don't use sin
                offset.y = offset.x;
            }
            else
            {
                throw new NotImplementedException("requested formation is not implemented");
            }
            if (pos.x < Math.Min(offset.x, g.mapSize / 2)) pos.x = Math.Min(offset.x, g.mapSize / 2);
            if (pos.x > g.mapSize - Math.Min(offset.x, g.mapSize / 2)) pos.x = g.mapSize - Math.Min(offset.x, g.mapSize / 2);
            if (pos.y < Math.Min(offset.y, g.mapSize / 2)) pos.y = Math.Min(offset.y, g.mapSize / 2);
            if (pos.y > g.mapSize - Math.Min(offset.y, g.mapSize / 2)) pos.y = g.mapSize - Math.Min(offset.y, g.mapSize / 2);
            // move units
            foreach (int unit in units)
            {
                if (unitCanMove(g, unit))
                {
                    int unit2 = unit;
                    curPos = g.u[unit].calcPos(timeCmd);
                    if (formation == Formation.Tight || formation == Formation.Loose)
                    {
                        goal = pos + new FP.Vector((i % rows.x) * spacing - offset.x, i / rows.x * spacing - offset.y);
                    }
                    else if (formation == Formation.Ring)
                    {
                        // TODO: don't use sin or cos
                        goal = pos + offset.x * new FP.Vector(FP.fromDouble(Math.Cos(2 * Math.PI * i / count)), FP.fromDouble(Math.Sin(2 * Math.PI * i / count)));
                    }
                    else
                    {
                        throw new NotImplementedException("requested formation is not implemented");
                    }
                    if (goal.x < 0) goal.x = 0;
                    if (goal.x > g.mapSize) goal.x = g.mapSize;
                    if (goal.y < 0) goal.y = 0;
                    if (goal.y > g.mapSize) goal.y = g.mapSize;
                    if (timeCmd < g.timeSim) unit2 = g.u[unit].prepareNonLivePath(timeCmd); // move replacement unit instead of live unit if in past
                    g.u[unit2].addMove(Unit.Move.fromSpeed(timeCmd, g.unitT[g.u[unit2].type].speed, curPos, goal));
                    i++;
                }
            }
        }

        private bool unitCanMove(Sim g, int unit)
        {
            return g.u[unit].exists(timeCmd) && (timeCmd >= g.timeSim || timeCmd >= g.u[unit].timeCohere) && g.unitT[g.u[unit].type].speed > 0;
        }
    }

    /// <summary>
    /// command to make a new unit
    /// </summary>
    public class MakeUnitCmdEvt : CmdEvt
    {
        public int type;

        public MakeUnitCmdEvt(long timeVal, long timeCmdVal, int[] unitsVal, int typeVal)
            : base(timeVal, timeCmdVal, unitsVal)
        {
            type = typeVal;
        }

        public override void apply(Sim g)
        {
            base.apply(g);
            foreach (int unit in units)
            {
                // happens at timeCmd + 1 so addTileMoveEvts() knows to initially put new unit on visibility tiles
                // TODO: move new unit immediately after making it
                if (g.u[unit].makeChildUnit(timeCmd + 1, false, type)) return;
            }
        }
    }

    public enum UnitAction : byte { MakePath, DeletePath };

    /// <summary>
    /// command to apply an action to a set of units
    /// </summary>
    public class UnitActionCmdEvt : CmdEvt
    {
        public UnitAction action;

        public UnitActionCmdEvt(long timeVal, long timeCmdVal, int[] unitsVal, UnitAction actionVal)
            : base(timeVal, timeCmdVal, unitsVal)
        {
            action = actionVal;
        }

        public override void apply(Sim g)
        {
            base.apply(g);
            foreach (int unit in units)
            {
                if (action == UnitAction.MakePath)
                {
                    // happens at timeCmd + 1 so addTileMoveEvts() knows to initially put new unit on visibility tiles
                    // TODO: move new unit immediately after making it
                    g.u[unit].makeChildUnit(timeCmd + 1, true);
                }
                else if (action == UnitAction.DeletePath)
                {
                    // check if unit changed index due to a previous path deletion
                    int unit2 = unit;
                    for (int i = 0; i < g.unitIdChgs.Count / 2; i++)
                    {
                        if (unit2 == g.unitIdChgs[i * 2]) unit2 = g.unitIdChgs[i * 2 + 1];
                    }
                    // happens at timeCmd so that when paused, making path then deleting parent path doesn't move parent's tile pos off map
                    // (where child's tile pos initially is)
                    if (unit2 >= 0) g.u[unit2].delete(timeCmd);
                }
            }
        }
    }

    /// <summary>
    /// event to update various things at regular intervals
    /// </summary>
    public class UpdateEvt : SimEvt
    {
        public UpdateEvt(long timeVal)
        {
            time = timeVal;
        }

        public override void apply(Sim g)
        {
            FP.Vector pos;
            int target;
            long distSq, targetDistSq;
            int i, j;
            // update units
            for (i = 0; i < g.nUnits; i++)
            {
                if (g.u[i].isLive(time) && time >= g.u[i].timeAttack + g.unitT[g.u[i].type].reload)
                {
                    // done reloading, look for closest target to potentially attack
                    pos = g.u[i].calcPos(time);
                    target = -1;
                    targetDistSq = g.unitT[g.u[i].type].range * g.unitT[g.u[i].type].range + 1;
                    for (j = 0; j < g.nUnits; j++)
                    {
                        if (i != j && g.u[j].isLive(time) && g.players[g.u[i].player].mayAttack[g.u[j].player] && g.unitT[g.u[i].type].damage[g.u[j].type] > 0)
                        {
                            distSq = (g.u[j].calcPos(time) - pos).lengthSq();
                            if (distSq < targetDistSq)
                            {
                                target = j;
                                targetDistSq = distSq;
                            }
                        }
                    }
                    if (target >= 0)
                    {
                        // attack target
                        // take health with 1 ms delay so earlier units in array don't have unfair advantage
                        for (j = 0; j < g.unitT[g.u[i].type].damage[g.u[target].type]; j++) g.u[target].takeHealth(time + 1);
                        g.u[i].timeAttack = time;
                    }
                }
            }
            // add events to move units between tiles
            // this shouldn't be done in Sim.update() because addTileMoveEvts() sometimes adds events before timeSim
            for (i = 0; i < g.nUnits; i++)
            {
                g.u[i].addTileMoveEvts(ref g.events, time, time + g.updateInterval);
            }
            g.movedUnits.Clear();
            // add next UpdateEvt
            g.events.add(new UpdateEvt(time + g.updateInterval));
            g.timeUpdateEvt = time;
        }
    }

    /// <summary>
    /// event in which unit moves between visibility tiles
    /// </summary>
    /// <remarks>
    /// when making this event, can't rely on a unit's tileX and tileY being up-to-date
    /// because the latest TileMoveEvts for that unit might not be applied yet
    /// </remarks>
    public class TileMoveEvt : SimEvt
    {
        public int unit;
        public int tileX, tileY; // new tile position, set to int.MinValue to keep current value

        public TileMoveEvt(long timeVal, int unitVal, int tileXVal, int tileYVal)
        {
            time = timeVal;
            unit = unitVal;
            tileX = tileXVal;
            tileY = tileYVal;
        }

        public override void apply(Sim g)
        {
            int i, tXPrev, tYPrev, tX, tY;
            if (g.u[unit].tileX == Sim.OffMap) return; // skip event if unit no longer exists
            if (tileX == int.MinValue) tileX = g.u[unit].tileX;
            if (tileY == int.MinValue) tileY = g.u[unit].tileY;
            tXPrev = g.u[unit].tileX;
            tYPrev = g.u[unit].tileY;
            g.u[unit].tileX = tileX;
            g.u[unit].tileY = tileY;
            // add unit to visibility tiles
            for (tX = tileX - g.tileVisRadius(); tX <= tileX + g.tileVisRadius(); tX++)
            {
                for (tY = tileY - g.tileVisRadius(); tY <= tileY + g.tileVisRadius(); tY++)
                {
                    if (!g.inVis(tX - tXPrev, tY - tYPrev) && g.inVis(tX - tileX, tY - tileY))
                    {
                        visAdd(g, unit, tX, tY, time);
                    }
                }
            }
            // remove unit from visibility tiles
            for (tX = tXPrev - g.tileVisRadius(); tX <= tXPrev + g.tileVisRadius(); tX++)
            {
                for (tY = tYPrev - g.tileVisRadius(); tY <= tYPrev + g.tileVisRadius(); tY++)
                {
                    if (g.inVis(tX - tXPrev, tY - tYPrev) && !g.inVis(tX - tileX, tY - tileY))
                    {
                        visRemove(g, unit, tX, tY, time);
                    }
                }
            }
            if (tileX >= 0 && tileX < g.tileLen() && tileY >= 0 && tileY < g.tileLen())
            {
                // update whether this unit may time travel
                if (!g.u[unit].coherent() && g.tiles[tileX, tileY].coherentLatest(g.u[unit].player))
                {
                    g.u[unit].cohere(time);
                }
                else if (g.u[unit].coherent() && !g.tiles[tileX, tileY].coherentLatest(g.u[unit].player))
                {
                    g.u[unit].decohere();
                }
                if (tXPrev >= 0 && tXPrev < g.tileLen() && tYPrev >= 0 && tYPrev < g.tileLen())
                {
                    // if this unit moved out of another player's visibility, remove that player's visibility here
                    for (i = 0; i < g.nPlayers; i++)
                    {
                        if (i != g.u[unit].player && g.tiles[tXPrev, tYPrev].playerDirectVisLatest(i) && !g.tiles[tileX, tileY].playerDirectVisLatest(i))
                        {
                            for (tX = Math.Max(0, tileX - 1); tX <= Math.Min(g.tileLen() - 1, tileX + 1); tX++)
                            {
                                for (tY = Math.Max(0, tileY - 1); tY <= Math.Min(g.tileLen() - 1, tileY + 1); tY++)
                                {
                                    // TODO?: use more accurate time at tiles other than (tileX, tileY)
                                    g.events.add(new PlayerVisRemoveEvt(time, i, tX, tY));
                                }
                            }
                        }
                    }
                }
            }
            if (tXPrev >= 0 && tXPrev < g.tileLen() && tYPrev >= 0 && tYPrev < g.tileLen())
            {
                // if this player can no longer directly see another player's unit, remove this player's visibility there
                foreach (int j in g.tiles[tXPrev, tYPrev].unitVis.Keys)
                {
                    if (g.u[j].player != g.u[unit].player && g.u[j].healthLatest() > 0 && g.inVis(g.u[j].tileX - tXPrev, g.u[j].tileY - tYPrev) && !g.tiles[g.u[j].tileX, g.u[j].tileY].playerDirectVisLatest(g.u[unit].player))
                    {
                        for (tX = Math.Max(0, g.u[j].tileX - 1); tX <= Math.Min(g.tileLen() - 1, g.u[j].tileX + 1); tX++)
                        {
                            for (tY = Math.Max(0, g.u[j].tileY - 1); tY <= Math.Min(g.tileLen() - 1, g.u[j].tileY + 1); tY++)
                            {
                                // TODO?: use more accurate time at tiles other than (u[j].tileX, u[j].tileY)
                                g.events.add(new PlayerVisRemoveEvt(time, g.u[unit].player, tX, tY));
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// makes specified tile visible to specified unit starting at specified time, including effects on player visibility
        /// </summary>
        private static void visAdd(Sim g, int unit, int tileX, int tileY, long time)
        {
            int i, tX, tY;
            if (tileX >= 0 && tileX < g.tileLen() && tileY >= 0 && tileY < g.tileLen())
            {
                if (g.tiles[tileX, tileY].unitVisLatest(unit)) throw new InvalidOperationException("unit " + unit + " already sees tile (" + tileX + ", " + tileY + ")");
                // add unit to unit visibility tile
                g.tiles[tileX, tileY].unitVisToggle(unit, time);
                // TODO: use smarter playerVis adding algorithm
                // also, if opponent units that can't make anything enter then exit region previously indirectly visible, should use smarter playerVis adding algorithm where last one exited
                if (!g.tiles[tileX, tileY].playerVisLatest(g.u[unit].player))
                {
                    g.tiles[tileX, tileY].playerVis[g.u[unit].player].Add(time);
                    // check if a tile cohered for this player, or decohered for another player
                    for (i = 0; i < g.nPlayers; i++)
                    {
                        for (tX = Math.Max(0, tileX - g.tileVisRadius()); tX <= Math.Min(g.tileLen() - 1, tileX + g.tileVisRadius()); tX++)
                        {
                            for (tY = Math.Max(0, tileY - g.tileVisRadius()); tY <= Math.Min(g.tileLen() - 1, tileY + g.tileVisRadius()); tY++)
                            {
                                if (i == g.u[unit].player && !g.tiles[tX, tY].coherentLatest(i) && g.calcCoherent(i, tX, tY))
                                {
                                    g.coherenceAdd(i, tX, tY, time);
                                }
                                else if (i != g.u[unit].player && g.tiles[tX, tY].coherentLatest(i) && !g.calcCoherent(i, tX, tY))
                                {
                                    g.coherenceRemove(i, tX, tY, time);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// makes specified tile not visible to specified unit starting at specified time, including effects on player visibility
        /// </summary>
        private static void visRemove(Sim g, int unit, int tileX, int tileY, long time)
        {
            int tX, tY;
            long timePlayerVis = long.MaxValue;
            if (tileX >= 0 && tileX < g.tileLen() && tileY >= 0 && tileY < g.tileLen())
            {
                if (!g.tiles[tileX, tileY].unitVisLatest(unit)) throw new InvalidOperationException("unit " + unit + " already doesn't see tile (" + tileX + ", " + tileY + ")");
                // remove unit from unit visibility tile
                g.tiles[tileX, tileY].unitVisToggle(unit, time);
                // check if player can't directly see this tile anymore
                if (g.tiles[tileX, tileY].playerVisLatest(g.u[unit].player) && !g.tiles[tileX, tileY].playerDirectVisLatest(g.u[unit].player))
                {
                    // find lowest time that surrounding tiles lost visibility
                    for (tX = Math.Max(0, tileX - 1); tX <= Math.Min(g.tileLen() - 1, tileX + 1); tX++)
                    {
                        for (tY = Math.Max(0, tileY - 1); tY <= Math.Min(g.tileLen() - 1, tileY + 1); tY++)
                        {
                            if ((tX != tileX || tY != tileY) && !g.tiles[tX, tY].playerVisLatest(g.u[unit].player))
                            {
                                if (g.tiles[tX, tY].playerVis[g.u[unit].player].Count == 0)
                                {
                                    timePlayerVis = long.MinValue;
                                }
                                else if (g.tiles[tX, tY].playerVis[g.u[unit].player][g.tiles[tX, tY].playerVis[g.u[unit].player].Count - 1] < timePlayerVis)
                                {
                                    timePlayerVis = g.tiles[tX, tY].playerVis[g.u[unit].player][g.tiles[tX, tY].playerVis[g.u[unit].player].Count - 1];
                                }
                            }
                        }
                    }
                    // if player can't see all neighboring tiles, they won't be able to tell if another player's unit moves into this tile
                    // so remove this tile's visibility for this player
                    if (timePlayerVis != long.MaxValue)
                    {
                        timePlayerVis = Math.Max(time, timePlayerVis + (1 << FP.Precision) / g.maxSpeed); // TODO: use more accurate time
                        g.events.add(new PlayerVisRemoveEvt(timePlayerVis, g.u[unit].player, tileX, tileY));
                    }
                }
            }
        }
    }

    /// <summary>
    /// event in which a player stops seeing a tile
    /// </summary>
    public class PlayerVisRemoveEvt : SimEvt
    {
        public int player;
        public int tileX, tileY;

        public PlayerVisRemoveEvt(long timeVal, int playerVal, int tileXVal, int tileYVal)
        {
            time = timeVal;
            player = playerVal;
            tileX = tileXVal;
            tileY = tileYVal;
        }

        public override void apply(Sim g)
        {
            int i, tX, tY;
            if (g.tiles[tileX, tileY].playerVisLatest(player) && !g.tiles[tileX, tileY].playerDirectVisLatest(player))
            {
                g.tiles[tileX, tileY].playerVis[player].Add(time);
                // check if a tile decohered for this player, or cohered for another player
                for (i = 0; i < g.nPlayers; i++)
                {
                    for (tX = Math.Max(0, tileX - g.tileVisRadius()); tX <= Math.Min(g.tileLen() - 1, tileX + g.tileVisRadius()); tX++)
                    {
                        for (tY = Math.Max(0, tileY - g.tileVisRadius()); tY <= Math.Min(g.tileLen() - 1, tileY + g.tileVisRadius()); tY++)
                        {
                            if (i == player && g.tiles[tX, tY].coherentLatest(i) && !g.calcCoherent(i, tX, tY))
                            {
                                g.coherenceRemove(i, tX, tY, time);
                            }
                            else if (i != player && !g.tiles[tX, tY].coherentLatest(i) && g.calcCoherent(i, tX, tY))
                            {
                                g.coherenceAdd(i, tX, tY, time);
                            }
                        }
                    }
                }
                // add events to remove visibility from surrounding tiles
                for (tX = Math.Max(0, tileX - 1); tX <= Math.Min(g.tileLen() - 1, tileX + 1); tX++)
                {
                    for (tY = Math.Max(0, tileY - 1); tY <= Math.Min(g.tileLen() - 1, tileY + 1); tY++)
                    {
                        if ((tX != tileX || tY != tileY) && g.tiles[tX, tY].playerVisLatest(player))
                        {
                            // TODO: use more accurate time
                            g.events.add(new PlayerVisRemoveEvt(time + (1 << FP.Precision) / g.maxSpeed, player, tX, tY));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// list of simulation events in order of ascending event time
    /// </summary>
    public class SimEvtList
    {
        public List<SimEvt> events;

        public SimEvtList()
        {
            events = new List<SimEvt>();
        }

        /// <summary>
        /// inserts specified event into list in order of ascending event time
        /// </summary>
        public void add(SimEvt evt)
        {
            int ins;
            for (ins = events.Count; ins >= 1 && evt.time < events[ins - 1].time; ins--) ;
            events.Insert(ins, evt);
        }

        /// <summary>
        /// pops the first (earliest) event from the list, returning null if list is empty
        /// </summary>
        public SimEvt pop()
        {
            if (events.Count == 0) return null;
            SimEvt ret = events[0];
            events.RemoveAt(0);
            return ret;
        }

        /// <summary>
        /// returns time of first (earliest) event in list, or long.MaxValue if list is empty
        /// </summary>
        public long peekTime()
        {
            if (events.Count == 0) return long.MaxValue;
            return events[0].time;
        }
    }
}
