using System.Collections;
using System.Collections.Generic;
using LiteNetLib.Utils;
using UnityEngine;

namespace NetScene
{
    public struct SpawnObjectPacket : INetSerializable
    {
        public int index;
        public int parentIndex;
        public int childIndex;
        public string assetId;
        public string json;

        void INetSerializable.Deserialize(NetDataReader reader)
        {
            index = reader.GetInt();
            parentIndex = reader.GetInt();
            childIndex = reader.GetInt();
            assetId = reader.GetString();
            json = reader.GetString();
        }

        void INetSerializable.Serialize(NetDataWriter writer)
        {
            writer.Put(index);
            writer.Put(parentIndex);
            writer.Put(childIndex);
            writer.Put(assetId);
            writer.Put(json);
        }
    }

    public struct DestroyObjectPacket : INetSerializable
    {
        public int index;

        void INetSerializable.Deserialize(NetDataReader reader)
        {
            index = reader.GetInt();
        }

        void INetSerializable.Serialize(NetDataWriter writer)
        {
            writer.Put(index);
        }
    }

    public struct UpdateIndexPacket : INetSerializable
    {
        public int index;

        void INetSerializable.Deserialize(NetDataReader reader)
        {
            index = reader.GetInt();
        }

        void INetSerializable.Serialize(NetDataWriter writer)
        {
            writer.Put(index);
        }
    }
}