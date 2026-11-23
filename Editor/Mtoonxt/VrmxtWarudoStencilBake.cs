using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UniVRMXT.Mtoonxt.Editor
{
    // Serialize the native MToonXT passes before the SDK reimports the VRM with its own shaders.
    // Reuse the live relationship compiler's submesh selection, draw order, depths and stencil refs.
    internal static class VrmxtWarudoStencilBake
    {
        sealed class Surface
        {
            public Renderer renderer;
            public Mesh mesh;
            public Material[] materials;
            public List<int> overlays = new List<int>();
        }

        static T Save<T>(T copy, string path) where T : Object
        {
            copy.hideFlags = HideFlags.None;
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!existing) { AssetDatabase.CreateAsset(copy, path); return copy; }
            EditorUtility.CopySerialized(copy, existing);
            Object.DestroyImmediate(copy);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Mesh MeshOf(Renderer r) => r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;

        static bool UsesOutline(Material material) => material && material.HasProperty("_VRMXTGeometricOutline")
            && material.GetFloat("_OutlineWidthMode") != 0 && material.GetFloat("_OutlineWidth") > 0;

        internal static void Prepare(string prefabPath, VrmxtShadowRuntime original, bool geometricOutlines)
        {
            var directory = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            var assetFolder = directory + "/VRMXT-" + AssetDatabase.AssetPathToGUID(prefabPath);
            var store = original.GetComponent<VrmxtMaterialsMtoonxtInstance>();
            var preview = original.GetComponent<VrmxtMaterialsMtoonxtAuxiliaryRenderer>();
            bool alreadyBaked = Array.Exists(original.materialSlots, entry => entry.renderer &&
                Array.Exists(entry.renderer.sharedMaterials, m => m && AssetDatabase.GetAssetPath(m).StartsWith(assetFolder + "/", StringComparison.Ordinal)));
            if (alreadyBaked) return;
            if (preview) preview.RestoreNativeMaterials();
            original.CaptureSlots();
            if (!store || store.StencilRelationships.Count == 0) return;
            VrmxtMaterialsMtoonxtApplier.ReapplyRelationships(original.gameObject, store);
            preview = original.GetComponent<VrmxtMaterialsMtoonxtAuxiliaryRenderer>();
            if (original.GetComponent<VrmxtStencilGraphRenderer>() is VrmxtStencilGraphRenderer graph && graph.enabled)
                throw new InvalidOperationException("The character uses camera coverage stencil passes that cannot be baked into renderer slots.");
            if (!AssetDatabase.IsValidFolder(assetFolder)) AssetDatabase.CreateFolder(directory, Path.GetFileName(assetFolder));
            var surfaces = new List<Surface>();
            var materials = new Dictionary<Material, Material>();
            foreach (var entry in original.materialSlots)
            {
                var r = entry.renderer;
                if (!r) continue;
                var slots = r.sharedMaterials;
                var surface = new Surface { renderer = r, materials = slots };
                bool changed = false;
                for (int i = 0; i < slots.Length; ++i)
                {
                    var m = slots[i];
                    if (!m || !m.shader || !m.shader.name.StartsWith("VRMXT/", StringComparison.Ordinal)) continue;
                    if (!materials.TryGetValue(m, out var baked))
                    {
                        // Unity uses the asset filename as the material name inside the built mod.
                        var passFolder = "pass-" + materials.Count;
                        if (!AssetDatabase.IsValidFolder(assetFolder + "/" + passFolder))
                            AssetDatabase.CreateFolder(assetFolder, passFolder);
                        var filename = string.Join("_", m.name.Split(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }));
                        if (string.IsNullOrWhiteSpace(filename)) filename = "Material";
                        baked = Save(new Material(m) { name = m.name }, assetFolder + "/" + passFolder + "/" + filename + ".mat");
                        materials.Add(m, baked);
                    }
                    slots[i] = baked; changed = true;
                }
                if (!changed) continue;
                var mesh = MeshOf(r);
                bool needsDirections = geometricOutlines && Array.Exists(slots, UsesOutline);
                if (mesh && (needsDirections || !EditorUtility.IsPersistent(mesh)))
                {
                    var copy = Object.Instantiate(mesh);
                    if (needsDirections)
                    {
                        VrmxtOutlineDirections.Bake(copy, entry.authored.Length);
                        foreach (var mat in slots)
                            if (UsesOutline(mat)) mat.SetFloat("_VRMXTGeometricOutline", 1);
                    }
                    surface.mesh = Save(copy, assetFolder + "/mesh-" + surfaces.Count + ".asset");
                }
                if (preview)
                    foreach (var draw in preview.Draws)
                    {
                        if (!draw.AfterOpaque) throw new InvalidOperationException("The character uses a stencil camera prepass that cannot be baked into renderer slots.");
                        if (draw.Renderer == r) surface.overlays.Add(draw.Submesh);
                    }
                if (surface.overlays.Count != 0)
                {
                    var canonical = new List<Material>(entry.authored);
                    foreach (int index in surface.overlays) canonical.Add(entry.authored[index]);
                    entry.authored = canonical.ToArray();
                }
                surfaces.Add(surface);
            }
            // Keep the portable relationship graph for re-export. OnEnable would otherwise
            // append its auxiliary slots again to the already baked prefab after loading.
            store.enabled = false;
            if (preview) preview.PrepareExportCopy();
            foreach (var surface in surfaces)
            {
                if (surface.mesh)
                {
                    if (surface.renderer is SkinnedMeshRenderer sk) sk.sharedMesh = surface.mesh;
                    else surface.renderer.GetComponent<MeshFilter>().sharedMesh = surface.mesh;
                }
                surface.renderer.sharedMaterials = surface.materials;
            }
        }
    }
}
