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
        public Dictionary<int, int> selections;
        public Dictionary<int, PeerData> peers;
        public int sceneObjectCount = int.MinValue;
        int id = int.MinValue;
        public int prevSelect;
        public int localId;

        private NetScene()
        {
            _singleton = this;
            Init();
        }

        public void Host(int port)
        {
            isServer = true;
            selections.Clear();
            peers.Clear();
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
            selections.Clear();
            peers.Clear();
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
            selections = new Dictionary<int, int>();
            peers = new Dictionary<int, PeerData>();
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
            selections = null;
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
            if (UnitySceneObject.Get(prevSelect) != null)
                manager.SendToAll(processor.WriteNetSerializable(new SelectPacket()
                {
                    selected = false,
                    obj = UnitySceneObject.Get(prevSelect),
                }), DeliveryMethod.ReliableOrdered);
            selections.Remove(prevSelect);
            if (Selection.activeTransform == null)
                return;
            {
                manager.SendToAll(processor.WriteNetSerializable(new SelectPacket()
                {
                    selected = true,
                    obj = UnitySceneObject.Get(Selection.activeTransform),
                    id = localId,
                }), DeliveryMethod.ReliableOrdered);
                selections.Add(UnitySceneObject.Get(Selection.activeTransform).id, localId);
                prevSelect = UnitySceneObject.Get(Selection.activeTransform).id;
            }
        }

        private void Gui(SceneView view)
        {
            foreach (var item in selections)
            {
                if (!peers.ContainsKey(item.Value))
                    continue;
                var ob = UnitySceneObject.Get(item.Key).GetObject();
                if (ob is GameObject go)
                {
                    Handles.color = peers[item.Value].color;
                    Handles.DrawWireDisc(go.transform.position, (view.camera.transform.position - go.transform.position).normalized, 1f);
                    GUI.color = peers[item.Value].color;
                    Handles.Label(go.transform.position, peers[item.Value].name);
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
            if (obj != null && UnitySceneObject.IsValid(obj))
            {
                if (UnitySceneObject.Get(obj) == null)
                {
                    switch (obj)
                    {
                        case Component cmp:
                            new UnitySceneObject(cmp);
                        break;
                    }
                }
                var packet = new SpawnObjectPacket()
                {
                    obj = UnitySceneObject.Get(obj),
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
                    name = obj.name
                }), DeliveryMethod.ReliableOrdered);
                return;
            }
            else
                peers.Add(obj.id, new PeerData(obj.id, obj.name, obj.color));
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
            if (selections.ContainsKey(obj.obj.id))
            {
                if (obj.selected)
                {
                    selections[obj.obj.id] = peers[obj.id].id;
                }
                else
                {
                    selections.Remove(obj.obj.id);
                }
            }
            else
            {
                if (obj.selected)
                {
                    selections.Add(obj.obj.id, peers[obj.id].id);
                }
            }
            if (isServer)
            {
                manager.SendToAll(processor.WriteNetSerializable(obj), DeliveryMethod.ReliableOrdered, peer);
            }
        }

        private void SpawnObject(SpawnObjectPacket obj, NetPeer peer)
        {
            var scnObj = ((UnitySceneObject)obj.obj).GetObject();
            if (scnObj != null)
            {
                if (isServer)
                    Undo.RecordObject(scnObj, $"{peer.EndPoint} Network Modify Object {scnObj.name}");
                EditorJsonUtility.FromJsonOverwrite(obj.json, scnObj);
            }
            else
            {
                Type t = Type.GetType(obj.assetId);
                if (t.IsSubclassOf(typeof(Transform)))
                {
                    var ob = new GameObject();
                    ob.transform.parent = UnitySceneObject.Get(obj.obj.parent)?.GetObject() as Transform;
                    scnObj = ob;
                }
                else if (t.IsSubclassOf(typeof(Component)))
                {
                    Object ob = UnitySceneObject.Get(obj.obj.parent).GetObject();
                    if (ob is Transform xform)
                    {
                        scnObj = xform.gameObject.AddComponent(t);
                    }
                }
                else
                {
                    return;
                }
                if (isServer)
                    Undo.RecordObject(scnObj, $"{peer.EndPoint} Network Modify Object {scnObj}");
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
                    name = username
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
                    id = item.Key,
                    color = item.Value.color,
                    name = item.Value.name
                };
                peer.Send(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
            }
            foreach (var item in UnitySceneObject.objectLookup)
            {
                Debug.Log(item.Key.name);
                var packet = new SpawnObjectPacket()
                {
                    obj = UnitySceneObject.Get(item.Key),
                    assetId = item.Value.GetType().AssemblyQualifiedName,
                    json = EditorJsonUtility.ToJson(item.Value, false)
                };
                peer.Send(processor.WriteNetSerializable(packet), DeliveryMethod.ReliableOrdered);
            }
            foreach (var item in selections)
            {
                var packet = new SelectPacket()
                {
                    selected = true,
                    obj = UnitySceneObject.Get(item.Key),
                    id = item.Value
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
