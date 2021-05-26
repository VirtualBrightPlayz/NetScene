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
        public bool isServer;
        public Dictionary<int, UnityEngine.Object> data;

        public void Init()
        {
            Debug.Log("INIT");
            password = string.Empty;
            data = new Dictionary<int, UnityEngine.Object>();
            manager = new NetManager(this);
            processor = new NetPacketProcessor();
            processor.SubscribeNetSerializable<SpawnObjectPacket, NetPeer>(SpawnObject, () => new SpawnObjectPacket());
            EditorApplication.update += Update;
        }

        public void OnDestroy()
        {
            data = null;
            manager = null;
            processor = null;
            EditorApplication.update -= Update;
        }

        public void Update()
        {
            // if (manager != null)
            manager.PollEvents();
        }

        public void Host(int port)
        {
            isServer = true;
            manager.Start(IPAddress.Any, IPAddress.IPv6Any, port);
        }

        public void Connect(string ip, int port)
        {
            isServer = false;
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
                object ob = Type.GetType(obj.assetId).GetConstructor(new Type[0]).Invoke(new object[0]);
                data.Add(obj.index, ob as UnityEngine.Object);
                EditorJsonUtility.FromJsonOverwrite(obj.json, data[obj.index]);
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
            var arr = GameObject.FindObjectsOfType<GameObject>();
            for (int i = 0; i < arr.Length; i++)
            {
                peer.Send(processor.WriteNetSerializable(new SpawnObjectPacket()
                {
                    index = i,
                    assetId = arr[i].GetType().AssemblyQualifiedName,
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
