using System;

namespace ombarella.FikaSupport
{
    /// <summary>
    /// Role + client publish policy. No Fika types in signatures (FikaProbe/FikaNet hold them).
    ///   Solo/Host : meter own player locally. Host also receives clients' meters (bots live here).
    ///   Client    : meter own player, send it to host. No bot AI here.
    ///   Headless  : no local player, no rendering; receive only.
    /// </summary>
    internal static class FikaSync
    {
        const float KeepaliveSeconds = 3f;
        const float ProbeRetrySeconds = 5f;
        const float AckTimeoutSeconds = 16f;
        const float MinChange = 0.01f;

        static float _firstProbe = -1f, _lastSend = float.NegativeInfinity, _lastValue = -1f;
        static bool _gaveUp, _lastIr;
        internal static bool AckReceived;
        internal static bool RemoteDemand = true;

        internal static RaidRole ResolveRole()
        {
            try
            {
                if (FikaProbe.IsHost()) return FikaProbe.IsHeadless() ? RaidRole.Headless : RaidRole.Host;
                return FikaProbe.IsClient() ? RaidRole.Client : RaidRole.Solo;
            }
            catch (Exception ex)
            {
                // Client is the safe fallback: samples locally, sends nothing it cannot deliver
                OmbarellaFikaPlugin.Log.LogError("[fika] could not read role, assuming Client: " + ex.Message);
                return RaidRole.Client;
            }
        }

        internal static void ResetRaid()
        {
            _firstProbe = -1f; _lastSend = float.NegativeInfinity; _lastValue = -1f; _lastIr = false; _gaveUp = false; AckReceived = false; RemoteDemand = true;
            try { FikaNet.Reset(); } catch (Exception) { }
        }

        /// <summary>Client: false when host reports no bot is watching (sampling can pause).</summary>
        internal static bool ClientShouldSample() { return RemoteDemand; }

        internal static void HostTick(float now)
        {
            try { FikaNet.TickHost(now); } catch (Exception ex) { OmbarellaFikaPlugin.Log.LogWarning("[fika] host tick: " + ex.Message); }
        }

        /// <summary>Client: send meter on change or keepalive. Probes for a host that has Ombarella.Fika; gives up quietly if none answers.</summary>
        internal static void ClientPublish(string profileId, float meter, float now, bool ir)
        {
            if (_gaveUp || string.IsNullOrEmpty(profileId)) return;

            if (!AckReceived)
            {
                // host may lack Ombarella.Fika (unknown packet = error on its side): limited probing
                if (_firstProbe < 0f) _firstProbe = now;
                if (now - _firstProbe > AckTimeoutSeconds)
                {
                    _gaveUp = true;
                    OmbarellaFikaPlugin.Log.LogWarning("[fika] host/headless never answered: it likely lacks Ombarella.Fika. Your light meter will not affect bots this raid.");
                    return;
                }
                if (now - _lastSend < ProbeRetrySeconds) return;
            }
            else if (ir == _lastIr && Math.Abs(meter - _lastValue) < MinChange && now - _lastSend < KeepaliveSeconds)
            {
                return;
            }

            if (FikaNet.SendMeter(profileId, meter, ir))
            {
                _lastSend = now;
                _lastValue = meter;
                _lastIr = ir;
            }
        }
    }
}
