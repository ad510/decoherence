// Copyright (c) 2013-2014 Andrew Downing
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;

[ProtoContract]
public class Player {
	[ProtoMember(1, AsReference = true)] public Sim g;
	[ProtoMember(2)] public int id; // index in player array
	// stored in scenario files
	[ProtoMember(3)] public string name;
	[ProtoMember(4)] public bool isUser; // whether actively participates in the game
	[ProtoMember(5)] public int user; // -2 = nobody, -1 = computer, 0+ = human
	[ProtoMember(12)] public int populationLimit;
	[ProtoMember(6)] public long[] startRsc; // resources at beginning of game
	[ProtoMember(7)] public bool[] canAttack; // if this player's units may attack each other player's units
	[ProtoMember(14)] public bool mapHack;
	// not stored in scenario files
	[ProtoMember(8)] public bool immutable; // whether player's units will never unpredictably move or change
	[ProtoMember(9)] public bool hasNonLivePaths; // whether currently might have time traveling paths (ok to sometimes incorrectly be set to true)
	[ProtoMember(10)] public long timeGoLiveFailedAttempt; // latest time that player's time traveling paths failed to go live (resets to long.MinValue after success)
	[ProtoMember(11)] public long timeGoLiveProblem; // time that player would be in invalid state if time traveling paths went live
	[ProtoMember(15)] public Dictionary<int, HashSet<Path>> goLiveStackPaths; // key is nSeeUnits, value is paths to stack when going live
	[ProtoMember(13)] public int unseenTiles;
	
	[ProtoAfterDeserialization]
	private void afterDeserialize() {
		if (goLiveStackPaths == null) goLiveStackPaths = new Dictionary<int, HashSet<Path>>();
	}

	/// <summary>
	/// update player's non-live (time traveling) paths
	/// </summary>
	public void updatePast(long curTime) {
		if (hasNonLivePaths) {
			foreach (Path path in g.paths) {
				if (this == path.player) path.updatePast(curTime, true);
			}
			if (curTime >= g.timeSim && g.timeSim >= timeGoLiveFailedAttempt + g.updateInterval) {
				// make player's time traveling paths go live
				long timeTravelStart = long.MaxValue;
				// find earliest time that player's paths started time traveling
				foreach (Path path in g.paths) {
					if (this == path.player && path.timeSimPast != long.MaxValue && path.segments[0].timeStart < timeTravelStart) {
						timeTravelStart = path.segments[0].timeStart;
					}
				}
				if (timeTravelStart != long.MaxValue) { // skip if player has no time traveling paths
					// check if going live would lead to player ever having negative resources or going over population limit
					timeGoLiveProblem = checkNegRsc(timeTravelStart, true);
					if (timeGoLiveProblem < 0 && populationLimit >= 0) {
						timeGoLiveProblem = checkPopulation(timeTravelStart);
					}
					if (timeGoLiveProblem >= 0) {
						// indicate failure to go live, then return
						timeGoLiveFailedAttempt = g.timeSim;
						return;
					}
					// safe for paths to become live, so do so
					foreach (Path path in g.paths) {
						if (this == path.player && path.timeSimPast != long.MaxValue) {
							List<SegmentUnit> queue = new List<SegmentUnit>();
							SegmentUnit nonLiveChild = new SegmentUnit();
							path.timeSimPast = long.MaxValue;
							foreach (Segment segment in path.segments) {
								queue.AddRange (segment.segmentUnits ());
							}
							while (queue.Count > 0) {
								foreach (SegmentUnit prev in queue[0].prev ()) {
									if (prev.segment.path.timeSimPast != long.MaxValue) {
										prev.segment.path.timeSimPast = long.MaxValue;
										queue.Add (prev);
									}
								}
								foreach (SegmentUnit parent in queue[0].parents ()) {
									if (parent.segment.path.timeSimPast != long.MaxValue) {
										parent.segment.path.timeSimPast = long.MaxValue;
										queue.Add (parent);
									}
								}
								if (nonLiveChild.g == null && queue[0].children().Any ()) nonLiveChild = queue[0];
								queue.RemoveAt (0);
							}
							if (nonLiveChild.g != null) g.deleteOtherPaths (new SegmentUnit[] { nonLiveChild }, true, false);
						}
					}
				}
				// indicate success
				hasNonLivePaths = false;
				timeGoLiveFailedAttempt = long.MinValue;
				// try again to stack paths that failed to stack in the past
				foreach (var item in goLiveStackPaths) {
					g.addStackEvts (item.Value.ToList (), item.Key);
				}
				goLiveStackPaths = new Dictionary<int, HashSet<Path>>();
			}
		}
	}

	public long resource(long time, int rscType, bool nonLive) {
		long ret = startRsc[rscType];
		foreach (SegmentUnit segmentUnit in newUnitSegments (nonLive)) {
			long existsInterval = ((segmentUnit.unit.healthWhen (time) == 0) ? segmentUnit.unit.timeHealth[segmentUnit.unit.nTimeHealth - 1] : time) - segmentUnit.segment.path.segments[0].timeStart;
			if (existsInterval >= 0) {
				ret += segmentUnit.unit.type.rscCollectRate[rscType] * existsInterval;
				if (segmentUnit.segment.path.id >= g.nRootPaths) ret -= segmentUnit.unit.type.rscCost[rscType];
			}
		}
		return ret;
	}

	/// <summary>
	/// checks whether player had negative resources since timeMin
	/// </summary>
	/// <returns>a time that player had negative resources, or -1 if no such time found</returns>
	public long checkNegRsc(long timeMin, bool nonLive) {
		foreach (Path path in g.paths) {
			// check all times since timeMin that a path of specified player was made
			if (this == path.player && path.segments[0].timeStart >= timeMin) {
				for (int i = 0; i < g.rscNames.Length; i++) {
					if (resource(path.segments[0].timeStart, i, nonLive) < 0) {
						return path.segments[0].timeStart;
					}
				}
			}
		}
		return -1;
	}
	
	/// <summary>
	/// checks whether player was overpopulated since timeMin
	/// </summary>
	/// <returns>a time that player was overpopulated, or -1 if no such time found</returns>
	public long checkPopulation(long timeMin) {
		if (populationLimit < 0) return -1;
		foreach (Path path in g.paths) {
			// check all times since timeMin that a path of specified player was made
			if (this == path.player && path.segments[0].timeStart >= timeMin) {
				if (population (path.segments[0].timeStart) > populationLimit) {
					return path.segments[0].timeStart;
				}
			}
		}
		return -1;
	}
	
	public int population(long time) {
		return newUnitSegments (true).Where(u => time >= u.segment.path.segments[0].timeStart && u.unit.healthWhen (time) > 0 && u.unit.type.speed > 0).Count();
	}
	
	/// <summary>
	/// returns all SegmentUnits containing a new unit of this player
	/// </summary>
	public IEnumerable<SegmentUnit> newUnitSegments(bool nonLive) {
		foreach (Path path in g.paths) {
			if (this == path.player && (nonLive || path.timeSimPast == long.MaxValue)) {
				// this assumes all new units are made on a single new path, which is currently a valid assumption
				foreach (SegmentUnit segmentUnit in path.segments[0].segmentUnits ()) {
					if (!segmentUnit.prev ().Any ()) yield return segmentUnit;
				}
			}
		}
	}

	/// <summary>
	/// returns whether player's units will never unpredictably move or change
	/// </summary>
	public bool calcImmutable() {
		// check that player isn't an active participant and isn't controlled by anyone
		if (isUser || user >= Sim.compUser) return false;
		// check that no one can attack this player
		foreach (Player player2 in g.players) {
			if (player2.canAttack[id]) return false;
		}
		return true;
	}
}
