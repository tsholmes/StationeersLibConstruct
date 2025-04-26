
using Assets.Scripts.GridSystem;
using Assets.Scripts.Localization2;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Pipes;
using UnityEngine;

namespace LibConstruct
{
  public interface IPlacementBoardStructure
  {
    public PlacementBoard Board { get; set; }
    public PlacementBoard.BoardCell[] BoardCells { get; set; }
    public string name { get; } // from UnityEngine.Object
    public void SetStructureData(Quaternion localRotation, ulong ownerClientId, Grid3 localGrid, int customColourIndex); // from Structure
  }

  // Any class implementing IPlacementBoardStructure needs to call all of these in the instance methods of the same name.
  // These are separated out so we don't require a specific base class and can extend any builtin thing type.
  public static class BoardStructureHooks
  {
    public static void Awake(IPlacementBoardStructure boardStruct)
    {
      if (boardStruct is Structure structure)
      {
        structure.PlacementType = (PlacementSnap)(-1); // skip normal placement behavior
      }
    }

    // used like:
    // var constructInfo = BoardStructureHooks.CanConstruct(this);
    // if (!constructInfo.CanConstruct)
    //   return constructInfo;
    // <custom construction checks>
    public static CanConstructInfo CanConstruct(IPlacementBoardStructure boardStruct)
    {
      if (boardStruct is not Structure structure) return CanConstructInfo.ValidPlacement;
      if (boardStruct.Board == null) return CanConstructInfo.InvalidPlacement("must be placed on board"); // TODO: localize
      foreach (var cell in boardStruct.Board.BoundsCells(structure.Transform, structure.Bounds))
      {
        if (cell == null)
          return CanConstructInfo.InvalidPlacement("overflows board bounds"); // TODO: localize
        if (cell.Structure is Structure other)
          return CanConstructInfo.InvalidPlacement(GameStrings.PlacementBlockedByStructure.AsString(other.DisplayName));
      }
      return CanConstructInfo.ValidPlacement;
    }
  }
}