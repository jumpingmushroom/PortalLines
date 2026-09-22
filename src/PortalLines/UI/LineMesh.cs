using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PortalLines.UI
{
    /// <summary>Quad-strip helpers shared by the line and route graphics.</summary>
    internal static class LineMesh
    {
        /// <summary>Guard against pathological zoom: a 4000 px line at 14 px dashes is 170 quads.</summary>
        private const int MaxDashesPerLine = 512;

        private static readonly List<UIVertex> s_verts = new List<UIVertex>(4);

        public static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color colorA, Color colorB,
            float dash = 0f, float gap = 0f)
        {
            if (dash <= 0f)
            {
                AddQuad(vh, a, b, width, colorA, colorB);
                return;
            }

            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.001f)
                return;

            Vector2 dir = d / len;
            float period = dash + gap;
            int dashes = Mathf.Min(MaxDashesPerLine, Mathf.CeilToInt(len / period));
            float t = 0f;
            for (int i = 0; i < dashes && t < len; i++)
            {
                float end = Mathf.Min(len, t + dash);
                AddQuad(vh, a + dir * t, a + dir * end, width,
                    Color.Lerp(colorA, colorB, t / len), Color.Lerp(colorA, colorB, end / len));
                t += period;
            }
        }

        public static void AddQuad(VertexHelper vh, Vector2 p0, Vector2 p1, float width, Color c0, Color c1)
        {
            Vector2 d = p1 - p0;
            float len = d.magnitude;
            if (len < 0.001f)
                return;

            Vector2 n = new Vector2(-d.y, d.x) / len * (width * 0.5f);

            // Extend the ends by half the width so consecutive dashes and joins do not show gaps.
            Vector2 e = d / len * (width * 0.5f);
            p0 -= e;
            p1 += e;

            int start = vh.currentVertCount;
            s_verts.Clear();
            s_verts.Add(Vert(p0 - n, c0));
            s_verts.Add(Vert(p0 + n, c0));
            s_verts.Add(Vert(p1 + n, c1));
            s_verts.Add(Vert(p1 - n, c1));
            for (int i = 0; i < 4; i++)
                vh.AddVert(s_verts[i]);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }

        /// <summary>A filled disc, for route markers.</summary>
        public static void AddDisc(VertexHelper vh, Vector2 c, float radius, Color color, int segments = 20)
        {
            int start = vh.currentVertCount;
            vh.AddVert(Vert(c, color));
            for (int i = 0; i < segments; i++)
            {
                float ang = i * Mathf.PI * 2f / segments;
                vh.AddVert(Vert(c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius, color));
            }
            for (int i = 0; i < segments; i++)
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % segments);
        }

        /// <summary>A ring, for the destination marker.</summary>
        public static void AddRing(VertexHelper vh, Vector2 c, float radius, float thickness, Color color, int segments = 24)
        {
            int start = vh.currentVertCount;
            float inner = radius - thickness * 0.5f;
            float outer = radius + thickness * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float ang = i * Mathf.PI * 2f / segments;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                vh.AddVert(Vert(c + dir * inner, color));
                vh.AddVert(Vert(c + dir * outer, color));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2;
                int b = start + ((i + 1) % segments) * 2;
                vh.AddTriangle(a, a + 1, b + 1);
                vh.AddTriangle(b + 1, b, a);
            }
        }

        /// <summary>A triangle pointing along <paramref name="dir"/> with its tip at <paramref name="tip"/>.</summary>
        public static void AddArrowHead(VertexHelper vh, Vector2 tip, Vector2 dir, float length, float halfWidth, Color color)
        {
            Vector2 n = new Vector2(-dir.y, dir.x);
            Vector2 back = tip - dir * length;
            int start = vh.currentVertCount;
            vh.AddVert(Vert(tip, color));
            vh.AddVert(Vert(back + n * halfWidth, color));
            vh.AddVert(Vert(back - n * halfWidth, color));
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 1);
        }

        public static UIVertex Vert(Vector2 p, Color c)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            return v;
        }
    }
}
