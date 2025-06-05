using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;

namespace LibConstruct
{
  public abstract partial class PlacementBoard
  {
    public static bool Loading;
    private static Dictionary<long, IBoardRef> LoadingRefs = new();
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

    public static void RegisterLoading(IPlacementBoardStructure structure, long boardID, long primaryHostId)
    {
      if (Loading)
      {
        // board elements are ordered after the primary host so the ref should always be here
        var iref = LoadingRefs[boardID];
        iref.Board.AwaitingRegister.Add(structure);
      }
      else
      {
        // if we aren't loading, this is a newly created object on a client and we should just register it directly
        var board = FindExisting(boardID, primaryHostId);
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
        foreach (var structure in iref.Board.AwaitingRegister)
          iref.Board.Register(structure);
        iref.Board.AwaitingRegister.Clear();
      }
      LoadingRefs.Clear();
      Loading = false;
    }
  }
}