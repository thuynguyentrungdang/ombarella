using BepInEx.Bootstrap;
using ombarella.Core;
using System;
using UnityEngine;

namespace ombarella
{
    /// <summary>
    /// Makes the meter camera see the lighting the player sees. EFT's ambient on our camera is the flat vanilla term, while with
    /// TraceSix the screen shows TraceSix's indirect light (darker in shade and indoors), so the meter read brighter than the
    /// picture. Attach mode puts TraceSix's voxel light pass on our camera through its public API (TraceSixApi).
    /// No TraceSix type is named here: only TraceSixBridge does, and only after the plugin is seen loaded.
    /// </summary>
    internal sealed class LightMatch
    {
        LightMatchMode _mode = LightMatchMode.Off;
        LightMatchState _state = LightMatchState.Off;
        bool _resolved;
        bool _attached;
        Camera _cam;
        int _apiVersion = -2; // -2 = not read yet, -1 = unreadable
        bool _failLogged;

        public LightMatchState State { get { return _state; } }

        /// <summary>Resolve now (raid start / mode change) and return a log line.</summary>
        public string Describe()
        {
            Resolve();
            return "light match " + _mode + " -> " + _state +
                   (_state == LightMatchState.Attached ? " (TraceSix API v" + _apiVersion + ")" : "");
        }

        /// <summary>Each sample, before the render: follows the config switch and keeps the pass on the right camera.</summary>
        public void Sync(Camera cam)
        {
            LightMatchMode mode = OmbarellaConfig.LightMatch.Value;
            if (!_resolved || mode != _mode || cam != _cam)
            {
                if (_attached && _cam != null && cam != _cam) Detach();
                _cam = cam;
                Resolve();
            }
            if (_state == LightMatchState.Attached)
            {
                if (!_attached && _cam != null) Attach();
            }
            else if (_attached) Detach();
        }

        public void Release()
        {
            if (_attached) Detach();
            _cam = null;
            _resolved = false;
        }

        void Resolve()
        {
            _mode = OmbarellaConfig.LightMatch.Value;
            bool loaded = Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(TraceSixBridge.PluginGuid);
            if (_mode == LightMatchMode.Attach && loaded && TraceSixBridge.Compiled && _apiVersion == -2)
            {
                try { _apiVersion = TraceSixBridge.ApiVersion(); }
                catch (Exception e)
                {
                    _apiVersion = -1; // older TraceSix without the API (MissingMethod / TypeLoad): stay off
                    Plugin.Log.LogWarning("light match: TraceSix API not usable (" + e.GetType().Name + "), meter stays on EFT ambient");
                }
            }
            LightMatchState was = _state;
            _state = LightMatchPolicy.Resolve(_mode, loaded, TraceSixBridge.Compiled, _apiVersion);
            _resolved = true;
            if (_state != was) Plugin.Log.LogInfo("light match: " + Describe2());
        }

        string Describe2()
        {
            return _mode + " -> " + _state;
        }

        void Attach()
        {
            try
            {
                _attached = TraceSixBridge.Attach(_cam);
                Plugin.Log.LogInfo("light match: TraceSix light pass attached to meter camera (path " + _cam.actualRenderingPath + ", hdr " + _cam.allowHDR +
                                   "); TraceSix warns in its own log if it cannot run on it");
            }
            catch (Exception e)
            {
                _state = LightMatchState.ApiTooOld;
                if (!_failLogged) { _failLogged = true; Plugin.Log.LogWarning("light match: attach failed, meter stays on EFT ambient: " + e); }
            }
        }

        void Detach()
        {
            try { if (_cam != null) TraceSixBridge.Detach(_cam); }
            catch (Exception) { }
            _attached = false;
        }
    }
}
