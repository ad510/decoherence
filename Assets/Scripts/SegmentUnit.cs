// Copyright (c) 2013-2014 Andrew Downing
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// identifies a unit on a path segment
/// </summary>
/// <remarks>this is a struct instead of a class because it compares by value</remarks>
public struct SegmentUnit {
	public readonly Sim g;
	public readonly Segment segment;
	public readonly Unit unit;
	
	public SegmentUnit(Segment segmentVal, Unit unitVal) {
		segment = segmentVal;
		unit = unitVal;
		g = segment.g;
	}
	
	public SegmentUnit(SegmentUnit segmentUnit) {
		this = segmentUnit;
	}

	/// <summary>
	/// removes unit from segment if doing so wouldn't affect anything that another player saw, returns whether successful
	/// </summary>
	public bool delete() {
		if (!segment.units.Contains (unit)) return true; // if this segment already doesn't contain this unit, return true
		List<SegmentUnit> ancestors = new List<SegmentUnit> { this };
		Dictionary<Segment, List<Unit>> removed = new Dictionary<Segment, List<Unit>>();
		long timeEarliestChild = long.MaxValue;
		int i;
		// find all ancestor segments to start removal from
		for (i = 0; i < ancestors.Count; i++) {
			if (ancestors[i].prev ().Any ()) {
				// if this ancestor has a sibling segment that we're not currently planning to remove unit from,
				// don't remove unit from previous segments shared by both
				bool hasSibling = false;
				foreach (Segment seg in ancestors[i].segment.branches) {
					if (seg.units.Contains (unit) && !ancestors.Contains (new SegmentUnit(seg, unit))
						&& (seg.path.timeSimPast == long.MaxValue || ancestors[i].segment.path.timeSimPast != long.MaxValue)) {
						hasSibling = true;
						break;
					}
				}
				if (!hasSibling) {
					// indicate to remove unit from previous segments
					ancestors.AddRange (ancestors[i].prev ());
					ancestors.RemoveAt(i);
					i--;
				}
			}
			else if (ancestors[i].segment.prev ().Any ()) {
				// unit has a parent but we're deleting its first segment, so may need to check resources starting at this time
				timeEarliestChild = Math.Min (timeEarliestChild, ancestors[i].segment.timeStart);
			}
			else {
				// reached a segment with no previous segment whatsoever, so return false (we assume other players know the scenario's starting state)
				return false;
			}
		}
		// remove unit recursively, starting at the ancestor segments we found
		for (i = 0; i < ancestors.Count; i++) {
			if (!ancestors[i].deleteAfter (ref removed, ref timeEarliestChild)) break;
		}
		// if a removeUnitAfter() call failed or removing unit might have led to player having negative resources,
		// add units back to segments they were removed from
		if (i < ancestors.Count || (timeEarliestChild != long.MaxValue && segment.path.player.checkNegRsc (timeEarliestChild, false) >= 0)) {
			foreach (KeyValuePair<Segment, List<Unit>> item in removed) {
				item.Key.units.AddRange (item.Value);
			}
			return false;
		}
		// remove paths that no longer contain units from visibility tiles
		foreach (Segment seg in removed.Keys) {
			if (seg.id == seg.path.segments.Count - 1 && seg.units.Count == 0 && seg.path.tileX != Sim.OffMap) {
				g.events.add(new TileMoveEvt(g.timeSim, seg.path.id, Sim.OffMap, 0));
			}
		}
		return true;
	}
	
	private bool deleteAfter(ref Dictionary<Segment, List<Unit>> removed, ref long timeEarliestChild) {
		if (segment.units.Contains (unit)) {
			if (!segment.unseen && segment.timeStart < g.timeSim) return false;
			// only remove units from next segments if this is their only previous segment
			if (segment.nextOnPath () == null || new SegmentUnit(segment.nextOnPath (), unit).prev ().Count () == 1) {
				// remove unit from next segments
				foreach (SegmentUnit segmentUnit in next ()) {
					if (!segmentUnit.deleteAfter (ref removed, ref timeEarliestChild)) return false;
				}
				// remove child units that only this unit could have made
				foreach (SegmentUnit child in children ().ToArray ()) {
					// TODO: if has alternate non-live parent, do we need to recursively make children non-live?
					if (child.parents ().Count () == 1) {
						if (!child.deleteAfter (ref removed, ref timeEarliestChild)) return false;
						timeEarliestChild = Math.Min (timeEarliestChild, child.segment.timeStart);
					}
				}
			}
			// remove unit from this segment
			segment.units.Remove (unit);
			if (!removed.ContainsKey (segment)) removed.Add (segment, new List<Unit>());
			removed[segment].Add (unit);
		}
		return true;
	}
	
	public bool unseenAfter() {
		if (!segment.unseen) return false;
		foreach (SegmentUnit segmentUnit in next ()) {
			if (!segmentUnit.unseenAfter ()) return false;
		}
		foreach (SegmentUnit child in children ()) {
			if (!child.unseenAfter ()) return false;
		}
		return true;
	}

	/// <summary>
	/// returns resource amount gained by this unit and its children (subtracting cost to make children) for every combination of paths,
	/// from this segment's start time to specified time
	/// </summary>
	public List<Dictionary<Unit, long>> rscCollected(long time, int rscType, bool includeNonLiveChildren) {
		List<Dictionary<Unit, long>> ret = new List<Dictionary<Unit, long>>();
		// if this segment wasn't active yet, unit can't have collected anything
		if (time < segment.timeStart) return ret;
		// if next segment wasn't active yet, return resources collected from timeStart to time
		if (segment.nextOnPath () == null || time < segment.nextOnPath ().timeStart) {
			ret.Add (new Dictionary<Unit, long>());
			ret[0][unit] = unit.type.rscCollectRate[rscType] * (time - segment.timeStart);
			return ret;
		}
		// add resources gained in each combination of next segments
		foreach (SegmentUnit segmentUnit in next ()) {
			if (includeNonLiveChildren || segmentUnit.segment.path.timeSimPast == long.MaxValue) {
				ret.AddRange (segmentUnit.rscCollected (time, rscType, includeNonLiveChildren));
			}
		}
		// add resources gained by children
		foreach (SegmentUnit child in children ()) {
			List<Dictionary<Unit, long>> childCollected = child.rscCollected (time, rscType, includeNonLiveChildren);
			List<Dictionary<Unit, long>> newRet = new List<Dictionary<Unit, long>>();
			// subtract cost to make child unit
			foreach (Dictionary<Unit, long> childCombination in childCollected) {
				childCombination[child.unit] -= child.unit.type.rscCost[rscType];
			}
			// add each child unit combination to each combination so far
			foreach (Dictionary<Unit, long> combination in ret) {
				foreach (Dictionary<Unit, long> childCombination in childCollected) {
					Dictionary<Unit, long> newCombination = new Dictionary<Unit, long>(combination);
					foreach (KeyValuePair<Unit, long> item in childCombination) {
						if (!newCombination.ContainsKey (item.Key)) newCombination[item.Key] = item.Value;
						if (newCombination[item.Key] != item.Value) throw new SystemException("unit collected different amount of resources in different calculations (this should never happen)");
					}
					newRet.Add (newCombination);
				}
			}
			ret = newRet;
		}
		// add resources collected on this segment
		foreach (Dictionary<Unit, long> combination in ret) {
			if (!combination.ContainsKey (unit)) combination[unit] = 0;
			combination[unit] += unit.type.rscCollectRate[rscType] * (segment.nextOnPath ().timeStart - segment.timeStart);
		}
		return ret;
	}
	
	/// <summary>
	/// returns whether this unit is in the same path before specified time,
	/// so if it makes a child unit, it's unambiguous who is the parent
	/// </summary>
	public bool canBeUnambiguousParent(long time) {
		return segment.timeStart < time || (segment.prevOnPath () != null && segment.prevOnPath ().units.Contains (unit));
	}
	
	/// <summary>
	/// iterates over all segment/unit pairs that could have made this unit in this segment
	/// </summary>
	public IEnumerable<SegmentUnit> parents() {
		if (!prev ().Any ()) {
			foreach (Segment seg in segment.prev ()) {
				foreach (SegmentUnit segmentUnit in seg.segmentUnits ()) {
					if (segmentUnit.unit.type.canMake[unit.type.id]) {
						yield return segmentUnit;
					}
				}
			}
		}
	}
	
	/// <summary>
	/// iterates over all segment/unit pairs that this unit in this segment could have made
	/// </summary>
	public IEnumerable<SegmentUnit> children() {
		foreach (Segment seg in segment.next ()) {
			foreach (SegmentUnit segmentUnit in seg.segmentUnits ()) {
				if (unit.type.canMake[segmentUnit.unit.type.id] && !segmentUnit.prev ().Any ()) {
					yield return segmentUnit;
				}
			}
		}
	}
	
	/// <summary>
	/// iterates over all segments containing this unit that merge onto the beginning of this segment
	/// </summary>
	public IEnumerable<SegmentUnit> prev() {
		foreach (Segment seg in segment.prev()) {
			if (seg.units.Contains (unit)) yield return new SegmentUnit(seg, unit);
		}
	}
	
	/// <summary>
	/// iterates over all segments containing this unit that branch off from the end of this segment
	/// </summary>
	public IEnumerable<SegmentUnit> next() {
		foreach (Segment seg in segment.next()) {
			if (seg.units.Contains (unit)) yield return new SegmentUnit(seg, unit);
		}
	}
}
