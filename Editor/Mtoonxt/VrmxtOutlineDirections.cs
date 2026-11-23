using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UniVRMXT.Mtoonxt.Editor
{
    internal static class VrmxtOutlineDirections
    {
        // Only the generated outline pass reads this stream. Its tangent basis follows skinning.
        // Ignore repeated stencil submeshes when computing the angle-weighted geometric normal.
        internal static void Bake(Mesh mesh, int sourceSlots)
        {
            if (mesh.HasVertexAttribute(VertexAttribute.TexCoord7))
                throw new InvalidOperationException(mesh.name + " already uses UV8. Preserve that channel by disabling geometric outline preparation.");
            var positions = mesh.vertices;
            var surface = mesh.normals;
            if (surface.Length != positions.Length)
                throw new InvalidOperationException(mesh.name + " has no shading normals for geometric outline preparation.");
            var tangents = mesh.tangents;
            if (tangents.Length != positions.Length) { mesh.RecalculateTangents(); tangents = mesh.tangents; }
            if (tangents.Length != positions.Length)
                throw new InvalidOperationException(mesh.name + " has no tangent basis for geometric outline preparation.");
            var directions = new Vector3[positions.Length];
            for (int i = 0; i < Math.Min(sourceSlots, mesh.subMeshCount); ++i)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles) continue;
                var triangle = mesh.GetTriangles(i);
                for (int j = 0; j < triangle.Length; j += 3)
                {
                    int a = triangle[j], b = triangle[j+1], c = triangle[j+2];
                    var ab = positions[b] - positions[a];
                    var ac = positions[c] - positions[a];
                    var bc = positions[c] - positions[b];
                    var cross = Vector3.Cross(ab, ac);
                    float area = cross.magnitude;
                    if (area <= 1e-15f) continue;
                    var normal = cross / area;
                    directions[a] += normal * Mathf.Atan2(area, Vector3.Dot(ab, ac));
                    directions[b] += normal * Mathf.Atan2(area, Vector3.Dot(-ab, bc));
                    directions[c] += normal * Mathf.Atan2(area, Vector3.Dot(-ac, -bc));
                }
            }
            var stream = new List<Vector3>(positions.Length);
            for (int i = 0; i < positions.Length; ++i)
            {
                var n = surface[i].normalized;
                var t = Vector3.ProjectOnPlane((Vector3)tangents[i], n).normalized;
                var g = directions[i].normalized;
                stream.Add(t.sqrMagnitude > .5f && g.sqrMagnitude > .5f
                    ? new Vector3(Vector3.Dot(g,t), Vector3.Dot(g,Vector3.Cross(n,t)), Vector3.Dot(g,n))
                    : Vector3.forward);
            }
            mesh.SetUVs(7, stream);
        }
    }
}
