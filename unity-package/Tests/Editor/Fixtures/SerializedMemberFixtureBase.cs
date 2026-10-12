using UnityEngine;

namespace Patina.Editor.Tests.Fixtures
{
    public abstract class SerializedMemberFixtureBase : MonoBehaviour
    {
        [SerializeField]
        private int _baseValue;

        private int _baseHidden = -1;

        public int ReadBaseValue() => _baseValue;

        public int ReadBaseHidden() => _baseHidden;
    }
}
