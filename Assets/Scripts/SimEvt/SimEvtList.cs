// Written in 2013 by Andrew Downing
// To the extent possible under law, the author(s) have dedicated all copyright and related and neighboring rights to this software to the public domain worldwide. This software is distributed without any warranty.
// You should have received a copy of the CC0 Public Domain Dedication along with this software. If not, see https://creativecommons.org/publicdomain/zero/1.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;

/// <summary>
/// list of simulation events in order of ascending event time
/// </summary>
[ProtoContract]
public class SimEvtList {
	[ProtoMember(1)] public List<SimEvt> events;

	public SimEvtList() {
		events = new List<SimEvt>();
	}

	/// <summary>
	/// inserts specified event into list in order of ascending event time
	/// </summary>
	public void add(SimEvt evt) {
		int ins;
		for (ins = events.Count; ins >= 1 && evt.time < events[ins - 1].time; ins--) ;
		events.Insert(ins, evt);
	}

	/// <summary>
	/// pops the first (earliest) event from the list, returning null if list is empty
	/// </summary>
	public SimEvt pop() {
		if (events.Count == 0) return null;
		SimEvt ret = events[0];
		events.RemoveAt(0);
		return ret;
	}

	/// <summary>
	/// returns time of first (earliest) event in list, or long.MaxValue if list is empty
	/// </summary>
	public long peekTime() {
		if (events.Count == 0) return long.MaxValue;
		return events[0].time;
	}
}
