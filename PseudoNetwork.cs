
using System;
using System.Collections.Generic;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Pipes;

namespace LibConstruct
{
  public interface IPseudoNetworkMember<T> where T : IPseudoNetworkMember<T>
  {
    // connections that should connect network members
    public IEnumerable<Connection> Connections { get; }
    public PseudoNetwork<T> Network { get; }
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

    // These helpers only exist to make it more convenient to have one class implement multiple
    // network types. The interface members will have to be implemented explicitly, so calling
    // these on the definition will implicitly call the right interface methods.
    public PseudoNetwork<T> MemberNetwork(T member) => member.Network;
    public IEnumerable<Connection> MemberConnections(T member) => member.Connections;
  }

  public class PseudoNetwork<T> where T : IPseudoNetworkMember<T>
  {
    public readonly PseudoNetworkType<T> Type;
    public readonly HashSet<T> Members;

    internal PseudoNetwork(PseudoNetworkType<T> type)
    {
      this.Type = type;
      this.Members = new();
    }
  }
}