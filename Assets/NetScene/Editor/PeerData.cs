using System.Collections.Generic;
using UnityEngine;

namespace NetScene
{
    public class PeerData
    {
        public int id;
        public string name;
        public Color color;
        public int[] selected = new int[0];

        public PeerData(int id, string name, Color color)
        {
            this.id = id;
            this.name = name;
            this.color = color;
        }
    }
}