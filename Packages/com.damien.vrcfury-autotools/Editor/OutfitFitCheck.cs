using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCFuryAutoTools {
    /// <summary>
    /// Self-check for the outfit fitting math on a made-up leg with a bulge on it. Throws on failure.
    /// Unity.exe -batchmode -projectPath &lt;project&gt; -executeMethod VRCFuryAutoTools.OutfitFitCheck.Run -quit
    /// </summary>
    public static class OutfitFitCheck {
        private const float Gap = 0.002f, BodyRadius = 0.06f, BulgeRadius = 0.04f;
        // a breast-like ball beside the hips, away from the thigh
        private static readonly Vector3 BulgeCenter = new Vector3(0.3f, 1, 0);

        public static void Run() {
            Check(new[] { "Upper_Leg.L", "UpperLeg_L", "Left UpperLeg", "leftupperleg" }.All(n => OutfitBonePlan.Canon(n) == "upperleg#l"), "bone names normalise");
            Check(OutfitBonePlan.Canon("Bright") == "bright", "words ending in 'right' keep their name");

            // avatar thigh is 0.4 long inside a body tube of radius 0.06
            var avatar = new GameObject("Avatar").transform;
            try {
                var hips = Bone("Hips", avatar, new Vector3(0, 1, 0));
                var neck = Bone("Neck", hips, new Vector3(0, 1.5f, 0));
                var leg = Bone("UpperLeg_L", hips, new Vector3(0.1f, 0.9f, 0));
                var knee = Bone("LowerLeg_L", leg, new Vector3(0.1f, 0.5f, 0));
                var body = Skinned("Body", avatar, Tube(BodyRadius, 0.95f, 0.45f, 0), new[] { hips });
                var bulge = Skinned("Bulge", avatar, Sphere(BulgeCenter, BulgeRadius), new[] { hips });

                // outfit made for a shorter (0.3) and thinner (0.05) thigh, so it clips into this one
                var outfit = new GameObject("Outfit").transform;
                outfit.SetParent(avatar);
                var oHips = Bone("Hips", outfit, new Vector3(0, 1, 0));
                Bone("Neck", oHips, new Vector3(0, 1.5f, 0));
                var oLeg = Bone("Upper_Leg.L", oHips, new Vector3(0.1f, 0.92f, 0));
                var oKnee = Bone("Lower_Leg.L", oLeg, new Vector3(0.1f, 0.62f, 0));
                var pants = Skinned("Pants", outfit, Tube(0.05f, 0.92f, 0.62f, 2), new[] { oLeg, oKnee });
                var loose = Skinned("Loose", outfit, Tube(0.09f, 0.88f, 0.7f, 0), new[] { oLeg, oKnee });
                // flat shirt front 1 cm behind the middle of the ball, so its nearest way out is backwards
                var shirt = Skinned("Shirt", outfit, Sheet(BulgeCenter - new Vector3(0.01f, 0, 0), 0.06f, 13), new[] { oHips });

                var plan = OutfitBonePlan.Create(outfit, hips, neck, 1);
                Check(plan.outfitHips == oHips && plan.match.TryGetValue(oLeg, out var m) && m == leg && plan.match[oKnee] == knee, "outfit bones matched");

                var surface = BodySurface.Create(new[] { body, bulge });
                var fit = OutfitFitMesh.Create(pants, plan, surface);
                fit.Solve(Gap, 0.01f, 8, true, null, null, null);
                // the tube is a 16-sided prism, so its flat faces sit at r * cos(pi / 16)
                var minRadius = BodyRadius * Mathf.Cos(Mathf.PI / 16) + Gap - 0.0005f;
                Check(fit.result.All(p => Radial(p) >= minRadius), "clipping pushed out");
                var hem = pants.sharedMesh.vertices.Select((v, i) => (v, i)).Where(x => x.v.y < 0.63f).Select(x => fit.result[x.i]).ToList();
                Check(hem.Count > 0 && hem.All(p => Mathf.Abs(p.y - 0.5f) < 0.005f), "hem follows the longer thigh to the knee");

                var sf = OutfitFitMesh.Create(shirt, plan, surface);
                sf.Solve(Gap, 0.01f, 8, true, null, null, null);
                // the ball is a 16 x 8 UV sphere, so its faces sit a bit inside r
                Check(sf.result.All(p => (p - BulgeCenter).magnitude >= BulgeRadius * 0.95f), "shirt pushed out of the ball");
                Check(sf.result[6 * 13 + 6].x >= BulgeCenter.x + BulgeRadius * 0.95f, "shirt goes over the front of the ball, not behind it");

                var lf = OutfitFitMesh.Create(loose, plan, surface);
                lf.Solve(Gap, 0.01f, 8, true, null, null, null);
                Check(lf.result.All(p => Radial(p) > 0.085f), "loose parts keep their shape");
                var n = loose.sharedMesh.vertexCount;
                lf.Solve(Gap, 0.01f, 8, true, null, null, Enumerable.Repeat(1f, n).ToArray());
                Check(lf.result.All(p => Radial(p) < 0.065f), "painted tight pulls onto the body");
                lf.Solve(Gap, 0.01f, 8, true, Enumerable.Repeat(0.01f, n).ToArray(), null, Enumerable.Repeat(1f, n).ToArray());
                Check(lf.result.All(p => Radial(p) > 0.068f && Radial(p) < 0.075f), "painted push adds its offset");

                // build path: bones move, mesh is rewritten, and skinning it must give the fitted positions
                plan.MoveBones();
                fit.Write();
                Check((oLeg.position - leg.position).sqrMagnitude < 1e-10f && (oKnee.position - knee.position).sqrMagnitude < 1e-10f, "outfit bones moved onto the avatar's");
                var baked = new Mesh();
                pants.BakeMesh(baked, true);
                var world = baked.vertices.Select(v => pants.transform.position + pants.transform.rotation * v).ToArray();
                Object.DestroyImmediate(baked);
                Check(world.Select((p, i) => (p - fit.result[i]).magnitude).Max() < 1e-4f, "written mesh renders the fitted shape");
            } finally {
                Object.DestroyImmediate(avatar.gameObject);
            }
            Debug.Log("[VRCFuryAutoTools] OutfitFitCheck passed");
        }

        private static float Radial(Vector3 p) => new Vector2(p.x - 0.1f, p.z).magnitude;

        private static void Check(bool ok, string what) {
            if (!ok) throw new Exception("OutfitFitCheck failed: " + what);
        }

        private static Transform Bone(string name, Transform parent, Vector3 position) {
            var t = new GameObject(name).transform;
            t.SetParent(parent);
            t.position = position;
            return t;
        }

        /// Open tube around x = 0.1, z = 0. Ring weights: 0 = all first bone, 1 = all second bone, 2 = last ring on the second bone.
        private static Mesh Tube(float r, float top, float bottom, int weights) {
            const int sides = 16, rings = 9;
            var verts = new Vector3[sides * rings];
            var normals = new Vector3[verts.Length];
            var bw = new BoneWeight[verts.Length];
            for (var j = 0; j < rings; j++)
                for (var i = 0; i < sides; i++) {
                    var a = i * Mathf.PI * 2 / sides;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var k = j * sides + i;
                    verts[k] = new Vector3(0.1f, Mathf.Lerp(top, bottom, j / (rings - 1f)), 0) + dir * r;
                    normals[k] = dir;
                    bw[k] = new BoneWeight { boneIndex0 = weights == 1 || weights == 2 && j == rings - 1 ? 1 : 0, weight0 = 1 };
                }
            var tris = Enumerable.Range(0, rings - 1).SelectMany(j => Enumerable.Range(0, sides).SelectMany(i => {
                int a = j * sides + i, b = j * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                return new[] { a, c, b, b, c, d };
            })).ToArray();
            return new Mesh { vertices = verts, normals = normals, triangles = tris, boneWeights = bw };
        }

        /// Closed 16 x 8 UV sphere on the first bone.
        private static Mesh Sphere(Vector3 center, float r) {
            const int sides = 16, rings = 9;
            var verts = new Vector3[sides * rings];
            var normals = new Vector3[verts.Length];
            for (var j = 0; j < rings; j++)
                for (var i = 0; i < sides; i++) {
                    float a = i * Mathf.PI * 2 / sides, b = j * Mathf.PI / (rings - 1);
                    var dir = new Vector3(Mathf.Sin(b) * Mathf.Cos(a), Mathf.Cos(b), Mathf.Sin(b) * Mathf.Sin(a));
                    verts[j * sides + i] = center + dir * r;
                    normals[j * sides + i] = dir;
                }
            var tris = Enumerable.Range(0, rings - 1).SelectMany(j => Enumerable.Range(0, sides).SelectMany(i => {
                int a = j * sides + i, b = j * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                return new[] { a, c, b, b, c, d };
            })).ToArray();
            var bw = Enumerable.Repeat(new BoneWeight { weight0 = 1 }, verts.Length).ToArray();
            return new Mesh { vertices = verts, normals = normals, triangles = tris, boneWeights = bw };
        }

        /// Flat square grid facing +x, n x n vertices, on the first bone.
        private static Mesh Sheet(Vector3 center, float half, int n) {
            var verts = new Vector3[n * n];
            for (var j = 0; j < n; j++)
                for (var i = 0; i < n; i++)
                    verts[j * n + i] = center + new Vector3(0, Mathf.Lerp(-half, half, j / (n - 1f)), Mathf.Lerp(-half, half, i / (n - 1f)));
            var tris = Enumerable.Range(0, n - 1).SelectMany(j => Enumerable.Range(0, n - 1).SelectMany(i => {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                return new[] { a, c, b, b, c, d };
            })).ToArray();
            var bw = Enumerable.Repeat(new BoneWeight { weight0 = 1 }, verts.Length).ToArray();
            return new Mesh { vertices = verts, normals = Enumerable.Repeat(Vector3.right, verts.Length).ToArray(), triangles = tris, boneWeights = bw };
        }

        private static SkinnedMeshRenderer Skinned(string name, Transform parent, Mesh mesh, Transform[] bones) {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * go.transform.localToWorldMatrix).ToArray();
            smr.bones = bones;
            smr.sharedMesh = mesh;
            return smr;
        }
    }
}
