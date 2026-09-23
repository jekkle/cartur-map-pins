using System.Collections.Generic;
using UnityEngine;

namespace CarturMapPins
{
    /// Tracks the ore nodes we've seen, so a pin can be dropped once its node is gone.
    ///
    /// Ore deposits never respawn, so a pin on a mined-out node marks nothing - it is worse than
    /// no pin, because you will walk back to it. The sweep that removes those pins needs to tell
    /// "the node is gone" from "the node is not loaded right now", and Unity nulls a destroyed
    /// object's reference either way. Positions are kept here rather than components for exactly
    /// that reason: what matters is where a node was, so the sweep can ask ZoneSystem whether that
    /// spot is still loaded before concluding anything.
    ///
    /// The spawn hook sees every ore node as its zone loads, which is both cheaper and more
    /// precise than walking every MineRock in the scene.
    internal static class OreRegistry
    {
        private struct Node
        {
            public GameObject Go;
            public Vector3 Pos;
            /// Which ore this node yields - the same string the pin's record carries as its
            /// subtype. Without it every question asked here is answered for the wrong deposit:
            /// the radius the sweep works in covers whatever else is standing nearby, and a tin
            /// node ten metres away would vouch for a mined-out copper pin forever.
            public string Type;
        }

        private static readonly List<Node> Seen = new List<Node>();

#if DIAGNOSTICS
        public static int Count => Seen.Count;
#endif

        public static void Add(GameObject go, string type)
        {
            if (go == null)
                return;
            for (int i = 0; i < Seen.Count; i++)
            {
                if (Seen[i].Go == go)
                    return;
            }
            Seen.Add(new Node { Go = go, Pos = go.transform.position, Type = type });
        }

        public static void Clear() => Seen.Clear();

        /// Every node we have seen that is still standing. Prunes as it goes, same as NodeNear.
        public static IEnumerable<Vector3> AlivePositions()
        {
            for (int i = Seen.Count - 1; i >= 0; i--)
            {
                if (Seen[i].Go == null)
                {
                    Seen.RemoveAt(i);
                    continue;
                }
                yield return Seen[i].Pos;
            }
        }

        /// True when a node we have seen is still standing within `radius` of a position.
        ///
        /// `type` is the ore the asker is asking about, and only nodes of that ore answer. Pass
        /// null to accept any, which is what a record with no subtype - written by a version of
        /// this mod that did not record one - has to fall back to.
        ///
        /// Prunes as it goes: an entry whose object Unity has nulled is either mined or unloaded,
        /// and either way it is no longer evidence that something is there.
        public static bool NodeNear(Vector3 pos, float radius, string type)
        {
            float sqr = radius * radius;
            bool found = false;

            for (int i = Seen.Count - 1; i >= 0; i--)
            {
                if (Seen[i].Go == null)
                {
                    Seen.RemoveAt(i);
                    continue;
                }

                if (type != null && Seen[i].Type != null && Seen[i].Type != type)
                    continue;

                Vector3 d = Seen[i].Pos - pos;
                if (d.x * d.x + d.z * d.z <= sqr)
                    found = true;
            }

            return found;
        }
    }
}
