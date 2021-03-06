// Copyright (c) 2013-2014 Andrew Downing
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using ProtoBuf;

/// <summary>
/// base class for simulation events
/// </summary>
[ProtoContract]
[ProtoInclude(10, typeof(CmdEvt))]
[ProtoInclude(11, typeof(UpdateEvt))]
[ProtoInclude(12, typeof(TileUpdateEvt))]
[ProtoInclude(13, typeof(StackEvt))]
[ProtoInclude(14, typeof(PlayerVisRemoveEvt))]
[ProtoInclude(15, typeof(WaypointAddEvt))]
public abstract class SimEvt {
	[ProtoMember(1)] public long time;

	public abstract void apply(Sim g);
}
