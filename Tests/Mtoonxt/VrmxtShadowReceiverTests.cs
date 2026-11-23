using NUnit.Framework;
using UnityEngine;
using UniVRMXT.Mtoonxt;

namespace UniVRMXT.Tests
{
    public sealed class VrmxtShadowReceiverTests
    {
        [Test]
        public void DepthIndependentFaceFillFollowsBothOutlinePasses()
        {
            var material = new Material(Shader.Find("VRMXT/MToonXT10"));
            try {
                foreach (var outline in new[] { "FORWARD_BASE_OUTLINE", "FORWARD_BASE_OUTLINE_OVERLAY" })
                {
                    int pass = material.FindPass(outline);
                    Assert.That(pass, Is.GreaterThanOrEqualTo(0));
                    Assert.That(material.FindPass("FORWARD_BASE"), Is.GreaterThan(pass));
                    Assert.That(material.FindPass("FORWARD_BASE_OVERLAY"), Is.GreaterThan(pass));
                }
            } finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void ReciprocalFaceExclusionsShareAGroupAndKeepHair()
        {
            var shader = Shader.Find("Standard");
            var left = new Material(shader);
            var right = new Material(shader);
            var hair = new Material(shader);
            try {
                var a = new VrmxtShadowRuntime.Policy { receiver = left, self = false, excluded = new[]{right} };
                var b = new VrmxtShadowRuntime.Policy { receiver = right, self = false, excluded = new[]{left} };
                Assert.IsTrue(a.SameCasters(b));
                Assert.IsFalse(a.Accepts(left));
                Assert.IsFalse(a.Accepts(right));
                Assert.IsTrue(a.Accepts(hair));
                b.self = true;
                Assert.IsFalse(a.SameCasters(b));
                Assert.IsTrue(b.Accepts(right));
            } finally {
                Object.DestroyImmediate(hair); Object.DestroyImmediate(right); Object.DestroyImmediate(left);
            }
        }
    }
}
