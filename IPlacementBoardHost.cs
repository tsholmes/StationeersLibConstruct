
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using UnityEngine;

namespace LibConstruct
{
  public interface IPlacementBoardHost : IReferencable
  {
    // for each hosted board, add:
    // BoardRef<MyBoardType> MyBoardRef;
    // MyBoardType MyBoard => this.MyBoardRef.Board; // optional

    public IEnumerable<BoxCollider> CollidersForBoard(PlacementBoard board);
    public IEnumerable<PlacementBoard> GetPlacementBoards();
  }

  // Host needs a BoardRef member for each hosted board
  public class BoardRef<T> : IBoardRef where T : PlacementBoard, new()
  {
    public T Board;

    PlacementBoard IBoardRef.Board
    {
      get => this.Board;
      set => this.Board = (T)value;
    }
  }

  interface IBoardRef
  {
    PlacementBoard Board { get; set; }
  }

  public static class BoardHostHooks
  {
    // call this in InitializeSaveData for each hosted board
    public static PlacementBoardHostSaveData SerializeBoard<T>(IPlacementBoardHost host, T board) where T : PlacementBoard
    {
      var saveData = new PlacementBoardHostSaveData
      {
        BoardId = board.ID,
        PrimaryHostId = board.PrimaryHost.ReferenceId,
      };
      if (host.ReferenceId != saveData.PrimaryHostId)
        return saveData;
      saveData.BoardSaveData = board.SerializeSave();
      return saveData;
    }
    public static PlacementBoardHostSaveData SerializeBoard<T>(IPlacementBoardHost host, BoardRef<T> board) where T : PlacementBoard, new() => SerializeBoard(host, board.Board);

    // call this in DeserializeSave for each hosted board
    public static void DeserializeBoard<T>(IPlacementBoardHost host, PlacementBoardHostSaveData saveData, out BoardRef<T> boardRef, Transform origin) where T : PlacementBoard, new()
    {
      if (saveData == null)
      {
        boardRef = null;
        return;
      }
      boardRef = PlacementBoard.LoadRef<T>(saveData.BoardId);
      if (host.ReferenceId != saveData.PrimaryHostId)
        return;
      boardRef.Board = new()
      {
        ID = saveData.BoardId,
        Origin = origin,
      };
      boardRef.Board.DeserializeSave(saveData.BoardSaveData);
      boardRef.Board.AddHost(host);
    }

    // call this in OnFinishedLoad for each hosted board
    public static void OnFinishedLoadBoard<T>(IPlacementBoardHost host, ref BoardRef<T> boardRef, Transform origin) where T : PlacementBoard, new()
    {
      if (boardRef == null)
      {
        boardRef = new BoardRef<T> { Board = new() { Origin = origin } };
        boardRef.Board.AddHost(host);
        return;
      }
      if (boardRef.Board.PrimaryHost != host)
        boardRef.Board.AddHost(host);
    }

    // call this in OnRegistered for each hosted board
    public static void OnRegisteredBoard<T>(IPlacementBoardHost host, ref BoardRef<T> boardRef, Transform origin) where T : PlacementBoard, new()
    {
      if (GameManager.GameState == GameState.Loading)
        return;
      // TODO: check GameManager.RunSimulation here?
      boardRef = new BoardRef<T> { Board = new() { Origin = origin } };
      boardRef.Board.AddHost(host);
    }
  }
}