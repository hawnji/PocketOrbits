// Minimal stand-ins for UnityEngine.TestRunner types so PlayMode tests can be compile-checked outside Unity.
// Compile-check only; the real Unity Test Framework provides these.
using System;

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)]
    public class UnityTestAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class UnitySetUpAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class UnityTearDownAttribute : Attribute { }
}
