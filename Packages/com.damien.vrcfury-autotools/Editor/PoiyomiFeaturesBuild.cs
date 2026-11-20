using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using com.vrcfury.api;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

namespace VRCFuryAutoTools {
    public static class PoiyomiFeatures {
        private static readonly MethodInfo RenameSuffix = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Thry.ThryEditor.ShaderOptimizer") ?? a.GetType("Thry.ShaderOptimizer"))
            .FirstOrDefault(t => t != null)
            ?.GetMethod("GetRenamedPropertySuffix", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        public static string MenuPath(PoiyomiFeature c) =>
            string.Join("/", new[] { (c.menuRoot ?? "").Trim().Trim('/'), c.Label }.Where(s => s != ""));

        /// <summary>
        /// Name the property has once the material is locked: with "Animated (when locked)" set to renamed (tag "2"),
        /// Poiyomi adds the material's suffix. Same rule VRCFury's Material Property uses.
        /// </summary>
        private static string LockedName(Material m, string property) =>
            m.GetTag(property + "Animated", false) == "2" && RenameSuffix != null
                ? property + "_" + RenameSuffix.Invoke(null, new object[] { m })
                : property;

        private static bool Has(Material m, string property) => m.HasProperty(property) || m.HasProperty(LockedName(m, property));

        private static IEnumerable<string> Properties(PoiyomiFeature c) =>
            c.Sliders.SelectMany(s => s.properties).Prepend(c.ToggleProperty).Where(p => p != null);

        /// <summary>Every renderer and material of the avatar that has one of the feature's properties.</summary>
        private static List<(Renderer renderer, Material material)> Targets(PoiyomiFeature c, Transform avatar) {
            var properties = Properties(c).ToArray();
            return avatar.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials.Where(m => m != null && properties.Any(p => Has(m, p))).Select(m => (r, m)))
                .ToList();
        }

        /// <summary>[DoNotLock] properties (Light Data's) stay live when locked, so they need no Animated flag.</summary>
        private static bool StaysLive(Shader shader, string property) {
            var i = shader.FindPropertyIndex(property);
            return i >= 0 && shader.GetPropertyAttributes(i).Contains("DoNotLock");
        }

        private static string Description(Shader shader, string property) {
            var i = shader.FindPropertyIndex(property);
            var label = i >= 0 ? shader.GetPropertyDescription(i).Split(new[] { "--" }, StringSplitOptions.None)[0].Trim() : "";
            return label != "" ? $"{label} ({property})" : property;
        }

        /// <summary>Warnings for the inspector: no material with the feature, feature switched off, properties not marked Animated.</summary>
        public static List<string> Problems(PoiyomiFeature c, Transform avatar) {
            var materials = Targets(c, avatar).Select(t => t.material).Distinct().ToList();
            if (materials.Count == 0) return new List<string> { $"No material on this avatar has Poiyomi's {c.Label}. Nothing is added at build." };

            var problems = new List<string>();
            var off = materials.Where(m => c.EnableProperty != null && m.HasProperty(c.EnableProperty) && m.GetFloat(c.EnableProperty) == 0)
                .Select(m => m.name).ToList();
            if (off.Count > 0)
                problems.Add($"{c.Label} is switched off on these materials, so locking leaves it out and the toggle does nothing there. "
                    + "Switch it on in the material if it should work there too:\n" + string.Join("\n", off));

            var notAnimated = materials.Select(m => (m, missing: Properties(c)
                    .Where(p => Has(m, p) && m.GetTag(p + "Animated", false) == "" && !StaysLive(m.shader, p))
                    .Select(p => Description(m.shader, p)).ToList()))
                .Where(x => x.missing.Count > 0)
                .Select(x => $"{x.m.name}: {string.Join(", ", x.missing)}").ToList();
            if (notAnimated.Count > 0)
                problems.Add("These properties are not marked Animated (when locked), so locking bakes them in and the menu cannot change them. "
                    + "Right-click each one in the Poiyomi material inspector and pick Animated (when locked), then re-lock:\n" + string.Join("\n", notAnimated));
            return problems;
        }

        /// <summary>
        /// Adds a VRCFury Full Controller: "menuRoot/Feature/Toggle" (only when the feature has a toggle property) plus one
        /// radial per slider. One layer per slider: Off sets its properties and the feature's toggle to 0, On scrubs a
        /// 0-to-max clip with the radial. Without a toggle the layer is On only.
        /// </summary>
        internal static void Build(PoiyomiFeature c, Transform avatar) {
            var targets = Targets(c, avatar);
            if (targets.Count == 0) return;

            // no toggle property (Light Data): radials only, always live
            var toggle = c.ToggleProperty != null ? c.Label : null;
            var controller = new AnimatorController { name = c.Label };
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var parameters = new List<VRCExpressionParameters.Parameter>();
            if (toggle != null) {
                controller.AddParameter(new AnimatorControllerParameter { name = toggle, type = AnimatorControllerParameterType.Bool, defaultBool = c.defaultOn });
                menu.controls.Add(new VRCExpressionsMenu.Control {
                    name = "Toggle", type = VRCExpressionsMenu.Control.ControlType.Toggle, parameter = new VRCExpressionsMenu.Control.Parameter { name = toggle },
                });
                parameters.Add(new VRCExpressionParameters.Parameter {
                    name = toggle, valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = c.defaultOn ? 1 : 0, saved = c.saved, networkSynced = true,
                });
            }

            var sliders = c.Sliders;
            for (var i = 0; i < sliders.Length; i++) {
                var s = sliders[i];
                var radial = $"{c.Label} {s.label}";
                controller.AddParameter(new AnimatorControllerParameter { name = radial, type = AnimatorControllerParameterType.Float, defaultFloat = s.defaultValue });
                menu.controls.Add(new VRCExpressionsMenu.Control {
                    name = s.label, type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = radial } },
                });
                parameters.Add(new VRCExpressionParameters.Parameter {
                    name = radial, valueType = VRCExpressionParameters.ValueType.Float, defaultValue = s.defaultValue, saved = c.saved, networkSynced = true,
                });

                var offClip = new AnimationClip { name = radial + " Off" };
                var onClip = new AnimationClip { name = radial + " On" };
                foreach (var p in s.properties) {
                    Key(offClip, targets, avatar, p, 0, 0);
                    Key(onClip, targets, avatar, p, 0, s.max);
                }
                if (i == 0 && c.ToggleProperty != null) {
                    Key(offClip, targets, avatar, c.ToggleProperty, 0, 0);
                    Key(onClip, targets, avatar, c.ToggleProperty, 1, 1);
                }
                AddLayer(controller, radial, toggle, offClip, onClip, radial);
            }

            var prms = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            prms.parameters = parameters.ToArray();
            var full = FuryComponents.CreateFullController(c.gameObject);
            full.AddController(controller);
            full.AddMenu(menu, MenuPath(c));
            full.AddParams(prms);
        }

        /// <summary>Curve from 'from' at 0 s to 'to' at 1 s on every target that has the property. Paths from the avatar root.</summary>
        private static void Key(AnimationClip clip, List<(Renderer renderer, Material material)> targets, Transform avatar, string property, float from, float to) {
            var done = new HashSet<(Renderer, string)>();
            foreach (var (renderer, material) in targets) {
                if (!Has(material, property)) continue;
                var name = LockedName(material, property);
                if (!done.Add((renderer, name))) continue;
                clip.SetCurve(AnimationUtility.CalculateTransformPath(renderer.transform, avatar), renderer.GetType(), "material." + name,
                    AnimationCurve.Linear(0, from, 1, to));
            }
        }

        private static void AddLayer(AnimatorController controller, string name, string toggle, Motion offClip, Motion onClip, string radial) {
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            controller.AddLayer(new AnimatorControllerLayer { name = name, stateMachine = machine, defaultWeight = 1 });
            AnimatorState off = null;
            if (toggle != null) off = machine.AddState("Off"); // first state added is the default one
            var on = machine.AddState("On");
            on.motion = onClip;
            on.writeDefaultValues = false;
            on.timeParameterActive = true; // the radial scrubs the 1 s clip
            on.timeParameter = radial;
            if (off == null) return;
            off.motion = offClip;
            off.writeDefaultValues = false;
            var enter = off.AddTransition(on);
            enter.hasExitTime = false;
            enter.duration = 0;
            enter.AddCondition(AnimatorConditionMode.If, 0, toggle);
            var leave = on.AddTransition(off);
            leave.hasExitTime = false;
            leave.duration = 0;
            leave.AddCondition(AnimatorConditionMode.IfNot, 0, toggle);
        }
    }

    /// <summary>Runs before VRCFury (-10000) so the Full Controllers we add get built with everything else.</summary>
    internal class PoiyomiFeaturesHook : IVRCSDKPreprocessAvatarCallback {
        public int callbackOrder => -20000;

        public bool OnPreprocessAvatar(GameObject avatar) {
            foreach (var c in avatar.GetComponentsInChildren<PoiyomiFeature>(true)) PoiyomiFeatures.Build(c, avatar.transform);
            return true;
        }
    }
}
