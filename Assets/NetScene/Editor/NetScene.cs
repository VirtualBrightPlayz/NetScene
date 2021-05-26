using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NetScene
{
    public class NetScene : INetEventListener
    {
        public NetManager manager;
        public NetPacketProcessor processor;
        public string password;
        public bool isServer;
        public Dictionary<int, UnityEngine.Object> data;
        int id = int.MinValue;

        public NetScene()
        {
            Init();
        }

        public void Init()
        {
            Debug.Log("INIT");
            password = string.Empty;
            data = new Dictionary<int, UnityEngine.Object>();
            manager = new NetManager(this);
            processor = new NetPacketProcessor();
            processor.SubscribeNetSerializable<SpawnObjectPacket, NetPeer>(SpawnObject, () => new SpawnObjectPacket());
            processor.SubscribeNetSerializable<DestroyObjectPacket, NetPeer>(DestroyObject, () => new DestroyObjectPacket());
            EditorApplication.update += Update;
            ObjectChangeEvents.changesPublished += ObjectChanged;
        }

        public void OnDestroy()
        {
            data = null;
            manager = null;
            processor = null;
            EditorApplication.update -= Update;
            ObjectChangeEvents.changesPublished -= ObjectChanged;
        }

        private void ObjectChanged(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        stream.GetCreateGameObjectHierarchyEvent(i, out var d);
                        ProcessChanges(d.instanceId);
                    }
                    break;
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        stream.GetDestroyGameObjectHierarchyEvent(i, out var d);
                        ProcessChanges(d.instanceId);
                    }
                    break;
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var d);
                        ProcessChanges(d.instanceId);
                    }
                    break;
                }
            }
        }

        private void ProcessRootObject(int id)
        {
            var obj = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (obj == null)
                return;
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                obj.transform.GetChild(i);
            }
        }

        private int GetChildIndex(UnityEngine.Object obj)
        {
            if (obj is GameObject go)
            {
                return go.transform.GetSiblingIndex();
            }
            else if (obj is Component cmp)
            {
                return cmp.GetComponents<Component>().ToList().IndexOf(cmp);
            }
            return -1;
        }

        private void ProcessChanges(int id)
        {
            var obj = EditorUtility.InstanceIDToObject(id);
            if (obj == null || obj.hideFlags.HasFlag(HideFlags.DontSave))
            {
                manager.SendToAll(processor.WriteNetSerializable(new DestroyObjectPacket()
                {
                    index = id
                }), DeliveryMethod.ReliableOrdered);
                if (!data.ContainsKey(id))
                    data.Remove(id);
            }
            else
            {
                var packet = new SpawnObjectPacket()
                {
                    index = id,
                    childIndex = GetChildIndex(obj),
                    assetId = obj.GetType().AssemblyQualifiedName,
                    json = EditorJsonUtility.ToJson(obj, false)
                };
                manager.SendToAll(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
                if (!data.ContainsKey(id))
                    data.Add(id, obj);
            }
        }

        public void Update()
        {
            if (manager == null)
            {
                return;
            }
            /*if (isServer)
            {
                List<int> list = new List<int>();
                foreach (var item in data)
                {
                    if (item.Value == null || item.Value.hideFlags != HideFlags.None)
                    {
                        manager.SendToAll(processor.WriteNetSerializable(new DestroyObjectPacket()
                        {
                            index = item.Key
                        }), DeliveryMethod.ReliableOrdered);
                        list.Add(item.Key);
                    }
                }
                foreach (var item in list)
                    data.Remove(item);
                var arr = GameObject.FindObjectsOfType<GameObject>();
                for (int i = 0; i < arr.Length; i++)
                {
                    if (!data.ContainsValue(arr[i]))
                    {
                        var packet = new SpawnObjectPacket()
                        {
                            index = id++,
                            assetId = arr[i].GetType().AssemblyQualifiedName,
                            json = EditorJsonUtility.ToJson(arr[i], false)
                        };
                        manager.SendToAll(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
                        data.Add(packet.index, arr[i]);
                    }
                }
            }*/
            manager.PollEvents();
        }

        public void Host(int port)
        {
            isServer = true;
            id = int.MinValue;
            data.Clear();
            for (int j = 0; j < EditorSceneManager.sceneCount; j++)
            {
                var arr = EditorSceneManager.GetSceneAt(j).GetRootGameObjects();
                for (int i = 0; i < arr.Length; i++)
                {
                    ProcessRootObject(arr[i].GetInstanceID());
                }
            }
            manager.Start(IPAddress.Any, IPAddress.IPv6Any, port);
        }

        public void Connect(string ip, int port)
        {
            isServer = false;
            data.Clear();
            id = int.MinValue;
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            manager.Start();
            manager.Connect(ip, port, password);
        }

        public void Stop()
        {
            if (manager != null)
                manager.Stop(true);
        }

        private void SpawnObject(SpawnObjectPacket obj, NetPeer peer)
        {
            if (data.ContainsKey(obj.index) && data[obj.index] != null)
            {
                EditorJsonUtility.FromJsonOverwrite(obj.json, data[obj.index]);
            }
            else
            {
                Type t = Type.GetType(obj.assetId);
                Debug.Assert(t != null, obj.assetId);
                if (t.IsSubclassOf(typeof(Component)))
                {
                    object ob = (data[obj.parentIndex] as GameObject).AddComponent(t);
                    data.Add(obj.index, ob as UnityEngine.Object);
                }
                else
                {
                    object ob = t.GetConstructor(new Type[0]).Invoke(new object[0]);
                    data.Add(obj.index, ob as UnityEngine.Object);
                }
                EditorJsonUtility.FromJsonOverwrite(obj.json, data[obj.index]);
            }
        }

        private void DestroyObject(DestroyObjectPacket obj, NetPeer peer)
        {
            if (data.ContainsKey(obj.index) && data[obj.index] != null)
            {
                UnityEngine.Object.DestroyImmediate(data[obj.index]);
                data.Remove(obj.index);
            }
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            Debug.Log($"{request.RemoteEndPoint.ToString()} requested to connect.");
            request.AcceptIfKey(password);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Debug.Log($"Error from {endPoint.ToString()} - {socketError.ToString()}");
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            processor.ReadAllPackets(reader, peer);
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            Debug.Log($"{peer.EndPoint.ToString()} connected.");
            if (!isServer)
                return;
            foreach (var item in data)
            {
                var packet = new SpawnObjectPacket()
                {
                    index = item.Key,
                    childIndex = GetChildIndex(item.Value),
                    assetId = item.Value.GetType().AssemblyQualifiedName,
                    json = EditorJsonUtility.ToJson(item.Value, false)
                };
                peer.Send(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
            }
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Debug.Log($"{peer.EndPoint.ToString()} disconnected.");
        }
    }
}
