using UnityEngine;

namespace ombarella
{
    /// <summary>
    /// IMGUI overlay in its own component: Plugin enables it only in a raid with indicator/preview on, so the game
    /// pays no OnGUI cost otherwise. Draws on Repaint only.
    /// </summary>
    public sealed class MeterHud : MonoBehaviour
    {
        readonly GUIStyle _style = new GUIStyle();
        public System.Func<float> GetValue;
        public System.Func<string> GetDebug;
        public System.Func<RenderTexture> GetTexture;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            if (OmbarellaConfig.MeterViz.Value && GetValue != null)
            {
                _style.normal.textColor = Color.grey;
                _style.fontSize = 20;
                GUI.Label(new Rect(20f, 10f, 40f, 40f), Visualiser.GetLevelString(GetValue(), false), _style);
                if (OmbarellaConfig.IsDebug.Value && GetDebug != null)
                    GUI.Label(new Rect(20f, 50f, 40f, 40f), GetDebug(), _style);
            }
            DrawPreview();
        }

        void DrawPreview()
        {
            RenderTexture rt = GetTexture == null ? null : GetTexture();
            if (!OmbarellaConfig.ShowRenderTexturePreview.Value || rt == null) return;

            float size = OmbarellaConfig.RenderTexturePreviewSize.Value;
            Rect rect = new Rect(20f, 80f, size, size);
            Color prev = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = Color.white;
            GUI.DrawTexture(rect, rt, ScaleMode.ScaleToFit, false);
            GUI.color = prev;
        }
    }
}
