using System.Collections.Generic;
using NUnit.Framework;
using PurrNet.Transports;
using UnityEngine;

public sealed class LocalTransportRegressionTests
{
    [Test]
    public void RepeatedHostShutdownDoesNotNotifyDisposedModulesAndCanRestart()
    {
        var root = new GameObject("Local transport regression") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var transport = root.AddComponent<LocalTransport>();
            var serverStates = new List<ConnectionState>();
            transport.onConnectionState += (state, asServer) => { if (asServer) serverStates.Add(state); };
            transport.Listen(0);
            transport.Connect(null, 0);
            transport.StopListening();
            Assert.That(transport.clientState, Is.EqualTo(ConnectionState.Disconnected));
            Assert.That(serverStates, Is.EqualTo(new[] { ConnectionState.Connecting, ConnectionState.Connected,
                ConnectionState.Disconnecting, ConnectionState.Disconnected }));
            transport.StopListening();
            Assert.That(serverStates.Count, Is.EqualTo(4), "Duplicate shutdown must not notify disposed network modules.");
            transport.Listen(0);
            transport.Connect(null, 0);
            Assert.That(transport.listenerState, Is.EqualTo(ConnectionState.Connected));
            Assert.That(transport.clientState, Is.EqualTo(ConnectionState.Connected));
            transport.StopListening();
        }
        finally { Object.DestroyImmediate(root); }
    }
}
