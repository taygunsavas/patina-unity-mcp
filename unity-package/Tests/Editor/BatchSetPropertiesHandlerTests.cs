using System;
using System.Collections;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Patina.Editor.Commands;
using Patina.Editor.Tests.Fixtures;
using UnityEngine;
using UnityEngine.TestTools;

namespace Patina.Editor.Tests
{
    public sealed class BatchSetPropertiesHandlerTests
    {
        private GameObject _target;
        private SerializedMemberFixture _fixture;
        private BatchSetPropertiesHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("PatinaBatchTarget_" + Guid.NewGuid().ToString("N"));
            _fixture = _target.AddComponent<SerializedMemberFixture>();
            _handler = new BatchSetPropertiesHandler();
        }

        [TearDown]
        public void TearDown()
        {
            if (_target != null)
                UnityEngine.Object.DestroyImmediate(_target);
        }

        [UnityTest]
        public IEnumerator HandleAsync_PrivateSerializeFieldOnConcreteClass_SetsValue()
        {
            JObject item = null;
            yield return Run(Operation("_label", "hello"), result => item = result);

            Assert.That(item.Value<bool>("success"), Is.True, item.Value<string>("error"));
            Assert.That(_fixture.ReadLabel(), Is.EqualTo("hello"));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PrivateSerializeFieldOnBaseClass_SetsValue()
        {
            JObject item = null;
            yield return Run(Operation("_baseValue", 42), result => item = result);

            Assert.That(item.Value<bool>("success"), Is.True, item.Value<string>("error"));
            Assert.That(_fixture.ReadBaseValue(), Is.EqualTo(42));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PrivateFieldWithoutSerializeField_IsRejected()
        {
            JObject item = null;
            yield return Run(Operation("_hidden", 3.5f), result => item = result);

            Assert.That(item.Value<bool>("success"), Is.False);
            Assert.That(
                item.Value<string>("error"),
                Does.Contain("Property or field '_hidden' not found")
            );
            Assert.That(_fixture.ReadHidden(), Is.EqualTo(-1f));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PrivateFieldWithoutSerializeFieldOnBaseClass_IsRejected()
        {
            JObject item = null;
            yield return Run(Operation("_baseHidden", 7), result => item = result);

            Assert.That(item.Value<bool>("success"), Is.False);
            Assert.That(
                item.Value<string>("error"),
                Does.Contain("Property or field '_baseHidden' not found")
            );
            Assert.That(_fixture.ReadBaseHidden(), Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PublicField_SetsValue()
        {
            JObject item = null;
            yield return Run(Operation("PublicCount", 9), result => item = result);

            Assert.That(item.Value<bool>("success"), Is.True, item.Value<string>("error"));
            Assert.That(_fixture.PublicCount, Is.EqualTo(9));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PublicWritableProperty_SetsValue()
        {
            JObject item = null;
            yield return Run(Operation("Speed", 2.5f), result => item = result);

            Assert.That(item.Value<bool>("success"), Is.True, item.Value<string>("error"));
            Assert.That(_fixture.Speed, Is.EqualTo(2.5f));
        }

        [UnityTest]
        public IEnumerator HandleAsync_UnknownName_IsRejected()
        {
            JObject item = null;
            yield return Run(Operation("DoesNotExist", 1), result => item = result);

            Assert.That(item.Value<bool>("success"), Is.False);
            Assert.That(
                item.Value<string>("error"),
                Does.Contain("Property or field 'DoesNotExist' not found")
            );
        }

        [UnityTest]
        public IEnumerator HandleAsync_MixedOperations_ReportsEachItemIndependently()
        {
            var parameters = new JObject
            {
                ["operations"] = new JArray(
                    Operation("_baseValue", 5),
                    Operation("_hidden", 1.0f),
                    Operation("_label", "mixed")
                ),
            };
            Task<object> task = _handler.HandleAsync(parameters);
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            var results = (JArray)((JObject)task.Result)["results"];
            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].Value<bool>("success"), Is.True);
            Assert.That(results[1].Value<bool>("success"), Is.False);
            Assert.That(results[2].Value<bool>("success"), Is.True);
            Assert.That(_fixture.ReadBaseValue(), Is.EqualTo(5));
            Assert.That(_fixture.ReadLabel(), Is.EqualTo("mixed"));
        }

        private IEnumerator Run(JObject operation, Action<JObject> onItem)
        {
            var parameters = new JObject { ["operations"] = new JArray(operation) };
            Task<object> task = _handler.HandleAsync(parameters);
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            var results = (JArray)((JObject)task.Result)["results"];
            Assert.That(results.Count, Is.EqualTo(1));
            onItem((JObject)results[0]);
        }

        private JObject Operation(string property, JToken value) =>
            new JObject
            {
                ["game_object_name"] = _target.name,
                ["component_type"] = nameof(SerializedMemberFixture),
                ["property_name"] = property,
                ["value"] = value,
            };
    }
}
