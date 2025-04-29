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
Defines the spacing between itmes on the grid

#### `EquivalentStructure` (required)
Controls the replacement behavior for the construction cursor.
When in construct placement mode (right clicking with a kit in hand) and pointing at a board, this function will be called with the prefab selected in the kit, and can return a different prefab to be built on the board.

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
A host is a regular structure that can contain one or more board surfaces to build on. It must implement the [`IPlacementBoardHost`](/IPlacementBoardHost.cs) ([Example](https://github.com/tsholmes/StationeersExampleBoard/blob/master/Assets/Scripts/ExampleBoardStructure.cs)).
These are handled as an interface so that you can extend any structure class to add a board.
This can be important when other functionality requires the structure to be a specific class (e.g. `Device` for structures on a data network).
Since there is not a base class that can handle everything needed for hosting a board, that functionality is all in hook functions that must be called in the proper places. These are listed below along with the required interface methods.

#### TODO: unity object setup (origin vector + box colliders)

#### TODO: interface methods

#### TODO: hooks

### Board Structure
TODO: IPlacementBoardStructure

#### TODO: unity object setup

#### TODO: interface methods

#### TODO: hooks