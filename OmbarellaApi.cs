using System;

namespace ombarella
{
    public enum RaidRole { Solo, Host, Client, Headless }

    /// <summary>
    /// Extension points for add-on plugins (Ombarella.Fika). Core has no Fika dependency; an add-on sets these in its Awake.
    /// All hooks are optional. Core wraps none of them in try/catch except where noted: add-ons must not throw.
    ///   Solo/Host : meter own player locally. Host also receives remote meters via Meters.Registry.
    ///   Client    : meter own player, hand it to ClientPublish. No bot AI here.
    ///   Headless  : no local player, no rendering; HostTick only.
    /// </summary>
    public static class OmbarellaApi
    {
        /// <summary>Called at raid start. Null = Solo.</summary>
        public static Func<RaidRole> RoleResolver;

        /// <summary>Client only: false = remote host reports no bot is looking, sampling may pause. Null = always sample.</summary>
        public static Func<bool> ClientShouldSample;

        /// <summary>Client only: (profileId, rawMeter, time, irLightOn). Called every poll tick; add-on rate-limits.</summary>
        public static Action<string, float, float, bool> ClientPublish;

        /// <summary>Host and headless: called every poll tick during a raid with Time.time.</summary>
        public static Action<float> HostTick;

        /// <summary>Raid start and end: add-on drops per-raid state.</summary>
        public static Action RaidReset;

        internal static RaidRole ResolveRole()
        {
            var f = RoleResolver;
            return f == null ? RaidRole.Solo : f();
        }

        internal static void FireRaidReset() { var f = RaidReset; if (f != null) f(); }

        internal static void FireClientPublish(string id, float raw, float now, bool ir) { var f = ClientPublish; if (f != null) f(id, raw, now, ir); }

        internal static bool ShouldSampleAsClient()
        {
            var f = ClientShouldSample;
            return f == null || f();
        }
    }
}
