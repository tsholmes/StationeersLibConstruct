
using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Util;

namespace LibConstruct
{
  public interface IPseudoNetworkMember<T> : IReferencable where T : IPseudoNetworkMember<T>
  {
    // connections that should connect network members
    public IEnumerable<Connection> Connections { get; }
    public PseudoNetwork<T> Network { get; }

    // When network members are changed, called once for each new member
    public void OnMemberAdded(T member);
    // When network members are changed, called once for each member no longer in network
    public void OnMemberRemoved(T member);
    // When network members are changed, called once after all OnMemberAdded/OnMemberRemoved hooks
    public void OnMembersChanged();
  }

  internal static class PseudoNetworks
  {
    private static object _networkTypeLock = new();
    // this gives us 23 custom network types that hopefully won't already be used
    private static NetworkType _nextNetworkType = (NetworkType)0x00100000;
    private static List<(Type, ConnectionGetter)> _connectionGetters = new();

    private delegate IEnumerable<Connection> ConnectionGetter(Device device);

    private static NetworkType NextNetworkType()
    {
      lock (_networkTypeLock)
      {
        var res = _nextNetworkType;
        if (res > NetworkType.All)
          throw new Exception("Exhausted custom network types");
        _nextNetworkType = (NetworkType)((long)_nextNetworkType << 1);
        return res;
      }
    }

    private static IEnumerable<Connection> GetConnections<T>(Device device) where T : IPseudoNetworkMember<T>
    {
      if (device is not IPseudoNetworkMember<T> member)
        yield break;
      foreach (var connection in member.Connections)
        yield return connection;
    }

    public static NetworkType AddNetworkType<T>() where T : IPseudoNetworkMember<T>
    {
      _connectionGetters.Add((typeof(T), GetConnections<T>));

      return NextNetworkType();
    }

    public static List<Device> RemovePseudoConnected(List<Device> connectedDevices, Device device)
    {
      for (var i = connectedDevices.Count - 1; i >= 0; i--)
      {
        var connected = connectedDevices[i];
        var keep = true;
        foreach (var (memberType, getConnections) in _connectionGetters)
        {
          if (!memberType.IsAssignableFrom(device.GetType()) || !memberType.IsAssignableFrom(connected.GetType()))
            continue;
          // if both devices implement the same member interface, check the connections for matches
          foreach (var conn in getConnections(device))
          {
            if (connected.IsConnected(conn))
            {
              keep = false;
              break;
            }
          }
          if (!keep) break;
        }
        if (!keep)
          connectedDevices.RemoveAt(i);
      }
      return connectedDevices;
    }
  }

  public class PseudoNetworkType<T> where T : IPseudoNetworkMember<T>
  {
    public NetworkType ConnectionType { get; }

    public PseudoNetworkType()
    {
      // This isn't sent over the network so it doesn't matter if these get assigned different
      // numbers in host/client, as long as it is unique for each.
      this.ConnectionType = PseudoNetworks.AddNetworkType<T>();
    }

    public void PatchConnections(T memberPrefab)
    {
      foreach (var connection in memberPrefab.Connections)
        connection.ConnectionType = this.ConnectionType;
    }

    public PseudoNetwork<T> Join() => new(this);

    // Call this at the end of OnRegistered
    public void RebuildNetworkCreate(T newMember)
    {
      this.RebuildNetworkFrom(newMember, false);
    }

    // Call this at the end of OnDeregistered
    public void RebuildNetworkDestroy(T destroyedMember)
    {
      this.RebuildNetworkFrom(destroyedMember, true);
    }

    private void RebuildNetworkFrom(T start, bool excludeStart)
    {
      var visited = new HashSet<T>();
      if (excludeStart) visited.Add(start);

      var memberList = start.Network.Members.ToList(); // make a copy
      if (memberList.Count == 0)
        memberList.Add(start);

      // run through each member and rebuild its network, skipping any included in networks we already rebuilt
      foreach (var member in memberList)
      {
        if (visited.Contains(member))
          continue;
        this.RebuildNetworkSingle(member, excludeStart ? start : default(T));
        visited.AddRange(member.Network.Members);
      }
    }

    private void RebuildNetworkSingle(T start, T exclude)
    {
      // BFS walk connections from start
      var queue = new Queue<T>();
      queue.Enqueue(start);
      var members = new HashSet<T>() { start };
      while (queue.Count > 0)
      {
        var member = queue.Dequeue();
        foreach (var conn in member.Connections)
        {
          var other = SmallCell.Get<T>(conn.GetLocalGrid());
          // exclude the member being destroyed (if there is one), and members we've already enqueued
          if (other == null || other.Equals(exclude) || members.Contains(other))
            continue;
          var connected = false;
          foreach (var oconn in other.Connections)
          {
            // connection must match the network type and be facing the opposite grids
            if (oconn.ConnectionType == this.ConnectionType && oconn.GetLocalGrid() == conn.GetFacingGrid() && oconn.GetFacingGrid() == conn.GetLocalGrid())
            {
              connected = true;
              break;
            }
          }
          if (connected)
          {
            queue.Enqueue(other);
            members.Add(other);
          }
        }
      }
      // Send updates to all member lists
      foreach (var member in members)
        member.Network.ReplaceMembers(member, members);
    }

    // These helpers only exist to make it more convenient to have one class implement multiple
    // network types. The interface members will have to be implemented explicitly, so calling
    // these on the definition will implicitly call the right interface methods.
    public PseudoNetwork<T> MemberNetwork(T member) => member.Network;
    public IEnumerable<Connection> MemberConnections(T member) => member.Connections;
  }

  // Contains the list of all connected members. Each member has its own PseudoNetwork object
  public class PseudoNetwork<T> where T : IPseudoNetworkMember<T>
  {
    public readonly PseudoNetworkType<T> Type;
    public HashSet<T> Members { get; internal set; }

    internal PseudoNetwork(PseudoNetworkType<T> type)
    {
      this.Type = type;
      this.Members = new();
    }

    internal void ReplaceMembers(T self, HashSet<T> newMembers)
    {
      var oldMembers = this.Members;
      this.Members = newMembers;
      foreach (var member in oldMembers)
        if (!newMembers.Contains(member))
          self.OnMemberRemoved(member);
      foreach (var member in newMembers)
        if (!oldMembers.Contains(member))
          self.OnMemberAdded(member);
      self.OnMembersChanged();
    }
  }
}