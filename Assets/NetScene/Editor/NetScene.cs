using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEditor;
using UnityEngine;

namespace NetScene
{
    public class NetScene : Editor, INetEventListener
    {
        public NetManager manager;
        public NetPacketProcessor processor;
        public string password;

        public Dictionary<int, UnityEngine.Object> data;

        public void Init()
        {
            Debug.Log("INIT");
            password = string.Empty;
            data = new Dictionary<int, UnityEngine.Object>();
            manager = new NetManager(this);
            processor = new NetPacketProcessor();
            processor.SubscribeNetSerializable<SpawnObjectPacket, NetPeer>(SpawnObject, () => new SpawnObjectPacket());
            // EditorApplication.update += Update;
        }

        public void OnDestroy()
        {
            data = null;
            manager = null;
            processor = null;
            // EditorApplication.update -= Update;
        }

        public void Update()
        {
            // if (manager != null)
            manager.PollEvents();
        }

        public void Host(int port)
        {
            manager.Start(IPAddress.Any, IPAddress.IPv6Any, port);
        }

        public void Connect(string ip, int port)
        {
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
            if (data.ContainsKey(obj.index))
            {
                EditorJsonUtility.FromJsonOverwrite(obj.json, data[obj.index]);
            }
            else
            {
                Debug.Log(obj.assetId);
                Debug.Log(Type.ReflectionOnlyGetType(obj.assetId, false, true));
                object ob = JsonUtility.FromJson(obj.json, Type.GetType(obj.assetId));
                if (ob is UnityEngine.Object)
                {
                    data.Add(obj.index, ob as UnityEngine.Object);
                }
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
            var arr = GameObject.FindObjectsOfType<GameObject>();
            for (int i = 0; i < arr.Length; i++)
            {
                peer.Send(processor.WriteNetSerializable(new SpawnObjectPacket()
                {
                    index = i,
                    assetId = arr[i].GetType().FullName,
                    json = EditorJsonUtility.ToJson(arr[i])
                }), DeliveryMethod.ReliableOrdered);
            }
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Debug.Log($"{peer.EndPoint.ToString()} disconnected.");
        }
    }
}
