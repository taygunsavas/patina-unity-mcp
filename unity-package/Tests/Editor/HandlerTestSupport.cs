using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Patina.Editor.Tests
{
    internal static class HandlerTestSupport
    {
        private const int MaxFrames = 600;

        internal static IEnumerator Wait(Task task)
        {
            int frames = 0;
            while (!task.IsCompleted)
            {
                if (++frames > MaxFrames)
                    Assert.Fail("Handler task did not complete within the frame budget");
                yield return null;
            }
        }
    }
}
