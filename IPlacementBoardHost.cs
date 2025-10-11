
using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Networking;
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

    // These are hooks for the host to handle structures being added/remove from a board it is hosting
    // OnBoardStructureRegistered is called *after* the structure is added and its cells registered
    // OnBoardStructureDeregistered is called *before* the structure is removed and its cells cleared
    public void OnBoardStructureRegistered(PlacementBoard board, IPlacementBoardStructure structure);
    public void OnBoardStructureDeregistered(PlacementBoard board, IPlacementBoardStructure structure);
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
      boardRef = PlacementBoard.LoadRef<T>(saveData.BoardId, saveData.PrimaryHostId);
      if (host.ReferenceId != saveData.PrimaryHostId)
        return;
      boardRef.Board = new() { ID = saveData.BoardId };
      boardRef.Board.AddHost(host, origin);
      boardRef.Board.DeserializeSave(saveData.BoardSaveData);
    }

    // call this in OnFinishedLoad for each hosted board
    public static void OnFinishedLoadBoard<T>(IPlacementBoardHost host, ref BoardRef<T> boardRef, Transform origin) where T : PlacementBoard, new()
    {
      if (boardRef == null)
      {
        boardRef = new BoardRef<T> { Board = new() };
        boardRef.Board.AddHost(host, origin);
        return;
      }
      if (boardRef.Board.PrimaryHost != host)
        boardRef.Board.AddHost(host, origin);
    }

    // call this in OnRegistered for each hosted board
    public static void OnRegisteredBoard<T>(IPlacementBoardHost host, ref BoardRef<T> boardRef, Transform origin) where T : PlacementBoard, new()
    {
      if (GameManager.GameState == GameState.Loading || NetworkManager.IsClient)
        return;
      boardRef = new BoardRef<T> { Board = new() };
      boardRef.Board.AddHost(host, origin);
    }

    // call this in OnDestroyed for each hosted board
    public static void OnDestroyedBoard<T>(IPlacementBoardHost host, BoardRef<T> boardRef) where T : PlacementBoard, new()
    {
      if (boardRef == null)
        return;
      boardRef.Board?.RemoveHost(host);
      boardRef.Board = null;
    }

    // call this in SerializeOnJoin for each hosted board
    public static void SerializeBoardOnJoin<T>(RocketBinaryWriter writer, IPlacementBoardHost host, T board) where T : PlacementBoard, new()
    {
      writer.WriteInt64(board.ID);
      writer.WriteInt64(board.PrimaryHost.ReferenceId);
      if (board.PrimaryHost == host)
        board.SerializeOnJoin(writer);
    }

    // call this in DeserializeOnJoin for each hosted board. make sure its called in the same order as SerializeOnJoin
    public static void DeserializeBoardOnJoin<T>(RocketBinaryReader reader, IPlacementBoardHost host, out BoardRef<T> boardRef, Transform origin) where T : PlacementBoard, new()
    {
      var id = reader.ReadInt64();
      var hostId = reader.ReadInt64();

      // if we aren't loading, the primary host needs to just create the board and not lookup a ref
      if (PlacementBoard.Loading || host.ReferenceId != hostId)
        boardRef = PlacementBoard.LoadRef<T>(id, hostId);
      else
        boardRef = new();
      if (host.ReferenceId != hostId)
        return;
      boardRef.Board = new() { ID = id };
      boardRef.Board.AddHost(host, origin);
      boardRef.Board.DeserializeOnJoin(reader);
    }

    // BuildBoardUpdate and ProcessBoardUpdate exist only as helpers to ensure state is only sent from the primary host
    // The caller is responsible for determing when to call these (based on their NetworkUpdateFlags or other state)
    // The base PlacementBoard has no state that needs network updates, so these are only needed for custom board state
    // If the board does not span multiple hosts, it will likely be easier to just hold the state in the host
    public static void BuildBoardUpdate<T>(RocketBinaryWriter writer, IPlacementBoardHost host, T board) where T : PlacementBoard, new()
    {
      if (host == board.PrimaryHost)
        board.BuildUpdate(writer);
    }
    public static void ProcessBoardUpdate<T>(RocketBinaryReader reader, IPlacementBoardHost host, T board) where T : PlacementBoard, new()
    {
      if (host == board.PrimaryHost)
        board.ProcessUpdate(reader);
    }
  }
}