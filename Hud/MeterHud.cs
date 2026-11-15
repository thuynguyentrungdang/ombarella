using UnityEngine;

namespace ombarella
{
    public enum HudAnchor { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>
    /// IMGUI overlay in its own component: Plugin enables it only in a raid with indicator/preview/verbose on, so the game
    /// pays no OnGUI cost otherwise. Draws on Repaint only. Blocks (meter, debug line, verbose text, preview) are stacked
    /// and the whole stack is anchored to a screen corner with a pixel offset.
    /// </summary>
    public sealed class MeterHud : MonoBehaviour
    {
        const float MeterWidth = 340f;
        const float VerboseWidth = 620f;

        GUIStyle _style;
        public System.Func<bool> GetActive;
        public System.Func<float> GetValue;
        public System.Func<string> GetDebug;
        public System.Func<string> GetVerbose;
        public System.Func<Texture> GetTexture;
        public System.Func<float> GetPreviewGain;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (GetActive == null || !GetActive()) return; // raid only: no meter, debug text or preview in menus/hideout

            bool meter = OmbarellaConfig.MeterViz.Value && GetValue != null;
            bool debugLine = meter && OmbarellaConfig.IsDebug.Value && GetDebug != null;
            bool verbose = OmbarellaConfig.VerboseDebug.Value && GetVerbose != null;
            Texture rt = OmbarellaConfig.ShowRenderTexturePreview.Value && GetTexture != null ? GetTexture() : null;
            if (!meter && !verbose && rt == null) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { wordWrap = false, richText = false };
                _style.normal.textColor = Color.white;
            }
            int font = Mathf.RoundToInt(OmbarellaConfig.MeterFontSize.Value);
            if (_style.fontSize != font) _style.fontSize = font;

            string meterText = meter ? Visualiser.GetLevelString(GetValue(), false) : null;
            string debugText = debugLine ? GetDebug() : null;
            string verboseText = verbose ? GetVerbose() : null;
            float previewSize = rt != null ? OmbarellaConfig.RenderTexturePreviewSize.Value : 0f;

            float meterH = meter ? _style.CalcHeight(new GUIContent(meterText), MeterWidth) : 0f;
            float debugH = debugLine ? _style.CalcHeight(new GUIContent(debugText), VerboseWidth) : 0f;
            float verboseH = verbose ? _style.CalcHeight(new GUIContent(verboseText), VerboseWidth) : 0f;
            float previewH = rt != null ? previewSize + 4f : 0f;

            float width = Mathf.Max(meter ? MeterWidth : 0f, debugLine || verbose ? VerboseWidth : 0f, previewSize + 4f);
            float height = meterH + debugH + verboseH + previewH;

            HudAnchor a = OmbarellaConfig.MeterAnchor.Value;
            float offX = OmbarellaConfig.MeterOffsetX.Value, offY = OmbarellaConfig.MeterOffsetY.Value;
            float x = (a == HudAnchor.TopRight || a == HudAnchor.BottomRight) ? Screen.width - width - offX : offX;
            float y = (a == HudAnchor.BottomLeft || a == HudAnchor.BottomRight) ? Screen.height - height - offY : offY;

            if (meter) { GUI.Label(new Rect(x, y, MeterWidth, meterH), meterText, _style); y += meterH; }
            if (debugLine) { GUI.Label(new Rect(x, y, VerboseWidth, debugH), debugText, _style); y += debugH; }
            if (verbose) { GUI.Label(new Rect(x, y, VerboseWidth, verboseH), verboseText, _style); y += verboseH; }
            if (rt != null)
            {
                var r = new Rect(x, y + 2f, previewSize, previewSize);
                DrawPreview(r, rt);
                float ev = GetPreviewGain != null ? GetPreviewGain() : 0f;
                GUI.Label(new Rect(r.x + 4f, r.y + 2f, 160f, 22f), "post-exposure " + ev.ToString("+0.#;-0.#;0") + " EV", _style);
            }
        }

        static void DrawPreview(Rect rect, Texture rt)
        {
            Color prev = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = Color.white;
            GUI.DrawTexture(rect, rt, ScaleMode.ScaleToFit, false);
            GUI.color = prev;
        }
    }
}
