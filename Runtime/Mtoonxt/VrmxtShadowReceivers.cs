using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UniVRMXT.Mtoonxt
{
    [Serializable]
    public sealed class VrmxtShadowReceiver
    {
        public Material Receiver;
        public bool ReceiveOthers = true;
        public bool ReceiveSelf = true;
        public List<Material> ExcludedCasters = new List<Material>();
    }

    internal sealed class IndexedShadowReceiver
    {
        public int Receiver;
        public bool Others;
        public bool Self;
        public List<int> Exclusions = new List<int>();
    }

    public static class VrmxtShadowReceivers
    {
        private static int Index(JToken value, int count)
        {
            if (value?.Type != JTokenType.Integer || !int.TryParse(value.ToString(), out var index))
                return -1;
            return index >= 0 && index < count ? index : -1;
        }

        internal static List<IndexedShadowReceiver> Read(JObject extension, JArray materials, HashSet<int> references)
        {
            var result = new List<IndexedShadowReceiver>();
            if ((string)extension?["specVersion"] != "1.0" || !(extension["shadowReceivers"] is JArray rows))
                return result;
            var seen = new HashSet<int>();
            foreach (var token in rows)
            {
                if (!(token is JObject row)) continue;
                var index = Index(row["receiver"], materials.Count);
                if (index < 0 || seen.Contains(index) || !(materials[index]?["extensions"]?["VRMC_materials_mtoon"] is JObject))
                    continue;
                if ((row["receiveSelf"] != null && row["receiveSelf"].Type != JTokenType.Boolean)
                    || (row["receiveOthers"] != null && row["receiveOthers"].Type != JTokenType.Boolean)
                    || (row["excludedCasters"] != null && !(row["excludedCasters"] is JArray))) continue;
                var rule = new IndexedShadowReceiver {
                    Receiver = index, Self = (bool?)row["receiveSelf"] ?? true, Others = (bool?)row["receiveOthers"] ?? true
                };
                if (row["excludedCasters"] is JArray exclusions)
                    foreach (var item in exclusions)
                    {
                        var caster = Index(item, materials.Count);
                        if (caster < 0 || caster == index || rule.Exclusions.Contains(caster)) continue;
                        rule.Exclusions.Add(caster);
                        references.Add(caster);
                    }
                seen.Add(index);
                references.Add(index);
                result.Add(rule);
            }
            return result;
        }

        public static JArray Export(IEnumerable<VrmxtShadowReceiver> policies, Func<Material, int?> resolve)
        {
            var rows = new JArray();
            var seen = new HashSet<int>();
            foreach (var policy in policies)
            {
                if (policy == null || !policy.Receiver) continue;
                var receiver = resolve(policy.Receiver);
                if (!receiver.HasValue || receiver < 0 || !seen.Add(receiver.Value)) continue;
                var omitted = new SortedSet<int>();
                foreach (var material in policy.ExcludedCasters)
                {
                    if (!material) continue;
                    var caster = resolve(material);
                    if (caster.HasValue && caster.Value >= 0 && caster != receiver) omitted.Add(caster.Value);
                }
                if (policy.ReceiveOthers && policy.ReceiveSelf && omitted.Count == 0) continue;
                rows.Add(new JObject {
                    ["receiver"] = receiver.Value, ["receiveOthers"] = policy.ReceiveOthers,
                    ["receiveSelf"] = policy.ReceiveSelf, ["excludedCasters"] = new JArray(omitted)
                });
            }
            return rows;
        }
    }
}
