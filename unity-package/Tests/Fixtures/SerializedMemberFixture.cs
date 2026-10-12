using UnityEngine;

namespace Patina.Editor.Tests.Fixtures
{
    public sealed class SerializedMemberFixture : SerializedMemberFixtureBase
    {
        public int PublicCount;

        [SerializeField]
        private string _label = string.Empty;

        private float _hidden = -1f;

        public float Speed { get; set; }

        public string ReadLabel() => _label;

        public float ReadHidden() => _hidden;
    }
}
