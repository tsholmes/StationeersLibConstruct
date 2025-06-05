
using Assets.Scripts.GridSystem;
using Assets.Scripts.Localization2;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using UnityEngine;

namespace LibConstruct
{
  public interface IPlacementBoardStructure
  {
    public PlacementBoard Board { get; set; }
    public PlacementBoard.BoardCell[] BoardCells { get; set; }
    public Transform Transform { get; }
    public string name { get; } // from UnityEngine.Object
    public void SetStructureData(Quaternion localRotation, ulong ownerClientId, Grid3 localGrid, int customColourIndex); // from Structure
  }

  // Any class implementing IPlacementBoardStructure needs to call all of these in the instance methods of the same name.
  // These are separated out so we don't require a specific base class and can extend any builtin thing type.
  public static class BoardStructureHooks
  {
    public static void Awake<T>(T structure) where T : Structure, IPlacementBoardStructure
    {
      structure.PlacementType = (PlacementSnap)(-1); // skip normal placement behavior
    }

    // used like:
    // var constructInfo = BoardStructureHooks.CanConstruct(this);
    // if (!constructInfo.CanConstruct)
    //   return constructInfo;
    // <custom construction checks>
    public static CanConstructInfo CanConstruct<T>(T structure) where T : Structure, IPlacementBoardStructure
    {
      if (structure.Board == null) return CanConstructInfo.InvalidPlacement(CustomGameStrings.BoardStructureNoBoard);
      foreach (var cell in structure.Board.BoundsCells(structure.Transform, structure.Bounds))
      {
        if (cell == null)
          return CanConstructInfo.InvalidPlacement(CustomGameStrings.BoardStructureBoundsOverflow);
        // if we are currently relocating, allow overlap with the relocating structure
        if (PlacementBoard.IsRelocating && cell.Structure == PlacementBoard.RelocatingStructure)
          continue;
        if (cell.Structure is Structure other)
          return CanConstructInfo.InvalidPlacement(GameStrings.PlacementBlockedByStructure.AsString(other.DisplayName));
      }
      return CanConstructInfo.ValidPlacement;
    }

    public static void OnDeregistered<T>(T structure) where T : Structure, IPlacementBoardStructure
    {
      structure.Board.Deregister(structure);
    }

    public static PlacementBoardStructureSaveData SerializeSave(IPlacementBoardStructure structure)
    {
      return new PlacementBoardStructureSaveData
      {
        BoardId = structure.Board.ID,
        PrimaryHostId = structure.Board.PrimaryHost.ReferenceId,
        Position = structure.Board.WorldToGrid(structure.Transform.position),
        Rotation = structure.Board.RotationToIndex(structure.Transform.rotation),
      };
    }

    public static void DeserializeSave(IPlacementBoardStructure structure, PlacementBoardStructureSaveData saveData)
    {
      PlacementBoard.RegisterLoading(structure, saveData);
    }

    public static void SerializeOnJoin(RocketBinaryWriter writer, IPlacementBoardStructure structure)
    {
      var data = SerializeSave(structure);
      writer.WriteInt64(data.BoardId);
      writer.WriteInt64(data.PrimaryHostId);
      writer.WriteGrid3(data.Position);
      writer.WriteSByte((sbyte)data.Rotation);
    }

    public static void DeserializeOnJoin(RocketBinaryReader reader, IPlacementBoardStructure structure)
    {
      var data = new PlacementBoardStructureSaveData();
      data.BoardId = reader.ReadInt64();
      data.PrimaryHostId = reader.ReadInt64();
      data.Position = reader.ReadGrid3();
      data.Rotation = reader.ReadSByte();
      PlacementBoard.RegisterLoading(structure, data);
    }
  }
}