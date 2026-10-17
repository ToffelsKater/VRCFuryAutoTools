using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRCFuryAutoTools {
    /// <summary>
    /// Previews the fit in the Scene view and paints per-vertex fixes onto the component. The preview is a hidden
    /// object drawn in place of each outfit mesh; the scene itself is never changed.
    /// </summary>
    [CustomEditor(typeof(OutfitFitter))]
    internal class OutfitFitterEditor : Editor {
        private enum Brush { PushPull, Smooth, TightLoose }
        private static readonly string[] BrushNames = { "Push / Pull", "Smooth", "Tight / Loose" };
        private static readonly string[] BrushHelp = {
            "Pushes the outfit away from the body. Hold Shift to pull it closer.",
            "Smooths dents and spikes. Hold Shift to remove smoothing.",
            "Paints areas tight, so they follow the body (red). Hold Shift to paint them loose, so they keep their shape (blue).",
        };
        private static Brush brush;
        private static float radius = 0.03f, strength = 0.5f;
        private static bool mirror = true, showPaint;

        private OutfitFitterBuild.Session session;
        private string lastError;
        private readonly List<(OutfitFitMesh fit, GameObject go, Mesh mesh)> previews = new List<(OutfitFitMesh, GameObject, Mesh)>();
        private readonly List<GameObject> hiddenOthers = new List<GameObject>();
        private Material paintMaterial;

        private OutfitFitter Fitter => (OutfitFitter)target;
        private bool Editing => session != null;

        private void OnDisable() {
            StopEditing();
            if (paintMaterial != null) DestroyImmediate(paintMaterial);
        }

        public override void OnInspectorGUI() {
            var before = PrepareKey();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck() && Editing) {
                if (PrepareKey() != before) StartEditing(); // bodies or width changed: bone fit again
                else ResolveAll();
            }

            if (!Editing) {
                EditorGUILayout.HelpBox("The outfit is fitted to the avatar when you upload. Use Edit fit to preview it and paint fixes. "
                    + "Link the outfit with a VRCFury Armature Link as usual.", MessageType.Info);
                if (lastError != null) EditorGUILayout.HelpBox(lastError, MessageType.Error);
                if (GUILayout.Button("Edit fit")) StartEditing();
                return;
            }

            EditorGUILayout.HelpBox($"{session.meshes.Count} meshes fitted, {session.plan.match.Count} bones matched. "
                + "Left-drag on the outfit in the Scene view to paint.", MessageType.Info);
            brush = (Brush)GUILayout.Toolbar((int)brush, BrushNames);
            EditorGUILayout.LabelField(BrushHelp[(int)brush], EditorStyles.wordWrappedMiniLabel);
            radius = EditorGUILayout.Slider("Radius", radius, 0.005f, 0.2f);
            strength = EditorGUILayout.Slider("Strength", strength, 0.05f, 1f);
            mirror = EditorGUILayout.Toggle("Mirror left / right", mirror);
            EditorGUI.BeginChangeCheck();
            showPaint = EditorGUILayout.Toggle("Show paint", showPaint);
            if (EditorGUI.EndChangeCheck()) foreach (var p in previews) UpdatePreview(p);

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Refit")) StartEditing();
                if (GUILayout.Button("Clear paint")) {
                    Undo.RecordObject(Fitter, "Clear outfit fit paint");
                    Fitter.edits.Clear();
                    ResolveAll();
                }
                if (GUILayout.Button("Done")) StopEditing();
            }
        }

        private string PrepareKey() => Fitter.widthScale + ":" + string.Join(",", Fitter.bodies.Select(b => b != null ? b.GetInstanceID() : 0));

        private void StartEditing() {
            StopEditing();
            var s = OutfitFitterBuild.Prepare(Fitter);
            lastError = s.error;
            if (s.error != null) return;
            session = s;
            if (paintMaterial == null) {
                paintMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                paintMaterial.SetInt("_Cull", 0);
                paintMaterial.SetInt("_ZWrite", 1);
            }
            // Only the bodies and the outfit stay visible. Things the user already hid stay hidden afterwards.
            var keep = new HashSet<Renderer>(OutfitFitterBuild.Bodies(Fitter));
            foreach (var r in FindObjectsOfType<Renderer>(true)) {
                var go = r.gameObject;
                if (keep.Contains(r) || go.transform.IsChildOf(Fitter.transform) || SceneVisibilityManager.instance.IsHidden(go)) continue;
                SceneVisibilityManager.instance.Hide(go, false);
                hiddenOthers.Add(go);
            }
            foreach (var f in s.meshes.Where(m => m.renderer.enabled && m.renderer.gameObject.activeInHierarchy)) {
                var mesh = Instantiate(f.source);
                mesh.hideFlags = HideFlags.HideAndDontSave;
                var go = EditorUtility.CreateGameObjectWithHideFlags("Outfit Fit Preview", HideFlags.HideAndDontSave, typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                SceneVisibilityManager.instance.Hide(f.renderer.gameObject, false);
                previews.Add((f, go, mesh));
            }
            Undo.undoRedoPerformed += StartEditing;
            ResolveAll();
        }

        private void StopEditing() {
            Undo.undoRedoPerformed -= StartEditing;
            foreach (var p in previews) {
                if (p.fit.renderer != null) SceneVisibilityManager.instance.Show(p.fit.renderer.gameObject, false);
                DestroyImmediate(p.go);
                DestroyImmediate(p.mesh);
            }
            previews.Clear();
            foreach (var go in hiddenOthers) if (go != null) SceneVisibilityManager.instance.Show(go, false);
            hiddenOthers.Clear();
            session = null;
            SceneView.RepaintAll();
        }

        private void ResolveAll() {
            foreach (var p in previews) {
                OutfitFitterBuild.Solve(Fitter, p.fit);
                UpdatePreview(p);
            }
            SceneView.RepaintAll();
        }

        private void UpdatePreview((OutfitFitMesh fit, GameObject go, Mesh mesh) p) {
            p.mesh.vertices = p.fit.result;
            p.mesh.normals = p.fit.normals;
            p.mesh.RecalculateBounds();
            var r = p.go.GetComponent<MeshRenderer>();
            if (!showPaint) {
                r.sharedMaterials = p.fit.renderer.sharedMaterials;
                return;
            }
            r.sharedMaterials = Enumerable.Repeat(paintMaterial, p.mesh.subMeshCount).ToArray();
            var e = OutfitFitterBuild.Edits(Fitter, p.fit, false);
            var colors = new Color[p.fit.VertexCount];
            for (var v = 0; v < colors.Length; v++) {
                var c = new Color(0.75f, 0.75f, 0.75f);
                if (e != null) {
                    c = e.tight[v] >= 0 ? Color.Lerp(c, Color.red, e.tight[v]) : Color.Lerp(c, Color.blue, -e.tight[v]);
                    c = Color.Lerp(c, Color.green, e.smooth[v] * 0.6f);
                    c = Color.Lerp(c, e.offset[v] > 0 ? Color.white : Color.black, Mathf.Clamp01(Mathf.Abs(e.offset[v]) / 0.01f) * 0.5f);
                }
                colors[v] = c;
            }
            p.mesh.colors = colors;
        }

        private void OnSceneGUI() {
            if (!Editing) return;
            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) {
                HandleUtility.AddDefaultControl(id); // no click-selecting other objects while painting
                return;
            }
            if (e.alt) return; // camera orbit

            var hit = Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out var point, out var normal);
            if (hit && e.type == EventType.Repaint) {
                Handles.color = Color.cyan;
                Handles.DrawWireDisc(point, normal, radius);
                if (mirror) Handles.DrawWireDisc(Mirror(point, true), Mirror(normal, false), radius);
            }
            switch (e.type) {
                case EventType.MouseMove:
                    SceneView.RepaintAll();
                    break;
                case EventType.MouseDown when e.button == 0 && hit:
                    GUIUtility.hotControl = id;
                    Paint(point, e.shift);
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    if (hit) Paint(point, e.shift);
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        private bool Raycast(Ray ray, out Vector3 point, out Vector3 normal) {
            var best = float.MaxValue;
            point = normal = Vector3.zero;
            foreach (var p in previews) {
                if (!p.fit.Raycast(ray, out var d, out var n) || d >= best) continue;
                best = d;
                point = ray.GetPoint(d);
                normal = n;
            }
            return best < float.MaxValue;
        }

        /// Mirrors across the avatar's left/right plane.
        private Vector3 Mirror(Vector3 v, bool isPoint) {
            var root = ZeroWeightBoneAnalysis.AvatarRoot(Fitter).transform;
            var l = isPoint ? root.InverseTransformPoint(v) : root.InverseTransformDirection(v);
            l.x = -l.x;
            return isPoint ? root.TransformPoint(l) : root.TransformDirection(l);
        }

        private void Paint(Vector3 center, bool invert) {
            var c = Fitter;
            Undo.RecordObject(c, "Paint outfit fit");
            var centers = mirror ? new[] { center, Mirror(center, true) } : new[] { center };
            var sign = invert ? -1 : 1;
            foreach (var p in previews) {
                var f = p.fit;
                OutfitFitter.PaintedEdits e = null;
                for (var v = 0; v < f.VertexCount; v++) {
                    var fall = 0f;
                    foreach (var m in centers) {
                        var d = (f.result[v] - m).magnitude;
                        if (d < radius) fall = Mathf.Max(fall, 1 - d / radius);
                    }
                    if (fall <= 0) continue;
                    var amount = fall * fall * strength * sign;
                    e ??= OutfitFitterBuild.Edits(c, f, true);
                    switch (brush) {
                        case Brush.PushPull: e.offset[v] = Mathf.Clamp(e.offset[v] + amount * 0.0005f, -0.05f, 0.05f); break;
                        case Brush.Smooth: e.smooth[v] = Mathf.Clamp01(e.smooth[v] + amount * 0.1f); break;
                        default: e.tight[v] = Mathf.Clamp(e.tight[v] + amount * 0.1f, -1, 1); break;
                    }
                }
                if (e == null) continue;
                OutfitFitterBuild.Solve(c, f);
                UpdatePreview(p);
            }
            EditorUtility.SetDirty(c);
        }
    }
}
