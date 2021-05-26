using System.Collections;
using System.Collections.Generic;
using LiteNetLib.Utils;
using UnityEngine;

public struct SpawnObjectPacket : INetSerializable
{
    public int index;
    public string assetId;
    public string json;

    void INetSerializable.Deserialize(NetDataReader reader)
    {
        index = reader.GetInt();
        assetId = reader.GetString();
        json = reader.GetString();
    }

    void INetSerializable.Serialize(NetDataWriter writer)
    {
        writer.Put(index);
        writer.Put(assetId);
        writer.Put(json);
    }
}
