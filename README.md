# LibConstruct

A library for custom structure placement in Stationeers.

## Placement Board
Placement Boards are surfaces that allow building structures on a custom 2D grid.
The board can define structure replacements to have construction automatically switch from a regular small grid structure to the board equivalent.
The surface grid does not need to be aligned with the existing small or large grid.
A placement board is hosted by one or more regular small grid or large grid structures (multi-host structures not yet fully supported).

The [Example Board](https://github.com/tsholmes/StationeersExampleBoard/tree/master/Assets/Scripts) can be referenced to see a working implementation.

### Board
The board surface is handled by a non-Thing class that extends [`PlacementBoard`](/PlacementBoard.cs) ([Example](https://github.com/tsholmes/StationeersExampleBoard/blob/master/Assets/Scripts/ExampleLetterBoard.cs)).
The board defines the grid size and structure replacement behavior, as well as optionally having custom save data, networking, and hooks for structures being added to or removed from the board.

#### `GridSize` (required)
Defines the spacing between items on the grid
```cs
public override float GridSize => SmallGrid.SmallGridSize / 8f;
```

#### `EquivalentStructure` (required)
Controls the replacement behavior for the construction cursor.
When in construct placement mode (right clicking with a kit in hand) and pointing at a board, this function will be called with the prefab selected in the kit, and can return a different prefab to be built on the board.

To use the structures in the kit as-is
```cs
public override IPlacementBoardStructure EquivalentStructure(Structure structure) => structure as IPlacementBoardStructure;
```

To use a different structure when pointing at a board
```cs
public override IPlacementBoardStructure EquivalentStructure(Structure structure) => structure?.PrefabName switch {
  "StructureA" => Prefab.Find("BoardStructureA"),
  "StructureB" => Prefab.Find("BoardStructureB"),
  _ => null,
};
```

#### `OnStructureRegistered` (optional)
Called immediately after a structure is build on the grid.

#### `OnStructureDeregistered` (optional)
Called immediately before a structure is deconstructed from the grid.

#### SaveData (optional)
If you need custom data saved with the board (separately from the host), make a new save data extending `PlacementBoardSaveData`, and override `SerializeSave`, `InitializeSaveData`, and `DeserializeSave` the same way you would for a structure.

#### Networking (optional)
If you need custom data sent on network join or update, override the `SerializeOnJoin`+`DeserializeOnJoin` methods and/or `BuildUpdate`+`ProcessUpdate`.
NOTE: `BuildUpdate`+`ProcessUpdate` are not called automatically. You must determine when an update is needed and call `BoardHostHooks.BuildBoardUpdate`/`BoardHostHooks.ProcessBoardUpdate` in the host.

### Host
A board host is a regular prefab extending `Thing` that can contain one or more board surfaces to build on. It must implement the [`IPlacementBoardHost`](/IPlacementBoardHost.cs) interface ([Example](https://github.com/tsholmes/StationeersExampleBoard/blob/master/Assets/Scripts/ExampleBoardStructure.cs)).
These are handled as an interface so that you can extend any structure class to add a board.
This can be important when other functionality requires the structure to be a specific class (e.g. `Device` for structures on a data network).
Since there is not a base class that can handle everything needed for hosting a board, that functionality is all in hook functions that must be called in the proper places. These are listed below along with the required interface methods.

For each board on a host, create a child object with the Z vector (blue arrow) pointing straight away from the board surface. The transform of this object should be used as the Origin transform passed to the host hooks. Then add a BoxCollider (or extra child objects with BoxColliders) aligned to the board surface so that the center of each valid grid position is contained within the BoxCollider(s).

#### Hosted boards
For each board, add a `BoardRef` member with your `PlacementBoard` implementation, optionally an accessor property for ease of use, a list of colliders that cover the board slots, and an origin transform;

```cs
private BoardRef<MyPlacementBoard> BoardRef;
public MyPlacementBoard Board => this.BoardRef?.Board;
public List<Collider> BoardColliders; // set in unity editor
public Transform BoardOrigin; // set in unity editor;
```

For each board, in the SaveData for your host structure add a `PlacementBoardHostSaveData` field
```cs
public class MyBoardHostSaveData : StructureSaveData
{
  [XmlElement]
  public PlacementBoardHostSaveData Board;
}
```

#### Interface methods
`IPlacementBoardHost.GetPlacementBoards` returns an enumerable of all placement boards hosted by this structure.
```cs
public IEnumerable<PlacementBoard> GetPlacementBoards() { yield return this.Board; }
```
`IPlacementBoardHost.CollidersForBoard` returns an enumerable of the colliders for a specific board this structure is hosting.
```cs
public IEnumerable<Collider> CollidersForBoard(PlacementBoard board) =>
  board == this.Board ? this.BoardColliders : new List<Collider>();
```
`IPlacementBoardHost.OnBoardStructureRegistered` is called after a board structure is added to a hosted board.
```cs
public void OnBoardStructureRegistered(PlacementBoard board, IPlacementBoardStructure structure) { }
```
`IPlacementBoardHost.OnBoardStructureDeregistered` is called before a board structure is removed from a hosted board.
```cs
public void OnBoardStructureDeregistered(PlacementBoard board, IPlacementBoardStructure structure) { }
```

#### Hooks
In order for all the functionality to work properly, you must override various `Thing` methods and call the equivalent `BoardHostHooks` function for each hosted board.

```cs
protected override void InitialiseSaveData(ref ThingSaveData baseData)
{
  base.InitialiseSaveData(ref baseData);
  if (baseData is not MyBoardHostSaveData saveData) return;
  saveData.Board = BoardHostHooks.SerializeBoard(this, this.Board);
}

public override void DeserializeSave(ThingSaveData baseData)
{
  base.DeserializeSave(baseData);
  if (baseData is not MyBoardHostSaveData saveData) return;
  BoardHostHooks.DeserializeBoard(this, saveData.Board, out this.BoardRef, this.BoardOrigin);
}

public override void OnFinishedLoad()
{
  base.OnFinishedLoad();
  BoardHostHooks.OnFinishedLoadBoard(this, ref this.BoardRef, this.BoardOrigin);
}

public override OnRegistered(Cell cell)
{
  base.OnRegistered(cell);
  BoardHostHooks.OnRegisteredBoard(this, ref this.BoardRef, this.BoardOrigin);
}

public override void OnDestroy()
{
  base.OnDestroy();
  BoardHostHooks.OnDestroyedBoard(this, this.BoardRef);
}

public override void SerializeOnJoin(RocketBinaryWriter writer)
{
  base.SerializeOnJoin(writer);
  BoardHostHooks.SerializeBoardOnJoin(writer, this, this.Board);
}

public override void DeserializeOnJoin(RocketBinaryReader reader)
{
  base.DeserializeOnJoin(reader);
  BoardHostHooks.DeserializeBoardOnJoin(reader, this, out this.BoardRef, this.BoardOrigin);
}
```

### Board Structure
TODO: IPlacementBoardStructure

#### TODO: unity object setup

#### TODO: interface methods

#### TODO: hooks