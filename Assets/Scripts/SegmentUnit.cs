// Written in 2013-2014 by Andrew Downing
// To the extent possible under law, the author(s) have dedicated all copyright and related and neighboring rights to this software to the public domain worldwide. This software is distributed without any warranty.
// You should have received a copy of the CC0 Public Domain Dedication along with this software. If not, see https://creativecommons.org/publicdomain/zero/1.0/.

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
	public bool delete(bool addMoveLines = false) {
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
				if (ancestors[i].unit.attacks.Count > 0) return false;
				if (ancestors[i].segment.timeStart < timeEarliestChild && ancestors[i].unit.type.rscCollectRate.Where (r => r > 0).Any ()) {
					timeEarliestChild = ancestors[i].segment.timeStart;
				}
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
		// if a deleteAfter() call failed or removing unit led to player ever having negative resources,
		// add units back to segments they were removed from
		if (i < ancestors.Count || (timeEarliestChild != long.MaxValue && segment.path.player.checkNegRsc (timeEarliestChild, false) >= 0)) {
			foreach (KeyValuePair<Segment, List<Unit>> item in removed) {
				item.Key.units.AddRange (item.Value);
			}
			return false;
		}
		foreach (KeyValuePair<Segment, List<Unit>> item in removed) {
			// add deleted units to list
			if (item.Key.timeStart < g.timeSim) {
				if (item.Key.nextOnPath () == null) item.Key.path.insertSegment (g.timeSim);
				item.Key.deletedUnits.AddRange (item.Value);
			}
		}
		if (addMoveLines) {
			// add deleted unit lines
			// TODO: tweak time if deleted before timeSimPast
			MoveLine deleteLine = new MoveLine(Math.Min (Math.Max (segment.path.timeSimPast, segment.path.moves[0].timeStart), g.timeSim), unit.player);
			foreach (Segment seg in removed.Keys) {
				deleteLine.vertices.AddRange (seg.path.moveLines (seg.timeStart,
					(seg.nextOnPath () == null || seg.nextOnPath ().timeStart > deleteLine.time) ? deleteLine.time : seg.nextOnPath ().timeStart));
			}
			g.deleteLines.Add (deleteLine);
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
						if (child.unit.attacks.Count > 0) return false;
						if (!child.deleteAfter (ref removed, ref timeEarliestChild)) return false;
						if (child.segment.timeStart < timeEarliestChild && child.unit.type.rscCollectRate.Where (r => r > 0).Any ()) {
							timeEarliestChild = child.segment.timeStart;
						}
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
	
	public bool unseenAfter(long time) {
		if (!segment.unseen || (unit.attacks.Count > 0 && time < unit.attacks.Last().time)) return false;
		foreach (SegmentUnit segmentUnit in next ()) {
			if (!segmentUnit.unseenAfter (time)) return false;
		}
		foreach (SegmentUnit child in children ()) {
			if (!child.unseenAfter (time)) return false;
		}
		return true;
	}
	
	public bool hasChildrenAfter() {
		if (children ().Any ()) return false; // TODO: this should return true if other units could make the child
		foreach (SegmentUnit segmentUnit in next ()) {
			if (!segmentUnit.hasChildrenAfter ()) return false;
		}
		return true;
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
