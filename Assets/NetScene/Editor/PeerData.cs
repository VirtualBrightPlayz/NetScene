using System.Collections.Generic;
using UnityEngine;

namespace NetScene
{
    public class PeerData
    {
        public int id;
        public string name;
        public Color color;

        public PeerData(int id, string name, Color color)
        {
            this.id = id;
            this.name = name;
            this.color = color;
        }
    }
}