using UnityEngine;

namespace ASH.Core
{
    /// <summary>Prefixed logging so the editor bridge can filter ASH messages.</summary>
    public static class ASHLog
    {
        public static void Info(string msg) => Debug.Log("[ASH] " + msg);
        public static void Warn(string msg) => Debug.LogWarning("[ASH] " + msg);
    }
}
