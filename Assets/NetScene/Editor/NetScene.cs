using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
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

        public void OnEnable()
        {
            Debug.Log("INIT");
            password = string.Empty;
            data = new Dictionary<int, UnityEngine.Object>();
            processor = new NetPacketProcessor();
            processor.SubscribeNetSerializable<SpawnObjectPacket, NetPeer>(SpawnObject, () => new SpawnObjectPacket());
            EditorApplication.update += Update;
        }

        void OnDestroy()
        {
            EditorApplication.update -= Update;
        }

        public void Update()
        {
            if (manager != null)
                manager.PollEvents();
        }

        public void Host(int port)
        {
            manager = new NetManager(this);
            manager.Start(port);
        }

        public void Connect(string ip, int port)
        {
            manager = new NetManager(this);
            manager.Start();
            manager.Connect(ip, port, password);
        }

        private void SpawnObject(SpawnObjectPacket obj, NetPeer peer)
        {
            if (data.ContainsKey(obj.index))
            {
                EditorJsonUtility.FromJsonOverwrite(obj.json, data[obj.index]);
            }
            else
            {
                object ob = JsonUtility.FromJson(obj.json, Type.GetType(obj.assetId));
                if (ob is UnityEngine.Object)
                {
                    data.Add(obj.index, ob as UnityEngine.Object);
                }
            }
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            request.AcceptIfKey(password);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
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
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Debug.Log($"{peer.EndPoint.ToString()} disconnected.");
        }
    }
}
