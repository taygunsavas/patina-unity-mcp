using System;
using System.Collections;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Patina.Editor.Commands;
using Patina.Editor.Tests.Fixtures;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Patina.Editor.Tests
{
    public sealed class EditPrefabAssetHandlerTests
    {
        private string _assetPath;
        private EditPrefabAssetHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _assetPath = "Assets/PatinaEditPrefabTest_" + Guid.NewGuid().ToString("N") + ".prefab";
            var template = new GameObject("PatinaEditPrefabRoot");
            try
            {
                var existing = new GameObject("Existing");
                existing.transform.SetParent(template.transform, false);
                var staticChild = new GameObject("StaticChild") { isStatic = true };
                staticChild.transform.SetParent(template.transform, false);
                PrefabUtility.SaveAsPrefabAsset(template, _assetPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(template);
            }
            _handler = new EditPrefabAssetHandler();
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (!string.IsNullOrEmpty(_assetPath))
                AssetDatabase.DeleteAsset(_assetPath);
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldLayerWithInt_SetsLayer()
        {
            Task<object> task = Run(
                GameObjectField("Existing", "UnityEngine.GameObject", "layer", 5)
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Existing").layer, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldLayerWithName_SetsLayer()
        {
            Task<object> task = Run(GameObjectField("Existing", "GameObject", "layer", "Water"));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Existing").layer, Is.EqualTo(LayerMask.NameToLayer("Water")));
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldWithEmptyComponentType_AppliesToGameObject()
        {
            Task<object> task = Run(GameObjectField("Existing", "", "LAYER", "UI"));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Existing").layer, Is.EqualTo(LayerMask.NameToLayer("UI")));
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldTag_SetsTag()
        {
            Task<object> task = Run(
                GameObjectField("Existing", "UnityEngine.GameObject", "tag", "Player")
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Existing").CompareTag("Player"), Is.True);
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldIsStatic_SetsStatic()
        {
            Task<object> task = Run(
                GameObjectField("Existing", "UnityEngine.GameObject", "isStatic", true)
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Existing").isStatic, Is.True);
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldName_RenamesGameObject()
        {
            Task<object> task = Run(
                GameObjectField("Existing", "UnityEngine.GameObject", "name", "Renamed")
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Renamed"), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldActiveSelf_DeactivatesGameObject()
        {
            Task<object> task = Run(
                GameObjectField("Existing", "UnityEngine.GameObject", "activeSelf", false)
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Existing").activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator HandleAsync_AddChildWithLayerTagAndStatic_AppliesToChild()
        {
            JObject action = new JObject
            {
                ["action_type"] = "add_child",
                ["child_name"] = "Wall0",
                ["layer"] = "Water",
                ["tag"] = "MainCamera",
                ["is_static"] = true,
            };
            Task<object> task = Run(action);
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            GameObject child = Load("Wall0");
            Assert.That(child.layer, Is.EqualTo(LayerMask.NameToLayer("Water")));
            Assert.That(child.CompareTag("MainCamera"), Is.True);
            Assert.That(child.isStatic, Is.True);
        }

        [UnityTest]
        public IEnumerator HandleAsync_UndefinedLayerName_IsRejected()
        {
            Task<object> task = Run(
                GameObjectField("Existing", "GameObject", "layer", "PatinaNoSuchLayer")
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(
                task.Exception.GetBaseException().Message,
                Does.Contain("Layer 'PatinaNoSuchLayer' is not defined")
            );
            AssertUnchanged();
        }

        [UnityTest]
        public IEnumerator HandleAsync_UnregisteredTag_IsRejected()
        {
            Task<object> task = Run(
                GameObjectField("Existing", "GameObject", "tag", "PatinaNoSuchTag")
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(
                task.Exception.GetBaseException().Message,
                Does.Contain("Tag 'PatinaNoSuchTag' is not registered")
            );
            AssertUnchanged();
        }

        [UnityTest]
        public IEnumerator HandleAsync_UnknownGameObjectField_ListsSupportedFields()
        {
            Task<object> task = Run(GameObjectField("Existing", "GameObject", "bogus", 1));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(
                task.Exception.GetBaseException().Message,
                Does.Contain("Supported fields: layer, tag, isStatic, name, activeSelf")
            );
            AssertUnchanged();
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldLayerOnRoot_PersistsLayer()
        {
            Task<object> task = Run(GameObjectField(null, "UnityEngine.GameObject", "layer", 4));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(_assetPath).layer, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldNameOnRoot_IsRejected()
        {
            Task<object> task = Run(
                GameObjectField(null, "UnityEngine.GameObject", "name", "Other")
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(
                task.Exception.GetBaseException().Message,
                Does.Contain("prefab root name follows the asset file name")
            );
            AssertUnchanged();
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldIsStaticFalse_ClearsStatic()
        {
            Assert.That(Load("StaticChild").isStatic, Is.True);

            Task<object> task = Run(
                GameObjectField("StaticChild", "GameObject", "isStatic", false)
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("StaticChild").isStatic, Is.False);
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldIsStaticWithString_SetsStatic()
        {
            Task<object> task = Run(GameObjectField("Existing", "GameObject", "isStatic", "True"));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Existing").isStatic, Is.True);
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldLayerWithNumericString_SetsLayer()
        {
            Task<object> task = Run(GameObjectField("Existing", "GameObject", "layer", "5"));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Existing").layer, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator HandleAsync_NegativeLayer_IsRejected()
        {
            Task<object> task = Run(GameObjectField("Existing", "GameObject", "layer", -1));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(task.Exception.GetBaseException().Message, Does.Contain("out of range"));
            AssertUnchanged();
        }

        [UnityTest]
        public IEnumerator HandleAsync_LayerAboveRange_IsRejected()
        {
            Task<object> task = Run(GameObjectField("Existing", "GameObject", "layer", 32));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(task.Exception.GetBaseException().Message, Does.Contain("out of range"));
            AssertUnchanged();
        }

        [UnityTest]
        public IEnumerator HandleAsync_UnknownFieldWithoutComponentType_HintsAtComponentType()
        {
            Task<object> task = Run(GameObjectField("Existing", "", "bogus", 1));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(
                task.Exception.GetBaseException().Message,
                Does.Contain("If this is a component field, set component_type.")
            );
        }

        [UnityTest]
        public IEnumerator HandleAsync_AddChildWithIntLayer_AppliesLayer()
        {
            JObject action = new JObject
            {
                ["action_type"] = "add_child",
                ["child_name"] = "Wall1",
                ["layer"] = 6,
            };
            Task<object> task = Run(action);
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(Load("Wall1").layer, Is.EqualTo(6));
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldOnComponent_StillUsesComponentPath()
        {
            Task<object> task = Run(
                GameObjectField("Existing", "Transform", "m_LocalPosition", new JArray(1f, 2f, 3f))
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(
                Load("Existing").transform.localPosition,
                Is.EqualTo(new Vector3(1f, 2f, 3f))
            );
        }

        [UnityTest]
        public IEnumerator HandleAsync_AddComponent_RunsOnValidateAndPersistsField()
        {
            Task<object> task = Run(AddFixture("Existing"));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            var fixture = Load("Existing").GetComponent<OnValidateFixture>();
            Assert.That(fixture.ValidateCount, Is.GreaterThan(0));
            Assert.That(fixture.InvokedByRunner, Is.True);
            Assert.That(((JObject)task.Result)["warnings"], Is.Null);
        }

        [UnityTest]
        public IEnumerator HandleAsync_SetFieldOnFixture_RunsOnValidate()
        {
            SeedFixture("Existing", throwOnValidate: false);
            Task<object> task = Run(
                GameObjectField("Existing", nameof(OnValidateFixture), "Marker", 7)
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            var fixture = Load("Existing").GetComponent<OnValidateFixture>();
            Assert.That(fixture.Marker, Is.EqualTo(7));
            Assert.That(fixture.InvokedByRunner, Is.True);
        }

        [UnityTest]
        public IEnumerator HandleAsync_UntouchedFixtureComponent_DoesNotRunOnValidate()
        {
            SeedFixture("Existing", throwOnValidate: false);
            SeedFixture("StaticChild", throwOnValidate: false);
            Task<object> task = Run(
                GameObjectField("Existing", nameof(OnValidateFixture), "Marker", 3)
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(
                Load("Existing").GetComponent<OnValidateFixture>().InvokedByRunner,
                Is.True
            );
            Assert.That(
                Load("StaticChild").GetComponent<OnValidateFixture>().InvokedByRunner,
                Is.False
            );
        }

        [UnityTest]
        public IEnumerator HandleAsync_ThrowingOnValidate_ReportsWarningAndKeepsSave()
        {
            LogAssert.ignoreFailingMessages = true;
            SeedFixture("Existing", throwOnValidate: true);
            Task<object> task = Run(
                GameObjectField("Existing", nameof(OnValidateFixture), "Marker", 5)
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            var result = (JObject)task.Result;
            Assert.That(result["success"].Value<bool>(), Is.True);
            Assert.That(result["warnings"], Has.Count.EqualTo(1));
            Assert.That(
                result["warnings"][0].Value<string>(),
                Does.Contain("OnValidateFixture refused to validate")
            );
            Assert.That(Load("Existing").GetComponent<OnValidateFixture>().Marker, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator HandleAsync_RenameAfterAddComponent_StillRunsOnValidate()
        {
            Task<object> task = RunAll(
                AddChild("Existing", "Inner"),
                AddFixture("Existing/Inner"),
                GameObjectField("Existing", "GameObject", "name", "Renamed")
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(((JObject)task.Result)["warnings"], Is.Null);
            Assert.That(
                Load("Renamed/Inner").GetComponent<OnValidateFixture>().InvokedByRunner,
                Is.True
            );
        }

        [UnityTest]
        public IEnumerator HandleAsync_RemoveChildAfterAddComponent_ProducesNoWarning()
        {
            Task<object> task = RunAll(
                AddChild("Existing", "Inner"),
                AddFixture("Existing/Inner"),
                new JObject
                {
                    ["action_type"] = "remove_child",
                    ["transform_path"] = "Existing/Inner",
                }
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(((JObject)task.Result)["warnings"], Is.Null);
        }

        [UnityTest]
        public IEnumerator HandleAsync_TwoComponentsOfSameType_RunsOnValidateOnBoth()
        {
            Task<object> task = RunAll(AddFixture("Existing"), AddFixture("Existing"));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            OnValidateFixture[] fixtures = Load("Existing").GetComponents<OnValidateFixture>();
            Assert.That(fixtures, Has.Length.EqualTo(2));
            Assert.That(fixtures[0].InvokedByRunner, Is.True);
            Assert.That(fixtures[1].InvokedByRunner, Is.True);
        }

        [UnityTest]
        public IEnumerator HandleAsync_RemoveComponentAfterAdd_ProducesNoWarning()
        {
            Task<object> task = RunAll(
                AddFixture("Existing"),
                new JObject
                {
                    ["action_type"] = "remove_component",
                    ["transform_path"] = "Existing",
                    ["component_type"] = nameof(OnValidateFixture),
                }
            );
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(((JObject)task.Result)["warnings"], Is.Null);
        }

        private Task<object> RunAll(params JObject[] actions) =>
            _handler.HandleAsync(
                new JObject { ["asset_path"] = _assetPath, ["actions"] = new JArray(actions) }
            );

        private static JObject AddChild(string transformPath, string childName) =>
            new JObject
            {
                ["action_type"] = "add_child",
                ["transform_path"] = transformPath,
                ["child_name"] = childName,
            };

        private static JObject AddFixture(string transformPath) =>
            new JObject
            {
                ["action_type"] = "add_component",
                ["transform_path"] = transformPath,
                ["component_type"] = nameof(OnValidateFixture),
            };

        private void SeedFixture(string childName, bool throwOnValidate)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(_assetPath);
            try
            {
                GameObject child = root.transform.Find(childName).gameObject;
                var fixture = child.AddComponent<OnValidateFixture>();
                fixture.ThrowOnValidate = throwOnValidate;
                fixture.ValidateCount = 0;
                PrefabUtility.SaveAsPrefabAsset(root, _assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private void AssertUnchanged()
        {
            Assert.That(Load("Existing").layer, Is.EqualTo(0));
            Assert.That(Load("Existing").CompareTag("Untagged"), Is.True);
        }

        private Task<object> Run(JObject action) =>
            _handler.HandleAsync(
                new JObject { ["asset_path"] = _assetPath, ["actions"] = new JArray(action) }
            );

        private static JObject GameObjectField(
            string transformPath,
            string componentType,
            string fieldName,
            JToken value
        ) =>
            new JObject
            {
                ["action_type"] = "set_field",
                ["transform_path"] = transformPath,
                ["component_type"] = componentType,
                ["field_name"] = fieldName,
                ["value"] = value,
            };

        private GameObject Load(string childName)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(_assetPath);
            Assert.That(root, Is.Not.Null, "Prefab asset not found");
            Transform child = root.transform.Find(childName);
            Assert.That(child, Is.Not.Null, $"Child '{childName}' not found");
            return child.gameObject;
        }
    }
}
