using EFT;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using Fika.Core.Networking.LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ombarella.FikaSupport
{
    /// <summary>Client to host: own raw meter. Fika keys packets by full type name; rename on layout change.</summary>
    public sealed class OmbMeterPacket : INetSerializable
    {
        public string ProfileId = "";
        public float Meter = 1f;

        public void Serialize(NetDataWriter writer) { writer.Put(ProfileId ?? ""); writer.Put(Meter); }
        public void Deserialize(NetDataReader reader) { ProfileId = reader.GetString(64); Meter = reader.GetFloat(); }
    }

    /// <summary>Host to client: ack + "a bot is looking at you" (client may skip sampling when false).</summary>
    public sealed class OmbDemandPacket : INetSerializable
    {
        public bool Demand = true;

        public void Serialize(NetDataWriter writer) { writer.Put(Demand); }
        public void Deserialize(NetDataReader reader) { Demand = reader.GetBool(); }
    }

    /// <summary>
    /// Packet transport. Handlers run on main thread (Fika polls from Update). Host trusts a peer to report its own
    /// profile (coop friends only); id must resolve to a live human player and value is clamped by the registry.
    /// </summary>
    internal static class FikaNet
    {
        sealed class Remote { public NetPeer Peer; public bool LastSentDemand; public bool Sent; }

        static object _server, _client;
        static readonly Dictionary<string, Remote> Remotes = new Dictionary<string, Remote>(StringComparer.Ordinal);
        static readonly List<string> Scratch = new List<string>();


        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register()
        {
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnCreated);
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerDestroyedEvent>(OnDestroyed);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void OnCreated(FikaNetworkManagerCreatedEvent ev)
        {
            try
            {
                Reset();
                FikaServer server = ev.Manager as FikaServer;
                if (server != null)
                {
                    _server = server;
                    server.RegisterPacket<OmbMeterPacket, NetPeer>(OnMeter);
                    OmbarellaFikaPlugin.Log.LogInfo("[fika] meter sync registered (host)");
                    return;
                }
                FikaClient client = ev.Manager as FikaClient;
                if (client != null)
                {
                    _client = client;
                    client.RegisterPacket<OmbDemandPacket>(OnDemand);
                    OmbarellaFikaPlugin.Log.LogInfo("[fika] meter sync registered (client)");
                }
            }
            catch (Exception ex)
            {
                OmbarellaFikaPlugin.Log.LogError("[fika] could not register meter packets, other players will not be metered: " + ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void OnDestroyed(FikaNetworkManagerDestroyedEvent ev)
        {
            if (ReferenceEquals(ev.Manager, _server)) _server = null;
            if (ReferenceEquals(ev.Manager, _client)) _client = null;
            if (_server == null && _client == null) Reset();
        }

        internal static void Reset()
        {
            Remotes.Clear();
            FikaSync.AckReceived = false;
            FikaSync.RemoteDemand = true;
        }

        // ---- client ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool SendMeter(string profileId, float meter)
        {
            FikaClient client = _client as FikaClient;
            if (client == null) return false;
            try
            {
                var p = new OmbMeterPacket { ProfileId = profileId, Meter = meter };
                client.SendData(ref p, DeliveryMethod.ReliableSequenced);
                return true;
            }
            catch (Exception ex)
            {
                OmbarellaFikaPlugin.Log.LogWarning("[fika] meter send failed: " + ex.Message);
                return false;
            }
        }

        static void OnDemand(OmbDemandPacket p)
        {
            FikaSync.AckReceived = true;
            FikaSync.RemoteDemand = p.Demand;
        }

        // ---- host ----

        static void OnMeter(OmbMeterPacket p, NetPeer peer)
        {
            try
            {
                if (p == null || !IsRemoteHuman(p.ProfileId)) return;
                Meters.Registry.Track(p.ProfileId);
                Meters.Registry.SetMeter(p.ProfileId, p.Meter, Time.time);

                Remote r;
                if (!Remotes.TryGetValue(p.ProfileId, out r))
                {
                    r = new Remote();
                    Remotes[p.ProfileId] = r;
                }
                r.Peer = peer;
                if (!r.Sent) SendDemand(r, Meters.Registry.HasDemand(p.ProfileId, Time.time, DemandWindow));
            }
            catch (Exception ex)
            {
                OmbarellaFikaPlugin.Log.LogError("[fika] bad meter packet: " + ex.Message);
            }
        }

        internal const float DemandWindow = 2f;

        /// <summary>Host tick: tell each client when its demand flips.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void TickHost(float now)
        {
            if (_server == null || Remotes.Count == 0) return;
            Scratch.Clear();
            foreach (var kv in Remotes) Scratch.Add(kv.Key);
            for (int i = 0; i < Scratch.Count; i++)
            {
                Remote r = Remotes[Scratch[i]];
                bool demand = Meters.Registry.HasDemand(Scratch[i], now, DemandWindow);
                if (!r.Sent || demand != r.LastSentDemand) SendDemand(r, demand);
            }
        }

        static void SendDemand(Remote r, bool demand)
        {
            FikaServer server = _server as FikaServer;
            if (server == null || r.Peer == null) return;
            try
            {
                var p = new OmbDemandPacket { Demand = demand };
                server.SendDataToPeer(ref p, DeliveryMethod.ReliableOrdered, r.Peer);
                r.Sent = true;
                r.LastSentDemand = demand;
            }
            catch (Exception ex)
            {
                OmbarellaFikaPlugin.Log.LogWarning("[fika] demand send failed: " + ex.Message);
            }
        }

        /// <summary>Id must be a live, non-AI, non-headless player that is not the host's own.</summary>
        static bool IsRemoteHuman(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 40) return false;
            var list = Utils.GetAllPlayers();
            Player main = Utils.GetMainPlayer();
            for (int i = 0; i < list.Count; i++)
            {
                Player pl = list[i];
                if (pl == null || pl.ProfileId != id) continue;
                return pl != main && !pl.IsAI && !Utils.IsHeadlessPlayer(pl);
            }
            return false;
        }
    }
}
