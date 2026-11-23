using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UniVRMXT.Mtoonxt
{
    // This component uses only Unity's built-in API so its source can travel in a Warudo mod.
    [DisallowMultipleComponent]
    public sealed class VrmxtShadowRuntime : MonoBehaviour
    {
        [Serializable]
        public class Policy
        {
            public Material receiver;
            public bool others = true;
            public bool self = true;
            public Material[] excluded = new Material[0];

            public bool Accepts(Material caster)
            {
                return caster == receiver ? self : others && Array.IndexOf(excluded, caster) < 0;
            }
            public bool SameCasters(Policy other)
            {
                if (others != other.others || Accepts(receiver) != other.Accepts(receiver)
                    || Accepts(other.receiver) != other.Accepts(other.receiver)) return false;
                foreach (var m in excluded) if (Accepts(m) != other.Accepts(m)) return false;
                foreach (var m in other.excluded) if (Accepts(m) != other.Accepts(m)) return false;
                return true;
            }

        }

        [Serializable]
        public sealed class MaterialSlots
        {
            public Renderer renderer;
            public Material[] authored;
        }

        public Policy[] policies = new Policy[0];
        public MaterialSlots[] materialSlots = new MaterialSlots[0];
        static readonly Dictionary<Renderer, int> sourceLayers = new Dictionary<Renderer, int>();
        static readonly List<Material> slotScratch = new List<Material>();
        static readonly Dictionary<Renderer, Material[]> liveSlots = new Dictionary<Renderer, Material[]>();

        public void CaptureSlots()
        {
            var bindings = new List<MaterialSlots>();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                bindings.Add(new MaterialSlots { renderer = renderer, authored = renderer.sharedMaterials });
            materialSlots = bindings.ToArray();
        }

        static bool SlotsChanged()
        {
            foreach (var host in hosts)
                foreach (var entry in host.materialSlots)
                {
                    if (!entry.renderer || !liveSlots.TryGetValue(entry.renderer, out var previous)) continue;
                    if (sourceLayers[entry.renderer] != entry.renderer.gameObject.layer) return true;
                    entry.renderer.GetSharedMaterials(slotScratch);
                    if (previous.Length != slotScratch.Count) return true;
                    for (int i = 0; i < previous.Length; ++i)
                        if (slotScratch[i] != previous[i]) return true;
                }
            return false;
        }
        static readonly List<VrmxtShadowRuntime> hosts = new List<VrmxtShadowRuntime>();
        static readonly Dictionary<Camera, int> cameraMasks = new Dictionary<Camera, int>();
        static bool rebuild;
        static float retryAt;
        static Light sun;
        static int originalMask;
        static int reservedMask;

        sealed class Group
        {
            public Policy rule;
            public int layer;
            public int originalLayers;
            public Light light;
        }

        struct Copy
        {
            public Renderer source, destination;
            public int layer;
            public SkinnedMeshRenderer skin, clonedSkin;
        }

        static readonly List<Group> groups = new List<Group>();
        static readonly HashSet<GameObject> retiring = new HashSet<GameObject>();
        static readonly List<GameObject> objects = new List<GameObject>();
        static readonly List<Mesh> meshes = new List<Mesh>();
        static readonly List<Renderer> suppressed = new List<Renderer>();
        static readonly List<Copy> copies = new List<Copy>();
        static readonly Dictionary<(Mesh, int), Mesh> sections = new Dictionary<(Mesh, int), Mesh>();

        void OnEnable()
        {
            hosts.Add(this);
            if (hosts.Count == 1)
            {
                SceneManager.sceneLoaded += SceneLoaded;
                Camera.onPreCull += UpdateCameraMask;
            }
            Refresh();
        }

        public void Refresh() { rebuild = true; retryAt = 0; }
        static void SceneLoaded(Scene scene, LoadSceneMode mode) { rebuild = true; }

        void OnDisable()
        {
            hosts.Remove(this);
            Restore();
            rebuild = true;
            if (hosts.Count == 0)
            {
                SceneManager.sceneLoaded -= SceneLoaded;
                Camera.onPreCull -= UpdateCameraMask;
            }
        }

        static void Restore()
        {
            if (sun) sun.cullingMask = (sun.cullingMask & ~reservedMask) | (originalMask & reservedMask);
            foreach (var source in suppressed) if (source) source.forceRenderingOff = false;
            foreach (var entry in cameraMasks)
                if (entry.Key)
                    entry.Key.cullingMask = (entry.Key.cullingMask & ~reservedMask) | entry.Value;
            foreach (var go in objects) if (go) { go.SetActive(false); retiring.Add(go); Destroy(go); }
            foreach (var mesh in meshes) if (mesh) Destroy(mesh);
            groups.Clear(); objects.Clear(); meshes.Clear(); sections.Clear(); copies.Clear();
            sourceLayers.Clear(); liveSlots.Clear(); suppressed.Clear(); cameraMasks.Clear(); reservedMask = 0; sun = null;
        }

        void LateUpdate()
        {
            if (hosts.Count == 0 || hosts[0] != this) return;
            ApplyPending();
        }

        public static void ApplyPending()
        {
            if (rebuild || SlotsChanged() || (groups.Count > 0 && (!sun || !sun.isActiveAndEnabled ||
                (RenderSettings.sun && RenderSettings.sun != sun))))
            {
                if (Time.unscaledTime < retryAt) return;
                Restore();
                rebuild = false;
                try { Build(); }
                catch (Exception e) { Restore(); Debug.LogWarning("MToonXT shadow receivers: " + e.Message); }
            }
            if (!sun) return;
            foreach (var group in groups)
            {
                var lamp = group.light;
                if (!lamp) continue;
                if (lamp.transform.rotation != sun.transform.rotation) lamp.transform.rotation = sun.transform.rotation;
                if (lamp.color != sun.color) lamp.color = sun.color;
                if (lamp.intensity != sun.intensity) lamp.intensity = sun.intensity;
                if (lamp.shadows != sun.shadows) lamp.shadows = sun.shadows;
                if (lamp.shadowStrength != sun.shadowStrength) lamp.shadowStrength = sun.shadowStrength;
                if (lamp.shadowBias != sun.shadowBias) lamp.shadowBias = sun.shadowBias;
                if (lamp.shadowNormalBias != sun.shadowNormalBias) lamp.shadowNormalBias = sun.shadowNormalBias;
                if (lamp.shadowNearPlane != sun.shadowNearPlane) lamp.shadowNearPlane = sun.shadowNearPlane;
                if (lamp.useColorTemperature != sun.useColorTemperature) lamp.useColorTemperature = sun.useColorTemperature;
                if (lamp.colorTemperature != sun.colorTemperature) lamp.colorTemperature = sun.colorTemperature;
                if (lamp.cookie != sun.cookie) lamp.cookie = sun.cookie;
                if (lamp.cookieSize != sun.cookieSize) lamp.cookieSize = sun.cookieSize;
                if (lamp.shadowResolution != sun.shadowResolution) lamp.shadowResolution = sun.shadowResolution;
                if (lamp.shadowCustomResolution != sun.shadowCustomResolution) lamp.shadowCustomResolution = sun.shadowCustomResolution;
                if (lamp.renderMode != sun.renderMode) lamp.renderMode = sun.renderMode;
            }
            foreach (var c in copies)
            {
                if (!c.source || !c.destination) { rebuild = true; continue; }
                if (c.destination.gameObject.layer != c.layer) c.destination.gameObject.layer = c.layer;
                if (c.destination.enabled != c.source.enabled) c.destination.enabled = c.source.enabled;
                if (!c.skin || !c.clonedSkin || !c.source.enabled) continue;
                int n = c.skin.sharedMesh ? c.skin.sharedMesh.blendShapeCount : 0;
                if (n != c.clonedSkin.sharedMesh.blendShapeCount) { rebuild = true; continue; }
                for (int i = 0; i < n; ++i)
                {
                    float value = c.skin.GetBlendShapeWeight(i);
                    if (value != c.clonedSkin.GetBlendShapeWeight(i)) c.clonedSkin.SetBlendShapeWeight(i, value);
                }
            }
        }

        static Light MainLight()
        {
            var key = RenderSettings.sun;
            if (key && key.isActiveAndEnabled && key.type == LightType.Directional) return key;
            Light best = null;
            foreach (var l in FindObjectsOfType<Light>())
                if (!retiring.Contains(l.gameObject) && l.isActiveAndEnabled && l.type == LightType.Directional && l.shadows != LightShadows.None &&
                    (!best || l.intensity > best.intensity)) best = l;
            return best;
        }

        static void Build()
        {
            retiring.RemoveWhere(go => !go);
            var originals = new Dictionary<(Renderer, int), Material>();
            foreach (var host in hosts)
                foreach (var entry in host.materialSlots)
                {
                    if (!entry.renderer || entry.authored == null) continue;
                    var slots = entry.renderer.sharedMaterials;
                    liveSlots[entry.renderer] = slots;
                    sourceLayers[entry.renderer] = entry.renderer.gameObject.layer;
                    for (int i = 0; i < entry.authored.Length && i < slots.Length; ++i)
                        if (entry.authored[i]) originals[(entry.renderer, i)] = entry.authored[i];
                }
            var policies = new Dictionary<Material, Policy>();
            foreach (var host in hosts)
                foreach (var rule in host.policies)
                    if (rule != null && rule.receiver && (!rule.self || !rule.others || rule.excluded.Length != 0))
                        policies[rule.receiver] = rule;
            if (policies.Count == 0) return;
            sun = MainLight();
            if (!sun) { rebuild = true; retryAt = Time.unscaledTime + 1; return; }
            originalMask = sun.cullingMask;
            int occupied = 0;
            // Other embedded runtimes may already own transient layers, even if their objects are hidden from FindObjectsOfType.
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.gameObject.scene.IsValid() && !retiring.Contains(t.gameObject))
                    occupied |= 1 << t.gameObject.layer;
            var renderers = FindObjectsOfType<Renderer>(true);
            var receivers = new Dictionary<(Material, int), Group>();
            foreach (var r in renderers)
            {
                if (retiring.Contains(r.gameObject)) continue;
                int bit = 1 << r.gameObject.layer;
                if ((originalMask & bit) == 0 || r.forceRenderingOff) continue;
                var activeSlots = r.sharedMaterials;
                for (int slot = 0; slot < activeSlots.Length; ++slot)
                {
                    if (!activeSlots[slot]) continue;
                    var mat = originals.TryGetValue((r, slot), out var authored) ? authored : activeSlots[slot];
                    if (!mat || !policies.TryGetValue(mat, out var rule)) continue;
                    if (!receivers.TryGetValue((mat, r.gameObject.layer), out var group))
                    {
                        group = groups.Find(g => g.originalLayers == bit && g.rule.SameCasters(rule));
                        if (group == null)
                        {
                            int layer = 31;
                            while (layer >= 8 && (((occupied | reservedMask) & (1 << layer)) != 0 ||
                                !String.IsNullOrEmpty(LayerMask.LayerToName(layer)))) --layer;
                            if (layer < 8) throw new InvalidOperationException("Insufficient unused unnamed Unity layers for these receivers.");
                            group = new Group { rule = rule, layer = layer, originalLayers = bit };
                            groups.Add(group); reservedMask |= 1 << layer;
                        }
                        receivers.Add((mat, r.gameObject.layer), group);
                    }
                }
            }
            if (groups.Count == 0) { sun = null; return; }
            foreach (var group in groups)
            {
                var go = Own("MToonXT shadow light", null, group.layer);
                var light = group.light = go.AddComponent<Light>();
                light.type = LightType.Directional; light.renderMode = sun.renderMode;
                light.cullingMask = 1 << group.layer;
                light.transform.rotation = sun.transform.rotation;
            }
            foreach (var camera in FindObjectsOfType<Camera>(true)) UpdateCameraMask(camera);
            sun.cullingMask &= ~reservedMask;
            foreach (var source in renderers)
            {
                if (retiring.Contains(source.gameObject) || source.forceRenderingOff || (originalMask & (1 << source.gameObject.layer)) == 0) continue;
                var skin = source as SkinnedMeshRenderer;
                var filter = source.GetComponent<MeshFilter>();
                var mesh = skin ? skin.sharedMesh : source is MeshRenderer && filter ? filter.sharedMesh : null;
                if (!mesh) continue;
                var mats = source.sharedMaterials;
                bool split = false;
                for (int i = 0; i < mats.Length && !split; ++i)
                {
                    var key = originals.TryGetValue((source, i), out var original) ? original : mats[i];
                    split = key && receivers.ContainsKey((key, source.gameObject.layer));
                }
                if (!split && source.shadowCastingMode == ShadowCastingMode.Off) continue;
                for (int i = 0; i < mats.Length && mesh.subMeshCount > 0; ++i)
                {
                    var material = mats[i]; if (!material) continue;
                    int index = Mathf.Min(i, mesh.subMeshCount - 1);
                    var canonical = originals.TryGetValue((source, i), out var stored) ? stored : material;
                    bool receiver = receivers.TryGetValue((canonical, source.gameObject.layer), out var group);
                    if (split)
                    {
                        if (!receiver || source.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                            Duplicate(source, mesh, index, material, receiver ? group.layer : source.gameObject.layer,
                                receiver ? ShadowCastingMode.Off : source.shadowCastingMode);
                        if (receiver && source.shadowCastingMode != ShadowCastingMode.Off)
                            Duplicate(source, mesh, index, material, source.gameObject.layer, ShadowCastingMode.ShadowsOnly);
                    }
                    if (source.shadowCastingMode == ShadowCastingMode.Off) continue;
                    foreach (var g in groups)
                        if (g.rule.Accepts(canonical)) Duplicate(source, mesh, index, material, g.layer, ShadowCastingMode.ShadowsOnly);
                }
                if (split) { suppressed.Add(source); source.forceRenderingOff = true; }
            }
        }

        static void UpdateCameraMask(Camera camera)
        {
            if (!camera || reservedMask == 0) return;
            int original = camera.cullingMask;
            int mask = original & ~reservedMask;
            foreach (var group in groups)
                if ((original & group.originalLayers) != 0) mask |= 1 << group.layer;
            if (!cameraMasks.ContainsKey(camera)) cameraMasks.Add(camera, original & reservedMask);
            if (original != mask) camera.cullingMask = mask;
        }

        static GameObject Own(string name, Transform parent, int layer)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = layer };
            go.transform.SetParent(parent, false); objects.Add(go); return go;
        }

        static void Duplicate(Renderer source, Mesh mesh, int index, Material material, int layer, ShadowCastingMode mode)
        {
            if (mesh.subMeshCount > 1)
            {
                if (!sections.TryGetValue((mesh, index), out var section))
                {
                    var desc = mesh.GetSubMesh(index);
                    section = Instantiate(mesh); meshes.Add(section);
                    section.SetSubMeshes(new[] { desc }, MeshUpdateFlags.DontRecalculateBounds);
                    sections.Add((mesh, index), section);
                }
                mesh = section;
            }
            var go = Own("MToonXT " + material.name, source.transform, layer);
            Renderer dest; var skin = source as SkinnedMeshRenderer;
            SkinnedMeshRenderer clonedSkin = null;
            if (skin)
            {
                clonedSkin = go.AddComponent<SkinnedMeshRenderer>();
                clonedSkin.sharedMesh = mesh; clonedSkin.bones = skin.bones;
                clonedSkin.rootBone = skin.rootBone; clonedSkin.localBounds = skin.localBounds;
                clonedSkin.quality = skin.quality; clonedSkin.updateWhenOffscreen = skin.updateWhenOffscreen;
                dest = clonedSkin;
            }
            else { go.AddComponent<MeshFilter>().sharedMesh = mesh; dest = go.AddComponent<MeshRenderer>(); }
            dest.sharedMaterial = material; dest.enabled = source.enabled;
            dest.receiveShadows = source.receiveShadows; dest.shadowCastingMode = mode;
            dest.lightProbeUsage = source.lightProbeUsage; dest.probeAnchor = source.probeAnchor;
            dest.reflectionProbeUsage = source.reflectionProbeUsage;
            dest.sortingLayerID = source.sortingLayerID; dest.sortingOrder = source.sortingOrder;
            dest.lightmapIndex = source.lightmapIndex; dest.lightmapScaleOffset = source.lightmapScaleOffset;
            var block = new MaterialPropertyBlock();
            source.GetPropertyBlock(block, index); if (block.isEmpty) source.GetPropertyBlock(block);
            if (!block.isEmpty) dest.SetPropertyBlock(block);
            copies.Add(new Copy { layer=layer, source=source, destination=dest, skin=skin, clonedSkin=clonedSkin });
        }
    }
}
