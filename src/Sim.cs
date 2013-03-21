﻿// game simulation
// Copyright (c) 2013 Andrew Downing
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SlimDX;

namespace Decoherence
{
    class Sim
    {
        // constants
        public const int OffMap = -10000; // don't set to int.MinValue so doesn't overflow in inVis()

        // game objects
        public class Player
        {
            public string name;
            public bool isUser; // whether actively controlled by either a human or AI
            public short user; // -1 = nobody, 0 = computer, 1+ = human
            public bool[] mayAttack; // if this player's units may attack each other player's units
        }

        public class UnitType
        {
            public string name;
            public string imgPath;
            /*public string sndSelect;
            public string sndMove;
            public string sndAnniCmd;
            public string sndAnnihilate;*/
            public int maxHealth;
            public long speed;
            public int[] damage; // damage done per attack to each unit type
            public long reload; // time needed to reload
            public long range; // range of attack
            public double selRadius;
        }

        public class Scenario
        {
            public long mapSize;
            public long camSpeed;
            public FP.Vector camPos;
            public float drawScl;
            public float drawSclMin;
            public float drawSclMax;
            public Color4 backCol;
            public Color4 borderCol;
            public Color4 noVisCol;
            public Color4 playerVisCol;
            public Color4 unitVisCol;
            public Color4 coherentCol;
            public Color4 amplitudeCol;
            //public string music;
            public long visRadius;
            public int nPlayers;
            public int nUnitT;
            public Player[] players;
            public UnitType[] unitT;

            // returns index of player with specified name, or -1 if no such player
            public int playerNamed(string name)
            {
                for (int i = 0; i < nPlayers; i++)
                {
                    if (name == players[i].name) return i;
                }
                return -1;
            }

            // returns index of unit type with specified name, or -1 if no such unit type
            public int unitTypeNamed(string name)
            {
                for (int i = 0; i < nUnitT; i++)
                {
                    if (name == unitT[i].name) return i;
                }
                return -1;
            }
        }

        public class UnitMove // unit movement (linearly interpolated between 2 points)
        {
            public long timeStart; // time when starts moving
            public long timeEnd; // time when finishes moving
            public FP.Vector vecStart; // z indicates rotation
            public FP.Vector vecEnd;

            public UnitMove(long timeStartVal, long timeEndVal, FP.Vector vecStartVal, FP.Vector vecEndVal)
            {
                timeStart = timeStartVal;
                timeEnd = timeEndVal;
                vecStart = vecStartVal;
                vecEnd = vecEndVal;
            }

            public UnitMove(long timeVal, FP.Vector vecVal)
                : this(timeVal, timeVal + 1, vecVal, vecVal)
            {
            }

            public static UnitMove fromSpeed(long timeStartVal, long speed, FP.Vector vecStartVal, FP.Vector vecEndVal)
            {
                return new UnitMove(timeStartVal, timeStartVal + new FP.Vector(vecEndVal - vecStartVal).length() / speed, vecStartVal, vecEndVal);
            }

            public FP.Vector calcPos(long time) // returns location at specified time
            {
                if (time >= timeEnd) return vecEnd;
                return vecStart + (vecEnd - vecStart) * FP.div(time - timeStart, timeEnd - timeStart);
            }

            public long timeAtX(long x)
            {
                return lineCalcX(new FP.Vector(timeStart, vecStart.x), new FP.Vector(timeEnd, vecEnd.x), x);
            }

            public long timeAtY(long y)
            {
                return lineCalcX(new FP.Vector(timeStart, vecStart.y), new FP.Vector(timeEnd, vecEnd.y), y);
            }
        }

        public class Unit
        {
            public int type;
            public int player;
            public int n; // number of moves
            public UnitMove[] m;
            public int mLive; // index of latest move that was live
            //public FP.Vector pos; // current position
            public int tileX, tileY; // current position on visibility tiles
            public int nTimeHealth;
            public long[] timeHealth; // times at which each health increment is removed
            public long timeAttack; // latest time that attacked a unit
            public bool coherent; // whether safe to time travel at simulation time
            public long timeCohere; // earliest time at which it's safe to time travel
            public int parentAmp; // unit which this unit split off from to form an amplitude (set to <0 if none)
            public int nChildAmps;
            public int[] childAmps; // unit amplitudes which split off from this unit

            public Unit(int typeVal, int playerVal, long startTime, FP.Vector startPos, int parentAmpVal = -1)
            {
                type = typeVal;
                player = playerVal;
                n = 1;
                m = new UnitMove[n];
                m[0] = new UnitMove(startTime, startPos);
                mLive = (startTime > timeSim) ? 0 : -1;
                //pos = startPos;
                tileX = OffMap;
                tileY = OffMap;
                nTimeHealth = 0;
                timeHealth = new long[g.unitT[type].maxHealth];
                timeAttack = long.MinValue;
                coherent = tileAt(startPos).coherentWhen(player, startTime);
                timeCohere = coherent ? startTime : long.MaxValue;
                parentAmp = parentAmpVal;
                nChildAmps = 0;
                childAmps = new int[nChildAmps];
            }

            public void setN(int newSize)
            {
                int i = 0;
                for (i = n; i < Math.Min(newSize, m.Length); i++)
                {
                    m[i] = new UnitMove(0, new FP.Vector());
                }
                n = newSize;
                if (n > m.Length)
                    Array.Resize(ref m, n * 2);
            }

            public void addMove(UnitMove newMove)
            {
                setN(n + 1);
                m[n - 1] = newMove;
                if (newMove.timeStart >= timeSim) mLive = n - 1;
            }

            public FP.Vector calcPos(long time)
            {
                return m[getMove(time)].calcPos(time);
            }

            public int getMove(long time)
            {
                int ret = n - 1;
                while (ret >= 0 && time < m[ret].timeStart) ret--;
                return ret;
            }

            public void addMoveEvts(ref SimEvtList events, int id, long timeMin, long timeMax)
            {
                int move, moveLast;
                FP.Vector pos, posLast;
                int i, tX, tY, dir;
                if (timeMax < m[0].timeStart) return;
                moveLast = getMove(timeMin);
                move = getMove(timeMax);
                if (moveLast < 0)
                {
                    // put unit on visibility tiles for the first time
                    events.add(new TileMoveEvt(m[0].timeStart, id, (int)(m[0].vecStart.x >> FP.Precision), (int)(m[0].vecStart.y >> FP.Precision)));
                    moveLast = 0;
                }
                for (i = moveLast; i <= move; i++)
                {
                    posLast = (i == moveLast) ? m[i].calcPos(Math.Max(timeMin, m[0].timeStart)) : m[i].vecStart;
                    pos = (i == move) ? m[i].calcPos(timeMax) : m[i + 1].vecStart;
                    // moving between columns (x)
                    dir = (pos.x >= posLast.x) ? 0 : -1;
                    for (tX = (int)(Math.Min(pos.x, posLast.x) >> FP.Precision) + 1; tX <= (int)(Math.Max(pos.x, posLast.x) >> FP.Precision); tX++)
                    {
                        events.add(new TileMoveEvt(m[i].timeAtX(tX << FP.Precision), id, tX + dir, int.MinValue));
                    }
                    // moving between rows (y)
                    dir = (pos.y >= posLast.y) ? 0 : -1;
                    for (tY = (int)(Math.Min(pos.y, posLast.y) >> FP.Precision) + 1; tY <= (int)(Math.Max(pos.y, posLast.y) >> FP.Precision); tY++)
                    {
                        events.add(new TileMoveEvt(m[i].timeAtY(tY << FP.Precision), id, int.MinValue, tY + dir));
                    }
                }
            }

            // remove 1 health increment at specified time
            public void takeHealth(long time)
            {
                if (nTimeHealth < g.unitT[type].maxHealth)
                {
                    nTimeHealth++;
                    timeHealth[nTimeHealth - 1] = time;
                    if (nTimeHealth >= g.unitT[type].maxHealth)
                    {
                        // unit lost all health
                        addMove(new UnitMove(time, new FP.Vector(OffMap << FP.Precision, 0)));
                    }
                }
            }

            public int healthLatest()
            {
                return g.unitT[type].maxHealth - nTimeHealth;
            }

            public int healthWhen(long time)
            {
                int i = nTimeHealth;
                while (i > 0 && time < timeHealth[i - 1]) i--;
                return g.unitT[type].maxHealth - i;
            }

            public void cohere(int id, long time)
            {
                coherent = true;
                timeCohere = time;
            }

            public void decohere(int id, long time)
            {
                coherent = false;
                timeCohere = long.MaxValue;
                deleteAllChildAmps();
                if (parentAmp >= 0) moveToParentAmp(id);
            }

            // if this unit is an amplitude, delete it and return true, otherwise return false
            public bool deleteAmp(int id)
            {
                if (nChildAmps > 0)
                {
                    // become the last child amplitude (overwriting our current amplitude in the process)
                    u[childAmps[nChildAmps - 1]].moveToParentAmp(childAmps[nChildAmps - 1]);
                    return true;
                }
                if (parentAmp >= 0)
                {
                    // if we don't have a child amplitude but have a parent amplitude, delete this unit completely
                    u[parentAmp].deleteChildAmp(id);
                    return true;
                }
                return false; // this unit is not an amplitude
            }

            public bool makeChildAmp(int id, long time)
            {
                if (coherent && (time > timeSim || time >= timeCohere))
                {
                    // check that unit is currently on map
                    FP.Vector pos = calcPos(time);
                    if (pos.x <= OffMap << FP.Precision) return false;
                    // make unit amplitude
                    setNUnits(nUnits + 1);
                    u[nUnits - 1] = new Unit(type, player, time, pos, id);
                    // add it to child amplitude list
                    nChildAmps++;
                    if (nChildAmps > childAmps.Length)
                        Array.Resize(ref childAmps, nChildAmps * 2);
                    childAmps[nChildAmps - 1] = nUnits - 1;
                    return true;
                }
                return false;
            }

            public void deleteChildAmp(int unit)
            {
                int index;
                for (index = 0; index < nChildAmps && childAmps[index] != unit; index++) ;
                if (index == nChildAmps) throw new ArgumentException("unit " + unit + " is not a child amplitude");
                // remove child amplitude from list
                for (int i = index; i < nChildAmps - 1; i++)
                {
                    childAmps[i] = childAmps[i + 1];
                }
                nChildAmps--;
                // delete child amplitude
                u[unit].addMove(new UnitMove(long.MinValue, new FP.Vector(OffMap << FP.Precision, 0)));
                u[unit].parentAmp = -1;
            }

            // recursively delete all child amplitudes
            private void deleteAllChildAmps()
            {
                for (int i = 0; i < nChildAmps; i++)
                {
                    u[childAmps[i]].addMove(new UnitMove(long.MinValue, new FP.Vector(OffMap << FP.Precision, 0)));
                    u[childAmps[i]].parentAmp = -1;
                    u[childAmps[i]].deleteAllChildAmps();
                }
                nChildAmps = 0;
            }

            // move all moves to parent amplitude (so parent amplitude becomes us)
            public void moveToParentAmp(int id)
            {
                for (int i = 0; i < n; i++)
                {
                    u[parentAmp].addMove(m[i]);
                }
                u[parentAmp].deleteChildAmp(id);
            }
        }

        public class Tile
        {
            public Dictionary<int, List<long>> unitVis;
            public List<long>[] playerVis;
            public List<long>[] coherence;

            public Tile()
            {
                unitVis = new Dictionary<int,List<long>>();
                playerVis = new List<long>[g.nPlayers];
                coherence = new List<long>[g.nPlayers];
                for (int i = 0; i < g.nPlayers; i++)
                {
                    playerVis[i] = new List<long>();
                    coherence[i] = new List<long>();
                }
            }

            public void unitVisToggle(int unit, long time)
            {
                if (!unitVis.ContainsKey(unit)) unitVis.Add(unit, new List<long>());
                unitVis[unit].Add(time);
            }

            public bool unitVisLatest(int unit)
            {
                return unitVis.ContainsKey(unit) && visLatest(unitVis[unit]);
            }

            public bool unitVisWhen(int unit, long time)
            {
                return unitVis.ContainsKey(unit) && visWhen(unitVis[unit], time);
            }

            // returns if the specified tile is in the direct line of sight of a unit of specified player
            public bool playerDirectVisLatest(int player)
            {
                foreach (int i in unitVis.Keys)
                {
                    if (player == u[i].player && visLatest(unitVis[i])) return true;
                }
                return false;
            }

            public bool playerDirectVisWhen(int player, long time)
            {
                foreach (int i in unitVis.Keys)
                {
                    if (player == u[i].player && visWhen(unitVis[i], time)) return true;
                }
                return false;
            }

            // returns if the specified tile is either in the direct line of sight for specified player at latest time,
            // or if player can infer that other players' units aren't in specified tile at latest time
            public bool playerVisLatest(int player)
            {
                return visLatest(playerVis[player]);
            }

            // returns if the specified tile is either in the direct line of sight for specified player at specified time,
            // or if player can infer that other players' units aren't in specified tile at specified time
            public bool playerVisWhen(int player, long time)
            {
                return visWhen(playerVis[player], time);
            }

            public bool coherentLatest(int player)
            {
                return visLatest(coherence[player]);
            }

            // returns if it is impossible for other players' units to see this location
            // this isn't the actual definition of coherence, but this is an important concept in the game and I need a name for it
            public bool coherentWhen(int player, long time)
            {
                return visWhen(coherence[player], time);
            }

            private static bool visLatest(List<long> vis)
            {
                return vis.Count % 2 == 1;
            }

            private static bool visWhen(List<long> vis, long time)
            {
                for (int i = vis.Count - 1; i >= 0; i--)
                {
                    if (time >= vis[i]) return i % 2 == 0;
                }
                return false;
            }
        }

        // simulation events
        public abstract class SimEvt // base class for simulation events
        {
            public long time;

            public abstract void apply();
        }

        public class SimEvtList
        {
            private List<SimEvt> events;

            public SimEvtList()
            {
                events = new List<SimEvt>();
            }

            public void add(SimEvt evt)
            {
                int ins;
                for (ins = events.Count; ins >= 1 && evt.time < events[ins - 1].time; ins--) ;
                events.Insert(ins, evt);
            }

            public SimEvt pop()
            {
                if (events.Count == 0) return null;
                SimEvt ret = events[0];
                events.RemoveAt(0);
                return ret;
            }

            public long peekTime()
            {
                if (events.Count == 0) return long.MaxValue;
                return events[0].time;
            }
        }

        public enum Formation : byte { Tight, Loose };

        public class CmdMoveEvt : SimEvt // command to move unit(s)
        {
            public long moveTime; // time is latest simulation time when command is given, moveTime is when units told to move (may be in past)
            public int[] units;
            public FP.Vector pos; // where to move to
            public Formation formation;

            public CmdMoveEvt(long timeVal, long moveTimeVal, int[] unitsVal, FP.Vector posVal, Formation formationVal)
            {
                time = timeVal;
                moveTime = moveTimeVal;
                units = unitsVal;
                pos = posVal;
                formation = formationVal;
            }

            public override void apply()
            {
                FP.Vector curPos, goal;
                long spacing;
                FP.Vector rows, offset;
                int count = 0, i = 0;
                // copy event to command history list (it should've already been popped from event list)
                cmdHistory.add(this);
                // count number of units able to move
                foreach (int unit in units)
                {
                    if ((moveTime > timeSimLast || (moveTime >= u[unit].timeCohere && u[unit].coherent)) && u[unit].calcPos(moveTime).x > OffMap << FP.Precision) count++;
                }
                if (count == 0) return;
                // calculate spacing
                // TODO: loose formation should be triangular
                // TODO: tight formation spacing should be customizable
                if (formation == Formation.Loose)
                {
                    spacing = FP.mul(g.visRadius, FP.fromDouble(Math.Sqrt(2))) >> FP.Precision << FP.Precision;
                }
                else
                {
                    spacing = 1 << FP.Precision;
                }
                rows.x = (int)Math.Ceiling(Math.Sqrt(count)); // TODO: don't use sqrt
                rows.y = (count - 1) / rows.x + 1;
                rows.z = 0;
                offset = (rows - new FP.Vector(1, 1)) * spacing / 2;
                if (pos.x < offset.x) pos.x = offset.x;
                if (pos.x > g.mapSize - offset.x) pos.x = g.mapSize - offset.x;
                if (pos.y < offset.y) pos.y = offset.y;
                if (pos.y > g.mapSize - offset.y) pos.y = g.mapSize - offset.y;
                // move units
                foreach (int unit in units)
                {
                    if (moveTime > timeSimLast || (moveTime >= u[unit].timeCohere && u[unit].coherent))
                    {
                        curPos = u[unit].calcPos(moveTime);
                        if (curPos.x <= OffMap << FP.Precision) continue;
                        goal = pos + new FP.Vector((i % rows.x) * spacing - offset.x, i / rows.x * spacing - offset.y);
                        if (goal.x < 0) goal.x = 0;
                        if (goal.x > g.mapSize) goal.x = g.mapSize;
                        if (goal.y < 0) goal.y = 0;
                        if (goal.y > g.mapSize) goal.y = g.mapSize;
                        u[unit].addMove(UnitMove.fromSpeed(moveTime, g.unitT[u[unit].type].speed, curPos, goal));
                        i++;
                    }
                }
            }
        }

        public class AttackEvt : SimEvt // event in which unit targets and possibly attacks a unit
        {
            public AttackEvt(long timeVal)
            {
                time = timeVal;
            }

            public override void apply()
            {
                FP.Vector pos;
                int target;
                long dist, targetDistSq;
                int i, i2;
                for (i = 0; i < nUnits; i++)
                {
                    if (time >= u[i].timeAttack + g.unitT[u[i].type].reload)
                    {
                        // done reloading, look for closest target
                        pos = u[i].calcPos(time);
                        target = -1;
                        targetDistSq = g.unitT[u[i].type].range * g.unitT[u[i].type].range + 1;
                        for (i2 = 0; i2 < nUnits; i2++)
                        {
                            if (i != i2 && g.players[u[i].player].mayAttack[u[i2].player] && g.unitT[u[i].type].damage[u[i2].type] > 0)
                            {
                                dist = (u[i2].calcPos(time) - pos).lengthSq();
                                if (dist < targetDistSq)
                                {
                                    target = i2;
                                    targetDistSq = dist;
                                }
                            }
                        }
                        if (target >= 0)
                        {
                            // attack target
                            // take health with 1 ms delay so earlier units in array don't have unfair advantage
                            for (i2 = 0; i2 < g.unitT[u[i].type].damage[u[target].type]; i2++) u[target].takeHealth(time + 1);
                            u[i].timeAttack = time;
                        }
                    }
                }
                events.add(new AttackEvt(time + 250));
            }
        }

        public class TileMoveEvt : SimEvt // event in which unit moves between visibility tiles
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

            public override void apply()
            {
                int i, tXPrev, tYPrev, tX, tY;
                if (tileX == int.MinValue) tileX = u[unit].tileX;
                if (tileY == int.MinValue) tileY = u[unit].tileY;
                tXPrev = u[unit].tileX;
                tYPrev = u[unit].tileY;
                u[unit].tileX = tileX;
                u[unit].tileY = tileY;
                // add unit to visibility tiles
                for (tX = tileX - tileVisRadius(); tX <= tileX + tileVisRadius(); tX++)
                {
                    for (tY = tileY - tileVisRadius(); tY <= tileY + tileVisRadius(); tY++)
                    {
                        if (!inVis(tX - tXPrev, tY - tYPrev) && inVis(tX - tileX, tY - tileY))
                        {
                            visAdd(unit, tX, tY, time);
                        }
                    }
                }
                // remove unit from visibility tiles
                for (tX = tXPrev - tileVisRadius(); tX <= tXPrev + tileVisRadius(); tX++)
                {
                    for (tY = tYPrev - tileVisRadius(); tY <= tYPrev + tileVisRadius(); tY++)
                    {
                        if (inVis(tX - tXPrev, tY - tYPrev) && !inVis(tX - tileX, tY - tileY))
                        {
                            visRemove(unit, tX, tY, time);
                        }
                    }
                }
                if (tileX >= 0 && tileX < tileLen() && tileY >= 0 && tileY < tileLen())
                {
                    // update whether this unit may time travel
                    if (!u[unit].coherent && tiles[tileX, tileY].coherentWhen(u[unit].player, time))
                    {
                        u[unit].cohere(unit, time);
                    }
                    else if (u[unit].coherent && !tiles[tileX, tileY].coherentWhen(u[unit].player, time))
                    {
                        u[unit].decohere(unit, time);
                    }
                    if (tXPrev >= 0 && tXPrev < tileLen() && tYPrev >= 0 && tYPrev < tileLen())
                    {
                        // if this unit moved out of another player's visibility, remove that player's visibility here
                        for (i = 0; i < g.nPlayers; i++)
                        {
                            if (i != u[unit].player && tiles[tXPrev, tYPrev].playerDirectVisLatest(i) && !tiles[tileX, tileY].playerDirectVisLatest(i))
                            {
                                for (tX = Math.Max(0, tileX - 1); tX <= Math.Min(tileLen() - 1, tileX + 1); tX++)
                                {
                                    for (tY = Math.Max(0, tileY - 1); tY <= Math.Min(tileLen() - 1, tileY + 1); tY++)
                                    {
                                        // TODO?: use more accurate time at tiles other than (tileX, tileY)
                                        events.add(new PlayerVisRemoveEvt(time, i, tX, tY));
                                    }
                                }
                            }
                        }
                        // if this player can no longer directly see another player's unit, remove this player's visibility there
                        foreach (int i2 in tiles[tXPrev, tYPrev].unitVis.Keys)
                        {
                            if (u[i2].player != u[unit].player && inVis(u[i2].tileX - tXPrev, u[i2].tileY - tYPrev) && !tiles[u[i2].tileX, u[i2].tileY].playerDirectVisLatest(u[unit].player))
                            {
                                for (tX = Math.Max(0, u[i2].tileX - 1); tX <= Math.Min(tileLen() - 1, u[i2].tileX + 1); tX++)
                                {
                                    for (tY = Math.Max(0, u[i2].tileY - 1); tY <= Math.Min(tileLen() - 1, u[i2].tileY + 1); tY++)
                                    {
                                        // TODO?: use more accurate time at tiles other than (p[i2].tileX, p[i2].tileY)
                                        events.add(new PlayerVisRemoveEvt(time, u[unit].player, tX, tY));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public class PlayerVisAddEvt : SimEvt // event in which a player starts seeing a tile (incomplete)
        {
            public int player;
            public int tileX, tileY;

            public PlayerVisAddEvt(long timeVal, int playerVal, int tileXVal, int tileYVal)
            {
                time = timeVal;
                player = playerVal;
                tileX = tileXVal;
                tileY = tileYVal;
            }

            public override void apply()
            {
                int i, tX, tY;
                // TODO: copy code from visAdd()
                // add events to add visibility to surrounding tiles (TODO: likely has bugs)
                for (tX = Math.Max(0, tileX - 1); tX <= Math.Min(tileLen() - 1, tileX + 1); tX++)
                {
                    for (tY = Math.Max(0, tileY - 1); tY <= Math.Min(tileLen() - 1, tileY + 1); tY++)
                    {
                        if ((tX != tileX || tY != tileY) && !tiles[tX, tY].playerVisLatest(player))
                        {
                            // TODO: use more accurate time
                            events.add(new PlayerVisAddEvt(time - (1 << FP.Precision) / maxSpeed, player, tX, tY));
                        }
                    }
                }
            }
        }

        public class PlayerVisRemoveEvt : SimEvt // event in which a player stops seeing a tile
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

            public override void apply()
            {
                int i, tX, tY;
                if (tiles[tileX, tileY].playerVisLatest(player) && !tiles[tileX, tileY].playerDirectVisLatest(player))
                {
                    tiles[tileX, tileY].playerVis[player].Add(time);
                    // check if a tile decohered for this player, or cohered for another player
                    for (i = 0; i < g.nPlayers; i++)
                    {
                        for (tX = Math.Max(0, tileX - tileVisRadius()); tX <= Math.Min(tileLen() - 1, tileX + tileVisRadius()); tX++)
                        {
                            for (tY = Math.Max(0, tileY - tileVisRadius()); tY <= Math.Min(tileLen() - 1, tileY + tileVisRadius()); tY++)
                            {
                                if (i == player && tiles[tX, tY].coherentLatest(i) && !calcCoherent(i, tX, tY, time))
                                {
                                    coherenceRemove(i, tX, tY, time);
                                }
                                else if (i != player && !tiles[tX, tY].coherentLatest(i) && calcCoherent(i, tX, tY, time))
                                {
                                    coherenceAdd(i, tX, tY, time);
                                }
                            }
                        }
                    }
                    // add events to remove visibility from surrounding tiles
                    for (tX = Math.Max(0, tileX - 1); tX <= Math.Min(tileLen() - 1, tileX + 1); tX++)
                    {
                        for (tY = Math.Max(0, tileY - 1); tY <= Math.Min(tileLen() - 1, tileY + 1); tY++)
                        {
                            if ((tX != tileX || tY != tileY) && tiles[tX, tY].playerVisLatest(player))
                            {
                                // TODO: use more accurate time
                                events.add(new PlayerVisRemoveEvt(time + (1 << FP.Precision) / maxSpeed, player, tX, tY));
                            }
                        }
                    }
                }
            }
        }

        // game variables
        public static Scenario g;
        public static int nUnits;
        public static Unit[] u;

        // helper variables
        public static Tile[,] tiles;
        public static SimEvtList events;
        public static SimEvtList cmdHistory;
        public static long maxSpeed;
        public static long timeSim;
        public static long timeSimLast;

        public static void setNUnits(int newSize)
        {
            nUnits = newSize;
            if (nUnits > u.Length)
                Array.Resize(ref u, nUnits * 2);
        }

        public static void update(long curTime)
        {
            FP.Vector pos;
            int i;
            // do timing
            if (curTime <= timeSim)
            {
                updatePast(curTime);
                return;
            }
            timeSimLast = timeSim;
            timeSim = curTime;
            // tiles visible at previous latest live move may no longer be visible
            for (i = 0; i < nUnits; i++)
            {
                if (u[i].mLive < u[i].n - 1)
                {
                    u[i].mLive = u[i].n - 1;
                    pos = u[i].calcPos(timeSimLast + 1);
                    events.add(new TileMoveEvt(timeSimLast + 1, i, (int)(pos.x >> FP.Precision), (int)(pos.y >> FP.Precision)));
                }
            }
            // check if units moved between tiles
            for (i = 0; i < nUnits; i++)
            {
                u[i].addMoveEvts(ref events, i, timeSimLast, timeSim);
            }
            // apply simulation events
            while (events.peekTime() <= timeSim)
            {
                events.pop().apply();
            }
        }

        public static void updatePast(long curTime)
        {
            FP.Vector pos;
            int i;
            // apply simulation events
            while (events.peekTime() <= timeSim)
            {
                events.pop().apply();
            }
            // restore to last coherent/live state if unit moves off coherent area
            // TODO: choose check state times more intelligently
            // (how do I do that in multiplayer, when time traveling at same time as updating present?)
            for (i = 0; i < nUnits; i++)
            {
                if (curTime >= u[i].timeCohere && u[i].mLive < u[i].n - 1)
                {
                    pos = u[i].calcPos(curTime);
                    if (pos.x >= 0 && !tileAt(pos).coherentWhen(u[i].player, curTime))
                    {
                        // if this is an amplitude then delete it, otherwise restore to previous state that was live
                        if (!u[i].deleteAmp(i)) u[i].setN(u[i].mLive + 1);
                    }
                }
            }
        }

        private static void visAdd(int unit, int tileX, int tileY, long time)
        {
            int i, tX, tY;
            bool filled = true;
            if (tileX >= 0 && tileX < tileLen() && tileY >= 0 && tileY < tileLen())
            {
                if (tiles[tileX, tileY].unitVisLatest(unit)) throw new InvalidOperationException("unit " + unit + " already sees tile (" + tileX + ", " + tileY + ")");
                // add unit to unit visibility tile
                tiles[tileX, tileY].unitVisToggle(unit, time);
                // TODO: use smarter playerVis adding algorithm
                // also, if opponent units that can't make anything enter then exit region previously indirectly visible, should use smarter playerVis adding algorithm where last one exited
                if (!tiles[tileX, tileY].playerVisLatest(u[unit].player))
                {
                    tiles[tileX, tileY].playerVis[u[unit].player].Add(time);
                    // check if a tile cohered for this player, or decohered for another player
                    for (i = 0; i < g.nPlayers; i++)
                    {
                        for (tX = Math.Max(0, tileX - tileVisRadius()); tX <= Math.Min(tileLen() - 1, tileX + tileVisRadius()); tX++)
                        {
                            for (tY = Math.Max(0, tileY - tileVisRadius()); tY <= Math.Min(tileLen() - 1, tileY + tileVisRadius()); tY++)
                            {
                                if (i == u[unit].player && !tiles[tX, tY].coherentLatest(i) && calcCoherent(i, tX, tY, time))
                                {
                                    coherenceAdd(i, tX, tY, time);
                                }
                                else if (i != u[unit].player && tiles[tX, tY].coherentLatest(i) && !calcCoherent(i, tX, tY, time))
                                {
                                    coherenceRemove(i, tX, tY, time);
                                }
                            }
                        }
                    }
                    for (tX = Math.Max(0, tileX - 1); tX <= Math.Min(tileLen() - 1, tileX + 1); tX++)
                    {
                        for (tY = Math.Max(0, tileY - 1); tY <= Math.Min(tileLen() - 1, tileY + 1); tY++)
                        {
                            if (!tiles[tileX, tileY].playerVisLatest(u[unit].player)) filled = false;
                        }
                    }
                    //if (filled) events.add(new PlayerVisAddEvt(time, u[unit].player, tileX, tileY));
                }
            }
        }

        private static void visRemove(int unit, int tileX, int tileY, long time)
        {
            int tX, tY;
            long timePlayerVis = long.MaxValue;
            if (tileX >= 0 && tileX < tileLen() && tileY >= 0 && tileY < tileLen())
            {
                if (!tiles[tileX, tileY].unitVisLatest(unit)) throw new InvalidOperationException("unit " + unit + " already doesn't see tile (" + tileX + ", " + tileY + ")");
                // remove unit from unit visibility tile
                tiles[tileX, tileY].unitVisToggle(unit, time);
                // check if player can't directly see this tile anymore
                if (tiles[tileX, tileY].playerVisLatest(u[unit].player) && !tiles[tileX, tileY].playerDirectVisLatest(u[unit].player))
                {
                    // find lowest time that surrounding tiles lost visibility
                    for (tX = Math.Max(0, tileX - 1); tX <= Math.Min(tileLen() - 1, tileX + 1); tX++)
                    {
                        for (tY = Math.Max(0, tileY - 1); tY <= Math.Min(tileLen() - 1, tileY + 1); tY++)
                        {
                            if ((tX != tileX || tY != tileY) && !tiles[tX, tY].playerVisLatest(u[unit].player))
                            {
                                if (tiles[tX, tY].playerVis[u[unit].player].Count == 0)
                                {
                                    timePlayerVis = long.MinValue;
                                }
                                else if (tiles[tX, tY].playerVis[u[unit].player][tiles[tX, tY].playerVis[u[unit].player].Count - 1] < timePlayerVis)
                                {
                                    timePlayerVis = tiles[tX, tY].playerVis[u[unit].player][tiles[tX, tY].playerVis[u[unit].player].Count - 1];
                                }
                            }
                        }
                    }
                    // if player can't see all neighboring tiles, they won't be able to tell if another player's unit moves into this tile
                    // so remove this tile's visibility for this player
                    if (timePlayerVis != long.MaxValue)
                    {
                        timePlayerVis = Math.Max(time, timePlayerVis + (1 << FP.Precision) / maxSpeed); // TODO: use more accurate time
                        events.add(new PlayerVisRemoveEvt(timePlayerVis, u[unit].player, tileX, tileY));
                    }
                }
            }
        }

        private static void coherenceAdd(int player, int tX, int tY, long time)
        {
            if (tiles[tX, tY].coherentLatest(player)) throw new InvalidOperationException("tile (" + tX + ", " + tY + ") is already coherent");
            tiles[tX, tY].coherence[player].Add(time);
            // this player's units that are on this tile may time travel starting now
            // TODO: actually safe to time travel at earlier times, as long as unit of same type is at same place when decoheres
            for (int i = 0; i < nUnits; i++)
            {
                if (player == u[i].player && tX == u[i].tileX && tY == u[i].tileY && !u[i].coherent)
                {
                    u[i].cohere(i, time);
                }
            }
        }

        private static void coherenceRemove(int player, int tX, int tY, long time)
        {
            if (!tiles[tX, tY].coherentLatest(player)) throw new InvalidOperationException("tile (" + tX + ", " + tY + ") is already not coherent");
            tiles[tX, tY].coherence[player].Add(time);
            // this player's units that are on this tile may not time travel starting now
            for (int i = 0; i < nUnits; i++)
            {
                if (player == u[i].player && tX == u[i].tileX && tY == u[i].tileY && u[i].coherent)
                {
                    u[i].decohere(i, time);
                }
            }
        }

        // calculates from player visibility tiles if it is impossible for other players' units to see this location
        private static bool calcCoherent(int player, int tileX, int tileY, long time)
        {
            int i, tX, tY;
            // check that this player can see all nearby tiles
            for (tX = Math.Max(0, tileX - tileVisRadius()); tX <= Math.Min(tileLen() - 1, tileX + tileVisRadius()); tX++)
            {
                for (tY = Math.Max(0, tileY - tileVisRadius()); tY <= Math.Min(tileLen() - 1, tileY + tileVisRadius()); tY++)
                {
                    if (inVis(tX - tileX, tY - tileY) && !tiles[tX, tY].playerVisWhen(player, time)) return false;
                }
            }
            // check that no other players can see this tile
            for (i = 0; i < g.nPlayers; i++)
            {
                if (i != player && tiles[tileX, tileY].playerVisWhen(i, time)) return false;
            }
            return true;
        }

        // returns index of unit that is the root parent amplitude of the specified unit
        public static int rootParentAmp(int unit)
        {
            int ret = unit;
            while (u[ret].parentAmp >= 0) ret = u[ret].parentAmp;
            return ret;
        }

        public static bool inVis(long tX, long tY)
        {
            //return Math.Max(Math.Abs(tX), Math.Abs(tY)) <= (int)(visRadius >> FP.Precision);
            return new FP.Vector(tX << FP.Precision, tY << FP.Precision).lengthSq() <= g.visRadius * g.visRadius;
        }

        public static int tileVisRadius()
        {
            return (int)(g.visRadius >> FP.Precision); // adding "+ 1" to this actually doesn't make a difference
        }

        public static Tile tileAt(FP.Vector pos)
        {
            return tiles[pos.x >> FP.Precision, pos.y >> FP.Precision];
        }

        public static int tileLen() // TODO: use unitVis.GetUpperBound instead of this function
        {
            return (int)((g.mapSize >> FP.Precision) + 1);
        }

        public static long lineCalcX(FP.Vector p1, FP.Vector p2, long y)
        {
            return FP.mul(y - p1.y, FP.div(p2.x - p1.x, p2.y - p1.y)) + p1.x;
        }

        public static long lineCalcY(FP.Vector p1, FP.Vector p2, long x)
        {
            return FP.mul(x - p1.x, FP.div(p2.y - p1.y, p2.x - p1.x)) + p1.y; // easily derived from point-slope form
        }
    }
}
