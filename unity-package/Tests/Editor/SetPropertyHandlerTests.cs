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
    public sealed class SetPropertyHandlerTests
    {
        private GameObject _target;
        private SerializedMemberFixture _fixture;
        private SetPropertyHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("PatinaSetPropertyTarget_" + Guid.NewGuid().ToString("N"));
            _fixture = _target.AddComponent<SerializedMemberFixture>();
            _handler = new SetPropertyHandler();
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
            Task<object> task = _handler.HandleAsync(Request("_label", "hello"));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(_fixture.ReadLabel(), Is.EqualTo("hello"));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PrivateSerializeFieldOnBaseClass_SetsValue()
        {
            Task<object> task = _handler.HandleAsync(Request("_baseValue", 42));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(_fixture.ReadBaseValue(), Is.EqualTo(42));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PrivateFieldWithoutSerializeField_IsRejected()
        {
            Task<object> task = _handler.HandleAsync(Request("_hidden", 3.5f));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(
                task.Exception.GetBaseException().Message,
                Does.Contain("Property or field '_hidden' not found")
            );
            Assert.That(_fixture.ReadHidden(), Is.EqualTo(-1f));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PrivateFieldWithoutSerializeFieldOnBaseClass_IsRejected()
        {
            Task<object> task = _handler.HandleAsync(Request("_baseHidden", 7));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(
                task.Exception.GetBaseException().Message,
                Does.Contain("Property or field '_baseHidden' not found")
            );
            Assert.That(_fixture.ReadBaseHidden(), Is.EqualTo(-1));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PublicField_SetsValue()
        {
            Task<object> task = _handler.HandleAsync(Request("PublicCount", 9));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(_fixture.PublicCount, Is.EqualTo(9));
        }

        [UnityTest]
        public IEnumerator HandleAsync_PublicWritableProperty_SetsValue()
        {
            Task<object> task = _handler.HandleAsync(Request("Speed", 2.5f));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            Assert.That(_fixture.Speed, Is.EqualTo(2.5f));
        }

        [UnityTest]
        public IEnumerator HandleAsync_UnknownName_IsRejected()
        {
            Task<object> task = _handler.HandleAsync(Request("DoesNotExist", 1));
            yield return HandlerTestSupport.Wait(task);

            Assert.That(task.IsFaulted, Is.True);
            Assert.That(
                task.Exception.GetBaseException().Message,
                Does.Contain("Property or field 'DoesNotExist' not found")
            );
        }

        private JObject Request(string property, JToken value) =>
            new JObject
            {
                ["game_object_name"] = _target.name,
                ["component_type"] = nameof(SerializedMemberFixture),
                ["property_name"] = property,
                ["value"] = value,
            };
    }
}
