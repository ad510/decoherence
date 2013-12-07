// Copyright (c) 2013 Andrew Downing
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

/// <summary>
/// contains initialization and user interface code
/// </summary>
public class App : MonoBehaviour {
	private class LineBox {
		public GameObject gameObject; // TODO: can use line.gameObject to refer to game object, don't need to store it separately
		public LineRenderer line;
		
		public LineBox() {
			gameObject = new GameObject();
			line = gameObject.AddComponent<LineRenderer>();
			line.material.shader = Shader.Find ("Diffuse");
			line.SetVertexCount (8);
		}
		
		public void setRect(Vector3 p1, Vector3 p2, float depth) {
			float minX = Math.Min (p1.x, p2.x);
			float minY = Math.Min (p1.y, p2.y);
			float maxX = Math.Max (p1.x, p2.x);
			float maxY = Math.Max (p1.y, p2.y);
			// extra vertices are needed to draw >1px thick lines correctly due to LineRenderer weirdness
			line.SetPosition (0, new Vector3(minX, minY, depth));
			line.SetPosition (1, new Vector3(maxX - 1, minY, depth));
			line.SetPosition (2, new Vector3(maxX, minY, depth));
			line.SetPosition (3, new Vector3(maxX, maxY - 1, depth));
			line.SetPosition (4, new Vector3(maxX, maxY, depth));
			line.SetPosition (5, new Vector3(minX + 1, maxY, depth));
			line.SetPosition (6, new Vector3(minX, maxY, depth));
			line.SetPosition (7, new Vector3(minX, minY, depth));
		}
		
		public void dispose() {
			Destroy(gameObject);
		}
	}
	
	private class UnitSprite {
		public GameObject sprite;
		public GameObject preview; // for showing unit at final position
		public GameObject healthBarBack;
		public GameObject healthBarFore;
		public LineRenderer pathLine;
		public int type;
		public int player;
		
		public UnitSprite(GameObject quadPrefab) {
			sprite = Instantiate(quadPrefab) as GameObject;
			preview = Instantiate(quadPrefab) as GameObject;
			healthBarBack = Instantiate(quadPrefab) as GameObject;
			healthBarFore = Instantiate(quadPrefab) as GameObject;
			pathLine = sprite.AddComponent<LineRenderer>();
			pathLine.material.shader = Shader.Find ("Diffuse");
			pathLine.SetVertexCount (2);
			type = -1;
			player = -1;
		}
		
		public void dispose() {
			Destroy (sprite);
			Destroy (preview);
			Destroy (healthBarBack);
			Destroy (healthBarFore);
		}
	}
	
	const bool EnableStacking = false;
	const double SelBoxMin = 100;
	const float FntSize = 1f / 40;
	const float TileDepth = 6f;
	const float BorderDepth = 5f;
	const float PathLineDepth = 4f;
	const float UnitDepth = 3f;
	const float HealthBarDepth = 2f;
	const float SelectBoxDepth = 1f;
	
	public GameObject quadPrefab;
	
	string appPath;
	string modPath = "mod/";
	float winDiag; // diagonal length of screen in pixels
	Texture2D texTile;
	GameObject sprTile;
	LineBox border;
	Texture[,] texUnits;
	List<List<UnitSprite>> sprUnits;
	GameObject sprMakeUnit;
	LineBox selectBox;
	GUIStyle lblStyle;
	Vector2 cmdsScrollPos;
	Vector2 makeUnitScrollPos;
	Vector2 selUnitsScrollPos;
	Sim g;
	int selPlayer;
	Dictionary<int, List<int>> selPaths; // TODO: this should consider time that paths were selected
	int makeUnitType;
	long timeNow;
	long timeLast;
	long timeGame;
	bool paused;
	int speed;
	long timeSpeedChg;
	Vector3[] mouseDownPos;
	Vector3[] mouseUpPos;
	string serverAddr = "127.0.0.1";
	int serverPort = 44247;
	
	/// <summary>
	/// use this for initialization
	/// </summary>
	void Start () {
		appPath = Application.streamingAssetsPath + '/';
		winDiag = new Vector2(Screen.width, Screen.height).magnitude;
		mouseDownPos = new Vector3[3];
		mouseUpPos = new Vector3[3];
		RenderSettings.ambientLight = Color.white;
		Camera.main.orthographicSize = Screen.height / 2;
		Camera.main.transform.position = new Vector3(Screen.width / 2f, Screen.height / 2f, 0);
		quadPrefab.renderer.material.shader = Shader.Find ("Transparent/Diffuse");
		quadPrefab.renderer.material.color = Color.white;
		quadPrefab.transform.rotation = new Quaternion(0, 1, 0, 0);
		sprTile = Instantiate (quadPrefab) as GameObject;
		border = new LineBox();
		border.line.SetWidth (2, 2); // TODO: make width customizable by mod
		sprMakeUnit = Instantiate (quadPrefab) as GameObject;
		// TODO: make color and width customizable by mod
		selectBox = new LineBox();
		selectBox.line.material.color = Color.white;
		selectBox.line.SetWidth (2, 2);
		// TODO: make font, size, and color customizable by mod
		lblStyle = GUIStyle.none;
		lblStyle.fontSize = (int)(Screen.height * FntSize);
		lblStyle.normal.textColor = Color.white;
		if (!scnOpen (appPath + modPath + "scn.json", 0, false)) {
			Debug.LogError ("Scenario failed to load.");
		}
	}
	
	/// <summary>
	/// loads scenario from json file and returns whether successful
	/// </summary>
	private bool scnOpen(string path, int user, bool multiplayer) {
		Hashtable json;
		ArrayList jsonA;
		int nUsers;
		bool b = false;
		// TODO: if this ever supports multiplayer games, host should load file & send data to other players, otherwise json double parsing may not match
		if (!System.IO.File.Exists(path)) return false;
		json = (Hashtable)Procurios.Public.JSON.JsonDecode(System.IO.File.ReadAllText(path), ref b);
		if (!b) return false;
		// base scenario
		g = new Sim();
		g.selUser = user;
		g.networkView = multiplayer ? networkView : null;
		g.events = new SimEvtList();
		g.cmdPending = new SimEvtList();
		g.cmdHistory = new SimEvtList();
		g.checksum = 0;
		g.synced = true;
		g.timeSim = -1;
		g.timeUpdateEvt = long.MinValue;
		g.events.add(new UpdateEvt(-1));
		g.maxSpeed = 0;
		g.mapSize = jsonFP(json, "mapSize");
		g.updateInterval = (long)jsonDouble(json, "updateInterval");
		g.visRadius = jsonFP(json, "visRadius");
		g.camPos = jsonFPVector(json, "camPos", new FP.Vector(g.mapSize / 2, g.mapSize / 2));
		g.camSpeed = jsonFP(json, "camSpeed");
		g.zoom = (float)jsonDouble(json, "zoom");
		g.zoomMin = (float)jsonDouble(json, "zoomMin");
		g.zoomMax = (float)jsonDouble(json, "zoomMax");
		g.zoomSpeed = (float)jsonDouble (json, "zoomSpeed");
		g.zoomMouseWheelSpeed = (float)jsonDouble (json, "zoomMouseWheelSpeed");
		g.uiBarHeight = (float)jsonDouble (json, "uiBarHeight");
		g.healthBarSize = jsonVector2(json, "healthBarSize");
		g.healthBarYOffset = (float)jsonDouble(json, "healthBarYOffset");
		g.backCol = jsonColor(json, "backCol");
		g.borderCol = jsonColor(json, "borderCol");
		g.noVisCol = jsonColor(json, "noVisCol");
		g.playerVisCol = jsonColor(json, "playerVisCol");
		g.unitVisCol = jsonColor(json, "unitVisCol");
		g.exclusiveCol = jsonColor(json, "exclusiveCol");
		g.pathCol = jsonColor(json, "pathCol");
		g.healthBarBackCol = jsonColor(json, "healthBarBackCol");
		g.healthBarFullCol = jsonColor(json, "healthBarFullCol");
		g.healthBarEmptyCol = jsonColor(json, "healthBarEmptyCol");
		//Sim.music = jsonString(json, "music");
		// resources
		g.rscNames = new string[0];
		jsonA = jsonArray(json, "resources");
		if (jsonA != null) {
			foreach (string rscName in jsonA) {
				Array.Resize(ref g.rscNames, g.rscNames.Length + 1);
				g.rscNames[g.rscNames.Length - 1] = rscName;
			}
		}
		// players
		nUsers = 0;
		g.players = new Player[0];
		jsonA = jsonArray(json, "players");
		if (jsonA != null) {
			foreach (Hashtable jsonO in jsonA) {
				Hashtable jsonO2 = jsonObject(jsonO, "startRsc");
				Player player = new Player();
				player.name = jsonString(jsonO, "name");
				player.isUser = jsonBool(jsonO, "isUser");
				player.user = (int)jsonDouble(jsonO, "user");
				if (player.user >= nUsers) nUsers = player.user + 1;
				player.startRsc = new long[g.rscNames.Length];
				for (int i = 0; i < g.rscNames.Length; i++) {
					player.startRsc[i] = (jsonO2 != null) ? jsonFP(jsonO2, g.rscNames[i]) : 0;
				}
				Array.Resize(ref g.players, g.players.Length + 1);
				g.players[g.players.Length - 1] = player;
			}
			foreach (Hashtable jsonO in jsonA) {
				ArrayList jsonA2 = jsonArray(jsonO, "mayAttack");
				Player player = g.players[g.playerNamed(jsonString(jsonO, "name"))];
				player.mayAttack = new bool[g.players.Length];
				for (int i = 0; i < g.players.Length; i++) {
					player.mayAttack[i] = false;
				}
				if (jsonA2 != null) {
					foreach (string s in jsonA2) {
						if (g.playerNamed(s) >= 0) {
							player.mayAttack[g.playerNamed(s)] = true;
						}
					}
				}
			}
			for (int i = 0; i < g.players.Length; i++) {
				g.players[i].immutable = g.calcPlayerImmutable(i);
			}
		}
		// users
		g.users = new User[nUsers];
		for (int i = 0; i < g.users.Length; i++) {
			g.users[i] = new User();
		}
		// unit types
		g.unitT = new UnitType[0];
		jsonA = jsonArray(json, "unitTypes");
		if (jsonA != null) {
			foreach (Hashtable jsonO in jsonA) {
				Hashtable jsonO2 = jsonObject(jsonO, "rscCost");
				Hashtable jsonO3 = jsonObject(jsonO, "rscCollectRate");
				UnitType unitT = new UnitType();
				unitT.name = jsonString(jsonO, "name");
				unitT.imgPath = jsonString(jsonO, "imgPath");
				unitT.imgOffset = jsonFPVector (jsonO, "imgOffset");
				unitT.imgHalfHeight = jsonFP (jsonO, "imgHalfHeight");
				unitT.selMinPos = jsonFPVector (jsonO, "selMinPos", new FP.Vector(unitT.imgOffset.x - unitT.imgHalfHeight, unitT.imgOffset.y - unitT.imgHalfHeight));
				unitT.selMaxPos = jsonFPVector (jsonO, "selMaxPos", new FP.Vector(unitT.imgOffset.x + unitT.imgHalfHeight, unitT.imgOffset.y + unitT.imgHalfHeight));
				unitT.maxHealth = (int)jsonDouble(jsonO, "maxHealth");
				unitT.speed = jsonFP(jsonO, "speed");
				if (unitT.speed > g.maxSpeed) g.maxSpeed = unitT.speed;
				unitT.reload = (long)jsonDouble(jsonO, "reload");
				unitT.range = jsonFP(jsonO, "range");
				unitT.tightFormationSpacing = jsonFP(jsonO, "tightFormationSpacing");
				unitT.makeUnitMinDist = jsonFP(jsonO, "makeUnitMinDist");
				unitT.makeUnitMaxDist = jsonFP(jsonO, "makeUnitMaxDist");
				unitT.makePathMinDist = jsonFP(jsonO, "makePathMinDist");
				unitT.makePathMaxDist = jsonFP(jsonO, "makePathMaxDist");
				unitT.rscCost = new long[g.rscNames.Length];
				unitT.rscCollectRate = new long[g.rscNames.Length];
				for (int i = 0; i < g.rscNames.Length; i++) {
					unitT.rscCost[i] = (jsonO2 != null) ? jsonFP(jsonO2, g.rscNames[i]) : 0;
					unitT.rscCollectRate[i] = (jsonO3 != null) ? jsonFP(jsonO3, g.rscNames[i]) : 0;
				}
				Array.Resize(ref g.unitT, g.unitT.Length + 1);
				g.unitT[g.unitT.Length - 1] = unitT;
			}
			foreach (Hashtable jsonO in jsonA) {
				Hashtable jsonO2 = jsonObject(jsonO, "damage");
				ArrayList jsonA2 = jsonArray(jsonO, "canMake");
				UnitType unitT = g.unitT[g.unitTypeNamed(jsonString(jsonO, "name"))];
				unitT.makeOnUnitT = g.unitTypeNamed(jsonString(jsonO, "makeOnUnitT"));
				unitT.damage = new int[g.unitT.Length];
				for (int i = 0; i < g.unitT.Length; i++) {
					unitT.damage[i] = (jsonO2 != null) ? (int)jsonDouble(jsonO2, g.unitT[i].name) : 0;
				}
				unitT.canMake = new bool[g.unitT.Length];
				for (int i = 0; i < g.unitT.Length; i++) {
					unitT.canMake[i] = false;
				}
				if (jsonA2 != null) {
					foreach (string s in jsonA2) {
						if (g.unitTypeNamed(s) >= 0) {
							unitT.canMake[g.unitTypeNamed(s)] = true;
						}
					}
				}
			}
		}
		texUnits = new Texture[g.unitT.Length, g.players.Length];
		for (int i = 0; i < g.unitT.Length; i++) {
			for (int j = 0; j < g.players.Length; j++) {
				if (!(texUnits[i, j] = loadTexture (appPath + modPath + g.players[j].name + '.' + g.unitT[i].imgPath))) {
					if (!(texUnits[i, j] = loadTexture (appPath + modPath + g.unitT[i].imgPath))) {
						Debug.LogWarning ("Failed to load " + modPath + g.players[j].name + '.' + g.unitT[i].imgPath);
					}
				}
			}
		}
		// tiles
		g.tiles = new Tile[g.tileLen(), g.tileLen()];
		for (int i = 0; i < g.tileLen(); i++) {
			for (int j = 0; j < g.tileLen(); j++) {
				g.tiles[i, j] = new Tile(g);
			}
		}
		texTile = new Texture2D(g.tileLen (), g.tileLen (), TextureFormat.ARGB32, false);
		// units
		g.units = new List<Unit>();
		g.paths = new List<Path>();
		jsonA = jsonArray(json, "units");
		if (jsonA != null) {
			foreach (Hashtable jsonO in jsonA) {
				if (g.playerNamed(jsonString(jsonO, "player")) >= 0) {
					ArrayList jsonA2 = jsonArray (jsonO, "types");
					List<int> units = new List<int>();
					if (jsonA2 != null) {
						foreach (string type in jsonA2) {
							if (g.unitTypeNamed(type) >= 0) {
								g.units.Add (new Unit(g, g.units.Count, g.unitTypeNamed(type), g.playerNamed(jsonString(jsonO, "player"))));
								units.Add (g.units.Count - 1);
							}
						}
					}
					g.paths.Add (new Path(g, g.paths.Count, units, (long)jsonDouble(jsonO, "startTime"),
						jsonFPVector(jsonO, "startPos", new FP.Vector((long)(UnityEngine.Random.value * g.mapSize), (long)(UnityEngine.Random.value * g.mapSize)))));
				}
			}
		}
		g.nRootPaths = g.paths.Count;
		if (sprUnits != null) {
			foreach (List<UnitSprite> sprs in sprUnits) {
				foreach (UnitSprite spr in sprs) {
					spr.dispose ();
				}
			}
		}
		sprUnits = new List<List<UnitSprite>>();
		// start game
		Camera.main.backgroundColor = g.backCol;
		border.line.material.color = g.borderCol;
		selPlayer = 0;
		while (g.players[selPlayer].user != g.selUser) selPlayer = (selPlayer + 1) % g.players.Length;
		selPaths = new Dictionary<int, List<int>>();
		makeUnitType = -1;
		paused = false;
		speed = 0;
		timeGame = 0;
		timeSpeedChg = (long)(Time.time * 1000) - 1000;
		timeNow = (long)(Time.time * 1000);
		return true;
	}
	
	private Texture2D loadTexture(string path) {
		if (!System.IO.File.Exists (path)) return null;
		Texture2D tex = new Texture2D(0, 0);
		byte[] imgBytes = System.IO.File.ReadAllBytes (path);
		tex.LoadImage (imgBytes);
		return tex;
	}
	
	/// <summary>
	/// Update is called once per frame
	/// </summary>
	void Update () {
		updateTime ();
		g.updatePast (selPlayer, timeGame);
		g.update (timeGame);
		updateInput ();
		draw ();
	}
	
	private void updateTime() {
		timeLast = timeNow;
		timeNow = (long)(Time.time * 1000);
		if (!paused) {
			long timeGameDiff;
			if (Input.GetKey (KeyCode.R)) {
				// rewind
				timeGameDiff = -(timeNow - timeLast);
			}
			else if (timeNow - timeLast > g.updateInterval && timeGame + timeNow - timeLast >= g.timeSim) {
				// cap time difference to a max amount
				timeGameDiff = g.updateInterval;
			}
			else {
				// normal speed
				timeGameDiff = timeNow - timeLast;
			}
			timeGame += (long)(timeGameDiff * Math.Pow(2, speed)); // adjust game speed based on user setting
		}
		// don't increment time past latest time that commands were synced across network
		if (g.networkView != null && timeGame >= g.timeUpdateEvt + g.updateInterval) {
			foreach (User user in g.users) {
				if (user.timeSync < g.timeUpdateEvt + g.updateInterval) {
					timeGame = g.timeUpdateEvt + g.updateInterval - 1;
					break;
				}
			}
		}
	}
	
	private void updateInput() {
		// handle changed mouse buttons
		if (Input.GetMouseButtonDown (0)) { // left button down
			mouseDownPos[0] = Input.mousePosition;
		}
		if (Input.GetMouseButtonDown (1)) { // right button down
			mouseDownPos[1] = Input.mousePosition;
		}
		if (Input.GetMouseButtonDown (2)) { // middle button down
			mouseDownPos[2] = Input.mousePosition;
		}
		if (Input.GetMouseButtonUp (0)) { // left button up
			mouseUpPos[0] = Input.mousePosition;
			if (mouseDownPos[0].y > Screen.height * g.uiBarHeight) {
				if (makeUnitType >= 0) {
					// make unit
					// happens at newCmdTime() + 1 so new unit starts out live if game is live
					FP.Vector pos = makeUnitPos();
					if (pos.x != Sim.OffMap) g.cmdPending.add(new MakeUnitCmdEvt(g.timeSim, newCmdTime() + 1, selPathsCopy(), makeUnitType, pos));
					makeUnitType = -1;
				}
				else {
					// select paths
					Vector3 mouseMinPos = new Vector3(Math.Min (mouseDownPos[0].x, Input.mousePosition.x), Math.Min (mouseDownPos[0].y, Input.mousePosition.y), 0);
					Vector3 mouseMaxPos = new Vector3(Math.Max (mouseDownPos[0].x, Input.mousePosition.x), Math.Max (mouseDownPos[0].y, Input.mousePosition.y), 0);
					if (!Input.GetKey (KeyCode.LeftControl) && !Input.GetKey (KeyCode.LeftShift)) selPaths.Clear();
					for (int i = 0; i < g.paths.Count; i++) {
						if (selPlayer == g.paths[i].player && timeGame >= g.paths[i].moves[0].timeStart
							&& FP.rectIntersects (drawToSimPos (mouseMinPos), drawToSimPos (mouseMaxPos),
							g.paths[i].selMinPos(timeGame), g.paths[i].selMaxPos(timeGame))) {
							// TODO: if not all units in path are selected, select remaining units instead of deselecting path
							if (selPaths.ContainsKey (i)) {
								selPaths.Remove(i);
							}
							else {
								selPaths.Add(i, new List<int>(g.paths[i].segments[g.paths[i].getSegment(timeGame)].units));
							}
							if (SelBoxMin > (Input.mousePosition - mouseDownPos[0]).sqrMagnitude) break;
						}
					}
				}
			}
		}
		if (Input.GetMouseButtonUp (1)) { // right button up
			mouseUpPos[1] = Input.mousePosition;
			if (mouseDownPos[1].y > Screen.height * g.uiBarHeight) {
				if (makeUnitType >= 0) {
					// cancel making unit
					makeUnitType = -1;
				}
				else {
					int stackPath = -1;
					for (int i = 0; i < g.paths.Count; i++) {
						if (selPlayer == g.paths[i].player && timeGame >= g.paths[i].moves[0].timeStart
							&& FP.rectContains (g.paths[i].selMinPos(timeGame), g.paths[i].selMaxPos(timeGame), drawToSimPos (Input.mousePosition))) {
							stackPath = i;
							break;
						}
					}
					if (EnableStacking && stackPath >= 0) {
						// stack selected paths onto clicked path
						g.cmdPending.add (new StackCmdEvt(g.timeSim, newCmdTime (), selPathsCopy (), stackPath));
					}
					else {
						// move selected paths
						g.cmdPending.add(new MoveCmdEvt(g.timeSim, newCmdTime(), selPathsCopy (), drawToSimPos (Input.mousePosition),
							Input.GetKey (KeyCode.LeftControl) ? Formation.Loose : Input.GetKey (KeyCode.LeftAlt) ? Formation.Ring : Formation.Tight));
					}
				}
			}
		}
		if (Input.GetMouseButtonUp (2)) { // middle button up
			mouseUpPos[2] = Input.mousePosition;
		}
		// handle changed keys
		if (Input.GetKeyDown (KeyCode.Escape)) {
			// exit
			Application.Quit ();
		}
		if (Input.GetKeyDown (KeyCode.Space)) {
			// change selected player
			do {
				selPlayer = (selPlayer + 1) % g.players.Length;
			} while (g.players[selPlayer].user != g.selUser);
			selPaths.Clear();
			makeUnitType = -1;
		}
		if (Input.GetKeyDown (KeyCode.P)) {
			// pause/resume
			paused = !paused;
		}
		if (Input.GetKeyDown (KeyCode.Equals)) {
			// increase speed
			speed++;
			timeSpeedChg = Environment.TickCount;
		}
		if (Input.GetKeyDown (KeyCode.Minus)) {
			// decrease speed
			speed--;
			timeSpeedChg = Environment.TickCount;
		}
		if (Input.GetKeyDown (KeyCode.N)) {
			// create new paths that selected units could take
			makePaths ();
		}
		if (Input.GetKeyDown (KeyCode.Delete)) {
			if (Input.GetKey (KeyCode.LeftShift) || Input.GetKey (KeyCode.RightShift)) {
				// delete unselected paths of selected units
				deleteOtherPaths ();
			}
			else {
				// delete selected paths
				deletePaths ();
			}
		}
		if (Input.GetKeyDown (KeyCode.D) && Input.GetKey (KeyCode.LeftShift)) {
			// delete unselected paths of selected units (alternate shortcut)
			deleteOtherPaths ();
		}
		// move camera
		if (Input.GetKey (KeyCode.LeftArrow) || (Input.mousePosition.x == 0 && Screen.fullScreen)) {
			g.camPos.x -= g.camSpeed * (timeNow - timeLast);
			if (g.camPos.x < 0) g.camPos.x = 0;
		}
		if (Input.GetKey (KeyCode.RightArrow) || (Input.mousePosition.x == Screen.width - 1 && Screen.fullScreen)) {
			g.camPos.x += g.camSpeed * (timeNow - timeLast);
			if (g.camPos.x > g.mapSize) g.camPos.x = g.mapSize;
		}
		if (Input.GetKey (KeyCode.DownArrow) || (Input.mousePosition.y == 0 && Screen.fullScreen)) {
			g.camPos.y -= g.camSpeed * (timeNow - timeLast);
			if (g.camPos.y < 0) g.camPos.y = 0;
		}
		if (Input.GetKey (KeyCode.UpArrow) || (Input.mousePosition.y == Screen.height - 1 && Screen.fullScreen)) {
			g.camPos.y += g.camSpeed * (timeNow - timeLast);
			if (g.camPos.y > g.mapSize) g.camPos.y = g.mapSize;
		}
		// zoom camera
		if (Input.GetKey (KeyCode.PageUp)) {
			g.zoom /= (float)Math.Exp (g.zoomSpeed * (timeNow - timeLast));
		}
		if (Input.GetKey (KeyCode.PageDown)) {
			g.zoom *= (float)Math.Exp (g.zoomSpeed * (timeNow - timeLast));
		}
		if (Input.mousePosition.y > Screen.height * g.uiBarHeight && Input.GetAxis ("Mouse ScrollWheel") != 0) {
			g.zoom *= (float)Math.Exp (g.zoomMouseWheelSpeed * Input.GetAxis ("Mouse ScrollWheel"));
		}
		if (g.zoom < g.zoomMin) g.zoom = g.zoomMin;
		if (g.zoom > g.zoomMax) g.zoom = g.zoomMax;
	}
	
	private void draw() {
		Vector3 vec = new Vector3();
		// visibility tiles
		// TODO: don't draw tiles off map
		for (int tX = 0; tX < g.tileLen(); tX++) {
			for (int tY = 0; tY < g.tileLen(); tY++) {
				Color col = g.noVisCol;
				if (g.tiles[tX, tY].playerVisWhen(selPlayer, timeGame)) {
					col += g.playerVisCol;
					if (g.tiles[tX, tY].playerDirectVisWhen(selPlayer, timeGame)) col += g.unitVisCol;
					if (g.tiles[tX, tY].exclusiveWhen(selPlayer, timeGame)) col += g.exclusiveCol;
				}
				texTile.SetPixel (tX, tY, col);
			}
		}
		texTile.Apply ();
		texTile.filterMode = FilterMode.Point;
		sprTile.renderer.material.mainTexture = texTile;
		sprTile.transform.position = simToDrawPos (new FP.Vector((g.tileLen () << FP.Precision) / 2, (g.tileLen () << FP.Precision) / 2), TileDepth);
		sprTile.transform.localScale = simToDrawScl (new FP.Vector((g.tileLen () << FP.Precision) / 2, (g.tileLen () << FP.Precision) / 2));
		// map border
		border.setRect (simToDrawPos (new FP.Vector()), simToDrawPos(new FP.Vector(g.mapSize, g.mapSize)), BorderDepth);
		// units
		for (int i = 0; i < g.paths.Count; i++) {
			int seg = g.paths[i].getSegment (timeGame);
			if (i == sprUnits.Count) sprUnits.Add (new List<UnitSprite>());
			if (seg >= 0) {
				while (sprUnits[i].Count < g.paths[i].segments[seg].units.Count) sprUnits[i].Add (new UnitSprite(quadPrefab));
			}
			for (int j = 0; j < sprUnits[i].Count; j++) {
				sprUnits[i][j].sprite.renderer.enabled = false;
				sprUnits[i][j].preview.renderer.enabled = false;
				sprUnits[i][j].healthBarBack.renderer.enabled = false;
				sprUnits[i][j].healthBarFore.renderer.enabled = false;
				sprUnits[i][j].pathLine.enabled = false;
			}
			if (pathDrawPos(i, ref vec)) {
				for (int j = 0; j < g.paths[i].segments[seg].units.Count; j++) {
					int unit = g.paths[i].segments[seg].units[j];
					if (sprUnits[i][j].type != g.units[unit].type || sprUnits[i][j].player != g.units[unit].player) {
						sprUnits[i][j].sprite.renderer.material.mainTexture = texUnits[g.units[unit].type, g.units[unit].player];
						sprUnits[i][j].preview.renderer.material.mainTexture = texUnits[g.units[unit].type, g.units[unit].player];
						sprUnits[i][j].pathLine.material.color = g.pathCol;
						sprUnits[i][j].type = g.units[unit].type;
						sprUnits[i][j].player = g.units[unit].player;
					}
					if (g.paths[i].timeSimPast == long.MaxValue) {
						sprUnits[i][j].sprite.renderer.material.color = new Color(1, 1, 1, 1);
					}
					else {
						sprUnits[i][j].sprite.renderer.material.color = new Color(1, 1, 1, 0.5f); // TODO: make transparency amount customizable
					}
					sprUnits[i][j].sprite.transform.position = vec + simToDrawScl (g.unitT[g.units[unit].type].imgOffset);
					sprUnits[i][j].sprite.transform.localScale = unitScale (g.units[unit].type, g.units[unit].player);
					sprUnits[i][j].sprite.renderer.enabled = true;
					for (int k = i + 1; k < g.paths.Count; k++) {
						int seg2 = g.paths[k].getSegment (timeGame);
						if (seg2 >= 0 && g.paths[i].speed == g.paths[k].speed && g.paths[i].player == g.paths[k].player
							&& g.paths[k].segments[seg2].units.Contains (unit)) {
							// unit path line
							sprUnits[i][j].pathLine.SetPosition (0, new Vector3(vec.x, vec.y, PathLineDepth));
							sprUnits[i][j].pathLine.SetPosition (1, simToDrawPos (g.paths[k].calcPos(timeGame), PathLineDepth));
							sprUnits[i][j].pathLine.enabled = true;
							break;
						}
					}
					if (Input.GetKey (KeyCode.LeftShift) && selPaths.ContainsKey(i)) {
						// show final position if holding shift
						sprUnits[i][j].preview.renderer.material.color = sprUnits[i][j].sprite.renderer.material.color;
						sprUnits[i][j].preview.transform.position = simToDrawPos(g.paths[i].moves.Last ().vecEnd + g.unitT[g.units[unit].type].imgOffset, UnitDepth);
						sprUnits[i][j].preview.transform.localScale = sprUnits[i][j].sprite.transform.localScale;
						sprUnits[i][j].preview.renderer.enabled = true;
					}
				}
			}
		}
		// unit to be made
		if (makeUnitType >= 0) {
			FP.Vector pos = makeUnitPos();
			sprMakeUnit.renderer.material.mainTexture = texUnits[makeUnitType, selPlayer];
			if (pos.x != Sim.OffMap) {
				sprMakeUnit.renderer.material.color = new Color(1, 1, 1, 1);
				sprMakeUnit.transform.position = simToDrawPos(pos + g.unitT[makeUnitType].imgOffset, UnitDepth);
			}
			else {
				sprMakeUnit.renderer.material.color = new Color(1, 1, 1, 0.5f); // TODO: make transparency amount customizable
				sprMakeUnit.transform.position = new Vector3(Input.mousePosition.x, Input.mousePosition.y, UnitDepth) + simToDrawScl (g.unitT[makeUnitType].imgOffset);
			}
			sprMakeUnit.transform.localScale = unitScale (makeUnitType, selPlayer);
			sprMakeUnit.renderer.enabled = true;
		}
		else {
			sprMakeUnit.renderer.enabled = false;
		}
		// health bars
		foreach (int path in selPaths.Keys) {
			if (pathDrawPos(path, ref vec)) {
				int seg = g.paths[path].getSegment (timeGame);
				for (int j = 0; j < g.paths[path].segments[seg].units.Count; j++) {
					int unit = g.paths[path].segments[seg].units[j];
					if (selPaths[path].Contains (unit)) {
						float f = ((float)g.units[unit].healthWhen(timeGame)) / g.unitT[g.units[unit].type].maxHealth;
						float f2 = vec.y + simToDrawScl (g.unitT[g.units[unit].type].selMaxPos.y) + g.healthBarYOffset * winDiag;
						// background
						if (g.units[unit].healthWhen(timeGame) > 0) {
							sprUnits[path][j].healthBarBack.renderer.material.color = g.healthBarBackCol;
							sprUnits[path][j].healthBarBack.transform.position = new Vector3(vec.x + g.healthBarSize.x * winDiag * f / 2, f2, HealthBarDepth);
							sprUnits[path][j].healthBarBack.transform.localScale = new Vector3(g.healthBarSize.x * winDiag * (1 - f) / 2, g.healthBarSize.y * winDiag / 2, 1);
							sprUnits[path][j].healthBarBack.renderer.enabled = true;
						}
						// foreground
						sprUnits[path][j].healthBarFore.renderer.material.color = g.healthBarEmptyCol + (g.healthBarFullCol - g.healthBarEmptyCol) * f;
						sprUnits[path][j].healthBarFore.transform.position = new Vector3(vec.x + g.healthBarSize.x * winDiag * (f - 1) / 2, f2, HealthBarDepth);
						sprUnits[path][j].healthBarFore.transform.localScale = new Vector3(g.healthBarSize.x * winDiag * f / 2, g.healthBarSize.y * winDiag / 2, 1);
						sprUnits[path][j].healthBarFore.renderer.enabled = true;
					}
				}
			}
		}
		// select box (if needed)
		if (Input.GetMouseButton (0) && makeUnitType < 0 && SelBoxMin <= (Input.mousePosition - mouseDownPos[0]).sqrMagnitude && mouseDownPos[0].y > Screen.height * g.uiBarHeight) {
			selectBox.setRect (mouseDownPos[0], Input.mousePosition, SelectBoxDepth);
			selectBox.line.enabled = true;
		}
		else {
			selectBox.line.enabled = false;
		}
	}
	
	void OnGUI() {
		GUI.skin.button.fontSize = lblStyle.fontSize;
		GUI.skin.textField.fontSize = lblStyle.fontSize;
		// text at top left
		GUILayout.BeginArea (new Rect(0, 0, Screen.width, Screen.height * (1 - g.uiBarHeight)));
		if (!g.synced) {
			lblStyle.normal.textColor = Color.red;
			GUILayout.Label ("OUT OF SYNC", lblStyle);
			lblStyle.normal.textColor = Color.white;
		}
		GUILayout.Label ((timeGame >= g.timeSim) ? "LIVE" : "TIME TRAVELING", lblStyle);
		if (paused) GUILayout.Label ("PAUSED", lblStyle);
		if (Environment.TickCount < timeSpeedChg) timeSpeedChg -= UInt32.MaxValue;
		if (Environment.TickCount < timeSpeedChg + 1000) GUILayout.Label ("SPEED: " + Math.Pow(2, speed) + "x", lblStyle);
		if (g.players[selPlayer].timeGoLiveFail != long.MaxValue) {
			lblStyle.normal.textColor = Color.red;
			GUILayout.Label ("ERROR: Going live may cause you to have negative resources " + (timeGame - g.players[selPlayer].timeNegRsc) / 1000 + " second(s) ago.", lblStyle);
		}
		// text at bottom left
		GUILayout.FlexibleSpace ();
		for (int i = 0; i < g.rscNames.Length; i++) {
			long rscMin = (long)Math.Floor(FP.toDouble(g.playerResource(selPlayer, timeGame, i, false, true)));
			long rscMax = (long)Math.Floor(FP.toDouble(g.playerResource(selPlayer, timeGame, i, true, true)));
			lblStyle.normal.textColor = (rscMin >= 0) ? Color.white : Color.red;
			GUILayout.Label (g.rscNames[i] + ": " + rscMin + ((rscMax != rscMin) ? " to " + rscMax : ""), lblStyle);
		}
		GUILayout.EndArea ();
		// TODO: formation buttons
		// TODO: timeline
		// TODO: mini map
		// command menu
		// TODO: show text or hide button if can't do any of these actions
		GUI.Box (new Rect(0, Screen.height * (1 - g.uiBarHeight), Screen.width / 2, Screen.height * g.uiBarHeight), new GUIContent());
		GUILayout.BeginArea (new Rect(0, Screen.height * (1 - g.uiBarHeight), Screen.width / 4, Screen.height * g.uiBarHeight));
		cmdsScrollPos = GUILayout.BeginScrollView (cmdsScrollPos);
		if (selPaths.Count > 0) {
			string plural = (selPaths.Count == 1) ? "" : "s";
			if (GUILayout.Button ("New Path" + plural)) makePaths ();
			if (GUILayout.Button ("Delete Path" + plural)) deletePaths ();
			if (GUILayout.Button ("Delete Other Paths")) deleteOtherPaths ();
		}
		GUILayout.EndScrollView ();
		GUILayout.EndArea ();
		// make unit menu
		GUILayout.BeginArea (new Rect(Screen.width / 4, Screen.height * (1 - g.uiBarHeight), Screen.width / 4, Screen.height * g.uiBarHeight));
		makeUnitScrollPos = GUILayout.BeginScrollView (makeUnitScrollPos);
		if (selPaths.Count > 0) {
			for (int i = 0; i < g.unitT.Length; i++) {
				foreach (int path in selPaths.Keys) {
					if (timeGame >= g.paths[path].moves[0].timeStart && g.paths[path].canMakeUnitType (timeGame, i)) { // TODO: sometimes canMake check should use existing selected units in path
						if (GUILayout.Button ("Make " + g.unitT[i].name)) makeUnit (i);
						break;
					}
				}
			}
		}
		GUILayout.EndScrollView ();
		GUILayout.EndArea ();
		// unit selection bar
		GUILayout.BeginArea (new Rect(Screen.width / 2, Screen.height * (1 - g.uiBarHeight), Screen.width / 2, Screen.height * g.uiBarHeight));
		selUnitsScrollPos = GUILayout.BeginScrollView (selUnitsScrollPos, "box");
		foreach (KeyValuePair<int, int> item in selUnits()) {
			if (GUILayout.Button (g.unitT[g.units[item.Key].type].name + (item.Value != 1 ? " (" + item.Value + " paths)" : ""))) {
				int[] selPathsKeys = new int[selPaths.Keys.Count];
				selPaths.Keys.CopyTo (selPathsKeys, 0);
				if (Event.current.button == 0) { // left button
					// select unit
					foreach (int path in selPathsKeys) {
						for (int i = 0; i < selPaths[path].Count; i++) {
							if (selPaths[path][i] != item.Key) {
								selPaths[path].RemoveAt (i);
								i--;
							}
						}
						if (selPaths[path].Count == 0) selPaths.Remove (path);
					}
				}
				else if (Event.current.button == 1) { // right button
					// deselect unit
					foreach (int path in selPathsKeys) {
						selPaths[path].Remove (item.Key);
						if (selPaths[path].Count == 0) selPaths.Remove (path);
					}
				}
			}
		}
		GUILayout.EndScrollView ();
		GUILayout.EndArea ();
		// multiplayer GUI
		// TODO: implement main menu and move this there
		GUILayout.BeginArea (new Rect(0, Screen.height / 3, lblStyle.fontSize * 10, Screen.height));
		if (Network.peerType == NetworkPeerType.Disconnected) {
			serverAddr = GUILayout.TextField (serverAddr);
			serverPort = int.Parse (GUILayout.TextField (serverPort.ToString ()));
			if (GUILayout.Button ("Connect as Client")) {
				Network.Connect (serverAddr, serverPort);
			}
			if (GUILayout.Button ("Start Server")) {
				Network.InitializeSecurity ();
				Network.InitializeServer (g.users.Length - 1, serverPort, !Network.HavePublicAddress ());
			}
		}
		else {
			if (GUILayout.Button ("Disconnect")) {
				Network.Disconnect (200);
			}
		}
		GUILayout.EndArea ();
	}
	
	void OnPlayerConnected(NetworkPlayer player) {
		if (Network.connections.Length == g.users.Length - 1) {
			int seed = UnityEngine.Random.Range (int.MinValue, int.MaxValue);
			scnOpenMultiplayer (0, seed);
			for (int i = 0; i < Network.connections.Length; i++) {
				networkView.RPC ("scnOpenMultiplayer", Network.connections[i], i + 1, seed);
			}
		}
	}
	
	[RPC]
	void scnOpenMultiplayer(int user, int seed) {
		UnityEngine.Random.seed = seed;
		scnOpen (appPath + modPath + "scn.json", user, true);
	}
	
	// TODO: add NetworkMessageInfo as last parameter to authenticate user, according to http://forum.unity3d.com/threads/141156-Determine-sender-of-RPC
	[RPC]
	void addCmd(int user, int cmdType, byte[] cmdData) {
		System.IO.MemoryStream stream = new System.IO.MemoryStream(cmdData);
		if (cmdType == (int)CmdEvtTag.move) {
			g.users[user].cmdReceived.add (Serializer.Deserialize<MoveCmdEvt>(stream));
		}
		else if (cmdType == (int)CmdEvtTag.makeUnit) {
			g.users[user].cmdReceived.add (Serializer.Deserialize<MakeUnitCmdEvt>(stream));
		}
		else if (cmdType == (int)CmdEvtTag.makePath) {
			g.users[user].cmdReceived.add (Serializer.Deserialize<MakePathCmdEvt>(stream));
		}
		else if (cmdType == (int)CmdEvtTag.deletePath) {
			g.users[user].cmdReceived.add (Serializer.Deserialize<DeletePathCmdEvt>(stream));
		}
		else if (cmdType == (int)CmdEvtTag.goLive) {
			g.users[user].cmdReceived.add (Serializer.Deserialize<GoLiveCmdEvt>(stream));
		}
		else if (cmdType == (int)CmdEvtTag.stack) {
			g.users[user].cmdReceived.add (Serializer.Deserialize<StackCmdEvt>(stream));
		}
		else if (cmdType == (int)CmdEvtTag.deleteOtherPaths) {
			g.users[user].cmdReceived.add (Serializer.Deserialize<DeleteOtherPathsCmdEvt>(stream));
		}
		else {
			throw new InvalidOperationException("received command of invalid type");
		}
	}
	
	[RPC]
	void allCmdsSent(int user, int checksum) {
		g.users[user].timeSync += g.updateInterval;
		g.users[user].checksums[g.users[user].timeSync] = checksum;
	}

	private string jsonString(Hashtable json, string key, string defaultVal = "") {
		if (json.ContainsKey(key) && json[key] is string) return (string)json[key];
		return defaultVal;
	}

	private double jsonDouble(Hashtable json, string key, double defaultVal = 0) {
		if (json.ContainsKey(key) && json[key] is double) return (double)json[key];
		return defaultVal;
	}

	private bool jsonBool(Hashtable json, string key, bool defaultVal = false) {
		if (json.ContainsKey(key) && json[key] is bool) return (bool)json[key];
		return defaultVal;
	}

	private long jsonFP(Hashtable json, string key, long defaultVal = 0) {
		if (json.ContainsKey(key)) {
			if (json[key] is double) return FP.fromDouble((double)json[key]);
			if (json[key] is string) {
				// parse as hex string, so no rounding errors when converting from double
				// allow beginning string with '-' to specify negative number, as alternative to prepending with f's
				long ret;
				if (long.TryParse(((string)json[key]).TrimStart('-'), System.Globalization.NumberStyles.HexNumber, null, out ret)) {
					return ((string)json[key])[0] == '-' ? -ret : ret;
				}
				return defaultVal;
			}
		}
		return defaultVal;
	}

	private Hashtable jsonObject(Hashtable json, string key) {
		if (json.ContainsKey(key) && json[key] is Hashtable) return (Hashtable)json[key];
		return null;
	}

	private ArrayList jsonArray(Hashtable json, string key) {
		if (json.ContainsKey(key) && json[key] is ArrayList) return (ArrayList)json[key];
		return null;
	}

	private FP.Vector jsonFPVector(Hashtable json, string key, FP.Vector defaultVal = new FP.Vector()) {
		if (json.ContainsKey(key) && json[key] is Hashtable) {
			return new FP.Vector(jsonFP((Hashtable)json[key], "x", defaultVal.x),
				jsonFP((Hashtable)json[key], "y", defaultVal.y),
				jsonFP((Hashtable)json[key], "z", defaultVal.z));
		}
		return defaultVal;
	}

	private Vector2 jsonVector2(Hashtable json, string key, Vector2 defaultVal = new Vector2()) {
		if (json.ContainsKey(key) && json[key] is Hashtable) {
			return new Vector2((float)jsonDouble((Hashtable)json[key], "x", defaultVal.x),
				(float)jsonDouble((Hashtable)json[key], "y", defaultVal.y));
		}
		return defaultVal;
	}

	private Color jsonColor(Hashtable json, string key) {
		if (json.ContainsKey(key) && json[key] is Hashtable) {
			return new Color((float)jsonDouble((Hashtable)json[key], "r", 0),
				(float)jsonDouble((Hashtable)json[key], "g", 0),
				(float)jsonDouble((Hashtable)json[key], "b", 0),
				(float)jsonDouble((Hashtable)json[key], "a", 1));
		}
		return new Color();
	}
	
	/// <summary>
	/// returns dictionary of selected units (keys) and how many of their paths are selected (values)
	/// </summary>
	private Dictionary<int, int> selUnits() {
		Dictionary<int, int> ret = new Dictionary<int, int>();
		foreach (List<int> units in selPaths.Values) {
			foreach (int unit in units) {
				// TODO: check for unit existence
				if (!ret.ContainsKey (unit)) ret.Add (unit, 0);
				ret[unit]++;
			}
		}
		return ret;
	}
	
	/// <summary>
	/// returns copy of selected paths that can be passed to CmdEvt constructors
	/// </summary>
	private Dictionary<int, int[]> selPathsCopy() {
		Dictionary<int, int[]> ret = new Dictionary<int, int[]>();
		foreach (KeyValuePair<int, List<int>> path in selPaths) {
			ret[path.Key] = path.Value.ToArray ();
		}
		return ret;
	}

	/// <summary>
	/// returns where to make new unit, or (Sim.OffMap, 0) if mouse is at invalid position
	/// </summary>
	private FP.Vector makeUnitPos() {
		if (FP.rectContains (new FP.Vector(), new FP.Vector(g.mapSize, g.mapSize), drawToSimPos(Input.mousePosition))) {
			if (g.unitT[makeUnitType].makeOnUnitT >= 0) {
				// selected unit type must be made on top of another unit of correct type
				// TODO: prevent putting multiple units on same unit (unless on different paths of same unit and maybe some other cases)
				foreach (Path path in g.paths) {
					if (timeGame >= path.segments[0].timeStart) {
						FP.Vector pos = path.calcPos(timeGame);
						if (g.tileAt (pos).playerVisWhen (selPlayer, timeGame)
							&& FP.rectContains (path.selMinPos (timeGame), path.selMaxPos (timeGame), drawToSimPos (Input.mousePosition))) {
							foreach (int unit in path.segments[path.getSegment (timeGame)].units) {
								if (g.units[unit].type == g.unitT[makeUnitType].makeOnUnitT) {
									return pos;
								}
							}
						}
					}
				}
			}
			else {
				return drawToSimPos(Input.mousePosition);
			}
		}
		return new FP.Vector(Sim.OffMap, 0);
	}

	/// <summary>
	/// returns where new unit of specified type can move out of the way after specified path makes it
	/// </summary>
	/// <remarks>chooses a random location between makeUnitMinDist and makeUnitMaxDist away from path</remarks>
	private FP.Vector makeUnitMovePos(long time, int path, int type) {
		FP.Vector ret;
		do {
			ret = new FP.Vector((long)((UnityEngine.Random.value - 0.5) * g.unitT[type].makeUnitMaxDist * 2),
				(long)((UnityEngine.Random.value - 0.5) * g.unitT[type].makeUnitMaxDist * 2));
		} while (ret.lengthSq() < g.unitT[type].makeUnitMinDist * g.unitT[type].makeUnitMinDist
			|| ret.lengthSq() > g.unitT[type].makeUnitMaxDist * g.unitT[type].makeUnitMaxDist);
		return ret + g.paths[path].calcPos(time);
	}

	/// <summary>
	/// returns where new path with specified units can move out of the way after specified path makes it
	/// </summary>
	/// <remarks>chooses a random location between makePathMinDist() and makePathMaxDist() away from path</remarks>
	private FP.Vector makePathMovePos(long time, int path, List<int> units) {
		long makePathMinDist = g.paths[path].makePathMinDist (time, units);
		long makePathMaxDist = g.paths[path].makePathMaxDist (time, units);
		FP.Vector ret;
		do {
			ret = new FP.Vector((long)((UnityEngine.Random.value - 0.5) * makePathMaxDist * 2),
				(long)((UnityEngine.Random.value - 0.5) * makePathMaxDist * 2));
		} while (ret.lengthSq() < makePathMinDist * makePathMinDist
			|| ret.lengthSq() > makePathMaxDist * makePathMaxDist);
		return ret + g.paths[path].calcPos(time);
	}
	
	/// <summary>
	/// creates new paths that selected units could take
	/// </summary>
	private void makePaths() {
		if (selPaths.Count > 0) {
			Dictionary<int, FP.Vector> pos = new Dictionary<int, FP.Vector>();
			foreach (KeyValuePair<int, List<int>> path in selPaths) {
				if (timeGame + 1 >= g.paths[path.Key].segments[0].timeStart) pos[path.Key] = makePathMovePos(timeGame + 1, path.Key, path.Value);
			}
			// happens at newCmdTime() + 1 so new path starts out live if game is live
			g.cmdPending.add(new MakePathCmdEvt(g.timeSim, newCmdTime() + 1, selPathsCopy(), pos));
		}
	}
	
	/// <summary>
	/// deletes selected paths
	/// </summary>
	private void deletePaths() {
		// happens at newCmdTime() instead of newCmdTime() + 1 so that when paused, making path then deleting parent path doesn't cause an error
		if (selPaths.Count > 0) g.cmdPending.add(new DeletePathCmdEvt(g.timeSim, newCmdTime(), selPathsCopy()));
	}
	
	/// <summary>
	/// deletes unselected paths of selected units
	/// </summary>
	private void deleteOtherPaths() {
		if (selPaths.Count > 0) g.cmdPending.add (new DeleteOtherPathsCmdEvt(g.timeSim, newCmdTime (), selPathsCopy ()));
	}
	
	/// <summary>
	/// makes a new unit using selected units
	/// </summary>
	private void makeUnit(int type) {
		// TODO: this should only iterate through existing paths (fix when selPaths considers selection time)
		foreach (KeyValuePair<int, List<int>> path in selPaths) {
			if (g.unitT[type].speed > 0 && g.unitT[type].makeOnUnitT < 0 && g.paths[path.Key].canMakeUnitType (timeGame + 1, type)) {
				// make unit now
				Dictionary<int, int[]> pathArray = new Dictionary<int, int[]>();
				pathArray.Add (path.Key, path.Value.ToArray ());
				// happens at newCmdTime() + 1 so new unit starts out live if game is live
				g.cmdPending.add(new MakeUnitCmdEvt(g.timeSim, newCmdTime() + 1, pathArray, type, makeUnitMovePos (timeGame + 1, path.Key, type)));
				break;
			}
			else if (g.unitsCanMake (path.Value, type)) {
				// don't make unit yet; let user pick where to place it
				makeUnitType = type;
				break;
			}
		}
	}

	/// <summary>
	/// sets pos to where base of path should be drawn at, and returns whether it should be drawn
	/// </summary>
	private bool pathDrawPos(int path, ref Vector3 pos) {
		FP.Vector simPos;
		if (timeGame < g.paths[path].moves[0].timeStart || (selPlayer != g.paths[path].player && g.paths[path].timeSimPast != long.MaxValue)) return false;
		simPos = g.paths[path].calcPos(timeGame);
		if (selPlayer != g.paths[path].player && !g.tileAt(simPos).playerVisWhen(selPlayer, timeGame)) return false;
		pos = simToDrawPos(simPos, UnitDepth);
		return true;
	}
	
	/// <summary>
	/// returns localScale of unit sprite with specified properties
	/// </summary>
	private Vector3 unitScale(int type, int player) {
		return new Vector3(simToDrawScl (g.unitT[type].imgHalfHeight) * texUnits[type, player].width / texUnits[type, player].height,
			simToDrawScl (g.unitT[type].imgHalfHeight), 1);
	}
	
	/// <summary>
	/// returns suggested timeCmd field for a new CmdEvt, corresponding to when it would appear to be applied
	/// </summary>
	private long newCmdTime() {
		if (g.networkView != null) { // multiplayer
			return Math.Min (timeGame, g.timeUpdateEvt) + g.updateInterval * 2;
		}
		else { // single player
			return timeGame;
		}
	}
	
	private float simToDrawScl(long coor) {
		return (float)(FP.toDouble(coor) * g.zoom * winDiag);
	}

	private long drawToSimScl(float coor) {
		return FP.fromDouble(coor / winDiag / g.zoom);
	}

	private Vector3 simToDrawScl(FP.Vector vec) {
		return new Vector3(simToDrawScl(vec.x), simToDrawScl(vec.y), simToDrawScl(vec.z));
	}

	private FP.Vector drawToSimScl(Vector3 vec) {
		return new FP.Vector(drawToSimScl(vec.x), drawToSimScl(vec.y), drawToSimScl(vec.z));
	}

	private Vector3 simToDrawPos(FP.Vector vec, float depth = 0f) {
		return new Vector3(simToDrawScl(vec.x - g.camPos.x) + Screen.width / 2, simToDrawScl(vec.y - g.camPos.y) + Screen.height / 2, depth);
	}

	private FP.Vector drawToSimPos(Vector3 vec) {
		return new FP.Vector(drawToSimScl(vec.x - Screen.width / 2), drawToSimScl(vec.y - Screen.height / 2)) + g.camPos;
	}
}