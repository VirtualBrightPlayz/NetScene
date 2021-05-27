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

    public struct UserSetInfoPacket : INetSerializable
    {
        public int newid;

        void INetSerializable.Deserialize(NetDataReader reader)
        {
            newid = reader.GetInt();
        }

        void INetSerializable.Serialize(NetDataWriter writer)
        {
            writer.Put(newid);
        }
    }

    public struct UserInfoPacket : INetSerializable
    {
        public int id;
        public string name;
        public Color color;

        void INetSerializable.Deserialize(NetDataReader reader)
        {
            id = reader.GetInt();
            name = reader.GetString();
            color = new Color(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), 1f);
        }

        void INetSerializable.Serialize(NetDataWriter writer)
        {
            writer.Put(id);
            writer.Put(name);
            writer.Put(color.r);
            writer.Put(color.g);
            writer.Put(color.b);
        }
    }

    public struct SelectPacket : INetSerializable
    {
        public bool selected;
        public int index;
        public int id;

        void INetSerializable.Deserialize(NetDataReader reader)
        {
            selected = reader.GetBool();
            index = reader.GetInt();
            id = reader.GetInt();
        }

        void INetSerializable.Serialize(NetDataWriter writer)
        {
            writer.Put(selected);
            writer.Put(index);
            writer.Put(id);
        }
    }
}