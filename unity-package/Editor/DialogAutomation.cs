using System;
using System.Reflection;

namespace Patina.Editor
{
    internal static class DialogAutomation
    {
        private static readonly Type s_interactionContextType;
        private static readonly Type s_globalInteractionContextType;
        private static readonly Type s_flagsType;
        private static readonly object s_disableDialogsFlag;
        private static readonly MethodInfo s_appendDialogResponse;
        private static readonly MethodInfo s_hasUnusedDialogResponses;
        private static readonly MethodInfo s_getCurrentDialogResponseAndAdvance;
        private static readonly ConstructorInfo s_globalContextCtor;
        private static readonly MethodInfo s_globalContextDispose;
        private static readonly MethodInfo s_getGlobalInteractionContext;
        private static readonly MethodInfo s_clearGlobalInteractionContext;

        public static bool IsAvailable { get; }

        static DialogAutomation()
        {
            try
            {
                Assembly assembly = typeof(UnityEditor.EditorUtility).Assembly;
                s_interactionContextType = assembly.GetType("UnityEditor.InteractionContext");
                s_globalInteractionContextType = assembly.GetType(
                    "UnityEditor.GlobalInteractionContext"
                );
                if (s_interactionContextType == null || s_globalInteractionContextType == null)
                    return;

                s_flagsType = s_interactionContextType.GetNestedType(
                    "Flags",
                    BindingFlags.Public | BindingFlags.NonPublic
                );
                if (s_flagsType == null)
                    return;

                s_disableDialogsFlag = Enum.Parse(s_flagsType, "DisableDialogs");

                s_appendDialogResponse = s_interactionContextType.GetMethod(
                    "AppendDialogResponse",
                    BindingFlags.Public | BindingFlags.Instance
                );
                s_hasUnusedDialogResponses = s_interactionContextType.GetMethod(
                    "HasUnusedDialogResponses",
                    BindingFlags.Public | BindingFlags.Instance
                );
                s_getCurrentDialogResponseAndAdvance = s_interactionContextType.GetMethod(
                    "GetCurrentDialogResponseAndAvance",
                    BindingFlags.Public | BindingFlags.Instance
                );

                s_globalContextCtor = s_globalInteractionContextType.GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { s_flagsType },
                    null
                );
                s_globalContextDispose = s_globalInteractionContextType.GetMethod(
                    "Dispose",
                    BindingFlags.Public | BindingFlags.Instance
                );
                s_getGlobalInteractionContext = s_globalInteractionContextType.GetMethod(
                    "GetGlobalInteractionContext",
                    BindingFlags.NonPublic | BindingFlags.Static
                );
                s_clearGlobalInteractionContext = s_globalInteractionContextType.GetMethod(
                    "ClearGlobalInteractionContext",
                    BindingFlags.NonPublic | BindingFlags.Static
                );

                IsAvailable =
                    s_appendDialogResponse != null
                    && s_hasUnusedDialogResponses != null
                    && s_getCurrentDialogResponseAndAdvance != null
                    && s_globalContextCtor != null
                    && s_globalContextDispose != null
                    && s_getGlobalInteractionContext != null
                    && s_clearGlobalInteractionContext != null;
            }
            catch
            {
                IsAvailable = false;
            }
        }

        public static IDisposable Scope(params (string Title, string Response)[] responses)
        {
            if (!IsAvailable)
                return NoOpScope.Instance;

            try
            {
                object existingContext = s_getGlobalInteractionContext.Invoke(null, null);
                if (existingContext != null)
                {
                    foreach (var response in responses)
                    {
                        s_appendDialogResponse.Invoke(
                            existingContext,
                            new object[] { response.Title, response.Response }
                        );
                    }
                    return new AttachedScope(existingContext);
                }

                object newContext = s_globalContextCtor.Invoke(new[] { s_disableDialogsFlag });
                foreach (var response in responses)
                {
                    s_appendDialogResponse.Invoke(
                        newContext,
                        new object[] { response.Title, response.Response }
                    );
                }
                return new OwnedScope(newContext);
            }
            catch
            {
                return NoOpScope.Instance;
            }
        }

        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new NoOpScope();

            public void Dispose() { }
        }

        private sealed class AttachedScope : IDisposable
        {
            private readonly object m_context;
            private bool m_disposed;

            public AttachedScope(object context)
            {
                m_context = context;
            }

            public void Dispose()
            {
                if (m_disposed)
                    return;
                m_disposed = true;
                try
                {
                    while ((bool)s_hasUnusedDialogResponses.Invoke(m_context, null))
                    {
                        s_getCurrentDialogResponseAndAdvance.Invoke(m_context, null);
                    }
                }
                catch { }
            }
        }

        private sealed class OwnedScope : IDisposable
        {
            private readonly object m_context;
            private bool m_disposed;

            public OwnedScope(object context)
            {
                m_context = context;
            }

            public void Dispose()
            {
                if (m_disposed)
                    return;
                m_disposed = true;
                try
                {
                    while ((bool)s_hasUnusedDialogResponses.Invoke(m_context, null))
                    {
                        s_getCurrentDialogResponseAndAdvance.Invoke(m_context, null);
                    }
                    s_globalContextDispose.Invoke(m_context, null);
                }
                catch
                {
                    try
                    {
                        s_clearGlobalInteractionContext.Invoke(null, null);
                    }
                    catch { }
                }
            }
        }
    }
}
