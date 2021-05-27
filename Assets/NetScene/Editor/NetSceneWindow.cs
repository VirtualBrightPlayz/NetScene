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
            NetSceneWindow window = GetWindow<NetSceneWindow>();
        }

        public void OnEnable()
        {
            if (netScene == null)
            {
                netScene = new NetScene();
            }
        }

        public string ip = "127.0.0.1";
        public int port = 9050;
        public string password;
        public string username;
        public Color color = Color.white;
        public NetScene netScene;

        public void OnGUI()
        {
            SerializedObject obj = new SerializedObject(this);
            EditorGUILayout.PropertyField(obj.FindProperty("ip"));
            EditorGUILayout.PropertyField(obj.FindProperty("port"));
            EditorGUILayout.PropertyField(obj.FindProperty("password"));
            EditorGUILayout.PropertyField(obj.FindProperty("username"));
            EditorGUILayout.PropertyField(obj.FindProperty("color"));
            obj.ApplyModifiedPropertiesWithoutUndo();
            if (GUILayout.Button("Reload"))
            {
                if (netScene != null)
                {
                    netScene.Stop();
                    netScene.OnDestroy();
                    netScene = null;
                }
                OnEnable();
            }
            if (netScene != null)
            {
                if (netScene.manager != null && netScene.manager.IsRunning)
                {
                    GUILayout.Label("Connected.");
                    if (GUILayout.Button("Stop"))
                    {
                        netScene.Stop();
                    }
                }
                else
                {
                    if (GUILayout.Button("Host"))
                    {
                        netScene.username = username;
                        netScene.color = color;
                        netScene.password = password;
                        netScene.Host(port);
                    }
                    if (GUILayout.Button("Connect"))
                    {
                        netScene.username = username;
                        netScene.color = color;
                        netScene.password = password;
                        netScene.Connect(ip, port);
                    }
                }
            }
            else
            {
            }
        }
    }
}
