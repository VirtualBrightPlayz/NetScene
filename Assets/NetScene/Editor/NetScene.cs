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
        // first is local unity id; second is net id
        public Dictionary<int, int> netdata;
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
            netdata = new Dictionary<int, int>();
            manager = new NetManager(this);
            processor = new NetPacketProcessor();
            processor.SubscribeNetSerializable<SpawnObjectPacket, NetPeer>(SpawnObject, () => new SpawnObjectPacket());
            processor.SubscribeNetSerializable<DestroyObjectPacket, NetPeer>(DestroyObject, () => new DestroyObjectPacket());
            processor.SubscribeNetSerializable<UpdateIndexPacket, NetPeer>(UpdateIndex, () => new UpdateIndexPacket());
            EditorApplication.update += Update;
            ObjectChangeEvents.changesPublished += ObjectChanged;
        }

        public void OnDestroy()
        {
            data = null;
            netdata = null;
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
            ProcessChanges(id);
            var obj = EditorUtility.InstanceIDToObject(id) as GameObject;
            if (obj == null)
                return;
            Component[] cmps = obj.GetComponents<Component>();
            for (int j = 0; j < cmps.Length; j++)
            {
                ProcessChanges(cmps[j].GetInstanceID());
            }
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                Transform xform = obj.transform.GetChild(i);
                ProcessRootObject(xform.gameObject.GetInstanceID());
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

        private int GetParentIndex(UnityEngine.Object obj)
        {
            if (obj is GameObject go)
            {
                if (go.transform.parent == null)
                    return -1;
                return go.transform.parent.gameObject.GetInstanceID();
            }
            else if (obj is Component cmp)
            {
                return cmp.gameObject.GetInstanceID();
            }
            return -1;
        }

        private int GetNetIndex(int id)
        {
            if (!netdata.ContainsKey(id))
            {
                netdata.Add(id, this.id++);
                manager.SendToAll(processor.WriteNetSerializable(new UpdateIndexPacket()
                {
                    index = netdata[id],
                }), DeliveryMethod.ReliableOrdered);
            }
            return netdata[id];
        }

        private void ProcessChanges(int id)
        {
            var obj = EditorUtility.InstanceIDToObject(id);
            if (obj == null || obj.hideFlags.HasFlag(HideFlags.DontSave))
            {
                manager.SendToAll(processor.WriteNetSerializable(new DestroyObjectPacket()
                {
                    index = GetNetIndex(id)
                }), DeliveryMethod.ReliableOrdered);
                if (!data.ContainsKey(GetNetIndex(id)))
                {
                    data.Remove(GetNetIndex(id));
                }
            }
            else
            {
                var packet = new SpawnObjectPacket()
                {
                    index = GetNetIndex(id),
                    childIndex = GetNetIndex(GetChildIndex(obj)),
                    parentIndex = GetNetIndex(GetParentIndex(obj)),
                    assetId = obj.GetType().AssemblyQualifiedName,
                    json = EditorJsonUtility.ToJson(obj, false)
                };
                manager.SendToAll(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
                if (!data.ContainsKey(GetNetIndex(id)))
                {
                    data.Add(GetNetIndex(id), obj);
                }
            }
        }

        public void Update()
        {
            if (manager == null)
            {
                return;
            }
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
            if (data.ContainsKey(GetNetIndex(obj.index)) && data[GetNetIndex(obj.index)] != null)
            {
                EditorJsonUtility.FromJsonOverwrite(obj.json, data[GetNetIndex(obj.index)]);
            }
            else
            {
                Type t = Type.GetType(obj.assetId);
                if (t.IsSubclassOf(typeof(Component)))
                {
                    if (!data.ContainsKey(GetNetIndex(obj.parentIndex)))
                    {
                        data.Add(GetNetIndex(obj.parentIndex), new GameObject());
                    }
                    UnityEngine.Object ob = (data[GetNetIndex(obj.parentIndex)] as GameObject).GetComponent(t);
                    if (ob == null)
                    {
                        ob = (data[GetNetIndex(obj.parentIndex)] as GameObject).AddComponent(t);
                    }
                    data.Add(GetNetIndex(obj.index), ob as UnityEngine.Object);
                }
                else
                {
                    object ob = t.GetConstructor(new Type[0]).Invoke(new object[0]);
                    data.Add(GetNetIndex(obj.index), ob as UnityEngine.Object);
                }
                Debug.Assert(data[GetNetIndex(obj.index)] == null, GetNetIndex(obj.index));
                EditorJsonUtility.FromJsonOverwrite(obj.json, data[GetNetIndex(obj.index)]);
            }
        }

        private void DestroyObject(DestroyObjectPacket obj, NetPeer peer)
        {
            if (data.ContainsKey(GetNetIndex(obj.index)) && data[GetNetIndex(obj.index)] != null)
            {
                UnityEngine.Object.DestroyImmediate(data[GetNetIndex(obj.index)]);
                data.Remove(GetNetIndex(obj.index));
            }
        }

        private void UpdateIndex(UpdateIndexPacket obj, NetPeer peer)
        {
            id = obj.index;
            if (isServer)
                manager.SendToAll(processor.WriteNetSerializable(new UpdateIndexPacket()
                {
                    index = id,
                }), DeliveryMethod.ReliableOrdered);
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
            peer.Tag = new PeerData();
            peer.Send(processor.WriteNetSerializable(new UpdateIndexPacket()
            {
                index = id,
            }), DeliveryMethod.ReliableOrdered);
            foreach (var item in data)
            {
                var packet = new SpawnObjectPacket()
                {
                    index = GetNetIndex(item.Key),
                    childIndex = GetNetIndex(GetChildIndex(item.Value)),
                    parentIndex = GetNetIndex(GetParentIndex(item.Value)),
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
