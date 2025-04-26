
using Assets.Scripts.GridSystem;
using UnityEngine;

namespace LibConstruct
{
  public class CreateBoardStructureInstance
  {
    public PlacementBoard Board;
    public PlacementBoardStructure Prefab;
    public Grid3 Position;
    public Quaternion Rotation;
    public int CustomColor = -1;
    public ulong OwnerClientId;

    public CreateBoardStructureInstance(
      PlacementBoardStructure prefabToCreate,
      PlacementBoard board,
      Grid3 position,
      Quaternion rotation,
      ulong ownerClientId,
      int colorIndex = -1
    )
    {
      this.Board = board;
      this.Prefab = prefabToCreate;
      this.Position = position;
      this.Rotation = rotation;
      this.OwnerClientId = ownerClientId;
      this.CustomColor = colorIndex;
    }

    public Vector3 WorldPosition => this.Board.GridToWorld(this.Position);
  }
}