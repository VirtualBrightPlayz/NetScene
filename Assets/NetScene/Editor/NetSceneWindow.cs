using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NetScene
{
    public class NetSceneWindow : EditorWindow
    {
        [MenuItem("Tools/NetScene")]
        public static void OpenWindow()
        {
            if (NetScene.singleton == null)
                new NetScene();
            NetSceneWindow window = GetWindow<NetSceneWindow>();
        }

        public string ip = "127.0.0.1";
        public int port = 9050;
        public string password;

        public void OnGUI()
        {
            SerializedObject obj = new SerializedObject(this);
            EditorGUILayout.PropertyField(obj.FindProperty("ip"));
            EditorGUILayout.PropertyField(obj.FindProperty("port"));
            EditorGUILayout.PropertyField(obj.FindProperty("password"));
            obj.ApplyModifiedPropertiesWithoutUndo();
            if (NetScene.singleton != null && NetScene.singleton.manager != null)
                GUILayout.Label("Connected.");
            if (GUILayout.Button("Host"))
            {
                NetScene.singleton.password = password;
                NetScene.singleton.Host(port);
            }
            if (GUILayout.Button("Connect"))
            {
                NetScene.singleton.password = password;
                NetScene.singleton.Connect(ip, port);
            }
        }
    }
}
