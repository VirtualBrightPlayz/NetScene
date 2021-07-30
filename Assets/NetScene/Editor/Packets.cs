using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LiteNetLib.Utils;
using UnityEngine;

namespace NetScene
{
    public struct SpawnObjectPacket : INetSerializable
    {
        public UnitySceneObject obj;
        public string assetId;
        public string json;

        void INetSerializable.Deserialize(NetDataReader reader)
        {
            obj = reader.Get<UnitySceneObject>();
            assetId = reader.GetString();
            json = reader.GetString();
        }

        void INetSerializable.Serialize(NetDataWriter writer)
        {
            writer.Put(obj);
            writer.Put(assetId);
            writer.Put(json);
        }
    }

    public struct DestroyObjectPacket : INetSerializable
    {
        public UnitySceneObject obj;

        void INetSerializable.Deserialize(NetDataReader reader)
        {
            obj = reader.Get<UnitySceneObject>();
        }

        void INetSerializable.Serialize(NetDataWriter writer)
        {
            writer.Put(obj);
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
        public int id;
        public UnitySceneObject obj;

        void INetSerializable.Deserialize(NetDataReader reader)
        {
            selected = reader.GetBool();
            id = reader.GetInt();
            obj = reader.Get<UnitySceneObject>();
        }

        void INetSerializable.Serialize(NetDataWriter writer)
        {
            writer.Put(selected);
            writer.Put(id);
            writer.Put(obj);
        }
    }

    public class UnitySceneObject : INetSerializable
    {
        public static Dictionary<Object, int> objectLookup = new Dictionary<Object, int>();
        public static Dictionary<int, UnitySceneObject> sceneObjectLookup = new Dictionary<int, UnitySceneObject>();
        public static int objectCount = int.MinValue;
        public int id;
        public int parent;

        public UnitySceneObject()
        {
        }

        public UnitySceneObject(Component obj)
        {
            if (obj == null)
                return;
            id = GetId(obj);
            if (obj is Transform transform)
                parent = GetId(transform.parent);
            else
                parent = GetId(obj.transform);
            sceneObjectLookup.Add(id, this);
        }

        public Object GetObject()
        {
            int id = this.id;
            return objectLookup.FirstOrDefault(p => p.Value == id).Key;
        }

        public static bool IsValid(Object obj)
        {
            if (obj is Component)
                return true;
            return false;
        }

        public static UnitySceneObject Get(Object obj)
        {
            if (IsValid(obj))
            {
                int id = GetId(obj);
                if (!sceneObjectLookup.ContainsKey(id))
                {
                    return null;
                }
                return sceneObjectLookup[id];
            }
            return null;
        }

        public static UnitySceneObject Get(int id)
        {
            if (sceneObjectLookup.ContainsKey(id))
            {
                return sceneObjectLookup[id];
            }
            return null;
        }

        private static int GetId(Object obj)
        {
            if (obj == null)
                return objectCount;
            if (!objectLookup.ContainsKey(obj))
            {
                objectLookup.Add(obj, ++objectCount);
            }
            return objectLookup[obj];
        }

        public void Deserialize(NetDataReader reader)
        {
            id = reader.GetInt();
            parent = reader.GetInt();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(id);
            writer.Put(parent);
        }
    }
}