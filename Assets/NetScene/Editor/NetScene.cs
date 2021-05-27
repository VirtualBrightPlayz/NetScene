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
        // second is local unity id; first is net id
        public Dictionary<int, int> netdata2;
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
            netdata2 = new Dictionary<int, int>();
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
            netdata2 = null;
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
                        ProcessRootObject(GetNetworkId(d.instanceId));
                    }
                    break;
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        stream.GetDestroyGameObjectHierarchyEvent(i, out var d);
                        ProcessRootObject(GetNetworkId(d.instanceId));
                    }
                    break;
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var d);
                        ProcessRootObject(GetNetworkId(d.instanceId));
                    }
                    break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                    {
                        stream.GetChangeGameObjectStructureEvent(i, out var d);
                        ProcessRootObject(GetNetworkId(d.instanceId));
                    }
                    break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    {
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var d);
                        ProcessRootObject(GetNetworkId(d.instanceId));
                    }
                    break;
                }
            }
        }

        private void ProcessRootObject(int id)
        {
            ProcessChanges(id);
            if (!netdata2.ContainsKey(id))
                return;
            var obj = EditorUtility.InstanceIDToObject(netdata2[id]) as GameObject;
            if (obj == null)
                return;
            Component[] cmps = obj.GetComponents<Component>();
            for (int j = 0; j < cmps.Length; j++)
            {
                ProcessChanges(GetNetworkId(cmps[j].GetInstanceID()));
            }
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                Transform xform = obj.transform.GetChild(i);
                ProcessRootObject(GetNetworkId(xform.gameObject.GetInstanceID()));
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
                    return GetNetworkId(obj.GetInstanceID());
                return GetNetworkId(go.transform.parent.gameObject.GetInstanceID());
            }
            else if (obj is Component cmp)
            {
                return GetNetworkId(cmp.gameObject.GetInstanceID());
            }
            return GetNetworkId(obj.GetInstanceID());
        }

        private int GetNetworkId(int unityid)
        {
            if (!netdata.ContainsKey(unityid))
            {
                netdata.Add(unityid, ++id);
                netdata2.Add(netdata[unityid], unityid);
                manager.SendToAll(processor.WriteNetSerializable(new UpdateIndexPacket()
                {
                    index = netdata[unityid]
                }), DeliveryMethod.ReliableOrdered);
            }
            return netdata[unityid];
        }

        private void ProcessChanges(int id)
        {
            var obj = EditorUtility.InstanceIDToObject(netdata2[id]);
            if (obj == null)
            {
                manager.SendToAll(processor.WriteNetSerializable(new DestroyObjectPacket()
                {
                    index = id
                }), DeliveryMethod.ReliableOrdered);
                if (data.ContainsKey(id))
                {
                    netdata.Remove(netdata2[id]);
                    netdata2.Remove(id);
                    if (isServer)
                        Undo.DestroyObjectImmediate(data[id]);
                    data.Remove(id);
                }
            }
            else
            {
                var packet = new SpawnObjectPacket()
                {
                    index = id,
                    childIndex = GetChildIndex(obj),
                    parentIndex = GetParentIndex(obj),
                    assetId = obj.GetType().AssemblyQualifiedName,
                    json = EditorJsonUtility.ToJson(obj, false)
                };
                manager.SendToAll(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
                if (!data.ContainsKey(id))
                {
                    data.Add(id, obj);
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
                    ProcessRootObject(GetNetworkId(arr[i].GetInstanceID()));
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
                if (isServer)
                    Undo.RecordObject(data[obj.index], $"{peer.EndPoint} Network Modify Object {data[obj.index].name}");
                EditorJsonUtility.FromJsonOverwrite(obj.json, data[obj.index]);
            }
            else
            {
                Type t = Type.GetType(obj.assetId);
                if (t.IsSubclassOf(typeof(Component)))
                {
                    if (!data.ContainsKey(obj.parentIndex))
                    {
                        Debug.LogError("Parent Does not exist!");
                        // return;
                    }
                    UnityEngine.Object ob = (data[obj.parentIndex] as GameObject).GetComponent(t);
                    if (ob == null)
                    {
                        ob = (data[obj.parentIndex] as GameObject).AddComponent(t);
                    }
                    data.Add(obj.index, ob);
                    netdata.Add(ob.GetInstanceID(), obj.index);
                    netdata2.Add(obj.index, ob.GetInstanceID());
                }
                else
                {
                    UnityEngine.Object ob = t.GetConstructor(new Type[0]).Invoke(new object[0]) as UnityEngine.Object;
                    data.Add(obj.index, ob);
                    netdata.Add(ob.GetInstanceID(), obj.index);
                    netdata2.Add(obj.index, ob.GetInstanceID());
                }
                Debug.Assert(data[obj.index] != null, obj.index);
                if (isServer)
                    Undo.RecordObject(data[obj.index], $"{peer.EndPoint} Network Modify Object {data[obj.index].name}");
                EditorJsonUtility.FromJsonOverwrite(obj.json, data[obj.index]);
            }
        }

        private void DestroyObject(DestroyObjectPacket obj, NetPeer peer)
        {
            if (data.ContainsKey(obj.index) && data[obj.index] != null)
            {
                netdata.Remove(data[obj.index].GetInstanceID());
                netdata2.Remove(obj.index);
                if (isServer)
                    Undo.DestroyObjectImmediate(data[obj.index]);
                else
                    UnityEngine.Object.DestroyImmediate(data[obj.index]);
                data.Remove(obj.index);
            }
            List<int> des = new List<int>();
            foreach (var d in data)
            {
                if (d.Value == null)
                {
                    netdata.Remove(netdata2[d.Key]);
                    netdata2.Remove(d.Key);
                    des.Add(d.Key);
                }
            }
            foreach (var d in des)
            {
                data.Remove(d);
            }
        }

        private void UpdateIndex(UpdateIndexPacket obj, NetPeer peer)
        {
            id = obj.index;
            if (isServer)
                manager.SendToAll(processor.WriteNetSerializable(new UpdateIndexPacket()
                {
                    index = id,
                }), DeliveryMethod.ReliableOrdered, peer);
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
                    index = item.Key,
                    childIndex = GetChildIndex(item.Value),
                    parentIndex = GetParentIndex(item.Value),
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
