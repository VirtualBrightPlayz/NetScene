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
using Object = UnityEngine.Object;

namespace NetScene
{
    public class NetScene : INetEventListener
    {
        private static NetScene _singleton;
        public static NetScene Singleton
        {
            get
            {
                if (_singleton == null)
                    _singleton = new NetScene();
                return _singleton;
            }
        }
        public NetManager manager;
        public NetPacketProcessor processor;
        public string password;
        public string username;
        public Color color;
        public bool isServer;
        public Dictionary<int, PeerData> peers;
        public int sceneObjectCount = int.MinValue;
        int id = int.MinValue;
        public int? prevSelect;
        public int localId;
        public Queue<SpawnObjectPacket> spawnQueue;

        private NetScene()
        {
            _singleton = this;
            Init();
        }

        public void Host(int port)
        {
            isServer = true;
            peers.Clear();
            spawnQueue.Clear();
            UnitySceneObject.Reset();
            id = int.MinValue;
            localId = -1;
            peers.Add(-1, new PeerData(localId, username, color));
            for (int j = 0; j < EditorSceneManager.sceneCount; j++)
            {
                var arr = EditorSceneManager.GetSceneAt(j).GetRootGameObjects();
                for (int i = 0; i < arr.Length; i++)
                {
                    ProcessRootObject(arr[i].transform);
                }
            }
            manager.Start(IPAddress.Any, IPAddress.IPv6Any, port);
        }

        public void Connect(string ip, int port)
        {
            isServer = false;
            peers.Clear();
            spawnQueue.Clear();
            UnitySceneObject.Reset();
            id = int.MinValue;
            localId = -1;
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

        public void Init()
        {
            Debug.Log("INIT");
            password = string.Empty;
            peers = new Dictionary<int, PeerData>();
            spawnQueue = new Queue<SpawnObjectPacket>();
            UnitySceneObject.Reset();
            manager = new NetManager(this);
            processor = new NetPacketProcessor();
            processor.RegisterNestedType<UnitySceneObjectPacket>();
            processor.SubscribeNetSerializable<SpawnObjectPacket, NetPeer>(SpawnObject, () => new SpawnObjectPacket());
            processor.SubscribeNetSerializable<DestroyObjectPacket, NetPeer>(DestroyObject, () => new DestroyObjectPacket());
            processor.SubscribeNetSerializable<UpdateIndexPacket, NetPeer>(UpdateIndex, () => new UpdateIndexPacket());
            processor.SubscribeNetSerializable<SelectPacket, NetPeer>(OnSelectPacket, () => new SelectPacket());
            processor.SubscribeNetSerializable<UserInfoPacket, NetPeer>(UserInfo, () => new UserInfoPacket());
            processor.SubscribeNetSerializable<UserSetInfoPacket, NetPeer>(OnSetLocalId, () => new UserSetInfoPacket());
            EditorApplication.update += Update;
            Selection.selectionChanged += Select;
            SceneView.duringSceneGui += Gui;
            ObjectChangeEvents.changesPublished += ObjectChanged;
        }

        public void OnDestroy()
        {
            _singleton = null;
            spawnQueue = null;
            peers = null;
            manager = null;
            processor = null;
            EditorApplication.update -= Update;
            Selection.selectionChanged -= Select;
            SceneView.duringSceneGui -= Gui;
            ObjectChangeEvents.changesPublished -= ObjectChanged;
        }

        private void Select()
        {
            if (manager == null)
                return;
            peers[localId].selected = Selection.transforms.Select(x => UnitySceneObject.Get(x).id).ToArray();
            manager.SendToAll(processor.WriteNetSerializable(new SelectPacket()
            {
                obj = peers[localId].selected,
                id = localId,
            }), DeliveryMethod.ReliableOrdered);
        }

        private void Gui(SceneView view)
        {
            if (Selection.activeTransform != null)
            {
                var go = Selection.activeTransform;
                Handles.color = color;
                Handles.DrawWireDisc(go.transform.position, (view.camera.transform.position - go.transform.position).normalized, 1f);
                GUI.color = color;
                Handles.Label(go.transform.position, username);
            }
            foreach (var item in peers)
            {
                foreach (var obj in item.Value.selected)
                {
                    var ob = UnitySceneObject.Get(obj)?.GetObject();
                    if (ob is Transform go && go.gameObject.activeInHierarchy)
                    {
                        Handles.color = item.Value.color;
                        Handles.DrawWireDisc(go.transform.position, (view.camera.transform.position - go.transform.position).normalized, 1f);
                        GUI.color = item.Value.color;
                        Handles.Label(go.transform.position, item.Value.name);
                    }
                }
            }
        }

        // when an object is modified
        private void ObjectChanged(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        stream.GetCreateGameObjectHierarchyEvent(i, out var d);
                        ProcessRootObject(EditorUtility.InstanceIDToObject(d.instanceId));
                    }
                    break;
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        stream.GetDestroyGameObjectHierarchyEvent(i, out var d);
                        Debug.Log(EditorUtility.InstanceIDToObject(d.instanceId));
                        ProcessRootObject(EditorUtility.InstanceIDToObject(d.instanceId));
                        ResetChanges();
                    }
                    break;
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var d);
                        ProcessRootObject(EditorUtility.InstanceIDToObject(d.instanceId));
                    }
                    break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                    {
                        stream.GetChangeGameObjectStructureEvent(i, out var d);
                        ProcessRootObject(EditorUtility.InstanceIDToObject(d.instanceId));
                        ResetChanges();
                    }
                    break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    {
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var d);
                        ProcessRootObject(EditorUtility.InstanceIDToObject(d.instanceId));
                        ResetChanges();
                    }
                    break;
                }
            }
        }

        private void ResetChanges()
        {
        }

        private void ProcessRootObject(Object obj)
        {
            ProcessChanges(obj);
            Transform scnObj = null;
            if (obj is Transform)
                scnObj = (Transform)obj;
            else if (obj is GameObject go)
                scnObj = go.transform;
            else if (obj is Component cmp1)
                scnObj = cmp1.transform;
            if (scnObj == null)
                return;
            Component[] cmps = scnObj.GetComponents<Component>();
            for (int j = 0; j < cmps.Length; j++)
            {
                ProcessChanges(cmps[j]);
            }
            for (int i = 0; i < scnObj.transform.childCount; i++)
            {
                Transform xform = scnObj.transform.GetChild(i);
                ProcessRootObject(xform);
            }
        }

        private void ProcessChanges(Object obj)
        {
            if (obj != null)
            {
                UnitySceneObject sceneObject = null;
                if (obj is GameObject gameObj)
                {
                    sceneObject = UnitySceneObject.Get(gameObj.transform);
                }
                else
                {
                    sceneObject = UnitySceneObject.Get(obj);
                }
                if (sceneObject == null)
                {
                    switch (obj)
                    {
                        case Transform xform:
                            sceneObject = new UnitySceneObject(xform);
                        break;
                        case Component cmp:
                            sceneObject = new UnitySceneObject(cmp);
                        break;
                        case GameObject go:
                            sceneObject = new UnitySceneObject(go.transform);
                        break;
                    }
                }
                var packet = new SpawnObjectPacket()
                {
                    obj = sceneObject,
                    assetId = obj.GetType().AssemblyQualifiedName,
                    json = EditorJsonUtility.ToJson(obj, false)
                };
                manager.SendToAll(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
            }
        }

        public void Update()
        {
            if (manager == null)
            {
                return;
            }
            manager.PollEvents();
            if (spawnQueue.Count > 0)
            {
                SpawnObject(spawnQueue.Dequeue(), manager.FirstPeer);
            }
        }

        private void UserInfo(UserInfoPacket obj, NetPeer peer)
        {
            if (isServer)
            {
                peers.Add(peer.Id, new PeerData(peer.Id, obj.name, obj.color));
                peer.Send(processor.WriteNetSerializable(new UserSetInfoPacket()
                {
                    newid = peer.Id
                }), DeliveryMethod.ReliableOrdered);
                manager.SendToAll(processor.WriteNetSerializable(new UserInfoPacket()
                {
                    id = peer.Id,
                    color = obj.color,
                    name = obj.name,
                    delete = false
                }), DeliveryMethod.ReliableOrdered);
                return;
            }
            else if (!obj.delete && !peers.ContainsKey(obj.id))
                peers.Add(obj.id, new PeerData(obj.id, obj.name, obj.color));
            else if (obj.delete && peers.ContainsKey(obj.id))
                peers.Remove(obj.id);
        }

        private void OnSetLocalId(UserSetInfoPacket obj, NetPeer peer)
        {
            if (!isServer)
            {
                localId = obj.newid;
                peers.Add(localId, new PeerData(localId, username, color));
            }
        }

        private void OnSelectPacket(SelectPacket obj, NetPeer peer)
        {
            if (isServer)
            {
                obj.id = peer.Id;
                manager.SendToAll(processor.WriteNetSerializable(obj), DeliveryMethod.ReliableOrdered, peer);
            }
            peers[obj.id].selected = obj.obj;
        }

        private void SpawnObject(SpawnObjectPacket obj, NetPeer peer)
        {
            var scnObj = ((UnitySceneObject)obj.obj)?.GetObject();
            if (scnObj != null)
            {
                scnObj.name = obj.obj.name;
                if (isServer)
                    Undo.RecordObject(scnObj, $"{peer.EndPoint} Network Modify Object {scnObj.name}");
                if (scnObj is GameObject go)
                {
                    scnObj = go.transform;
                }
                EditorJsonUtility.FromJsonOverwrite(obj.json, scnObj);
            }
            else
            {
                Type t = Type.GetType(obj.assetId);
                if (t.IsSubclassOf(typeof(Transform)) || t == typeof(Transform))
                {
                    var ob = new GameObject(obj.obj.name);
                    ob.transform.parent = UnitySceneObject.Get(obj.obj.parent)?.GetObject() as Transform;
                    if (t.IsSubclassOf(typeof(Transform)))
                    {
                        scnObj = ob.AddComponent(t);
                    }
                    else
                    {
                        scnObj = ob.transform;
                    }
                }
                else if (t.IsSubclassOf(typeof(Component)))
                {
                    Object ob = UnitySceneObject.Get(obj.obj.parent)?.GetObject();
                    if (ob is Transform xform)
                    {
                        scnObj = xform.gameObject.AddComponent(t);
                        if (scnObj == null)
                        {
                            scnObj = xform.gameObject.GetComponent(t);
                        }
                    }
                    else if (ob is GameObject gameObject)
                    {
                        scnObj = gameObject.AddComponent(t);
                    }
                }
                else
                {
                    Debug.LogError($"Unsupported type: {obj.assetId}");
                    return;
                }
                if (scnObj == null)
                {
                    Debug.LogWarning(obj.assetId);
                    spawnQueue.Enqueue(obj);
                    return;
                }
                if (isServer)
                    Undo.RecordObject(scnObj, $"{peer.EndPoint} Network Modify Object {scnObj}");
                if (UnitySceneObject.Get(scnObj) == null)
                {
                    if (scnObj is Component cmp)
                        new UnitySceneObject(cmp);
                }
                EditorJsonUtility.FromJsonOverwrite(obj.json, scnObj);
            }
        }

        private void DestroyObject(DestroyObjectPacket obj, NetPeer peer)
        {
            /*if (data.ContainsKey(obj.index) && data[obj.index] != null)
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
            }*/
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
            {
                manager.SendToAll(processor.WriteNetSerializable(new UserInfoPacket()
                {
                    color = color,
                    name = username,
                    delete = false
                }), DeliveryMethod.ReliableOrdered);
                return;
            }
            peer.Send(processor.WriteNetSerializable(new UpdateIndexPacket()
            {
                index = id,
            }), DeliveryMethod.ReliableOrdered);
            foreach (var item in peers)
            {
                var packet = new UserInfoPacket()
                {
                    id = item.Value.id,
                    color = item.Value.color,
                    name = item.Value.name,
                    delete = false
                };
                peer.Send(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
                var packet2 = new SelectPacket()
                {
                    id = item.Value.id,
                    obj = item.Value.selected
                };
                peer.Send(processor.WriteNetSerializable(packet2), DeliveryMethod.ReliableOrdered);
            }
            foreach (var item in UnitySceneObject.objectLookup)
            {
                var packet = new SpawnObjectPacket()
                {
                    obj = UnitySceneObject.Get(item.Key),
                    assetId = UnitySceneObject.Get(item.Value)?.GetObject().GetType().AssemblyQualifiedName,
                    json = EditorJsonUtility.ToJson(item.Key, false)
                };
                peer.Send(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
            }
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Debug.Log($"{peer.EndPoint.ToString()} disconnected.");
            if (isServer)
            {
                if (peers.ContainsKey(peer.Id))
                {
                    peers.Remove(peer.Id);
                    var packet = new UserInfoPacket()
                    {
                        delete = true,
                        id = peer.Id,
                    };
                    manager.SendToAll(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
                }
            }
            else
            {
                Stop();
            }
        }
    }
}
