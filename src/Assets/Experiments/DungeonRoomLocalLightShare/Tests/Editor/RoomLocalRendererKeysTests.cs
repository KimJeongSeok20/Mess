using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace DungeonRoomLocalLightShare.Tests
{
    public sealed class RoomLocalRendererKeysTests
    {
        [Test]
        public void DuplicateNamesGetOccurrenceSuffix()
        {
            var root = new GameObject("RoomRoot");
            try
            {
                var a = new GameObject("Wall");
                a.transform.SetParent(root.transform, false);
                a.AddComponent<MeshRenderer>();
                var b = new GameObject("Wall");
                b.transform.SetParent(root.transform, false);
                b.AddComponent<MeshRenderer>();

                Dictionary<string, Renderer> map = RoomLocalRendererKeys.BuildKeyMap(root.transform);
                Assert.IsTrue(map.ContainsKey("Wall#0"));
                Assert.IsTrue(map.ContainsKey("Wall#1"));
                Assert.AreEqual(2, map.Count);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NestedPathUsesSlash()
        {
            var root = new GameObject("RoomRoot");
            try
            {
                var parent = new GameObject("Doorways");
                parent.transform.SetParent(root.transform, false);
                var child = new GameObject("Door_SM_A");
                child.transform.SetParent(parent.transform, false);
                child.AddComponent<MeshRenderer>();

                Dictionary<string, Renderer> map = RoomLocalRendererKeys.BuildKeyMap(root.transform);
                Assert.IsTrue(map.ContainsKey("Doorways/Door_SM_A#0"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
