using UnityEngine;

namespace EscapeRoom.Net
{
    /// <summary>Escala el IMGUI para que el menú se vea igual a cualquier resolución (referencia: 720 px de alto).</summary>
    public static class UiScale
    {
        public static float Begin(out float width, out float height)
        {
            float s = Mathf.Max(0.6f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            width = Screen.width / s;
            height = Screen.height / s;
            return s;
        }

        public static void End() => GUI.matrix = Matrix4x4.identity;
    }
}
