# LibConstruct

A library for custom structure placement in Stationeers.

- [Placement Board](#placement-board)
  - [Board Surface](#board)
  - [Board Host](#host)
  - [Board Structure](#board-structure)
  - [Relocatable Structure](#relocatable-board-structure)
- [PsuedoNetwork](#pseudonetwork)

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
public override ThingSaveData SerializeSave()
{
  var saveData = new MyBoardHostSaveData();
  var baseData = saveData as ThingSaveData;
  this.InitialiseSavedata(ref baseData);
  return saveData;
}

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
  BoardHostHooks.OnDestroyedBoard(this, this.BoardRef);
  // IMPORTANT: OnDestroyedBoard must be called before base.OnDestroy
  base.OnDestroy();
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
A board structure is a prefab extending `Structure` that can (only) be built on a `PlacementBoard`. It must implement the [`IPlacementBoardStructure`](/IPlacementBoardStructure.cs) interface ([Example](https://github.com/tsholmes/StationeersExampleBoard/blob/master/Assets/Scripts/SmallLetter.cs)). These are again handled as an interface so you can extend any existing game class to use as a board structure.

The board structures are parented to the BoardOrigin transform with the z (blue) vector pointing in the same direction as the origin z vector, and can be rotated in 90 degree increments around the z vector. The local position relative to the BoardOrigin will always have the z component set to 0, and the xy components set to multiples of `PlacementBoard.GridSize`.

In the `SaveData` for your board structure, add a `PlacementBoardStructureSaveData` field
```cs
public class MyBoardStructureSaveData : StructureSaveData
{
  [XmlElement]
  public PlacementBoardStructureSaveData Board;
}
```

#### Interface methods

`IPlacementBoardStructure.Board` holds a reference to the parent board
```cs
public PlacementBoard Board { get; set; }
```
`IPlacementBoardStructure.BoardCells` holds an array of board cells this device covers
```cs
public PlacementBoard.BoardCell[] BoardCells { get; set; }
```
The rest of the interface methods should be implemented by the base class you are extending, and shouldn't need an implementation
```cs
public Transform Transform { get; } // from Thing
public string name { get; } // from UnityEngine.Object
public void SetStructureData(...); // from Structure
```

#### Hooks
In order for all the functionality to work properly, you must override various `Thing` methods and call the equivalent `BoardStructureHooks` function.

```cs
public override void Awake()
{
  base.Awake();
  BoardStructureHooks.Awake(this);
}

public override CanConstructInfo CanConstruct()
{
  // no need to call the base method here since we don't use any builtin structure CanConstruct checks
  return BoardStructureHooks.CanConstruct(this);
}

public override void OnDeregistered()
{
  base.OnDeregistered();
  BoardStructureHooks.OnDeregistered(this);
}

public override ThingSaveData SerializeSave()
{
  var saveData = new MyBoardStructureSaveData();
  var baseData = saveData as ThingSaveData;
  this.InitialiseSaveData(ref baseData);
  return saveData;
}

protected override void InitialiseSaveData(ref ThingSaveData baseData)
{
  base.InitialiseSaveData(baseData);
  if (baseData is not MyBoardStructureSaveData saveData) return;
  saveData.Board = BoardStructureHooks.SerializeSave(this);
}

public override void DeserializeSave(ThingSaveData baseData)
{
  base.DeserializeSave(baseData);
  if (baseData is not MyBoardStructureSaveData saveData) return;
  BoardStructureHooks.DeserializeSave(this, saveData.Board);
}

public override void SerializeOnJoin(RocketBinaryWriter writer)
{
  base.SerializeOnJoin(writer);
  BoardStructureHooks.SerializeOnJoin(writer, this);
}

public override void DeserializeOnJoin(RocketBinaryReader reader)
{
  base.DeserializeOnJoin(reader);
  BoardStructureHooks.DeserializeOnJoin(reader, this);
}
```

### Relocatable Board Structure
If you want board structures to be relocatable on the same board, your board structures must implement the `IPlacementBoardRelocatable` interface (which extends `IPlacementBoardStructure` so you don't need to list both).

In addition to all the required implementation from `IPlacementBoardStructure`, you must also implement the interface methods and add a hook for starting the relocation.

#### Interface methods

`IPlacementBoardRelocatable.OnStructureRelocated` is called after a board structure is relocated to a new set of cells on the same board.
```cs
public void OnStructureRelocated() { }
```
The rest of the interface methods should be implemented automatically by the base class.
```cs
public Thing GetAsThing { get; } // from Thing
public Structure AsStructure { get; } // from Structure
```

#### Hooks

```cs
public override DelayedActionInstance AttackWith(Attack attack, bool doAction = true)
{
  if (attack.SourceItem is Screwdriver tool) // you can use any tool here as the relocate tool
    return BoardRelocateHooks.StructureAttackWith(
      this, attack, doAction, BoardRelocateHooks.NormalToolRelocateContinue(tool));
  return base.AttackWith(attack, doAction);
}
```

## PseudoNetwork
A PseudoNetwork is a lightweight structure network equivalent that has holds no state except the list of members. It can be used to have a set of connected structures without the overhead of a full structure network. Each network type reserves a new connection type that will be used to connect members. Each type a network member structure is created or destroyed, it walks these connections to rebuild the member lists.

In order to create a PseudoNetwork, first you must register a `PseudoNetworkType` with the type of the network members (which must implement `IPseudoNetworkMember`). This will select a `NetworkType` value to be used as the connection type.
```cs
public static PseudoNetworkType<MyNetworkMember> MyNetworkType = new();
```

Wherever you load in your prefabs that are network members, call `PseudoNetworkType.PatchConnections()` to set the proper connection type.
```cs
foreach (var gameObject in prefabs)
{
  if (gameObject.TryGetComponent<MyNetworkMember>(out var member))
    MyNetworkMember.MyNetworkType.PatchConnections(member);
  
  // register prefab
}
```

### Interface

`IPseudoNetworkMember.Network` returns the network instance that this structure is a member of. This will be updated by the various hooks to contain the current list of members.
```cs
public PseudoNetwork<MyNetworkMember> Network { get; } = MyNetworkType.Join();
```

`IPseudoNetworkMember.Connections` returns an enumerable of connections that should be used to connect network members. It is recommended to set the connections to an otherwise unused type to easily find them.
```cs
public IEnumerable<Connection> Connections {
  get {
    foreach (var openEnd in this.OpenEnds)
      // set connections to LandingPad type in unity to mark them for replacement with new connection type
      if (openEnd.ConnectionType == NetworkType.LandingPad || openEnd.ConnectionType == MyNetworkType.ConnectionType)
        yield return openEnd;
  }
}
```

`IPseudoNetworkMember.OnMemberAdded` is called when a new member is added to the network.
`IPseudoNetworkMember.OnMemberRemoved` is called when a member is removed from the network.
`IPseudoNetworkMember.OnMembersChanged` is called once after all `OnMemberAdded`/`OnMemberRemoved` hooks are called.

If your structure is a member of multiple different types of networks, use explicit interface implementations for each type.

```cs
public interface INetworkMemberA : IPseudoNetworkMember<INetworkMemberA> { }
public interface INetworkMemberB : IPseudoNetworkMember<INetworkMemberB> { }

public class MyNetworkMember : Structure, INetworkMemberA, INetworkMemberB
{
  public static PseudoNetworkType<INetworkMemberA> NetworkTypeA = new();
  public static PseudoNetworkType<INetworkMemberB> NetworkTypeB = new();

  IEnumerable<Connection> INetworkMemberA.Network => NetworkTypeA.Join();
  IEnumerable<Connection> INetworkMemberA.Connections => ...;

  IEnumerable<Connection> INetworkMemberB.Network => NetworkTypeB.Join();
  IEnumerable<Connection> INetworkMemberB.Connections => ...;
}
```

### Hooks

```cs
public override void OnRegistered(Cell cell)
{
  base.OnRegistered(cell);

  // each hook must be called once for each network type this is a member of
  MyNetworkType.RebuildNetworkCreate(this);
}

public override void OnDeregistered()
{
  base.OnDeregistered();

  MyNetworkType.RebuildNetworkDestroy(this);
}
```