using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;

namespace LibConstruct
{
  public abstract partial class PlacementBoard
  {
    public static bool Loading;
    private static Dictionary<long, IBoardRef> LoadingRefs = new();

    private List<StructurePair> AwaitingRegister = new();

    public static BoardRef<T> LoadRef<T>(long id, long primaryHostId) where T : PlacementBoard, new()
    {
      if (!Loading)
      {
        // if we aren't loading, this is being called for a newly created object on a client.
        // in that case the primary host should already have the board created
        return new BoardRef<T> { Board = (T)FindExisting(id, primaryHostId) };
      }
      if (id >= NextID)
        NextID = id + 1;
      if (!LoadingRefs.TryGetValue(id, out var iref))
      {
        iref = new BoardRef<T>();
        LoadingRefs[id] = iref;
      }
      return (BoardRef<T>)iref;
    }

    public static PlacementBoard FindExisting(long id, long primaryHostId)
    {
      var host = Thing.Find<IPlacementBoardHost>(primaryHostId);
      foreach (var board in host.GetPlacementBoards())
        if (board?.ID == id)
          return board;
      throw new Exception($"could not find PlacementBoard {id} on host {primaryHostId}");
    }

    public static void RegisterLoading(IPlacementBoardStructure structure, PlacementBoardStructureSaveData saveData)
    {
      if (Loading)
      {
        // board elements are ordered after the primary host so the ref should always be here
        var iref = LoadingRefs[saveData.BoardId];
        iref.Board.AwaitingRegister.Add(new(structure, saveData));
      }
      else
      {
        // if we aren't loading, this is a newly created object on a client and we should just register it directly
        var board = FindExisting(saveData.BoardId, saveData.PrimaryHostId);
        structure.Transform.SetPositionAndRotation(
          board.GridToWorld(saveData.Position),
          board.IndexToRotation(saveData.Rotation)
        );
        board.Register(structure);
      }
    }

    public static void StartLoad()
    {
      LoadingRefs.Clear();
      Loading = true;
    }

    public static void FinishLoad()
    {
      foreach (var iref in LoadingRefs.Values)
      {
        foreach (var (structure, saveData) in iref.Board.AwaitingRegister)
        {
          var board = iref.Board;
          // update the position and rotation of the structure before registering
          structure.Transform.SetPositionAndRotation(
            board.GridToWorld(saveData.Position),
            board.IndexToRotation(saveData.Rotation)
          );

          board.Register(structure);
        }
        iref.Board.AwaitingRegister.Clear();
      }
      LoadingRefs.Clear();
      Loading = false;
    }

    private class StructurePair
    {
      public IPlacementBoardStructure Structure;
      public PlacementBoardStructureSaveData SaveData;

      public StructurePair(IPlacementBoardStructure structure, PlacementBoardStructureSaveData saveData)
      {
        this.Structure = structure;
        this.SaveData = saveData;
      }

      public void Deconstruct(out IPlacementBoardStructure structure, out PlacementBoardStructureSaveData saveData)
      {
        (structure, saveData) = (this.Structure, this.SaveData);
      }
    }
  }
}