using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCFuryAutoTools {
    /// <summary>Which outfit bone sits on which avatar bone, and how each outfit bone moves to get there.</summary>
    public class OutfitBonePlan {
        public Transform outfitHips;
        /// outfit bone -> avatar bone
        public readonly Dictionary<Transform, Transform> match = new Dictionary<Transform, Transform>();
        /// world-space move for vertices skinned to each transform under the outfit hips
        public readonly Dictionary<Transform, Matrix4x4> motion = new Dictionary<Transform, Matrix4x4>();
        /// new world pose of every transform under the outfit hips, parents first
        private readonly List<(Transform t, Vector3 pos, Quaternion rot, Vector3 scale)> moves = new List<(Transform, Vector3, Quaternion, Vector3)>();

        public Matrix4x4 Motion(Transform bone) => bone != null && motion.TryGetValue(bone, out var m) ? m : Matrix4x4.identity;

        private static readonly Regex BlenderCopy = new Regex(@"\.\d{3}$");
        private static readonly Regex SideEnd = new Regex(@"^(.+?)[ ._\-]+(left|right|l|r)$");
        private static readonly Regex SideStart = new Regex(@"^(left|right|l|r)[ ._\-]+(.+)$");
        private static readonly Regex SideWordStart = new Regex(@"^(left|right)(.{3,})$");
        private static readonly Regex SideWordEnd = new Regex(@"^(.{3,}?)(left|right)$");

        /// <summary>Lowercase, no separators, side moved to the end: "Upper_Leg.L", "UpperLeg_L" and "Left UpperLeg" all become "upperleg#l".</summary>
        public static string Canon(string name) {
            var s = BlenderCopy.Replace(name.Trim(), "").ToLowerInvariant();
            string core = s, side = "";
            Match m;
            if ((m = SideEnd.Match(s)).Success || (m = SideWordEnd.Match(s)).Success) { core = m.Groups[1].Value; side = m.Groups[2].Value; }
            else if ((m = SideStart.Match(s)).Success || (m = SideWordStart.Match(s)).Success) { core = m.Groups[2].Value; side = m.Groups[1].Value; }
            core = Regex.Replace(core, "[^a-z0-9]", "");
            return side == "" ? core : core + "#" + side[0];
        }

        /// <summary>
        /// Matches the outfit armature to the avatar's by name, like VRCFury's Armature Link but forgiving about case,
        /// separators and side markers, and computes how each outfit bone moves onto its avatar bone.
        /// Bones keep their shape around their own axis except for one overall width factor from the avatar size difference.
        /// </summary>
        public static OutfitBonePlan Create(Transform outfitRoot, Transform avatarHips, Transform avatarNeck, float widthScale) {
            var plan = new OutfitBonePlan();
            var hipsKey = Canon(avatarHips.name);
            plan.outfitHips = outfitRoot.GetComponentsInChildren<Transform>(true)
                .Where(t => t != outfitRoot && t.childCount > 0
                    && (t.name.IndexOf(avatarHips.name, StringComparison.OrdinalIgnoreCase) >= 0 || Canon(t.name) == hipsKey || Canon(t.name).Contains("hips")))
                .OrderBy(Depth).FirstOrDefault();
            if (plan.outfitHips == null) return plan;

            // same affix rule as Armature Link: "Hips_Outfit" next to "Hips" means "_Outfit" is stripped from every bone
            var affix = plan.outfitHips.name.Contains(avatarHips.name) && plan.outfitHips.name != avatarHips.name
                ? plan.outfitHips.name.Replace(avatarHips.name, "") : null;
            Transform Child(Transform a, string key) => a.Cast<Transform>().FirstOrDefault(ch => !ch.IsChildOf(outfitRoot) && Canon(ch.name) == key);
            void Walk(Transform o, Transform a) {
                foreach (Transform oc in o) {
                    var key = Canon(string.IsNullOrEmpty(affix) ? oc.name : oc.name.Replace(affix, ""));
                    // one level deeper covers an avatar with an extra mid-bone (UpperChest)
                    var ac = Child(a, key) ?? a.Cast<Transform>().Select(g => Child(g, key)).FirstOrDefault(x => x != null);
                    if (ac != null) {
                        plan.match[oc] = ac;
                        Walk(oc, ac);
                    } else Walk(oc, a); // an outfit-only bone: its children may still match under the same avatar bone
                }
            }
            plan.match[plan.outfitHips] = avatarHips;
            Walk(plan.outfitHips, avatarHips);

            Transform Matched(Transform t) {
                for (var p = t.parent; p != null; p = p.parent) if (plan.match.ContainsKey(p)) return p;
                return null;
            }
            var kids = new Dictionary<Transform, List<Transform>>();
            foreach (var o in plan.match.Keys) {
                var p = Matched(o);
                if (p == null) continue;
                if (!kids.TryGetValue(p, out var list)) kids[p] = list = new List<Transform>();
                list.Add(o);
            }
            // the bone each bone points at: its only matched child, or the one that best continues the parent's direction (Chest -> Neck, Hand -> finger)
            Transform Next(Transform o, Transform parent) {
                if (!kids.TryGetValue(o, out var list)) return null;
                if (list.Count == 1) return list[0];
                if (parent == null) return null;
                var dir = (o.position - parent.position).normalized;
                return list.OrderByDescending(k => Vector3.Dot((k.position - o.position).normalized, dir)).First();
            }

            var width = 1f;
            var outfitNeck = avatarNeck != null ? plan.match.FirstOrDefault(kv => kv.Value == avatarNeck).Key : null;
            if (outfitNeck != null) {
                var from = (outfitNeck.position - plan.outfitHips.position).magnitude;
                if (from > 1e-5f) width = Mathf.Clamp((avatarNeck.position - avatarHips.position).magnitude / from, 0.5f, 2f);
            }
            width *= widthScale;

            var turn = new Dictionary<Transform, Quaternion>();
            foreach (var o in plan.outfitHips.GetComponentsInChildren<Transform>(true)) {
                var parent = Matched(o);
                var r = parent != null ? turn[parent] : Quaternion.identity;
                Matrix4x4 m;
                if (plan.match.TryGetValue(o, out var a)) {
                    var length = 1f;
                    var axis = Vector3.zero;
                    var next = Next(o, parent);
                    if (next != null) {
                        var from = next.position - o.position;
                        var to = plan.match[next].position - a.position;
                        if (from.sqrMagnitude > 1e-10f && to.sqrMagnitude > 1e-10f) {
                            r = Quaternion.FromToRotation(from, to);
                            length = Mathf.Clamp(to.magnitude / from.magnitude, 0.5f, 2f);
                            axis = Quaternion.Inverse(o.rotation) * from.normalized;
                        }
                    }
                    m = Matrix4x4.TRS(a.position, r * o.rotation, Vector3.one) * AxisScale(axis, length, width)
                        * Matrix4x4.TRS(o.position, o.rotation, Vector3.one).inverse;
                    // exactly onto the avatar bone, so Armature Link's alignment changes nothing
                    plan.moves.Add((o, a.position, a.rotation, a.lossyScale));
                } else {
                    m = parent != null ? plan.motion[parent] : Matrix4x4.identity;
                    plan.moves.Add((o, m.MultiplyPoint3x4(o.position), r * o.rotation, o.lossyScale));
                }
                plan.motion[o] = m;
                turn[o] = r;
            }
            return plan;
        }

        /// <summary>Scales by <paramref name="length"/> along the unit axis d and by <paramref name="width"/> across it.</summary>
        private static Matrix4x4 AxisScale(Vector3 d, float length, float width) {
            var s = Matrix4x4.identity;
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 3; c++) s[r, c] = (r == c ? width : 0) + (length - width) * d[r] * d[c];
            return s;
        }

        private static int Depth(Transform t) {
            var d = 0;
            for (; t != null; t = t.parent) d++;
            return d;
        }

        /// <summary>Puts the outfit's bones where the plan says. Build only: the fitted meshes are written against these poses.</summary>
        public void MoveBones() {
            foreach (var (t, pos, rot, scale) in moves) {
                t.SetPositionAndRotation(pos, rot);
                var p = t.parent != null ? t.parent.lossyScale : Vector3.one;
                t.localScale = new Vector3(Div(scale.x, p.x), Div(scale.y, p.y), Div(scale.z, p.z));
            }
        }

        private static float Div(float a, float b) => Mathf.Abs(b) > 1e-8f ? a / b : a;
    }

    /// <summary>The avatar's body meshes baked into one world-space surface, for closest-point lookups and rays.</summary>
    public class BodySurface {
        private Vector3[] verts, normals;
        private int[] tris, triStart, triList; // vertex -> incident triangles
        private readonly Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();
        private readonly Dictionary<Vector3Int, List<int>> triGrid = new Dictionary<Vector3Int, List<int>>(); // cell -> triangles touching it
        private Vector3Int gridMin, gridMax;
        private float cell;

        public static BodySurface Create(IEnumerable<SkinnedMeshRenderer> bodies) {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            var baked = new Mesh();
            foreach (var smr in bodies) {
                smr.BakeMesh(baked, true);
                if (baked.normals.Length != baked.vertexCount) baked.RecalculateNormals();
                var pos = smr.transform.position;
                var rot = smr.transform.rotation;
                var start = v.Count;
                v.AddRange(baked.vertices.Select(p => pos + rot * p));
                n.AddRange(baked.normals.Select(x => rot * x));
                t.AddRange(baked.triangles.Select(i => i + start));
            }
            Object.DestroyImmediate(baked);

            var s = new BodySurface { verts = v.ToArray(), normals = n.ToArray(), tris = t.ToArray() };
            s.triStart = new int[s.verts.Length + 1];
            foreach (var i in s.tris) s.triStart[i + 1]++;
            for (var i = 0; i < s.verts.Length; i++) s.triStart[i + 1] += s.triStart[i];
            s.triList = new int[s.tris.Length];
            var fill = (int[])s.triStart.Clone();
            for (var i = 0; i < s.tris.Length; i++) s.triList[fill[s.tris[i]]++] = i / 3;

            if (s.verts.Length == 0) return s;
            var bounds = new Bounds(s.verts[0], Vector3.zero);
            foreach (var p in s.verts) bounds.Encapsulate(p);
            s.cell = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 1e-3f) / 64;
            s.gridMin = s.Key(bounds.min);
            s.gridMax = s.Key(bounds.max);
            for (var i = 0; i < s.verts.Length; i++) {
                var k = s.Key(s.verts[i]);
                if (!s.grid.TryGetValue(k, out var list)) s.grid[k] = list = new List<int>();
                list.Add(i);
            }
            for (var f = 0; f < s.tris.Length / 3; f++) {
                Vector3 a = s.verts[s.tris[f * 3]], b = s.verts[s.tris[f * 3 + 1]], c = s.verts[s.tris[f * 3 + 2]];
                Vector3Int lo = s.Key(Vector3.Min(a, Vector3.Min(b, c))), hi = s.Key(Vector3.Max(a, Vector3.Max(b, c)));
                for (var x = lo.x; x <= hi.x; x++)
                    for (var y = lo.y; y <= hi.y; y++)
                        for (var z = lo.z; z <= hi.z; z++) {
                            var k = new Vector3Int(x, y, z);
                            if (!s.triGrid.TryGetValue(k, out var list)) s.triGrid[k] = list = new List<int>();
                            list.Add(f);
                        }
            }
            return s;
        }

        private Vector3Int Key(Vector3 p) => Vector3Int.FloorToInt(p / cell);

        /// <summary>
        /// Closest point on the body, the smooth body normal there, and the normal of the triangle it is on (turned to the
        /// smooth side). False if there is no body.
        /// </summary>
        public bool Closest(Vector3 p, out Vector3 point, out Vector3 normal, out Vector3 face) {
            point = p;
            normal = face = Vector3.zero;
            var near = NearestVertex(p);
            if (near < 0) return false;
            point = verts[near];
            normal = face = normals[near].normalized;
            var best = (point - p).sqrMagnitude;
            for (var k = triStart[near]; k < triStart[near + 1]; k++) {
                var f = triList[k] * 3;
                int a = tris[f], b = tris[f + 1], c = tris[f + 2];
                var q = ClosestOnTriangle(p, verts[a], verts[b], verts[c], out var bary);
                var d = (q - p).sqrMagnitude;
                if (d > best) continue;
                best = d;
                point = q;
                var nn = normals[a] * bary.x + normals[b] * bary.y + normals[c] * bary.z;
                if (nn.sqrMagnitude > 1e-12f) normal = nn.normalized;
                var fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (fn.sqrMagnitude > 1e-20f) face = Vector3.Dot(fn, normal) < 0 ? -fn.normalized : fn.normalized;
            }
            return true;
        }

        /// <summary>First body surface along the unit ray dir within maxDistance, and the smooth body normal there.</summary>
        public bool Raycast(Vector3 p, Vector3 dir, float maxDistance, out float distance, out Vector3 normal) {
            distance = maxDistance;
            normal = Vector3.zero;
            var at = Key(p);
            var step = Vector3Int.zero;
            Vector3 next = default, delta = default;
            for (var k = 0; k < 3; k++) {
                step[k] = dir[k] > 0 ? 1 : dir[k] < 0 ? -1 : 0;
                next[k] = step[k] == 0 ? float.PositiveInfinity : ((at[k] + (step[k] > 0 ? 1 : 0)) * cell - p[k]) / dir[k];
                delta[k] = step[k] == 0 ? float.PositiveInfinity : cell / Mathf.Abs(dir[k]);
            }
            var hit = false;
            // walk the grid cells along the ray (Amanatides & Woo) until past the nearest hit
            for (var entered = 0f; entered <= distance;) {
                if (triGrid.TryGetValue(at, out var list))
                    foreach (var f in list) {
                        int a = tris[f * 3], b = tris[f * 3 + 1], c = tris[f * 3 + 2];
                        if (!RayTriangle(p, dir, verts[a], verts[b], verts[c], out var t, out var u, out var v) || t >= distance) continue;
                        distance = t;
                        normal = (normals[a] * (1 - u - v) + normals[b] * u + normals[c] * v).normalized;
                        hit = true;
                    }
                var axis = next.x < next.y ? (next.x < next.z ? 0 : 2) : (next.y < next.z ? 1 : 2);
                entered = next[axis];
                at[axis] += step[axis];
                next[axis] += delta[axis];
            }
            return hit;
        }

        /// <summary>Möller–Trumbore, both faces. t is along d, u and v are the weights of b and c.</summary>
        public static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v) {
            t = u = v = 0;
            Vector3 e1 = b - a, e2 = c - a;
            var pv = Vector3.Cross(d, e2);
            var det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-12f) return false;
            var tv = o - a;
            u = Vector3.Dot(tv, pv) / det;
            if (u < 0 || u > 1) return false;
            var qv = Vector3.Cross(tv, e1);
            v = Vector3.Dot(d, qv) / det;
            if (v < 0 || u + v > 1) return false;
            t = Vector3.Dot(e2, qv) / det;
            return t > 0;
        }

        private int NearestVertex(Vector3 p) {
            if (verts.Length == 0) return -1;
            var c = Key(p);
            var rings = Mathf.Max(
                Mathf.Max(Mathf.Abs(c.x - gridMin.x), Mathf.Abs(c.x - gridMax.x)),
                Mathf.Max(Mathf.Abs(c.y - gridMin.y), Mathf.Abs(c.y - gridMax.y)),
                Mathf.Max(Mathf.Abs(c.z - gridMin.z), Mathf.Abs(c.z - gridMax.z)));
            var best = -1;
            var bestD = float.MaxValue;
            void Visit(int x, int y, int z) {
                if (!grid.TryGetValue(new Vector3Int(x, y, z), out var list)) return;
                foreach (var i in list) {
                    var d = (verts[i] - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            // grow a cube shell of cells until nothing outside it can be closer
            for (var r = 0; r <= rings; r++) {
                for (var x = -r; x <= r; x++)
                    for (var y = -r; y <= r; y++) {
                        if (Mathf.Abs(x) == r || Mathf.Abs(y) == r) {
                            for (var z = -r; z <= r; z++) Visit(c.x + x, c.y + y, c.z + z);
                        } else if (r > 0) {
                            Visit(c.x + x, c.y + y, c.z - r);
                            Visit(c.x + x, c.y + y, c.z + r);
                        }
                    }
                if (best >= 0 && bestD <= r * cell * (r * cell)) break;
            }
            return best;
        }

        /// <summary>Ericson, Real-Time Collision Detection 5.1.5.</summary>
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary) {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) { bary = new Vector3(1, 0, 0); return a; }
            var bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) { bary = new Vector3(0, 1, 0); return b; }
            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) { var v = d1 / (d1 - d3); bary = new Vector3(1 - v, v, 0); return a + v * ab; }
            var cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) { bary = new Vector3(0, 0, 1); return c; }
            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) { var w = d2 / (d2 - d6); bary = new Vector3(1 - w, 0, w); return a + w * ac; }
            var va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) {
                var w = (d4 - d3) / (d4 - d3 + (d5 - d6));
                bary = new Vector3(0, 1 - w, w);
                return b + w * (c - b);
            }
            var sum = va + vb + vc;
            if (sum <= 1e-20f) { bary = new Vector3(1, 0, 0); return a; } // degenerate triangle
            float vv = vb / sum, ww = vc / sum;
            bary = new Vector3(1 - vv - ww, vv, ww);
            return a + ab * vv + ac * ww;
        }
    }

    /// <summary>
    /// One outfit mesh: where its vertices land after the bone fit, the body under each one, and the fitted result.
    /// Painted edits are per vertex; overlapping vertices (UV seams) are welded so they always move together.
    /// </summary>
    public class OutfitFitMesh {
        public SkinnedMeshRenderer renderer;
        public Mesh source;
        public int[] triangles;
        public Vector3[] result;  // fitted world position per vertex, filled by Solve
        public Vector3[] normals; // bone-fitted world normal per vertex
        private Matrix4x4[] skin; // per-vertex mesh -> bone-fitted world
        private Vector3[] basePos;
        private int[] weld, rep;     // vertex -> welded point, welded point -> its first vertex
        private int[] adjStart, adj; // welded point neighbours
        private Vector3[] bodyPoint, bodyNormal; // per welded point

        public int VertexCount => source.vertexCount;

        public static OutfitFitMesh Create(SkinnedMeshRenderer smr, OutfitBonePlan plan, BodySurface body) {
            var mesh = smr.sharedMesh;
            var n = mesh.vertexCount;
            var f = new OutfitFitMesh { renderer = smr, source = mesh, triangles = mesh.triangles };

            var bones = smr.bones;
            var bind = mesh.bindposes;
            var boneSkin = new Matrix4x4[bones.Length];
            var segA = new Vector3[bones.Length];
            var segB = new Vector3[bones.Length];
            for (var i = 0; i < bones.Length; i++) {
                var bone = bones[i];
                boneSkin[i] = bone == null || i >= bind.Length
                    ? smr.transform.localToWorldMatrix
                    : plan.Motion(bone) * bone.localToWorldMatrix * bind[i];
                if (bone == null) {
                    segA[i] = segB[i] = smr.transform.position;
                    continue;
                }
                // the bone as a segment from its head to the middle of its children, after the bone fit
                segA[i] = plan.Motion(bone).MultiplyPoint3x4(bone.position);
                segB[i] = bone.childCount == 0 ? segA[i]
                    : bone.Cast<Transform>().Aggregate(Vector3.zero, (sum, c) => sum + plan.Motion(c).MultiplyPoint3x4(c.position)) / bone.childCount;
            }

            var perVertex = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            var verts = mesh.vertices;
            var meshNormals = mesh.normals;
            f.skin = new Matrix4x4[n];
            f.basePos = new Vector3[n];
            f.normals = new Vector3[n];
            var anchors = new Vector3[n]; // weighted closest point on the bones under each vertex
            var wi = 0;
            for (var v = 0; v < n; v++) {
                var m = new Matrix4x4();
                var total = 0f;
                var count = v < perVertex.Length ? perVertex[v] : 0;
                for (var k = 0; k < count; k++) {
                    var bw = weights[wi + k];
                    if (bw.boneIndex >= boneSkin.Length || bw.weight <= 0) continue;
                    var b = boneSkin[bw.boneIndex];
                    for (var e = 0; e < 16; e++) m[e] += b[e] * bw.weight;
                    total += bw.weight;
                }
                if (total <= 0) m = smr.transform.localToWorldMatrix;
                else for (var e = 0; e < 16; e++) m[e] /= total;
                f.skin[v] = m;
                f.basePos[v] = m.MultiplyPoint3x4(verts[v]);
                f.normals[v] = v < meshNormals.Length ? m.inverse.transpose.MultiplyVector(meshNormals[v]).normalized : Vector3.up;

                var anchor = Vector3.zero;
                for (var k = 0; k < count; k++) {
                    var bw = weights[wi + k];
                    if (bw.boneIndex >= boneSkin.Length || bw.weight <= 0) continue;
                    anchor += ClosestOnSegment(f.basePos[v], segA[bw.boneIndex], segB[bw.boneIndex]) * bw.weight;
                }
                anchors[v] = total > 0 ? anchor / total : smr.transform.position;
                wi += count;
            }

            var size = mesh.bounds.size;
            var eps = Mathf.Max(Mathf.Max(size.x, size.y, size.z) * 1e-5f, 1e-7f);
            var ids = new Dictionary<Vector3Int, int>();
            var reps = new List<int>();
            f.weld = new int[n];
            for (var v = 0; v < n; v++) {
                var key = Vector3Int.RoundToInt(verts[v] / eps);
                if (!ids.TryGetValue(key, out var id)) {
                    id = reps.Count;
                    ids[key] = id;
                    reps.Add(v);
                }
                f.weld[v] = id;
            }
            f.rep = reps.ToArray();

            var neighbours = new HashSet<int>[f.rep.Length];
            for (var i = 0; i < neighbours.Length; i++) neighbours[i] = new HashSet<int>();
            for (var t = 0; t < f.triangles.Length; t += 3)
                for (var e = 0; e < 3; e++) {
                    int a = f.weld[f.triangles[t + e]], b = f.weld[f.triangles[t + (e + 1) % 3]];
                    if (a == b) continue;
                    neighbours[a].Add(b);
                    neighbours[b].Add(a);
                }
            f.adjStart = new int[f.rep.Length + 1];
            for (var i = 0; i < neighbours.Length; i++) f.adjStart[i + 1] = f.adjStart[i] + neighbours[i].Count;
            f.adj = neighbours.SelectMany(x => x).ToArray();

            f.bodyPoint = new Vector3[f.rep.Length];
            f.bodyNormal = new Vector3[f.rep.Length];
            for (var i = 0; i < f.rep.Length; i++) {
                var v = f.rep[i];
                var p = f.basePos[v];
                body.Closest(p, out var point, out var normal, out var face);
                // smooth normals lie at creases (under a breast): the triangle the closest point is on says if p is inside
                if (Vector3.Dot(point - p, face) > 0 != Vector3.Dot(point - p, normal) > 0) normal = face;
                f.bodyPoint[i] = point;
                f.bodyNormal[i] = normal;

                // The body as seen from the bones under this point: straight out from them to where the body ends.
                // Neighbouring points get neighbouring rays, so a bulge the outfit is deep inside (bigger breasts, a belly)
                // pushes it out evenly and the same on both sides, where the nearest body point would be a crease beside it.
                // Stays with the nearest body point where the bones aren't inside the body, the ray only grazes it, or p is
                // inside some other body part than the one the ray leaves.
                var s = anchors[v];
                var dir = p - s;
                var r = dir.magnitude;
                if (r < 1e-4f) continue;
                dir /= r;
                if (!body.Raycast(s, dir, r + MaxPush, out var exit, out var exitNormal) || Vector3.Dot(exitNormal, dir) < 0.3f) continue;
                if (exit < r && Vector3.Dot(point - p, normal) > 0) continue;
                f.bodyPoint[i] = s + dir * exit;
                f.bodyNormal[i] = dir;
            }
            return f;
        }

        // ponytail: fixed cap on how far past the outfit the body may reach; raise it if a bulge sticks out further than this
        private const float MaxPush = 0.15f;

        private static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b) {
            var ab = b - a;
            var len = ab.sqrMagnitude;
            return len < 1e-12f ? a : a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / len);
        }

        /// <summary>How much of a gap gets closed: automatic for small gaps, painted tight (+1) or loose (-1) overrides it.</summary>
        public static float Tightness(float gapNow, float tightenBelow, float paint) {
            var auto = tightenBelow > 0 ? Mathf.Clamp01((tightenBelow - gapNow) / (tightenBelow * 0.5f)) : 0;
            return paint >= 0 ? Mathf.Lerp(auto, 1, paint) : Mathf.Lerp(auto, 0, -paint);
        }

        /// <summary>
        /// Pushes clipping parts out to <paramref name="gap"/>, pulls small gaps in, smooths that displacement (not the
        /// positions, so folds and seams keep their shape), then adds the painted edits. Edit arrays may be null.
        /// </summary>
        // ponytail: displacement smoothing instead of an ARAP solve; upgrade if big shape changes shear folds
        public void Solve(float gap, float tightenBelow, int smoothing, bool fixClipping, float[] offset, float[] smooth, float[] tight) {
            var count = rep.Length;
            // distance along each point's own body normal, smoothed as a number: smoothing the vectors would shrink it on round parts
            var move = new float[count];
            for (var i = 0; i < count; i++) {
                var d = Vector3.Dot(basePos[rep[i]] - bodyPoint[i], bodyNormal[i]);
                move[i] = d < gap ? gap - d : -(d - gap) * Tightness(d, tightenBelow, tight != null ? tight[rep[i]] : 0);
            }
            var next = new float[count];
            for (var p = 0; p < smoothing; p++) {
                for (var i = 0; i < count; i++) {
                    var sum = 0f;
                    for (var k = adjStart[i]; k < adjStart[i + 1]; k++) sum += move[adj[k]];
                    var n = adjStart[i + 1] - adjStart[i];
                    next[i] = n > 0 ? (move[i] + sum / n) * 0.5f : move[i];
                }
                (move, next) = (next, move);
            }

            var pos = new Vector3[count];
            for (var i = 0; i < count; i++)
                pos[i] = basePos[rep[i]] + (move[i] + (offset != null ? offset[rep[i]] : 0)) * bodyNormal[i];
            if (smooth != null) pos = Relax(pos, 4, i => smooth[rep[i]]);
            if (fixClipping) {
                for (var i = 0; i < count; i++) {
                    var d = Vector3.Dot(pos[i] - bodyPoint[i], bodyNormal[i]);
                    if (d < gap) pos[i] += (gap - d) * bodyNormal[i];
                }
            }

            result = new Vector3[weld.Length];
            for (var v = 0; v < weld.Length; v++) result[v] = pos[weld[v]];
        }

        /// <summary>Moves each welded point toward the average of its neighbours. Reuses the given array.</summary>
        private Vector3[] Relax(Vector3[] x, int passes, Func<int, float> amount) {
            var next = new Vector3[x.Length];
            for (var p = 0; p < passes; p++) {
                for (var i = 0; i < x.Length; i++) {
                    var count = adjStart[i + 1] - adjStart[i];
                    var t = amount(i);
                    if (count == 0 || t <= 0) { next[i] = x[i]; continue; }
                    var sum = Vector3.zero;
                    for (var k = adjStart[i]; k < adjStart[i + 1]; k++) sum += x[adj[k]];
                    next[i] = Vector3.Lerp(x[i], sum / count, t);
                }
                (x, next) = (next, x);
            }
            return x;
        }

        /// <summary>Ray against the fitted mesh. Brute force over all triangles, fine for brushing one outfit.</summary>
        public bool Raycast(Ray ray, out float distance, out Vector3 normal) {
            distance = float.MaxValue;
            normal = Vector3.up;
            var hit = false;
            for (var t = 0; t < triangles.Length; t += 3) {
                Vector3 a = result[triangles[t]], b = result[triangles[t + 1]], c = result[triangles[t + 2]];
                if (!BodySurface.RayTriangle(ray.origin, ray.direction, a, b, c, out var d, out _, out _) || d >= distance) continue;
                distance = d;
                normal = Vector3.Cross(b - a, c - a).normalized;
                hit = true;
            }
            if (Vector3.Dot(normal, ray.direction) > 0) normal = -normal;
            return hit;
        }

        /// <summary>
        /// Build only, after <see cref="OutfitBonePlan.MoveBones"/>: gives the renderer a copy of its mesh that shows the
        /// fitted result in the new bone poses. Shape keys get the same bone-fit motion as the vertices under them.
        /// </summary>
        public void Write() {
            var mesh = Object.Instantiate(source);
            mesh.name = source.name;
            var toLocal = renderer.transform.worldToLocalMatrix;
            var n = source.vertexCount;

            var verts = new Vector3[n];
            for (var v = 0; v < n; v++) verts[v] = toLocal.MultiplyPoint3x4(result[v]);
            mesh.vertices = verts;
            if (source.normals.Length == n) mesh.normals = normals.Select(x => toLocal.MultiplyVector(x).normalized).ToArray();
            var tangents = source.tangents;
            if (tangents.Length == n) {
                for (var v = 0; v < n; v++) {
                    var t = tangents[v];
                    var d = toLocal.MultiplyVector(skin[v].MultiplyVector(t)).normalized;
                    tangents[v] = new Vector4(d.x, d.y, d.z, t.w);
                }
                mesh.tangents = tangents;
            }

            if (source.blendShapeCount > 0) {
                mesh.ClearBlendShapes();
                var dv = new Vector3[n];
                var dn = new Vector3[n];
                var dt = new Vector3[n];
                for (var s = 0; s < source.blendShapeCount; s++) {
                    for (var fr = 0; fr < source.GetBlendShapeFrameCount(s); fr++) {
                        source.GetBlendShapeFrameVertices(s, fr, dv, dn, dt);
                        for (var v = 0; v < n; v++) {
                            var m = toLocal * skin[v];
                            dv[v] = m.MultiplyVector(dv[v]);
                            dn[v] = m.MultiplyVector(dn[v]);
                            dt[v] = m.MultiplyVector(dt[v]);
                        }
                        mesh.AddBlendShapeFrame(source.GetBlendShapeName(s), source.GetBlendShapeFrameWeight(s, fr), dv, dn, dt);
                    }
                }
            }

            var bones = renderer.bones;
            var bind = source.bindposes;
            var meshToWorld = renderer.transform.localToWorldMatrix;
            for (var i = 0; i < bind.Length; i++)
                if (i < bones.Length && bones[i] != null) bind[i] = bones[i].worldToLocalMatrix * meshToWorld;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            renderer.sharedMesh = mesh;
        }
    }
}
